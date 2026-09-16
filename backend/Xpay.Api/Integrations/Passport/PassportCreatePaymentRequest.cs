using System.Text.Json.Serialization;

namespace Xpay.Api.Integrations.Passport;

// XPAY-373 — body de POST /v1/payments/breb (contrato confirmado en
// XPAY-372/373 vía docs.passportfintech.com/EN/initiate-a-payment,
// consultado en solo lectura — nunca copiado literalmente, sólo su forma
// de campos). account_id/resolution_id/amount confirmados como
// requeridos; display_name confirmado opcional.
//
// account_id: SIEMPRE la cuenta OPERATIVA de XPAY (PassportOptions.
// EnvOperationalAccountId) — JAMÁS una cuenta de usuario individual (ver
// BrebPaymentRequestBuilder, que es el único lugar que construye este
// record, y nunca acepta account_id de un caller externo).
//
// Nunca se loguea una instancia completa de este record (resolution_id es
// un identificador correlacionable con el titular resuelto).
public sealed record PassportCreatePaymentRequest(
    [property: JsonPropertyName("account_id")]    string AccountId,
    [property: JsonPropertyName("resolution_id")] string ResolutionId,
    [property: JsonPropertyName("amount")]        PassportPaymentAmountRequest Amount)
{
    [JsonPropertyName("display_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; init; }
}

// Objeto `amount` anidado del request. currency confirmado documentalmente
// como enum de un solo valor posible ("COP") — Currency es un parámetro
// del constructor (no una constante hardcodeada en este DTO) para que la
// intención quede explícita en cada sitio de construcción, pero
// BrebPaymentRequestBuilder es el único lugar que la fija, siempre a "COP"
// (ver PassportPaymentClient.CopCurrency).
public sealed record PassportPaymentAmountRequest(
    [property: JsonPropertyName("value")]    string Value,
    [property: JsonPropertyName("currency")] string Currency);
