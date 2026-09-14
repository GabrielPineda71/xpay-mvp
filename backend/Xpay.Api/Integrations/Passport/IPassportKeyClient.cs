namespace Xpay.Api.Integrations.Passport;

// XPAY-287 — contrato tipado para Create Bre-B Key, confirmado
// documentalmente en XPAY-286. Construido SOBRE IPassportHttpClient
// (XPAY-272) — no introduce un segundo stack de HTTP/OAuth.
//
// NO implementa: resolve-key, QR, payments/breb, webhooks — eso queda para
// fases posteriores sobre esta misma base.
//
// NO persiste el key_id remoto ni ningún status remoto — cada método
// devuelve el DTO de respuesta completo (cuando aplica); dónde y cómo
// persistirlo queda diferido (mismo criterio ya aplicado a customer_id/
// account_id en XPAY-277/278, y explícitamente NO resuelto para el status
// remoto de la llave en XPAY-292/293 — ver PassportKeyClient.cs).
public interface IPassportKeyClient
{
    // POST /v1/keys
    Task<PassportKeyResponse> CreateKeyAsync(
        PassportCreateKeyRequest request, CancellationToken cancellationToken = default);

    // PATCH /v1/keys/{key_id}/suspend — sin request body (contrato confirmado
    // en XPAY-292). keyId es el campo `id` devuelto por CreateKeyAsync.
    Task<PassportKeyResponse> SuspendKeyAsync(
        string keyId, CancellationToken cancellationToken = default);

    // PATCH /v1/keys/{key_id}/activate — Passport nombra esta operación
    // "Activate Key" (el anexo de certificación la llama "Reactivate");
    // sin request body (contrato confirmado en XPAY-292).
    Task<PassportKeyResponse> ActivateKeyAsync(
        string keyId, CancellationToken cancellationToken = default);

    // DELETE /v1/keys/{key_id} — eliminación física/irreversible según
    // documentación (XPAY-292); 204 No Content, sin response body.
    Task DeleteKeyAsync(
        string keyId, CancellationToken cancellationToken = default);
}
