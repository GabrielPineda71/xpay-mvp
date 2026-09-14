namespace Xpay.Api.Integrations.Passport;

// XPAY-287 — contrato tipado para Create Bre-B Key, confirmado
// documentalmente en XPAY-286. Construido SOBRE IPassportHttpClient
// (XPAY-272) — no introduce un segundo stack de HTTP/OAuth.
//
// NO implementa: resolve-key, suspend/reactivate/delete, QR, payments/breb,
// webhooks — eso queda para fases posteriores sobre esta misma base.
//
// NO persiste el key_id remoto — el método devuelve el DTO de respuesta
// completo; dónde y cómo persistirlo queda diferido (mismo criterio ya
// aplicado a customer_id/account_id en XPAY-277/278).
public interface IPassportKeyClient
{
    // POST /v1/keys
    Task<PassportKeyResponse> CreateKeyAsync(
        PassportCreateKeyRequest request, CancellationToken cancellationToken = default);
}
