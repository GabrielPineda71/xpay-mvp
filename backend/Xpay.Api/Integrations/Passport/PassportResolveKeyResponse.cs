using System.Text.Json.Serialization;

namespace Xpay.Api.Integrations.Passport;

// XPAY-324 — respuesta de Resolve Bre-B Key (POST /v1/resolve-key). Shape
// exactamente el confirmado en el diagnóstico previo (estructura real
// observada en Passport Sandbox) y consistente con el contrato documentado
// en docs.passportfintech.com/EN/resolve-key (XPAY-310). No se agregan
// campos adicionales no listados en la autorización.
//
// `Id` es el resolution_id: debe reutilizarse tal cual en el retry de un
// payment como idempotency key — confirmado en vivo (XPAY-310): "Make sure
// to re-use the resolution_id when you are re-trying a payment. This is
// crucial as we guarantee that only one payment will be created with the
// same resolution_id." Vigencia confirmada de 30 minutos
// (resolved_at → expires_at).
//
// `Key` reutiliza PassportKeyResponseDetail (ya existente en
// PassportKeyResponse.cs) — shape exacto (key_type/key_value, ambos
// string?) confirmado por coincidencia de campos, no por semejanza de
// nombre (mismo criterio ya aplicado en Decode QR, XPAY-305).
//
// TRANSPORTE DEFENSIVO — todas las propiedades nullable, mismo criterio que
// el resto de DTOs de respuesta de esta integración: status/type/
// identification_type nunca se modelan como enum estricto del lado
// respuesta.
public sealed class PassportResolveKeyResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("receptor_node")]
    public string? ReceptorNode { get; set; }

    [JsonPropertyName("resolved_at")]
    public string? ResolvedAt { get; set; }

    [JsonPropertyName("expires_at")]
    public string? ExpiresAt { get; set; }

    [JsonPropertyName("customer_id")]
    public string? CustomerId { get; set; }

    [JsonPropertyName("owner")]
    public PassportResolveKeyOwnerResponse? Owner { get; set; }

    [JsonPropertyName("key")]
    public PassportKeyResponseDetail? Key { get; set; }

    [JsonPropertyName("participant")]
    public PassportResolveKeyParticipantResponse? Participant { get; set; }

    [JsonPropertyName("account")]
    public PassportResolveKeyAccountResponse? Account { get; set; }
}

// Objeto `owner` anidado de la respuesta.
public sealed class PassportResolveKeyOwnerResponse
{
    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("second_name")]
    public string? SecondName { get; set; }

    [JsonPropertyName("first_last_name")]
    public string? FirstLastName { get; set; }

    [JsonPropertyName("second_last_name")]
    public string? SecondLastName { get; set; }

    [JsonPropertyName("business_name")]
    public string? BusinessName { get; set; }

    [JsonPropertyName("identification_type")]
    public string? IdentificationType { get; set; }

    [JsonPropertyName("identification_number")]
    public string? IdentificationNumber { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

// Objeto `participant` anidado de la respuesta.
public sealed class PassportResolveKeyParticipantResponse
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("identification_number")]
    public string? IdentificationNumber { get; set; }
}

// Objeto `account` anidado de la respuesta.
public sealed class PassportResolveKeyAccountResponse
{
    [JsonPropertyName("account_number")]
    public string? AccountNumber { get; set; }

    [JsonPropertyName("account_type")]
    public string? AccountType { get; set; }
}
