using Xpay.Api.Integrations.MiDecisor;

namespace Xpay.Api.Tests.Services;

// Fake de IConsultaRiesgoAutorizacion para los tests de M2.3a: autoriza o no
// según se construya, y cuenta invocaciones.
internal sealed class FakeConsultaRiesgoAutorizacion : IConsultaRiesgoAutorizacion
{
    private readonly bool _autoriza;

    private int _callCount;
    public int CallCount => Volatile.Read(ref _callCount);

    public FakeConsultaRiesgoAutorizacion(bool autoriza) => _autoriza = autoriza;

    public Task<bool> TieneAutorizacionVigenteAsync(
        long idUsuario, long idSolicitud, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult(_autoriza);
    }
}
