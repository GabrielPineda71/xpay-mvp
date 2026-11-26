using Xpay.Api.Integrations.MiDecisor;

namespace Xpay.Api.Common;

// La bandera productiva no abre consultas a todo usuario autenticado. La
// pertenencia y los requisitos actuales del grupo limitado se releen antes
// de la autorización durable y de las reglas de recencia del proveedor.
public sealed class AutorizacionConsultaRiesgoPiloto(
    IConsultaRiesgoAutorizacion autorizacionInterna,
    IConfiguration config,
    Func<long, CancellationToken, Task<bool>> elegible,
    ILogger<AutorizacionConsultaRiesgoPiloto> logger) : IConsultaRiesgoAutorizacion
{
    internal IConsultaRiesgoAutorizacion AutorizacionInterna => autorizacionInterna;

    public async Task<bool> TieneAutorizacionVigenteAsync(long idUsuario, long idSolicitud, CancellationToken cancellationToken = default)
    {
        if (idUsuario <= 0 || idSolicitud <= 0 || !MiDecisorProduccionConfig.Habilitado(config)) return false;
        try
        {
            if (!await elegible(idUsuario, cancellationToken).ConfigureAwait(false)) return false;
            return await autorizacionInterna.TieneAutorizacionVigenteAsync(idUsuario, idSolicitud, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("midecisor.piloto: no se pudo comprobar la habilitación del titular.");
            return false;
        }
    }
}
