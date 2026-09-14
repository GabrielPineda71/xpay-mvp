using System.Text.Json.Serialization;

namespace Xpay.Api.Integrations.Passport;

// XPAY-279 — body de POST /v1/accounts/link (contrato confirmado vía
// documentación oficial docs.passportfintech.com, ver XPAY-278).
//
// `account_type` es un valor contractual FIJO documentado (único valor
// soportado: "ORDINARY") — se fija como default init-only, mismo patrón que
// `Type`/`IdentificationType` en PassportLinkMerchantRequest.
//
// `CustomerId` debe ser un customer_id Passport ya existente (obtenido de una
// llamada previa a LinkMerchantAsync/RetrieveCustomerAsync) — esta clase NO
// valida su existencia; eso lo hace Passport (ver PassportAuthenticationException/
// PassportTransportException si Passport rechaza el valor).
public sealed record PassportLinkAccountRequest(
    [property: JsonPropertyName("customer_id")]    string CustomerId,
    [property: JsonPropertyName("account_number")] string AccountNumber)
{
    [JsonPropertyName("account_type")]
    public string AccountType { get; init; } = "ORDINARY";
}
