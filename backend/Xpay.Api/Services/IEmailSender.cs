namespace Xpay.Api.Services;

// P0-B — envío de email para verificación de cuenta y recuperación de
// contraseña. La abstracción no conoce proveedor ni credenciales. El cuerpo
// puede contener un enlace con token de un solo uso: ninguna implementación
// debe registrarlo en logs.
public interface IEmailSender
{
    Task EnviarAsync(MensajeEmail mensaje, CancellationToken ct = default);
}

// Direcciones previstas para producción. Todavía no hay proveedor configurado.
public static class EmailRemitentes
{
    public const string NoReply = "no-reply@xpaycol.com";
    public const string Soporte = "xpay@xpaycol.com";
}

// ToString() sobrescrito: un log accidental del mensaje nunca muestra el
// destinatario completo ni el cuerpo (que puede llevar el enlace con token).
public sealed record MensajeEmail(string Destinatario, string Asunto, string CuerpoTexto)
{
    public override string ToString() =>
        $"MensajeEmail {{ Destinatario = {EnmascararDestinatario(Destinatario)}, Asunto = {Asunto}, CuerpoTexto = *** }}";

    internal static string EnmascararDestinatario(string? destinatario)
    {
        var arroba = destinatario?.LastIndexOf('@') ?? -1;
        return arroba < 0 ? "***" : "***" + destinatario![arroba..];
    }
}
