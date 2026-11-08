using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Xpay.Api.Integrations.MiDecisor;

// XPAY-656 — compuerta de QA para la PRIMERA consulta real de MiDecisor.
//
// Decora la autorización durable (consentimiento V1,
// AutorizacionConsultaRiesgoDurable) y la restringe a UNA sola solicitud
// configurada explícitamente. Devuelve true EXCLUSIVAMENTE si se cumplen
// TODAS estas condiciones:
//   1. MIDECISOR_QA_ALLOWED_SOLICITUD_ID representa exactamente UN long > 0
//      (sólo dígitos ASCII: sin signo, espacios, separadores ni listas) y es
//      igual a idSolicitud;
//   2. MIDECISOR_STOP_AFTER_CONSULTA vale exactamente "true" (comparación
//      ordinal). Sin el stop activo la consulta no se habilita, para que la
//      ejecución nunca avance a consumo, decisión ni cupo;
//   3. la autorización durable devuelve true. El consentimiento sigue siendo
//      obligatorio; esta clase NUNCA lo sustituye.
// Cualquier otra combinación devuelve false (fail-closed). Sin configuración
// nadie queda autorizado: registrar esta clase en DI tiene el mismo efecto que
// el stub AutorizacionConsultaRiesgoNoDisponible.
//
// La configuración se evalúa ANTES de consultar la autorización durable; sin
// configuración QA no se accede a la base de datos.
//
// En DI la dependencia interna es SIEMPRE AutorizacionConsultaRiesgoDurable
// (ver CarteraRiesgoRuntimeWiring). Se recibe como IConsultaRiesgoAutorizacion
// únicamente para poder probar el gate sin SQL.
//
// Logging saneado: nunca idSolicitud, idUsuario, valores de configuración,
// documento, apellido ni secretos; sólo el motivo genérico del rechazo.
public sealed class AutorizacionConsultaRiesgoQaGate(
    IConsultaRiesgoAutorizacion autorizacionDurable,
    IConfiguration configuration,
    ILogger<AutorizacionConsultaRiesgoQaGate> logger,
    IQaClienteRiesgoStore? qaClienteStore = null)
    : IConsultaRiesgoAutorizacion
{
    // Expuesto sólo para verificar el wiring de DI en tests.
    internal IConsultaRiesgoAutorizacion AutorizacionDurable => autorizacionDurable;

    public async Task<bool> TieneAutorizacionVigenteAsync(
        long idUsuario, long idSolicitud, CancellationToken cancellationToken = default)
    {
        // XPAY-702 - modo explicito: rama QA_CLIENT_FULL_FLOW (o rechazo).
        // Sin modo: rama heredada XPAY-656 sin cambios.
        var modo = configuration[MiDecisorQaGateConfig.EnvGateMode];
        if (!string.IsNullOrEmpty(modo))
            return await AutorizarClienteQaAsync(modo, idUsuario, idSolicitud, cancellationToken)
                .ConfigureAwait(false);

        if (!MiDecisorQaGateConfig.TryLeerSolicitudPermitida(configuration, out var permitida))
        {
            logger.LogInformation("midecisor.qagate: no hay solicitud QA permitida configurada; no autorizado.");
            return false;
        }

        if (idSolicitud != permitida)
        {
            logger.LogInformation("midecisor.qagate: la solicitud no es la permitida en QA; no autorizado.");
            return false;
        }

        if (!MiDecisorQaGateConfig.StopAfterConsultaActivo(configuration))
        {
            logger.LogWarning("midecisor.qagate: STOP_AFTER_CONSULTA no está activo; no autorizado.");
            return false;
        }

        // La cancelación se propaga. La autorización durable ya es fail-closed
        // ante errores operacionales.
        return await autorizacionDurable
            .TieneAutorizacionVigenteAsync(idUsuario, idSolicitud, cancellationToken)
            .ConfigureAwait(false);
    }

    // XPAY-702 - modo QA_CLIENT_FULL_FLOW. Autoriza SOLO si: el modo es
    // exactamente QA_CLIENT_FULL_FLOW; STOP vale exactamente "false"; el
    // usuario tiene la doble marca persistente SIMULACION_QA; el usuario no
    // tiene ninguna consulta real previa; y el consentimiento durable es
    // valido. Errores de lectura del store: no autorizado (fail-closed).
    private async Task<bool> AutorizarClienteQaAsync(
        string modo, long idUsuario, long idSolicitud, CancellationToken cancellationToken)
    {
        if (!string.Equals(modo, MiDecisorQaGateConfig.ModoClienteQaFullFlow, StringComparison.Ordinal))
        {
            logger.LogWarning("midecisor.qagate: modo QA desconocido; no autorizado.");
            return false;
        }

        if (!MiDecisorQaGateConfig.StopAfterConsultaInactivoExplicito(configuration))
        {
            logger.LogWarning("midecisor.qagate: modo cliente QA exige STOP=false; no autorizado.");
            return false;
        }

        if (qaClienteStore is null)
        {
            logger.LogWarning("midecisor.qagate: store de cliente QA no disponible; no autorizado.");
            return false;
        }

        bool esClienteQa;
        int consultasPrevias;
        try
        {
            esClienteQa = await qaClienteStore
                .EsClienteQaPreparadoAsync(idUsuario, cancellationToken)
                .ConfigureAwait(false);
            consultasPrevias = esClienteQa
                ? await qaClienteStore
                    .ContarConsultasRealesAsync(idUsuario, cancellationToken)
                    .ConfigureAwait(false)
                : 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("midecisor.qagate: error leyendo marcas de cliente QA; no autorizado.");
            return false;
        }

        if (!esClienteQa)
        {
            logger.LogInformation("midecisor.qagate: el usuario no es cliente QA preparado; no autorizado.");
            return false;
        }

        if (consultasPrevias > 0)
        {
            logger.LogWarning("midecisor.qagate: el cliente QA ya tiene una consulta real; no autorizado.");
            return false;
        }

        return await autorizacionDurable
            .TieneAutorizacionVigenteAsync(idUsuario, idSolicitud, cancellationToken)
            .ConfigureAwait(false);
    }
}

// XPAY-656 — claves e interpretación ESTRICTA de la configuración QA para la
// primera consulta. La comparten el gate y el orquestador (stop).
public static class MiDecisorQaGateConfig
{
    public const string EnvAllowedSolicitudId = "MIDECISOR_QA_ALLOWED_SOLICITUD_ID";
    public const string EnvStopAfterConsulta  = "MIDECISOR_STOP_AFTER_CONSULTA";

    // Único valor que activa el stop. Comparación ordinal exacta.
    public const string ValorStopActivo = "true";

    // XPAY-702 - modo del gate. Ausente o vacio: modo heredado (allowlist por
    // solicitud + STOP "true"). "QA_CLIENT_FULL_FLOW": cliente QA con doble
    // marca SIMULACION_QA. Cualquier otro valor: no autorizado.
    public const string EnvGateMode           = "MIDECISOR_QA_GATE_MODE";
    public const string ModoClienteQaFullFlow = "QA_CLIENT_FULL_FLOW";

    // En QA_CLIENT_FULL_FLOW el STOP debe valer exactamente "false".
    public const string ValorStopInactivo = "false";

    public static bool StopAfterConsultaInactivoExplicito(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var raw = configuration[EnvStopAfterConsulta];
        return string.Equals(raw, ValorStopInactivo, StringComparison.Ordinal);
    }

    // Exactamente UN id: sólo dígitos ASCII (NumberStyles.None: sin signo,
    // espacios, separadores de miles ni listas) y mayor que cero.
    // Ausente, vacío o inválido → false.
    public static bool TryLeerSolicitudPermitida(IConfiguration configuration, out long idSolicitud)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        idSolicitud = 0;

        var raw = configuration[EnvAllowedSolicitudId];
        if (string.IsNullOrEmpty(raw))
            return false;

        if (!long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var valor) || valor <= 0)
            return false;

        idSolicitud = valor;
        return true;
    }

    // Activo SÓLO si el valor es exactamente "true" (ordinal). "True", "TRUE",
    // " true", "1", "yes", "on", "false", vacío o ausente → inactivo.
    public static bool StopAfterConsultaActivo(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return string.Equals(configuration[EnvStopAfterConsulta], ValorStopActivo, StringComparison.Ordinal);
    }
}
