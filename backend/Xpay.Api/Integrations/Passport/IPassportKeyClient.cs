namespace Xpay.Api.Integrations.Passport;

// XPAY-287 — contrato tipado para Create Bre-B Key, confirmado
// documentalmente en XPAY-286. Construido SOBRE IPassportHttpClient
// (XPAY-272) — no introduce un segundo stack de HTTP/OAuth.
//
// NO implementa: QR, payments/breb, webhooks — eso queda para fases
// posteriores sobre esta misma base.
//
// NO persiste el key_id remoto ni ningún status remoto — cada método
// devuelve el DTO de respuesta completo (cuando aplica); dónde y cómo
// persistirlo queda diferido (mismo criterio ya aplicado a customer_id/
// account_id en XPAY-277/278, y explícitamente NO resuelto para el status
// remoto de la llave en XPAY-292/293 — ver PassportKeyClient.cs).
//
// XPAY-324 — agrega Resolve Key (POST /v1/resolve-key), contrato confirmado
// en XPAY-310 y en el diagnóstico previo. NO persiste el resolution_id
// remoto — el método devuelve el DTO de respuesta completo; su reutilización
// futura (p.ej. como idempotency key de un Payment) queda diferida a una
// fase posterior, mismo criterio que el resto de esta integración.
//
// XPAY-328 — agrega List Keys (GET /v1/keys), contrato confirmado en la
// investigación XPAY-327. Read-only, sin mutación de estado — pensado como
// mecanismo de recuperación determinística del key_id remoto de una llave
// ya existente (identificable de forma inequívoca por account_id+key_type+
// key_value), cuando ese key_id no fue persistido localmente. NO reemplaza
// Resolve Key (resolution_id, concepto distinto) ni introduce paginación —
// sólo el filtro exacto necesario para esa recuperación.
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

    // POST /v1/resolve-key — contrato confirmado en XPAY-310.
    Task<PassportResolveKeyResponse> ResolveKeyAsync(
        PassportResolveKeyRequest request, CancellationToken cancellationToken = default);

    // GET /v1/keys?account_id=&key_type=&key_value= — contrato confirmado
    // en XPAY-327/328. Filtro EXACTO por los tres campos simultáneamente
    // (no expone los demás filtros documentados — customer_id/key_id/status/
    // paginación/orden — por no ser necesarios para el caso de uso actual;
    // se agregarán en una fase posterior si hace falta, sin necesidad de
    // romper esta firma). keyType es PassportKeyType (mismo enum tipado que
    // Create/Resolve Key — "PHONE", nunca "MOBILE"; ver PassportKeyType.cs).
    Task<PassportListKeysResponse> ListKeysAsync(
        string accountId, PassportKeyType keyType, string keyValue, CancellationToken cancellationToken = default);
}
