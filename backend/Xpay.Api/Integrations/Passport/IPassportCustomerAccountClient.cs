namespace Xpay.Api.Integrations.Passport;

// XPAY-279 — contrato tipado para las 4 operaciones Customer/Account de
// Passport confirmadas documentalmente en XPAY-278 (Link Merchant, Retrieve
// Customer, Link Account, Retrieve Account). Construido SOBRE la
// abstracción HTTP base (IPassportHttpClient, XPAY-272) — no introduce un
// segundo stack de HTTP/OAuth.
//
// NO implementa: resolve-key, Bre-B keys, QR, payments/breb, webhooks — eso
// queda para fases posteriores sobre esta misma base.
//
// NO persiste customer_id/account_id — cada método devuelve el DTO de
// respuesta completo; la decisión de dónde y cómo persistir esos IDs queda
// explícitamente diferida (ver XPAY-277 Auditoría 7 / XPAY-278 Fase 6).
public interface IPassportCustomerAccountClient
{
    // POST /v1/customers/business/link
    Task<PassportCustomerResponse> LinkMerchantAsync(
        PassportLinkMerchantRequest request, CancellationToken cancellationToken = default);

    // GET /v1/customers/{customer_id}
    Task<PassportCustomerResponse> RetrieveCustomerAsync(
        string customerId, CancellationToken cancellationToken = default);

    // POST /v1/accounts/link
    Task<PassportAccountResponse> LinkAccountAsync(
        PassportLinkAccountRequest request, CancellationToken cancellationToken = default);

    // GET /v1/accounts/{account_id}
    Task<PassportAccountResponse> RetrieveAccountAsync(
        string accountId, CancellationToken cancellationToken = default);
}
