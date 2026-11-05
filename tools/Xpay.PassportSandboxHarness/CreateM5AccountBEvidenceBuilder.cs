using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-531 — construye el EvidenceRecord saneado para M5-T1-B (Create/Link
// Account B, POST /v1/accounts/link). Función PURA — sin I/O, sin red, sin
// escritura de archivos.
//
// AISLAMIENTO ARQUITECTÓNICO: esta evidencia describe ÚNICAMENTE la llamada
// directa harness → IPassportCustomerAccountClient → Passport.
//
// XPAY-531 §13 — esta evidencia NO afirma que account_id_B se usará como
// Payment.account_id; documenta exclusivamente la creación/vínculo de
// Account B a Customer B.
//
// Política de saneamiento — NUNCA valores crudos (customer_id, account_number,
// account_id de respuesta): sólo presencia + fingerprint.
public static class CreateM5AccountBEvidenceBuilder
{
    public const string CaseId    = "M5-T1-B";
    public const string Operation = "POST /v1/accounts/link";

    public static EvidenceRecord BuildSuccess(
        PassportLinkAccountRequest request,
        PassportAccountResponse response,
        int? httpStatus,
        string commitSha,
        DateTime executedAtUtc,
        string? automatedTestReference,
        bool privateArtifactCaptured)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);

        var requestSanitized = BuildRequestSanitized(request);

        var responseSanitized = new Dictionary<string, object?>
        {
            ["request_executed"]          = true,
            ["request_accepted"]          = true,
            ["status"]                    = response.Status,
            ["account_id_present"]        = !string.IsNullOrWhiteSpace(response.Id),
            ["account_id_fingerprint"]    = Fingerprint.Compute(response.Id),
            ["private_artifact_captured"] = privateArtifactCaptured,
            ["retry_count"]               = 0,
            ["customer_create_calls"]     = 0,
            ["account_create_calls"]      = 1,
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
        PassportLinkAccountRequest? requestAttempted,
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

    private static Dictionary<string, object?> BuildRequestSanitized(PassportLinkAccountRequest request) => new()
    {
        ["customer_id_present"]      = !string.IsNullOrWhiteSpace(request.CustomerId),
        ["customer_id_fingerprint"]  = Fingerprint.Compute(request.CustomerId),
        ["account_number_present"]     = !string.IsNullOrWhiteSpace(request.AccountNumber),
        ["account_number_fingerprint"] = Fingerprint.Compute(request.AccountNumber),
        ["account_type"]              = request.AccountType,
    };

    private static Dictionary<string, object?> BuildFailureResponseSanitized(
        int? httpStatus, string? safeErrorCode, string? safeErrorMessage) => new()
    {
        ["request_executed"]      = true,
        ["request_accepted"]      = false,
        ["observed_http_status"]  = httpStatus,
        ["safe_error_code"]       = safeErrorCode,
        ["safe_error_message"]    = safeErrorMessage,
        ["retry_count"]           = 0,
        ["customer_create_calls"] = 0,
        ["account_create_calls"]  = 1,
    };

    private static string FormatTimestamp(DateTime executedAtUtc) =>
        executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
}
