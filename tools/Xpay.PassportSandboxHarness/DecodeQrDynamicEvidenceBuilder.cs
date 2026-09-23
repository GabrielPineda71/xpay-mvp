using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-517 — construye el EvidenceRecord saneado para M4-T5 (Decode QR
// Code — DINÁMICO, reutilizando el QR ya creado en M4-T4). Función PURA de
// transformación — sin I/O, sin red, sin escritura de archivos. Mirror
// ESTRUCTURAL de DecodeQrStaticEvidenceBuilder (M4-T2), con dos campos
// NUEVOS exigidos por XPAY-517 §4/§5: qr_code_reference_match_m4_t4 y
// amount_value_matches_expected/amount_currency_matches_expected — ambos
// booleanos calculados en memoria, NUNCA los valores crudos comparados.
//
// Política de saneamiento (idéntica a DecodeQrStaticEvidenceBuilder salvo
// lo señalado):
//   - customer_id (request): SOLO fingerprint.
//   - qr_code_data (request): NUNCA el valor — sólo presencia + fingerprint.
//   - decode_source (request): literal fijo "M4-T4_PRIVATE_ARTIFACT" — NO
//     sensible, documenta la procedencia sin exponer ninguna ruta.
//   - status/type/channel/acquirer_network_identifier (response): literales
//     — categorías de protocolo, no PII.
//   - amount_present (response): booleano únicamente — nunca value/currency
//     literales (XPAY-517 §5: preferimos booleanos de comparación).
//   - amount_value_matches_expected/amount_currency_matches_expected: el
//     RESULTADO de comparar en memoria contra las constantes de
//     certificación de ESTE caso ("10000.00"/"COP") — nunca los valores
//     mismos.
//   - vat_present/inc_present (response): booleanos únicamente.
//   - qr_code_reference_present/_fingerprint (response): mismo criterio que
//     M4-T2 — fingerprint del valor DECODIFICADO, nunca crudo.
//   - qr_code_reference_match_m4_t4: resultado de comparar
//     Fingerprint.Compute(response.QrCodeReference) contra el fingerprint
//     equivalente del qr_code_reference_received preservado en el artefacto
//     privado de M4-T4 — nunca ningún valor crudo.
//   - key (response): mismo criterio que M4-T2 — key_type literal,
//     key_value SOLO fingerprint.
//   - limitations.tip_not_modeled_by_decode_dto: constante YES/true — NO
//     implica que Passport no haya enviado tip en el JSON real (ver
//     PassportQrClient.DecodeQrCodeWithRawCaptureAsync/M4T5PrivateCapture
//     para dónde SÍ podría preservarse esa evidencia, privadamente).
//   - private_artifact_captured: booleano — si el JSON crudo de la
//     respuesta quedó preservado privadamente (mismo criterio que XPAY-508
//     para M4-T4). NUNCA la ruta privada.
public static class DecodeQrDynamicEvidenceBuilder
{
    public const string CaseId    = "M4-T5";
    public const string Operation = "POST /v1/qrcodes/decode";

    public const string DecodeSourceM4T4PrivateArtifact = "M4-T4_PRIVATE_ARTIFACT";

    // Constantes de ESTE caso de certificación M4-T5 — NO una regla global
    // de PassportQrClient/producto (XPAY-517 §5/§13). Mismo valor oficial
    // (ejemplo Postman de Passport) ya usado para construir el QR original
    // en M4-T4 (XPAY-512).
    public const string ExpectedAmountValue    = "10000.00";
    public const string ExpectedAmountCurrency = "COP";

    public static EvidenceRecord BuildSuccess(
        PassportDecodeQrCodeRequest request,
        PassportDecodeQrCodeResponse response,
        string? m4T4QrCodeReferenceReceived,
        int? httpStatus,
        string commitSha,
        DateTime executedAtUtc,
        string? automatedTestReference,
        bool privateArtifactCaptured)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);

        var requestSanitized = new Dictionary<string, object?>
        {
            ["customer_id_fingerprint"]  = Fingerprint.Compute(request.CustomerId),
            ["qr_code_data_present"]     = !string.IsNullOrEmpty(request.QrCodeData),
            ["qr_code_data_fingerprint"] = Fingerprint.Compute(request.QrCodeData),
            ["decode_source"]            = DecodeSourceM4T4PrivateArtifact,
        };

        var responseQrCodeReferenceFingerprint = Fingerprint.Compute(response.QrCodeReference);
        var m4T4QrCodeReferenceFingerprint     = Fingerprint.Compute(m4T4QrCodeReferenceReceived);
        var qrCodeReferenceMatch = response.QrCodeReference is not null
            && m4T4QrCodeReferenceReceived is not null
            && responseQrCodeReferenceFingerprint == m4T4QrCodeReferenceFingerprint;

        var responseSanitized = new Dictionary<string, object?>
        {
            ["status"]                      = response.Status,
            ["type"]                        = response.Type,
            ["channel"]                     = response.Channel,
            ["acquirer_network_identifier"] = response.AcquirerNetworkIdentifier,

            ["amount_present"]                      = response.Amount is not null,
            ["amount_value_matches_expected"]       = response.Amount?.Value == ExpectedAmountValue,
            ["amount_currency_matches_expected"]    = response.Amount?.Currency == ExpectedAmountCurrency,

            ["vat_present"] = response.Vat is not null,
            ["inc_present"] = response.Inc is not null,

            ["qr_code_reference_present"]    = response.QrCodeReference is not null,
            ["qr_code_reference_fingerprint"] = responseQrCodeReferenceFingerprint,
            ["qr_code_reference_match_m4_t4"] = qrCodeReferenceMatch,

            ["key_present"]           = response.Key is not null,
            ["key_type"]              = response.Key?.KeyType,
            ["key_value_fingerprint"] = Fingerprint.Compute(response.Key?.KeyValue),

            ["private_artifact_captured"] = privateArtifactCaptured,

            ["limitations"] = new Dictionary<string, object?>
            {
                ["tip_not_modeled_by_decode_dto"] =
                    "PassportDecodeQrCodeResponse no modela `tip` a la fecha de esta ejecución. " +
                    "Esto NO demuestra que Passport no lo haya enviado en el JSON real — System.Text.Json " +
                    "descarta silenciosamente propiedades no modeladas. Si private_artifact_captured=true, " +
                    "el JSON crudo (que sí puede contener `tip` si Passport lo envió) quedó preservado " +
                    "privadamente, fuera de este expediente versionable.",
            },
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
            Notes: null,
            ReviewStatus: EvidenceRecord.ReviewPendingPassportReview);
    }

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
