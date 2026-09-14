using System.Text.Json.Serialization;

namespace Xpay.Api.Integrations.Passport;

// XPAY-279 — body de POST /v1/customers/business/link (contrato confirmado
// vía documentación oficial docs.passportfintech.com, ver XPAY-278).
//
// `type` e `identification_type` son valores contractuales FIJOS documentados
// (únicos valores soportados: "BUSINESS" / "NIT") — se fijan como default
// init-only, mismo patrón que GrantType en PassportOAuthTokenRequest, para
// que el caller no pueda enviar por error un valor distinto al contrato.
//
// Nunca se loguea una instancia de este record (contiene datos de identificación).
public sealed record PassportLinkMerchantRequest(
    [property: JsonPropertyName("business_name")]           string BusinessName,
    [property: JsonPropertyName("email")]                   string Email,
    [property: JsonPropertyName("mobile_phone_number")]     string MobilePhoneNumber,
    [property: JsonPropertyName("identification_number")]   string IdentificationNumber,
    [property: JsonPropertyName("merchant_category_code")]  string MerchantCategoryCode,
    [property: JsonPropertyName("address")]                 PassportMerchantAddressRequest Address)
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "BUSINESS";

    [JsonPropertyName("identification_type")]
    public string IdentificationType { get; init; } = "NIT";
}

// Objeto `address` del body de Link Merchant. line_2/line_3 son OPCIONALES
// según el contrato documentado — se omiten del JSON serializado cuando son
// null (WhenWritingNull), en vez de enviarse como `null` explícito.
public sealed record PassportMerchantAddressRequest(
    [property: JsonPropertyName("line_1")]    string Line1,
    [property: JsonPropertyName("city")]      string City,
    [property: JsonPropertyName("state")]     string State,
    [property: JsonPropertyName("post_code")] string PostCode,
    [property: JsonPropertyName("country")]   string Country)
{
    [JsonPropertyName("line_2")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Line2 { get; init; }

    [JsonPropertyName("line_3")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Line3 { get; init; }
}
