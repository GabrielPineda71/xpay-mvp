namespace Xpay.Api.Services;

// P0-B — ambientes productivos sin proveedor real aprobado: fail-closed.
// Cualquier intento de envío falla antes de tocar red; nunca simula éxito.
public sealed class EmailSenderNoConfigurado : IEmailSender
{
    public Task EnviarAsync(MensajeEmail mensaje, CancellationToken ct = default) =>
        Task.FromException(new InvalidOperationException(
            "No hay proveedor de email configurado para este ambiente."));
}
