namespace Xpay.Api.Integrations.Passport;

// XPAY-298 — contrato tipado para Create QR Code (STATIC y DYNAMIC),
// confirmado documentalmente en XPAY-297/298. Construido SOBRE
// IPassportHttpClient (XPAY-272) — no introduce un segundo stack de
// HTTP/OAuth.
//
// NO implementa: Retrieve QR, List QR, Delete QR, payments/breb,
// webhooks — eso queda para fases posteriores sobre esta misma base.
//
// NO persiste el qr id remoto — el método devuelve el DTO de respuesta
// completo; dónde y cómo persistirlo queda diferido (mismo criterio ya
// aplicado a customer_id/account_id/key_id en fases anteriores).
//
// XPAY-305 — agrega Decode QR Code (POST /v1/qrcodes/decode), contrato
// confirmado en XPAY-304. Reutiliza íntegramente la misma IPassportHttpClient
// sin ningún cambio a la base HTTP/OAuth.
public interface IPassportQrClient
{
    // POST /v1/qrcodes
    Task<PassportQrCodeResponse> CreateQrCodeAsync(
        PassportCreateQrCodeRequest request, CancellationToken cancellationToken = default);

    // POST /v1/qrcodes/decode
    Task<PassportDecodeQrCodeResponse> DecodeQrCodeAsync(
        PassportDecodeQrCodeRequest request, CancellationToken cancellationToken = default);
}
