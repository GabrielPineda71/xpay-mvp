namespace Xpay.Api.Services;

// P0-B — sender de Development/QA/Test: NO envía correo y NO abre red. Solo
// deja constancia de que el envío se omitió, con el destinatario enmascarado
// (solo dominio) y la longitud del cuerpo. Nunca registra el cuerpo, que
// puede contener el enlace con el token.
public sealed class EmailSenderLocalSinEnvio(ILogger<EmailSenderLocalSinEnvio> logger) : IEmailSender
{
    public Task EnviarAsync(MensajeEmail mensaje, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mensaje);
        ct.ThrowIfCancellationRequested();
        logger.LogInformation(
            "Email NO enviado (sender local sin proveedor): destinatario {Destinatario}, cuerpo de {Longitud} caracteres.",
            MensajeEmail.EnmascararDestinatario(mensaje.Destinatario),
            mensaje.CuerpoTexto?.Length ?? 0);
        return Task.CompletedTask;
    }
}
