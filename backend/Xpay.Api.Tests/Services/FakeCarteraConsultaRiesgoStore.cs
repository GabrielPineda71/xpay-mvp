using Xpay.Api.Common;
using Xpay.Api.Services;

namespace Xpay.Api.Tests.Services;

// Fake de ICarteraConsultaRiesgoStore para los tests de M2.3a/b1. Simula el
// pre-flight, la transición guardada TX-A (ganada / perdida), la marca de fase
// ENVIO_INCIERTO y la persistencia TX-B, sin EF ni SQL. Los tests configuran el
// contexto, el resultado de la transición y excepciones inyectadas por
// operación, y verifican los efectos (incluida la fase durable simulada del
// intento numero_intento = 1).
internal sealed class FakeCarteraConsultaRiesgoStore : ICarteraConsultaRiesgoStore
{
    public ConsultaRiesgoContexto? Contexto { get; set; }
    public bool GanaTransicion { get; set; } = true;

    public Exception? CargarThrows { get; set; }
    public Exception? IniciarThrows { get; set; }
    public Exception? MarcarThrows { get; set; }
    public Exception? FinalizarThrows { get; set; }

    public int CargarCalls { get; private set; }
    public int IniciarCalls { get; private set; }
    public int MarcarCalls { get; private set; }
    public int FinalizarCalls { get; private set; }

    // Fase durable simulada del intento numero_intento = 1.
    public string FaseIntento { get; private set; } = CarteraIntentoFases.PreCall;

    // Fase con la que TX-B fue invocada (para asserts de orden).
    public string? FaseAlFinalizar { get; private set; }

    public ResultadoIntentoDurable? UltimoOutcome { get; private set; }
    // true sólo si FinalizarIntentoAsync llegó a "commit" (no lanzó guard/error).
    public bool FinalizoConExito { get; private set; }
    public bool FinalizarRecibioTokenCancelable { get; private set; }

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

    public Task MarcarEnvioInciertoAsync(
        long idSolicitud, long idUsuario, DateTime fechaUtc, CancellationToken cancellationToken)
    {
        MarcarCalls++;
        if (MarcarThrows is not null) throw MarcarThrows;
        FaseIntento = CarteraIntentoFases.EnvioIncierto;
        return Task.CompletedTask;
    }

    public Task FinalizarIntentoAsync(
        long idSolicitud, long idUsuario, ResultadoIntentoDurable outcome, CancellationToken cancellationToken)
    {
        FinalizarCalls++;
        FaseAlFinalizar = FaseIntento;
        UltimoOutcome = outcome;
        FinalizarRecibioTokenCancelable = cancellationToken.CanBeCanceled;
        if (FinalizarThrows is not null) throw FinalizarThrows;
        FaseIntento = CarteraIntentoFases.Finalizado;
        FinalizoConExito = true;
        return Task.CompletedTask;
    }
}
