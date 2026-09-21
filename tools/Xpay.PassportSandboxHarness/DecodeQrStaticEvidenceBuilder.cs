using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-465 — construye el EvidenceRecord saneado para M4-T2 (Decode QR
// Code) a partir del request real enviado y la respuesta real recibida (o
// su ausencia, en caso de fallo). Función PURA de transformación — no hace
// I/O, no llama a Passport, no escribe archivos (eso es responsabilidad de
// EvidenceWriter) — 100% testeable offline. Mismo criterio estructural que
// CreateQrStaticEvidenceBuilder (M4-T1), pero para un contrato de
// request/response completamente distinto.
//
// Requisito oficial de certificación (anexo, citado en XPAY-465): evidencia
// = "Payload decodificado" — la evidencia debe demostrar que el QR se
// decodificó a una estructura JSON válida con el shape esperado, sin
// filtrar el contenido sensible. Por eso esta evidencia expone, a
// diferencia de CreateQrStaticEvidenceBuilder (que sólo registra
// "X_present" para amount/vat), la PRESENCIA de CADA campo top-level
// documentado de la respuesta de Decode — es la única forma de demostrar
// "estructura JSON válida y completa" sin exponer el contenido.
//
// Política de saneamiento:
//   - customer_id (request): SOLO fingerprint.
//   - qr_code_data (request): NUNCA el valor real (payload EMVCo completo,
//     reconstruible a un QR de pago) — sólo presencia + fingerprint, mismo
//     criterio que CreateQrStaticEvidenceBuilder.
//   - status/type/channel/acquirer_network_identifier (response): se
//     conservan literales — son categorías de protocolo, no PII (mismo
//     criterio que type/channel en CreateQrStaticEvidenceBuilder).
//   - amount/additional_info/inc/vat (response): sólo "X_present" — nunca
//     sus subcampos (mismo criterio que CreateQrStaticEvidenceBuilder).
//   - key (response, key.key_type/key.key_value): key_type se conserva
//     literal (categoría, ej. "PHONE"/"BCODE" — no identifica a nadie por
//     sí solo); key_value NUNCA se conserva — puede ser PII directa
//     (teléfono/email/cédula, mismo criterio que key_value en el resto del
//     harness) — sólo presencia + fingerprint.
//   - merchant (response): merchant_category_code/merchant_country se
//     conservan literales (categorías institucionales, no identifican a
//     una persona); merchant_name/merchant_city/merchant_post_code NUNCA
//     se conservan literales (combinados pueden identificar el comercio/
//     ubicación específica) — sólo presencia + fingerprint cada uno.
//   - qr_code_data (response, eco de Passport): mismo criterio que en el
//     request — nunca el valor, sólo presencia + fingerprint.
//   - qr_code_reference (response): SOLO fingerprint (mismo criterio ya
//     establecido en CreateQrStaticEvidenceBuilder — XPAY-458/464 ya
//     documentaron que el valor de respuesta puede diferir del enviado en
//     Create; Decode simplemente refleja lo que Passport devuelva, sin
//     asumir equivalencia con nada).
public static class DecodeQrStaticEvidenceBuilder
{
    public const string CaseId    = "M4-T2";
    public const string Operation = "POST /v1/qrcodes/decode";

    public static EvidenceRecord BuildSuccess(
        PassportDecodeQrCodeRequest request,
        PassportDecodeQrCodeResponse response,
        int? httpStatus,
        string commitSha,
        DateTime executedAtUtc,
        string? automatedTestReference)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);

        var requestSanitized = new Dictionary<string, object?>
        {
            ["customer_id_fingerprint"]   = Fingerprint.Compute(request.CustomerId),
            ["qr_code_data_present"]      = !string.IsNullOrEmpty(request.QrCodeData),
            ["qr_code_data_fingerprint"]  = Fingerprint.Compute(request.QrCodeData),
        };

        var responseSanitized = new Dictionary<string, object?>
        {
            // Categorías de protocolo — no sensibles, se conservan literales.
            ["status"]                       = response.Status,
            ["type"]                         = response.Type,
            ["channel"]                      = response.Channel,
            ["acquirer_network_identifier"]  = response.AcquirerNetworkIdentifier,

            // Presencia de cada sub-estructura documentada — demuestra
            // "estructura JSON válida" sin exponer subcampos.
            ["amount_present"]           = response.Amount is not null,
            ["additional_info_present"]  = response.AdditionalInfo is not null,
            ["inc_present"]              = response.Inc is not null,
            ["vat_present"]              = response.Vat is not null,

            // key — key_type es categoría (no PII); key_value NUNCA crudo.
            ["key_present"]              = response.Key is not null,
            ["key_type"]                 = response.Key?.KeyType,
            ["key_value_fingerprint"]    = Fingerprint.Compute(response.Key?.KeyValue),

            // merchant — category_code/country son categorías; name/city/
            // post_code combinados pueden identificar el comercio/ubicación.
            ["merchant_present"]              = response.Merchant is not null,
            ["merchant_category_code"]        = response.Merchant?.MerchantCategoryCode,
            ["merchant_country"]              = response.Merchant?.MerchantCountry,
            ["merchant_name_fingerprint"]     = Fingerprint.Compute(response.Merchant?.MerchantName),
            ["merchant_city_fingerprint"]     = Fingerprint.Compute(response.Merchant?.MerchantCity),
            ["merchant_post_code_fingerprint"] = Fingerprint.Compute(response.Merchant?.MerchantPostCode),

            // qr_code_data (eco) / qr_code_reference — mismo criterio
            // conservador que CreateQrStaticEvidenceBuilder.
            ["qr_code_data_present"]           = !string.IsNullOrEmpty(response.QrCodeData),
            ["qr_code_data_fingerprint"]       = Fingerprint.Compute(response.QrCodeData),
            ["qr_code_reference_present"]      = response.QrCodeReference is not null,
            ["qr_code_reference_fingerprint"]  = Fingerprint.Compute(response.QrCodeReference),
        };

        return new EvidenceRecord(
            CaseId: CaseId,
            ExecutedAtUtc: FormatTimestamp(executedAtUtc),
            Environment: EvidenceRecord.EnvironmentSandbox,
            BackendCommitSha: commitSha,
            Operation: Operation,
            HttpStatus: httpStatus,
            Result: EvidenceRecord.ResultPass,
            RequestSanitized: requestSanitized,
            ResponseSanitized: responseSanitized,
            AutomatedTestReference: automatedTestReference,
            // XPAY-465 — mismo criterio ya corregido en XPAY-464 para
            // CreateQrStaticEvidenceBuilder: Notes=null en éxito, nunca un
            // texto fijo que pueda quedar desactualizado.
            Notes: null,
            ReviewStatus: EvidenceRecord.ReviewPendingPassportReview);
    }

    // Mismo criterio que CreateQrStaticEvidenceBuilder — bloqueo LOCAL
    // (target ausente, archivo de qr_code_data ausente/vacío, commit SHA no
    // resoluble) ANTES de intentar HTTP: nunca se finge como respuesta de
    // Passport.
    public static EvidenceRecord BuildLocalBlocked(string reason, string commitSha, DateTime executedAtUtc) => new(
        CaseId: CaseId,
        ExecutedAtUtc: FormatTimestamp(executedAtUtc),
        Environment: EvidenceRecord.EnvironmentSandbox,
        BackendCommitSha: commitSha,
        Operation: Operation,
        HttpStatus: null,
        Result: EvidenceRecord.ResultFail,
        RequestSanitized: new Dictionary<string, object?>(),
        ResponseSanitized: new Dictionary<string, object?>(),
        AutomatedTestReference: null,
        Notes: EvidenceRecord.NotePrefixLocalBlocked + reason,
        ReviewStatus: EvidenceRecord.ReviewPendingPassportReview);

    // Fallo real de Passport (HTTP no-2xx, o error de protocolo). Nunca
    // filtra el response body, customer_id ni qr_code_data.
    public static EvidenceRecord BuildPassportHttpFailure(
        string reason, int? httpStatus, string commitSha, DateTime executedAtUtc,
        string? safeErrorCode = null, string? safeErrorMessage = null) => new(
        CaseId: CaseId,
        ExecutedAtUtc: FormatTimestamp(executedAtUtc),
        Environment: EvidenceRecord.EnvironmentSandbox,
        BackendCommitSha: commitSha,
        Operation: Operation,
        HttpStatus: null,
        Result: EvidenceRecord.ResultFail,
        RequestSanitized: new Dictionary<string, object?>(),
        ResponseSanitized: BuildFailureResponseSanitized(httpStatus, safeErrorCode, safeErrorMessage),
        AutomatedTestReference: null,
        Notes: EvidenceRecord.NotePrefixPassportHttpFailure + reason,
        ReviewStatus: EvidenceRecord.ReviewPendingPassportReview);

    private static Dictionary<string, object?> BuildFailureResponseSanitized(
        int? httpStatus, string? safeErrorCode, string? safeErrorMessage)
    {
        var dict = new Dictionary<string, object?>();
        if (httpStatus is not null) dict["observed_http_status"] = httpStatus;
        if (safeErrorCode is not null) dict["error_code"] = safeErrorCode;
        if (safeErrorMessage is not null) dict["error_message"] = safeErrorMessage;
        return dict;
    }

    private static string FormatTimestamp(DateTime executedAtUtc) =>
        executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
}
