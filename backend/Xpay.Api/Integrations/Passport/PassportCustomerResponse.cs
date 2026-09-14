using System.Text.Json.Serialization;

namespace Xpay.Api.Integrations.Passport;

// XPAY-279 — respuesta de Link Merchant (POST /v1/customers/business/link) y
// de Retrieve Customer (GET /v1/customers/:customer_id) — misma forma
// documentada para ambos endpoints (ver XPAY-278).
//
// `Id` es el customer_id de Passport (campo `id` de la respuesta, documentado
// explícitamente como el identificador a usar en otros endpoints).
//
// TRANSPORTE DEFENSIVO — todas las propiedades nullable: campos adicionales
// desconocidos del proveedor no rompen la deserialización (System.Text.Json
// ignora propiedades JSON no mapeadas por defecto); campos documentados
// ausentes tampoco producen una excepción de deserialización, sólo quedan
// en null (mismo criterio que MiDecisorTokenResponse/PassportOAuthTokenResponse).
public sealed class PassportCustomerResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("business_name")]
    public string? BusinessName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("mobile_phone_number")]
    public string? MobilePhoneNumber { get; set; }

    [JsonPropertyName("identification_type")]
    public string? IdentificationType { get; set; }

    [JsonPropertyName("identification_number")]
    public string? IdentificationNumber { get; set; }

    [JsonPropertyName("merchant_category_code")]
    public string? MerchantCategoryCode { get; set; }

    [JsonPropertyName("address")]
    public PassportMerchantAddressResponse? Address { get; set; }

    // RAW string — formato de fecha no confirmado contractualmente; se
    // conserva verbatim (mismo criterio que PassportOAuthTokenResponse.CreatedAt).
    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }
}

// Objeto `address` de la respuesta — todos los campos nullable (transporte
// defensivo, igual que el resto de esta clase).
public sealed class PassportMerchantAddressResponse
{
    [JsonPropertyName("line_1")]
    public string? Line1 { get; set; }

    [JsonPropertyName("line_2")]
    public string? Line2 { get; set; }

    [JsonPropertyName("line_3")]
    public string? Line3 { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("post_code")]
    public string? PostCode { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }
}
