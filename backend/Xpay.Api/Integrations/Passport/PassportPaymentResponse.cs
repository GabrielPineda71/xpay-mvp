using System.Text.Json.Serialization;

namespace Xpay.Api.Integrations.Passport;

// XPAY-373 — respuesta de POST /v1/payments/breb (creación) y de
// GET /v1/payments/{payment_id} (consulta) — misma forma documentada para
// ambos endpoints (confirmado vía docs.passportfintech.com/EN/
// initiate-a-payment y /EN/retrieve-payment, XPAY-372/373).
//
// DTO MÍNIMO NECESARIO (XPAY-373 FASE 2): se modelan únicamente los campos
// que el código productivo realmente consume (id/status/account_id/
// resolution_id/amount/error) — sender/receiver/direction/
// end_to_end_identification/created_at/updated_at están documentados pero
// XPAY no los usa todavía; System.Text.Json ignora propiedades JSON no
// mapeadas por diseño (no se agregan sólo para "completitud").
//
// `Status` se modela como string, NUNCA como enum estricto — XPAY-372/373
// reconfirmó PENDING/PROCESSING/SETTLED/REJECTED documentalmente, pero un
// valor no documentado (o uno nuevo agregado por Passport en el futuro)
// nunca debe romper la deserialización — ver BrebPaymentStateMachine, que
// clasifica cualquier string no reconocido como DESCONOCIDO (tratado igual
// que TRANSITORIO: nunca mueve dinero).
//
// TRANSPORTE DEFENSIVO — todas las propiedades nullable, mismo criterio
// que PassportAccountResponse/PassportResolveKeyResponse.
public sealed class PassportPaymentResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("account_id")]
    public string? AccountId { get; set; }

    [JsonPropertyName("resolution_id")]
    public string? ResolutionId { get; set; }

    [JsonPropertyName("amount")]
    public PassportPaymentAmountResponse? Amount { get; set; }

    [JsonPropertyName("error")]
    public PassportPaymentErrorResponse? Error { get; set; }
}

public sealed class PassportPaymentAmountResponse
{
    [JsonPropertyName("value")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? Value { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }
}

// Confirmado en el ejemplo oficial de Retrieve Payment (status REJECTED):
// error.error_code/error.error_description ambos presentes como el mismo
// código corto (p.ej. "B002") — nunca texto libre extenso observado; aun
// así, nunca se loguea este objeto completo (ver BrebPaymentService).
public sealed class PassportPaymentErrorResponse
{
    [JsonPropertyName("error_code")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}
