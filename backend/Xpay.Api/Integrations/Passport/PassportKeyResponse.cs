using System.Text.Json.Serialization;

namespace Xpay.Api.Integrations.Passport;

// XPAY-287 — respuesta de Create Bre-B Key (POST /v1/keys), contrato
// canónico confirmado en XPAY-286 vía docs.passportfintech.com/ES/create-keys.
//
// `Id` es el key_id de Passport (campo `id` de la respuesta). NO se incluye
// `customer_id` en este DTO: la página canónica actual de Create Key NO lo
// confirma en su ejemplo de response (a diferencia de Link Account, donde
// customer_id sí está documentado explícitamente) — no se inventa por
// conveniencia (XPAY-286/287).
//
// `Key.KeyType` se modela como string (no como PassportKeyType) del lado de
// la respuesta — TRANSPORTE DEFENSIVO, mismo criterio que el resto de los
// DTOs de respuesta Passport: un valor inesperado del proveedor en este
// campo no debe romper la deserialización de toda la respuesta (a
// diferencia del request, que XPAY controla y por tanto sí tipa estrictamente).
//
// TRANSPORTE DEFENSIVO — todas las propiedades nullable: campos adicionales
// desconocidos no rompen la deserialización; campos documentados ausentes
// quedan en null.
public sealed class PassportKeyResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("key")]
    public PassportKeyResponseDetail? Key { get; set; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("account_id")]
    public string? AccountId { get; set; }

    // RAW string — formato de fecha no confirmado contractualmente más allá
    // del ejemplo ISO 8601 mostrado; se conserva verbatim (mismo criterio
    // que el resto de los CreatedAt/UpdatedAt en esta integración).
    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }
}

// Objeto `key` anidado de la respuesta.
public sealed class PassportKeyResponseDetail
{
    [JsonPropertyName("key_type")]
    public string? KeyType { get; set; }

    [JsonPropertyName("key_value")]
    public string? KeyValue { get; set; }
}
