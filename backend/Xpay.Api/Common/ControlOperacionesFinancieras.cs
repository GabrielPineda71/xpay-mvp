using System.Globalization;
using Xpay.Api.Exceptions;

namespace Xpay.Api.Common;

public enum MotivoBloqueoFinanciero
{
    Pausa,
    ConfiguracionInvalida,
    TopeOperacionUvb,
    LimiteDiarioXpay,
}

// Piloto PROD — corrección 3: interruptor de operaciones financieras y límites
// de retiro Bre-B.
//
// Fase visual (bloqueo financiero, fase 1): con el interruptor cerrado no se
// mueve dinero ni se crean obligaciones por NINGUNA vía. Tres capas lo usan:
//   · [OperacionFinanciera] (OperacionFinancieraFilter): 503 antes de ejecutar
//     cualquier endpoint que mueva dinero o llame a Passport/riesgo con efecto.
//   · BloqueoFinancieroInterceptor: rechaza al guardar cualquier asiento,
//     movimiento, cambio de saldo o cupo, venga de donde venga.
//   · Webhook de recaudo (LibranzaRecaudoAplicacionService) y conciliación
//     Bre-B (BrebPaymentReconciliationService): no aplican nada; el evento
//     queda sin procesar y Passport lo reintenta.
//
// Ambiente productivo (IAmbienteXpay) — fail-closed:
//   · XPAY_OPERACIONES_FINANCIERAS_HABILITADAS debe ser exactamente "true".
//   · XPAY_UVB_VALOR_COP (entero > 0), XPAY_UVB_ANIO (= año Colombia en curso)
//     y XPAY_TOPE_RETIRO_DIARIO_COP (entero > 0) son obligatorios para retirar.
// Ambiente no productivo: cada clave ausente desactiva su control (QA no
// cambia); una clave presente se valida igual que en PROD.
//
// El límite diario es un límite INTERNO de XPAY, no una regla de Bre-B. El
// tope por operación es 1.000 UVB (constante: cambiarlo exige un cambio de
// código revisado). Los valores se leen en cada llamada (sin caché).
public sealed class ControlOperacionesFinancieras(IConfiguration config, IAmbienteXpay ambiente)
{
    public const string ClaveInterruptor  = "XPAY_OPERACIONES_FINANCIERAS_HABILITADAS";
    public const string ClaveUvbValor     = "XPAY_UVB_VALOR_COP";
    public const string ClaveUvbAnio      = "XPAY_UVB_ANIO";
    public const string ClaveTopeDiario   = "XPAY_TOPE_RETIRO_DIARIO_COP";
    public const int    TopeRetiroUvb     = 1000;

    internal const string MensajeNoDisponible  = "Operaciones financieras no disponibles en este momento.";
    internal const string MensajeTopeOperacion = "El valor supera el máximo permitido por operación.";
    internal const string MensajeLimiteDiario  = "Has alcanzado el límite diario de retiros de XPAY.";

    public void ExigirInterruptor() =>
        Lanzar(EvaluarInterruptor(ambiente.EsProductivo, config[ClaveInterruptor]));

    public bool EstaHabilitado() =>
        EvaluarInterruptor(ambiente.EsProductivo, config[ClaveInterruptor]) is null;

    // acumuladoDiaCop: suma de retiros del usuario en el día Colombia de
    // ahoraUtc que no devolvieron la reserva (calculado bajo lock por el
    // llamador).
    public void ExigirRetiro(decimal montoCop, decimal acumuladoDiaCop, DateTime ahoraUtc) =>
        Lanzar(EvaluarRetiro(
            ambiente.EsProductivo, config[ClaveInterruptor], config[ClaveUvbValor], config[ClaveUvbAnio],
            config[ClaveTopeDiario], montoCop, acumuladoDiaCop, ahoraUtc));

    // Inicio (UTC) del día calendario Colombia que contiene ahoraUtc.
    public static DateTime InicioDiaColombiaUtc(DateTime ahoraUtc) =>
        ColombiaTime.DesdeUtc(ahoraUtc).Date.AddHours(-ColombiaTime.OffsetHoras);

    internal static MotivoBloqueoFinanciero? EvaluarInterruptor(bool esProductivo, string? interruptor)
    {
        if (esProductivo)
            return string.Equals(interruptor, "true", StringComparison.Ordinal) ? null : MotivoBloqueoFinanciero.Pausa;

        return string.IsNullOrWhiteSpace(interruptor) || string.Equals(interruptor, "true", StringComparison.Ordinal)
            ? null
            : MotivoBloqueoFinanciero.Pausa;
    }

    internal static MotivoBloqueoFinanciero? EvaluarRetiro(
        bool esProductivo, string? interruptor, string? uvbValor, string? uvbAnio, string? topeDiario,
        decimal montoCop, decimal acumuladoDiaCop, DateTime ahoraUtc)
    {
        if (EvaluarInterruptor(esProductivo, interruptor) is { } pausa)
            return pausa;

        // ── Tope por operación: 1.000 UVB del año en curso ─────────────────
        var uvbConfigurada = !string.IsNullOrWhiteSpace(uvbValor) || !string.IsNullOrWhiteSpace(uvbAnio);
        if (esProductivo || uvbConfigurada)
        {
            if (!TryEnteroPositivo(uvbValor, out var valorUvb) || !TryEnteroPositivo(uvbAnio, out var anioUvb))
                return MotivoBloqueoFinanciero.ConfiguracionInvalida;
            if (anioUvb != ColombiaTime.DesdeUtc(ahoraUtc).Year)
                return MotivoBloqueoFinanciero.ConfiguracionInvalida;
            if (montoCop > valorUvb * TopeRetiroUvb)
                return MotivoBloqueoFinanciero.TopeOperacionUvb;
        }

        // ── Límite diario interno XPAY ─────────────────────────────────────
        if (esProductivo || !string.IsNullOrWhiteSpace(topeDiario))
        {
            if (!TryEnteroPositivo(topeDiario, out var limite))
                return MotivoBloqueoFinanciero.ConfiguracionInvalida;
            if (acumuladoDiaCop + montoCop > limite)
                return MotivoBloqueoFinanciero.LimiteDiarioXpay;
        }

        return null;
    }

    private static bool TryEnteroPositivo(string? valor, out long resultado) =>
        long.TryParse(valor?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out resultado) && resultado > 0;

    private static void Lanzar(MotivoBloqueoFinanciero? motivo)
    {
        if (motivo is not { } m) return;
        var mensaje = m switch
        {
            MotivoBloqueoFinanciero.TopeOperacionUvb => MensajeTopeOperacion,
            MotivoBloqueoFinanciero.LimiteDiarioXpay => MensajeLimiteDiario,
            _                                        => MensajeNoDisponible,
        };
        throw new OperacionFinancieraBloqueadaException(m, mensaje);
    }
}
