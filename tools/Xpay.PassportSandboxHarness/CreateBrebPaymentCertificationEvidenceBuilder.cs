using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-529 — construye el EvidenceRecord saneado para la certificación M5
// Create Bre-B Payment (POST /v1/payments/breb). Función PURA — sin I/O,
// sin red, sin escritura de archivos.
//
// AISLAMIENTO ARQUITECTÓNICO (XPAY-529 §2): esta evidencia describe
// ÚNICAMENTE la llamada directa harness → IPassportPaymentClient → Passport
// — nunca pasa por BrebPaymentService/PassportBrebRetiro/Wallet/ledger.
//
// Un HTTP 2xx de Create Payment NUNCA se interpreta como SETTLED — Passport
// documenta PENDING/PROCESSING como estados iniciales transitorios
// (confirmado ya en BrebPaymentStateMachine, XPAY-373). Esta evidencia
// preserva el `status` exactamente como Passport lo devolvió, sin
// reinterpretarlo.
//
// Política de saneamiento — NUNCA se incluyen valores crudos, sólo
// presencia + fingerprint: account_id, resolution_id, payment_id.
public static class CreateBrebPaymentCertificationEvidenceBuilder
{
    public const string CaseId    = "M5-PAYMENT-CREATE";
    public const string Operation = "POST /v1/payments/breb";

    public static EvidenceRecord BuildSuccess(
        PassportCreatePaymentRequest request,
        PassportPaymentResponse response,
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
            ["payment_id_present"]        = !string.IsNullOrWhiteSpace(response.Id),
            ["payment_id_fingerprint"]    = Fingerprint.Compute(response.Id),
            ["private_artifact_captured"] = privateArtifactCaptured,
            ["retry_count"]               = 0,
            ["payment_create_calls"]      = 1,
            ["payment_retrieve_calls"]    = 0,
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
        PassportCreatePaymentRequest? requestAttempted,
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

    private static Dictionary<string, object?> BuildRequestSanitized(PassportCreatePaymentRequest request) => new()
    {
        ["account_id_present"]      = !string.IsNullOrWhiteSpace(request.AccountId),
        ["account_id_fingerprint"]  = Fingerprint.Compute(request.AccountId),
        ["resolution_id_present"]     = !string.IsNullOrWhiteSpace(request.ResolutionId),
        ["resolution_id_fingerprint"] = Fingerprint.Compute(request.ResolutionId),
        ["amount_present"]          = request.Amount is not null,
        ["currency"]                = request.Amount?.Currency,
        ["display_name_present"]    = request.DisplayName is not null,
    };

    private static Dictionary<string, object?> BuildFailureResponseSanitized(
        int? httpStatus, string? safeErrorCode, string? safeErrorMessage) => new()
    {
        ["request_executed"]       = true,
        ["request_accepted"]       = false,
        ["observed_http_status"]   = httpStatus,
        ["safe_error_code"]        = safeErrorCode,
        ["safe_error_message"]     = safeErrorMessage,
        ["retry_count"]            = 0,
        ["payment_create_calls"]   = 1,
        ["payment_retrieve_calls"] = 0,
    };

    private static string FormatTimestamp(DateTime executedAtUtc) =>
        executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
}
