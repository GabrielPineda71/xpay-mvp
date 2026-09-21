using System.Text.Json.Serialization;

namespace Xpay.Api.Integrations.Passport;

// XPAY-298 — body de POST /v1/qrcodes (contrato confirmado vía
// docs.passportfintech.com/EN/create-qr-codes y /EN/qr-code-guide, XPAY-297/298).
// Cubre STATIC y DYNAMIC — es el MISMO endpoint, diferenciado por `Type`.
//
// Campos modelados en ESTA fase (evidence-first, sólo lo confirmado y
// necesario para los dos happy paths de certificación M4-T1/M4-T4):
// key_id, customer_id, type, channel, additional_info{transaction_purpose,
// terminal_label} (opcional — ver XPAY-458 abajo), vat{vat_type,vat_value,
// vat_base_value} (opcional a nivel de DTO — XPAY-357: la documentación
// oficial vigente revisada por el director NO muestra vat en el ejemplo
// STATIC; la obligatoriedad real por caso vive en PassportQrClient.Validate
// y/o en el caller certificador, no en el DTO), qr_code_reference
// (opcional), amount{value,currency} (opcional), inc{inc_type,inc_value}
// (opcional — condicionalmente requerido por Passport cuando amount está
// presente en DYNAMIC, confirmado verbatim: "Required for Dynamic QR Codes
// if an Amount is provided" — XPAY no fuerza esa condicionalidad aquí; el
// caller es responsable de incluir Inc cuando incluye Amount, igual que
// Passport documenta la regla como condicional al proveedor, no como un
// guard local inventado), tip{tip_type,tip_value} (opcional — ver XPAY-458).
//
// XPAY-458 — respuesta oficial de Passport (Gustavo, 2026-09-21) confirmó,
// para M4-T1 (STATIC sin monto), un request funcional que NO incluye
// additional_info/transaction_purpose/terminal_label en absoluto, y SÍ
// incluye vat + inc + tip + qr_code_reference — contradiciendo el supuesto
// previo (XPAY-298) de que additional_info era incondicionalmente
// requerido. Por tanto:
//   - AdditionalInfo pasó de parámetro posicional REQUERIDO a propiedad
//     OPCIONAL (mismo patrón ya usado por Vat/QrCodeReference/Amount/Inc) —
//     la obligatoriedad de transaction_purpose/terminal_label CUANDO
//     additional_info SÍ está presente se preserva sin cambios en
//     PassportQrClient.Validate (ningún otro caso que sí lo envíe pierde
//     esa validación).
//   - Se agrega Tip (nuevo tipo dedicado, mismo criterio que Vat/Inc — ver
//     PassportQrTipRequest abajo). Los valores concretos de certificación
//     para M4-T1 viven en el harness (CreateQrStaticExecutor), nunca aquí
//     ni como regla financiera productiva.
//   - Passport también informó que VAT e INC pasan a ser obligatorios "en
//     ambos tipos de QR" (STATIC y DYNAMIC) — evidencia empírica ya
//     recogida en este repositorio respalda esto para STATIC
//     específicamente (evidence-2026-09-16T00-33-15Z.json: HTTP 400 "Field
//     'vat' is required"; evidence-2026-09-16T00-56-00Z.json: HTTP 400
//     "Field 'inc' is required"). XPAY-458 NO endurece
//     PassportQrClient.Validate para exigir vat/inc incondicionalmente en
//     TODO STATIC (eso reescribiría cobertura general existente —
//     CreateQrCodeAsync_StaticWithoutVat_IsAllowed,
//     CreateQrCodeAsync_DynamicWithoutAmount_IsAllowed, entre otros — sin
//     una segunda confirmación explícita de que esa regla aplica fuera del
//     caso M4-T1); la garantía concreta para M4-T1 vive enteramente en
//     CreateQrStaticExecutor, que construye vat/inc/tip siempre presentes,
//     nunca condicionados. Ver reporte XPAY-458 para esta decisión de
//     alcance, dejada explícita para el director técnico.
//
// Otros campos opcionales documentados (invoice_number, mobile_phone_number,
// store_label, loyalty_label, reference_label, customer_label, customer_info,
// channel_presentation) NO se modelan en esta fase — no son necesarios para
// representar fielmente los ejemplos oficiales de STATIC/DYNAMIC usados en
// los tests (XPAY-298, Fase 9/10; XPAY-458 para tip).
//
// Nunca se loguea una instancia de este record (contiene identificadores y
// datos transaccionales).
public sealed record PassportCreateQrCodeRequest(
    [property: JsonPropertyName("key_id")]         string KeyId,
    [property: JsonPropertyName("customer_id")]    string CustomerId,
    [property: JsonPropertyName("type")]           PassportQrType Type,
    [property: JsonPropertyName("channel")]        PassportQrChannel Channel)
{
    // XPAY-458 — pasó de parámetro posicional REQUERIDO a propiedad
    // OPCIONAL: el contrato confirmado por Passport para M4-T1 (STATIC sin
    // monto) no lo incluye en absoluto. Cuando SÍ está presente (otros
    // casos que lo requieran), PassportQrClient.Validate sigue exigiendo
    // transaction_purpose/terminal_label válidos, sin cambios.
    [JsonPropertyName("additional_info")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PassportQrAdditionalInfoRequest? AdditionalInfo { get; init; }

    // XPAY-357 — Vat pasó de parámetro posicional REQUERIDO a propiedad
    // opcional (mismo patrón ya usado por QrCodeReference/Amount/Inc):
    // el ejemplo oficial STATIC vigente no incluye vat, y M4-T1 (STATIC) no
    // debe enviarlo. La obligatoriedad para DYNAMIC (preservada sin cambios)
    // se exige en PassportQrClient.Validate, no aquí — el DTO en sí no
    // impone ninguna regla de negocio por tipo.
    [JsonPropertyName("vat")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PassportQrVatRequest? Vat { get; init; }

    [JsonPropertyName("qr_code_reference")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? QrCodeReference { get; init; }

    [JsonPropertyName("amount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PassportQrAmountRequest? Amount { get; init; }

    [JsonPropertyName("inc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PassportQrIncRequest? Inc { get; init; }

    // XPAY-458 — nuevo campo, contrato confirmado por Passport
    // (Gustavo, 2026-09-21) para M4-T1: tip{tip_type,tip_value}. Mismo
    // patrón de tipo que vat/inc (PassportQrVatType reutilizado — mismo
    // conjunto de valores de cálculo ya confirmado para vat_type/inc_type;
    // Passport no ha documentado un conjunto distinto para tip_type).
    [JsonPropertyName("tip")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PassportQrTipRequest? Tip { get; init; }
}

// additional_info — sólo transaction_purpose y terminal_label en esta fase.
// XPAY-458 — additional_info en sí es ahora OPCIONAL a nivel de request
// (ver PassportCreateQrCodeRequest); cuando SÍ está presente, ambos
// subcampos siguen siendo requeridos por contrato (PassportQrClient.Validate).
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

// tip — XPAY-458, mismo criterio que vat/inc: tip_value es String
// pre-formateado, no decimal. Contrato confirmado por Passport (Gustavo,
// 2026-09-21) específicamente para M4-T1; no se ha documentado localmente
// ninguna condicionalidad (p. ej. requerido sólo si amount está presente)
// distinta de la que el caller certificador decida.
public sealed record PassportQrTipRequest(
    [property: JsonPropertyName("tip_type")]  PassportQrVatType TipType,
    [property: JsonPropertyName("tip_value")] string TipValue);

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
