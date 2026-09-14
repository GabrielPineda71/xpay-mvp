using System.Text.Json.Serialization;

namespace Xpay.Api.Integrations.Passport;

// XPAY-287 — body de POST /v1/keys (contrato canónico confirmado en
// XPAY-286 vía docs.passportfintech.com/ES/create-keys).
//
// key_type/key_value van ANIDADOS bajo un objeto "key" — confirmado por dos
// ejemplos JSON independientes (/EN/creating-breb-keys y /ES/create-keys);
// la tabla resumen de /ES/create-keys los lista de forma plana, pero eso es
// una imprecisión de presentación, no evidencia de una forma de wire
// alternativa (XPAY-286).
//
// display_name es OPCIONAL — confirmado explícitamente ("Requerido: No") —
// se omite del JSON cuando es null (mismo patrón que line_2/line_3 en
// PassportLinkMerchantRequest), no se envía como null explícito.
//
// Nunca se loguea una instancia de este record (contiene key_value, que
// puede ser PII según el key_type — teléfono, email, cédula).
public sealed record PassportCreateKeyRequest(
    [property: JsonPropertyName("account_id")] string AccountId,
    [property: JsonPropertyName("key")]         PassportKeyRequest Key)
{
    [JsonPropertyName("display_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; init; }
}

// Objeto `key` anidado del request.
public sealed record PassportKeyRequest(
    [property: JsonPropertyName("key_type")]  PassportKeyType KeyType,
    [property: JsonPropertyName("key_value")] string KeyValue);
