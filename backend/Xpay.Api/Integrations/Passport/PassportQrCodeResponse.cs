using System.Text.Json.Serialization;

namespace Xpay.Api.Integrations.Passport;

// XPAY-298 — respuesta de Create QR Code (POST /v1/qrcodes), contrato
// confirmado en XPAY-297/298 vía docs.passportfintech.com/EN/create-qr-codes
// y /EN/qr-code-guide.
//
// `Id` es el qr id de Passport (campo `id` de la respuesta).
//
// `QrCodeImage` se incluye porque /EN/qr-code-guide confirma explícitamente
// el nombre exacto del campo y que forma parte de lo que el endpoint
// retorna ("Both return qr_code_data...and qr_code_image..."), aunque el
// ejemplo JSON truncado de /EN/create-qr-codes no lo mostraba explícitamente
// (ese ejemplo también truncaba merchant/vat/inc/additional_info con "...").
//
// XPAY-458 — inc/tip/qr_code_reference agregados a esta respuesta: Passport
// confirmó (Gustavo, 2026-09-21) un ejemplo de M4-T1 donde el request los
// incluye, y el contrato de Create QR Code es evidence-first — si el
// request los envía, la respuesta puede reflejarlos de vuelta. Mismo
// criterio defensivo que el resto del DTO (todo string/objeto anidado
// nullable; nunca se asume presencia).
//
// TRANSPORTE DEFENSIVO — todas las propiedades nullable: Type/Status/
// Channel/etc. se modelan como string? (no como los enums usados en el
// request) porque Passport podría evolucionar estos valores del lado
// respuesta sin que XPAY deba romper la deserialización — mismo criterio ya
// aplicado a PassportKeyResponse (Key.KeyType como string, no como
// PassportKeyType). Campos adicionales desconocidos no rompen la
// deserialización; campos documentados ausentes quedan en null.
public sealed class PassportQrCodeResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("customer_id")]
    public string? CustomerId { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("qr_code_data")]
    public string? QrCodeData { get; set; }

    [JsonPropertyName("qr_code_image")]
    public string? QrCodeImage { get; set; }

    // RAW string — formato de fecha no confirmado más allá del ejemplo ISO
    // 8601 mostrado; se conserva verbatim (mismo criterio que el resto de
    // CreatedAt en esta integración).
    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("key_id")]
    public string? KeyId { get; set; }

    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    [JsonPropertyName("amount")]
    public PassportQrAmountResponse? Amount { get; set; }

    [JsonPropertyName("vat")]
    public PassportQrVatResponse? Vat { get; set; }

    [JsonPropertyName("additional_info")]
    public PassportQrAdditionalInfoResponse? AdditionalInfo { get; set; }

    // XPAY-458.
    [JsonPropertyName("inc")]
    public PassportQrIncResponse? Inc { get; set; }

    [JsonPropertyName("tip")]
    public PassportQrTipResponse? Tip { get; set; }

    [JsonPropertyName("qr_code_reference")]
    public string? QrCodeReference { get; set; }
}

public sealed class PassportQrAmountResponse
{
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }
}

public sealed class PassportQrVatResponse
{
    [JsonPropertyName("vat_type")]
    public string? VatType { get; set; }

    [JsonPropertyName("vat_value")]
    public string? VatValue { get; set; }

    [JsonPropertyName("vat_base_value")]
    public string? VatBaseValue { get; set; }
}

public sealed class PassportQrAdditionalInfoResponse
{
    [JsonPropertyName("transaction_purpose")]
    public string? TransactionPurpose { get; set; }

    [JsonPropertyName("terminal_label")]
    public string? TerminalLabel { get; set; }
}

// XPAY-458 — reutiliza PassportQrIncResponse ya existente (definido en
// PassportDecodeQrCodeResponse.cs, XPAY-304/305) — mismo shape exacto
// (inc_type/inc_value, ambos string? defensivos), no se duplica el tipo.

// XPAY-458 — mismo shape que PassportQrVatResponse/PassportQrIncResponse.
public sealed class PassportQrTipResponse
{
    [JsonPropertyName("tip_type")]
    public string? TipType { get; set; }

    [JsonPropertyName("tip_value")]
    public string? TipValue { get; set; }
}
