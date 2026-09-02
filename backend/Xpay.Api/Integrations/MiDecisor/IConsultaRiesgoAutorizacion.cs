namespace Xpay.Api.Integrations.MiDecisor;

// M2.3a — abstracción de "¿el titular autorizó una consulta a central de
// riesgo para esta solicitud?".
//
// El orquestador (CarteraConsultaRiesgoService) consulta esta interfaz en el
// pre-flight, ANTES de cualquier transición durable y ANTES de cualquier
// llamada al proveedor. Si devuelve false, la solicitud permanece RECIBIDA y
// no se contacta a MiDecisor.
//
// M2.3a NO implementa la captura de consentimiento (texto legal versionado,
// timestamp, revocación, etc.): eso es un checkpoint aparte con revisión
// legal (Ley 1266 / Ley 1581). La implementación runtime de M2.3a
// (AutorizacionConsultaRiesgoNoDisponible) devuelve SIEMPRE false — habilitar
// consultas reales exige reemplazar el registro DI por una implementación
// respaldada por un almacén de consentimiento, no un flag de configuración.
public interface IConsultaRiesgoAutorizacion
{
    Task<bool> TieneAutorizacionVigenteAsync(
        long idUsuario,
        long idSolicitud,
        CancellationToken cancellationToken = default);
}
