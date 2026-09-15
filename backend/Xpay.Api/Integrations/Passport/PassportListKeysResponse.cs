using System.Text.Json.Serialization;

namespace Xpay.Api.Integrations.Passport;

// XPAY-328 — respuesta de List Bre-B Keys (GET /v1/keys), contrato
// confirmado en la investigación XPAY-327 vía
// docs.passportfintech.com/EN/list-keys: colección de objetos con
// EXACTAMENTE el mismo shape ya modelado por PassportKeyResponse
// (status/key{key_type,key_value}/account_id/created_at/updated_at/id) —
// se reutiliza ese DTO para los elementos en vez de duplicarlo.
//
// IMPORTANTE — semántica de `id`: en este endpoint (familia de recursos
// /v1/keys, igual que Create/Suspend/Activate/Delete Key), `id` es el
// key_id de la llave — NO debe confundirse con
// PassportResolveKeyResponse.Id, que es el resolution_id de un recurso
// completamente distinto (POST /v1/resolve-key, confirmado contractualmente
// en XPAY-310/324). Ver PassportKeyResponse.cs para la nota equivalente.
//
// `Keys` nunca es null: si Passport no devuelve el campo "keys" (o lo
// devuelve como [] ), el shape esperado sigue siendo una lista — nunca se
// exige la presencia del campo como se hace con `id` en Create/Suspend/
// Resolve (ahí la ausencia es un fallo de protocolo; aquí una lista vacía
// es un resultado legítimo: "no hay ninguna llave que coincida con el
// filtro", no un error).
//
// `PaginationInfo` se modela de forma mínima y completamente defensiva
// (TRANSPORTE DEFENSIVO, mismo criterio que el resto de esta integración):
// XPAY-328 no implementa navegación de páginas — sólo se conserva por si
// una fase futura la necesita, sin bloquear la deserialización del caso
// principal (filtro exacto que devuelve 0 o 1 elemento).
public sealed class PassportListKeysResponse
{
    [JsonPropertyName("keys")]
    public List<PassportKeyResponse> Keys { get; set; } = new();

    [JsonPropertyName("pagination_info")]
    public PassportListKeysPaginationInfo? PaginationInfo { get; set; }
}

// Objeto `pagination_info` anidado de la respuesta — todas las propiedades
// nullable, ninguna se exige.
public sealed class PassportListKeysPaginationInfo
{
    [JsonPropertyName("first_request_timestamp")]
    public string? FirstRequestTimestamp { get; set; }

    [JsonPropertyName("current_page")]
    public int? CurrentPage { get; set; }

    [JsonPropertyName("total_pages")]
    public int? TotalPages { get; set; }

    [JsonPropertyName("total_elements")]
    public int? TotalElements { get; set; }
}
