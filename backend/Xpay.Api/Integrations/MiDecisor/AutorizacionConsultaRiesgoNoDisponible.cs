namespace Xpay.Api.Integrations.MiDecisor;

// M2.3a — implementación runtime FAIL-CLOSED de IConsultaRiesgoAutorizacion.
//
// Devuelve SIEMPRE false. No lee configuración, no lee entorno, no tiene
// modo desarrollo/QA/bypass. Es una de las dos barreras independientes que
// impiden que M2.3a contacte al proveedor real (la otra: no existe endpoint,
// scheduler ni flujo que invoque el orquestador).
//
// Para habilitar consultas reales, un checkpoint posterior debe REEMPLAZAR
// este registro en Program.cs por una implementación respaldada por un
// almacén de consentimiento — un cambio de código revisado, nunca un toggle.
public sealed class AutorizacionConsultaRiesgoNoDisponible : IConsultaRiesgoAutorizacion
{
    public Task<bool> TieneAutorizacionVigenteAsync(
        long idUsuario,
        long idSolicitud,
        CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}
