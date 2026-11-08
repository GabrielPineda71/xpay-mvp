using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-522 — construye el EvidenceRecord saneado para M4-T6-C (Create QR
// Code DYNAMIC con customer_id incorrecto). Función PURA — sin I/O, sin
// red, sin escritura de archivos.
//
// Mismo principio de separación EXECUTION_RESULT vs. PASSPORT_BEHAVIOR_OBSERVED
// que CreateQrDynamicSuspendedKeyEvidenceBuilder (M4-T6-A) — ver ese archivo
// para el razonamiento completo. El histórico STATIC (M4-T3-C) produjo
// HTTP 400/invalid_parameter_value/"UUID version 4 expected" — esto es
// contexto histórico, NUNCA una expectativa contractual codificada aquí
// para DYNAMIC.
//
// customer_id_kind="SYNTHETIC_INVALID" — mismo criterio conservador que
// CreateQrStaticInvalidCustomerEvidenceBuilder (M4-T3-C): el customer_id
// sintético (InvalidCustomerIdGenerator) NO es secreto ni PII real, pero
// de todas formas nunca se distingue el saneamiento por si un identificador
// es "real" o "sintético" — sólo su fingerprint se registra, igual que
// cualquier otro identificador.
public static class CreateQrDynamicInvalidCustomerEvidenceBuilder
{
    public const string CaseId    = "M4-T6-C";
    public const string Operation = "POST /v1/qrcodes";

    public static EvidenceRecord BuildSuccess(
        PassportCreateQrCodeRequest request,
        PassportQrCodeResponse response,
        int? httpStatus,
        string commitSha,
        DateTime executedAtUtc,
        string? automatedTestReference)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);

        var requestSanitized = BuildRequestSanitized(request);

        var responseSanitized = new Dictionary<string, object?>
        {
            ["execution_completed"]        = true,
            ["request_accepted"]           = true,
            ["passport_behavior_observed"] = "QR_CREATED",
            ["status"]                     = response.Status,
            ["type"]                       = response.Type,
            ["qr_code_data_present"]       = !string.IsNullOrEmpty(response.QrCodeData),
            ["qr_code_image_present"]      = !string.IsNullOrEmpty(response.QrCodeImage),
            ["qr_code_reference_present"]  = response.QrCodeReference is not null,
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
        PassportCreateQrCodeRequest? requestAttempted,
        string reason, int? httpStatus, string commitSha, DateTime executedAtUtc,
        string? safeErrorCode = null, string? safeErrorMessage = null) => new(
        CaseId: CaseId,
        ExecutedAtUtc: FormatTimestamp(executedAtUtc),
        Environment: EvidenceRecord.EnvironmentSandbox,
        BackendCommitSha: commitSha,
        Operation: Operation,
        HttpStatus: null,
        Result: EvidenceRecord.ResultFail,
        RequestSanitized: requestAttempted is not null ? BuildRequestSanitized(requestAttempted) : new Dictionary<string, object?>(),
        ResponseSanitized: BuildFailureResponseSanitized(httpStatus, safeErrorCode, safeErrorMessage),
        AutomatedTestReference: null,
        Notes: EvidenceRecord.NotePrefixPassportHttpFailure + reason,
        ReviewStatus: EvidenceRecord.ReviewPendingPassportReview);

    private static Dictionary<string, object?> BuildRequestSanitized(PassportCreateQrCodeRequest request) => new()
    {
        ["type"]                    = request.Type.ToString(),
        ["channel"]                 = request.Channel.ToString(),
        ["key_id_fingerprint"]      = Fingerprint.Compute(request.KeyId),
        ["customer_id_fingerprint"] = Fingerprint.Compute(request.CustomerId),
        ["customer_id_kind"]        = "SYNTHETIC_INVALID",
        ["vat_present"]             = request.Vat is not null,
        ["inc_present"]             = request.Inc is not null,
        ["tip_present"]             = request.Tip is not null,
        ["amount_present"]          = request.Amount is not null,
        ["amount_currency_present"] = request.Amount?.Currency is not null,
        ["additional_info_present"] = request.AdditionalInfo is not null,
        ["qr_code_reference_present"]     = request.QrCodeReference is not null,
        ["qr_code_reference_fingerprint"] = Fingerprint.Compute(request.QrCodeReference),
    };

    private static Dictionary<string, object?> BuildFailureResponseSanitized(
        int? httpStatus, string? safeErrorCode, string? safeErrorMessage) => new()
    {
        ["execution_completed"]        = true,
        ["request_accepted"]           = false,
        ["passport_behavior_observed"] = "HTTP_ERROR_RECEIVED",
        ["observed_http_status"]       = httpStatus,
        ["safe_error_code"]            = safeErrorCode,
        ["safe_error_message"]         = safeErrorMessage,
    };

    private static string FormatTimestamp(DateTime executedAtUtc) =>
        executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
}
