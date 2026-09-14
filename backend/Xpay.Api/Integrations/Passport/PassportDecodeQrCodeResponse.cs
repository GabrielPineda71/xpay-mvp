using System.Text.Json.Serialization;

namespace Xpay.Api.Integrations.Passport;

// XPAY-305 — respuesta de Decode QR Code (POST /v1/qrcodes/decode), contrato
// confirmado en XPAY-304 vía docs.passportfintech.com/EN/decode-qr-code.
//
// NO reutiliza PassportQrCodeResponse como raíz: el conjunto de campos root
// difiere (agrega key/merchant/acquirer_network_identifier/inc; la
// documentación de Decode no confirma id/key_id/customer_id/qr_code_image a
// nivel raíz mediante una tabla exhaustiva, sólo mediante un ejemplo JSON —
// XPAY-304). SÍ reutiliza, por coincidencia de shape confirmada campo a
// campo (no por semejanza de nombre), las clases anidadas ya existentes:
// PassportQrAmountResponse, PassportQrVatResponse,
// PassportQrAdditionalInfoResponse (de PassportQrCodeResponse.cs) y
// PassportKeyResponseDetail (de PassportKeyResponse.cs).
//
// TRANSPORTE DEFENSIVO — todas las propiedades nullable, igual que el resto
// de DTOs de respuesta de esta integración: status/type/channel se modelan
// como string? (NO como los enums estrictos PassportQrType/
// PassportQrChannel/PassportQrVatType usados del lado request de Create QR)
// porque Passport podría evolucionar estos valores sin que XPAY deba romper
// la deserialización, y porque la página de Decode documenta el response
// sólo mediante un ejemplo JSON, no una tabla exhaustiva de campos/tipos
// (XPAY-304). NO se aplica ningún guard de protocolo tipo "RequireXxxId":
// XPAY-304 confirmó que Decode no documenta ningún campo `id` a nivel raíz,
// y ningún otro campo está documentado como requerido de forma exhaustiva en
// la respuesta — inventar un guard aquí violaría el criterio evidence-first
// ya aplicado en el resto de la integración.
//
// additional_info.transaction_purpose: XPAY-304 detectó que el ejemplo de
// Decode muestra "PURCHASE" (valor semántico), distinto de los códigos
// numéricos documentados para el REQUEST de Create QR ("00".."07"). Este DTO
// NO valida ese campo contra ValidTransactionPurposes (ese conjunto sólo
// aplica al request de Create QR) ni intenta traducir/normalizar el valor:
// se deserializa como string defensivo, tal cual llega.
public sealed class PassportDecodeQrCodeResponse
{
    [JsonPropertyName("amount")]
    public PassportQrAmountResponse? Amount { get; set; }

    [JsonPropertyName("additional_info")]
    public PassportQrAdditionalInfoResponse? AdditionalInfo { get; set; }

    [JsonPropertyName("inc")]
    public PassportQrIncResponse? Inc { get; set; }

    [JsonPropertyName("key")]
    public PassportKeyResponseDetail? Key { get; set; }

    [JsonPropertyName("qr_code_data")]
    public string? QrCodeData { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("acquirer_network_identifier")]
    public string? AcquirerNetworkIdentifier { get; set; }

    [JsonPropertyName("merchant")]
    public PassportDecodeMerchantResponse? Merchant { get; set; }

    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    [JsonPropertyName("vat")]
    public PassportQrVatResponse? Vat { get; set; }

    [JsonPropertyName("qr_code_reference")]
    public string? QrCodeReference { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

// inc — shape confirmado en XPAY-304 (mismos subcampos que
// PassportQrIncRequest, pero del lado RESPUESTA: string? defensivo, sin
// reutilizar el record de request ni su enum estricto PassportQrVatType).
// No existía un DTO de respuesta para inc antes de esta fase (Create QR
// deliberadamente no lo modelaba en su response — ver PassportQrCodeResponse.cs).
public sealed class PassportQrIncResponse
{
    [JsonPropertyName("inc_type")]
    public string? IncType { get; set; }

    [JsonPropertyName("inc_value")]
    public string? IncValue { get; set; }
}

// merchant — shape confirmado en XPAY-304, exclusivo de Decode: NO coincide
// con PassportCustomerResponse (que expone merchant_category_code como
// campo plano del customer, sin un objeto `merchant` anidado) ni con su
// PassportMerchantAddressResponse (line_1/line_2/line_3/city/state/
// post_code/country — campos distintos). Se modela como clase dedicada,
// nunca reutilizada por semejanza de nombre.
public sealed class PassportDecodeMerchantResponse
{
    [JsonPropertyName("merchant_category_code")]
    public string? MerchantCategoryCode { get; set; }

    [JsonPropertyName("merchant_country")]
    public string? MerchantCountry { get; set; }

    [JsonPropertyName("merchant_name")]
    public string? MerchantName { get; set; }

    [JsonPropertyName("merchant_city")]
    public string? MerchantCity { get; set; }

    [JsonPropertyName("merchant_post_code")]
    public string? MerchantPostCode { get; set; }
}
