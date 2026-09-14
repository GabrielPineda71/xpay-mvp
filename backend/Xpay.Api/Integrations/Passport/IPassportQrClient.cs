namespace Xpay.Api.Integrations.Passport;

// XPAY-298 — contrato tipado para Create QR Code (STATIC y DYNAMIC),
// confirmado documentalmente en XPAY-297/298. Construido SOBRE
// IPassportHttpClient (XPAY-272) — no introduce un segundo stack de
// HTTP/OAuth.
//
// NO implementa: Decode QR, Retrieve QR, List QR, Delete QR, payments/breb,
// webhooks — eso queda para fases posteriores sobre esta misma base.
//
// NO persiste el qr id remoto — el método devuelve el DTO de respuesta
// completo; dónde y cómo persistirlo queda diferido (mismo criterio ya
// aplicado a customer_id/account_id/key_id en fases anteriores).
public interface IPassportQrClient
{
    // POST /v1/qrcodes
    Task<PassportQrCodeResponse> CreateQrCodeAsync(
        PassportCreateQrCodeRequest request, CancellationToken cancellationToken = default);
}
