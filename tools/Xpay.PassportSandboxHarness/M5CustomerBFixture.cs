using System.Text.Json.Serialization;

namespace Xpay.PassportSandboxHarness;

// XPAY-531 — forma del archivo de fixture PRIVADO (fuera del repo) que
// CreateM5CustomerBExecutor lee para construir un PassportLinkMerchantRequest
// real. Mismo principio que PASSPORT_TEST_QR_DECODE_DATA_FILE (M4-T5): UNA
// sola variable de entorno apunta a un archivo JSON local, nunca versionado,
// en vez de una decena de variables de entorno sueltas con datos de
// identificación. Nada de este archivo, ni su ruta, ni su contenido se
// hardcodea en el repositorio — XPAY-531 §7 lo prohíbe explícitamente.
public sealed record M5CustomerBFixture(
    [property: JsonPropertyName("business_name")]          string? BusinessName,
    [property: JsonPropertyName("email")]                  string? Email,
    [property: JsonPropertyName("mobile_phone_number")]    string? MobilePhoneNumber,
    [property: JsonPropertyName("identification_number")]  string? IdentificationNumber,
    [property: JsonPropertyName("merchant_category_code")] string? MerchantCategoryCode,
    [property: JsonPropertyName("address")]                M5CustomerBFixtureAddress? Address);

public sealed record M5CustomerBFixtureAddress(
    [property: JsonPropertyName("line_1")]    string? Line1,
    [property: JsonPropertyName("city")]      string? City,
    [property: JsonPropertyName("state")]     string? State,
    [property: JsonPropertyName("post_code")] string? PostCode,
    [property: JsonPropertyName("country")]   string? Country,
    [property: JsonPropertyName("line_2")]    string? Line2 = null,
    [property: JsonPropertyName("line_3")]    string? Line3 = null);
