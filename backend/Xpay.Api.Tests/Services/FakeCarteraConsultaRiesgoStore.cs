using Xpay.Api.Services;

namespace Xpay.Api.Tests.Services;

// Fake de ICarteraConsultaRiesgoStore para los tests de M2.3a. Simula el
// pre-flight, la transición guardada TX-A (ganada / perdida) y la persistencia
// TX-B, sin EF ni SQL. Los tests configuran el contexto, el resultado de la
// transición y excepciones inyectadas por operación, y verifican los efectos.
internal sealed class FakeCarteraConsultaRiesgoStore : ICarteraConsultaRiesgoStore
{
    public ConsultaRiesgoContexto? Contexto { get; set; }
    public bool GanaTransicion { get; set; } = true;

    public Exception? CargarThrows { get; set; }
    public Exception? IniciarThrows { get; set; }
    public Exception? CompletarThrows { get; set; }

    public int CargarCalls { get; private set; }
    public int IniciarCalls { get; private set; }
    public int CompletarCalls { get; private set; }

    public ResultadoIntentoDurable? UltimoOutcome { get; private set; }
    public bool CompletarRecibioTokenCancelable { get; private set; }

    public Task<ConsultaRiesgoContexto?> CargarContextoAsync(
        long idSolicitud, long idUsuario, CancellationToken cancellationToken)
    {
        CargarCalls++;
        if (CargarThrows is not null) throw CargarThrows;
        // Ownership: la implementación real devuelve null si la solicitud no
        // pertenece a idUsuario (sin revelar existencia).
        if (Contexto is not null && Contexto.IdUsuario != idUsuario)
            return Task.FromResult<ConsultaRiesgoContexto?>(null);
        return Task.FromResult(Contexto);
    }

    public Task<bool> IntentarIniciarConsultaAsync(
        long idSolicitud, long idUsuario, DateTime fechaUtc, CancellationToken cancellationToken)
    {
        IniciarCalls++;
        if (IniciarThrows is not null) throw IniciarThrows;
        return Task.FromResult(GanaTransicion);
    }

    public Task CompletarIntentoAsync(
        long idSolicitud, ResultadoIntentoDurable outcome, CancellationToken cancellationToken)
    {
        CompletarCalls++;
        UltimoOutcome = outcome;
        CompletarRecibioTokenCancelable = cancellationToken.CanBeCanceled;
        if (CompletarThrows is not null) throw CompletarThrows;
        return Task.CompletedTask;
    }
}
