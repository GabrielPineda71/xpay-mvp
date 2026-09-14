using System.Text.Json.Serialization;

namespace Xpay.Api.Integrations.Passport;

// XPAY-298 — body de POST /v1/qrcodes (contrato confirmado vía
// docs.passportfintech.com/EN/create-qr-codes y /EN/qr-code-guide, XPAY-297/298).
// Cubre STATIC y DYNAMIC — es el MISMO endpoint, diferenciado por `Type`.
//
// Campos modelados en ESTA fase (evidence-first, sólo lo confirmado y
// necesario para los dos happy paths de certificación M4-T1/M4-T4):
// key_id, customer_id, type, channel, additional_info{transaction_purpose,
// terminal_label}, vat{vat_type,vat_value,vat_base_value}, qr_code_reference
// (opcional), amount{value,currency} (opcional), inc{inc_type,inc_value}
// (opcional — condicionalmente requerido por Passport cuando amount está
// presente en DYNAMIC, confirmado verbatim: "Required for Dynamic QR Codes
// if an Amount is provided" — XPAY no fuerza esa condicionalidad aquí; el
// caller es responsable de incluir Inc cuando incluye Amount, igual que
// Passport documenta la regla como condicional al proveedor, no como un
// guard local inventado).
//
// Otros campos opcionales documentados (invoice_number, mobile_phone_number,
// store_label, loyalty_label, reference_label, customer_label, customer_info,
// channel_presentation, tip.*) NO se modelan en esta fase — no son necesarios
// para representar fielmente los ejemplos oficiales de STATIC/DYNAMIC usados
// en los tests (XPAY-298, Fase 9/10).
//
// Nunca se loguea una instancia de este record (contiene identificadores y
// datos transaccionales).
public sealed record PassportCreateQrCodeRequest(
    [property: JsonPropertyName("key_id")]         string KeyId,
    [property: JsonPropertyName("customer_id")]    string CustomerId,
    [property: JsonPropertyName("type")]           PassportQrType Type,
    [property: JsonPropertyName("channel")]        PassportQrChannel Channel,
    [property: JsonPropertyName("additional_info")] PassportQrAdditionalInfoRequest AdditionalInfo,
    [property: JsonPropertyName("vat")]            PassportQrVatRequest Vat)
{
    [JsonPropertyName("qr_code_reference")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? QrCodeReference { get; init; }

    [JsonPropertyName("amount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PassportQrAmountRequest? Amount { get; init; }

    [JsonPropertyName("inc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PassportQrIncRequest? Inc { get; init; }
}

// additional_info — sólo transaction_purpose y terminal_label en esta fase
// (ambos requeridos por contrato).
//
// TransactionPurpose se modela como STRING PLANO, NO como enum C#: los
// valores documentados son códigos con cero inicial ("00","02","03","04",
// "05","06","07"). Un enum respaldado por int perdería el cero inicial al
// serializar (p.ej. "Compras"=0 → "0", no "00"), y la documentación no
// confirma nombres semánticos formales para inventar miembros de enum
// (XPAY-298 Fase 5). La validación de pertenencia al conjunto documentado
// vive en PassportQrClient (mismo criterio Enum.IsDefined, aplicado a un
// HashSet<string> en vez de a un enum real).
public sealed record PassportQrAdditionalInfoRequest(
    [property: JsonPropertyName("transaction_purpose")] string TransactionPurpose,
    [property: JsonPropertyName("terminal_label")]       string TerminalLabel);

// vat — vat_value y vat_base_value son String según contrato (no decimal):
// "FIXED: amount in COP with two decimals"; "PERCENTAGE: rate with five
// decimals" — formato de texto ya pre-formateado por el caller, no un
// número que XPAY deba redondear/formatear (XPAY-298 Fase 6/7: no inventar
// reglas de redondeo/escala que Passport no documenta).
public sealed record PassportQrVatRequest(
    [property: JsonPropertyName("vat_type")]       PassportQrVatType VatType,
    [property: JsonPropertyName("vat_value")]      string VatValue,
    [property: JsonPropertyName("vat_base_value")] string VatBaseValue);

// inc — mismo criterio que vat: inc_value es String pre-formateado, no
// decimal. Confirmado condicional (requerido si Amount está presente en
// DYNAMIC) — ver comentario en PassportCreateQrCodeRequest.
public sealed record PassportQrIncRequest(
    [property: JsonPropertyName("inc_type")]  PassportQrVatType IncType,
    [property: JsonPropertyName("inc_value")] string IncValue);

// amount — DTO DEDICADO, NO PassportBalance: el contrato de Create QR Code
// exige `value` como STRING JSON con comillas (ej. "80000.57"), confirmado
// con evidencia directa (XPAY-297/298), no ambigua. PassportBalance.Value
// es decimal? con [JsonNumberHandling(AllowReadingFromString)] — ese
// atributo sólo afecta LECTURA, no ESCRITURA: reutilizarlo aquí para un
// REQUEST habría serializado un número JSON crudo (80000.57 sin comillas),
// violando el contrato confirmado. `Value` se modela como string simple,
// validado mínimamente (no vacío) — sin inventar límites/redondeo/escala
// que Passport no documenta para QR.
//
// Currency es un valor contractual FIJO documentado (único valor
// confirmado: "COP") — mismo patrón init-only-con-default ya usado para
// Type/IdentificationType/AccountType en otros DTOs de esta integración.
public sealed record PassportQrAmountRequest(
    [property: JsonPropertyName("value")] string Value)
{
    [JsonPropertyName("currency")]
    public string Currency { get; init; } = "COP";
}
