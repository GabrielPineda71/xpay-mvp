using Xpay.Api.Common;

namespace Xpay.Api.Integrations.MiDecisor;

// XPAY-717 — regla de negocio de reconsulta MiDecisor en ambiente productivo.
//
// 1. Un titular documental que ya obtuvo RESPUESTA del proveedor no puede
//    generar otra consulta real durante 6 MESES CALENDARIO contados desde
//    fecha_fin (UTC) de la consulta que produjo esa respuesta:
//        limite = fechaFinUtc.AddMonths(6)
//        ahoraUtc <  limite → bloqueado
//        ahoraUtc >= limite → puede continuar (sujeto a las demás barreras)
//    DateTime.AddMonths conserva la hora y, si el día no existe en el mes
//    destino, usa el último día válido (31-ago → 28/29-feb). La comparación se
//    hace SIEMPRE hacia adelante desde la respuesta: la forma inversa sobre
//    "ahora" no es equivalente en finales de mes.
// 2. Prueban respuesta: ACEPTADA, SIN_INFORMACION, RECHAZADA_PROVEEDOR. No la
//    prueban ERROR_PROTOCOLO, RESULTADO_INCIERTO ni los errores locales.
// 3. Un cupo ordinario ACTIVO o SUSPENDIDO del mismo titular bloquea; CANCELADO
//    no bloquea por sí solo.
// Regla pura: sin DB, configuración, host ni reloj (recibe ahoraUtc).
public static class ReglaReconsultaMiDecisor
{
    public const int MesesVentana = 6;

    public static readonly IReadOnlyList<string> ResultadosConRespuesta = new[]
    {
        CarteraConsultaRiesgoResultados.Aceptada,
        CarteraConsultaRiesgoResultados.SinInformacion,
        CarteraConsultaRiesgoResultados.RechazadaProveedor,
    };

    public static readonly IReadOnlyList<string> EstadosCupoQueBloquean = new[] { "ACTIVO", "SUSPENDIDO" };

    public static DateTime Limite(DateTime ultimaRespuestaUtc) => ultimaRespuestaUtc.AddMonths(MesesVentana);

    public static bool Permite(RiesgoProduccionSnapshot snapshot, DateTime ahoraUtc)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.TieneCupoQueBloquea)
            return false;

        return snapshot.UltimaRespuestaUtc is not { } ultima
            || ahoraUtc >= Limite(ultima);
    }
}
