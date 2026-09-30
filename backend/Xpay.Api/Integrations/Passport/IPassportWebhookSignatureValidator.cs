namespace Xpay.Api.Integrations.Passport;

// XPAY-594 — verificación de la firma "X-Passport-Signature-V2".
public enum PassportWebhookSignatureOutcome
{
    Valid,
    SecretNotConfigured,
    SignatureMissing,
    SignatureInvalid,
    TimestampMissing,
    TimestampUnparseable,
    TimestampOutOfWindow,
}

/// <param name="SignatureValid">
/// Verdadero sólo si el HMAC calculado coincide y el timestamp está en ventana.
/// Es el valor que se persiste en <c>passport_webhook_events.signature_valid</c>.
/// </param>
/// <param name="ShouldReject">
/// Verdadero si la entrega debe responderse con 401. En ENFORCE equivale a
/// "no válida"; en OBSERVE es siempre falso.
/// </param>
public sealed record PassportWebhookSignatureResult(
    PassportWebhookSignatureOutcome Outcome,
    bool SignatureValid,
    bool ShouldReject);

public interface IPassportWebhookSignatureValidator
{
    /// <summary>
    /// Verifica la firma de una entrega de webhook.
    /// </summary>
    /// <param name="rawBody">Cuerpo crudo recibido, usado sólo para re-serializar canónicamente.</param>
    /// <param name="signatureHeader">Valor de <c>X-Passport-Signature-V2</c>.</param>
    /// <param name="timestampHeader">Valor de <c>X-Passport-Timestamp</c>.</param>
    PassportWebhookSignatureResult Validate(string rawBody, string? signatureHeader, string? timestampHeader);
}
