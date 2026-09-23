using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-529 — construye el EvidenceRecord saneado para la certificación M5
// Retrieve Bre-B Payment (GET /v1/payments/{payment_id}). Función PURA —
// sin I/O, sin red, sin escritura de archivos.
//
// AISLAMIENTO ARQUITECTÓNICO — mismo criterio que
// CreateBrebPaymentCertificationEvidenceBuilder: nunca pasa por
// BrebPaymentService/BD/reconciliación.
//
// error_present/safe_error_code/safe_error_message describen el objeto
// `error` embebido en la respuesta 200 OK (Passport puede devolver
// status=REJECTED con error.error_code/error_description dentro de una
// respuesta HTTP exitosa) — DISTINTO de un fallo de transporte (HTTP no-2xx,
// ver BuildPassportHttpFailure). Ambos casos se preservan sin reinterpretar
// cuál "cuenta" como el resultado de certificación.
public static class RetrieveBrebPaymentCertificationEvidenceBuilder
{
    public const string CaseId    = "M5-PAYMENT-RETRIEVE";
    public const string Operation = "GET /v1/payments/{payment_id}";

    public const string PaymentIdSourceM5CreatePrivateArtifact = "M5_PAYMENT_CREATE_PRIVATE_ARTIFACT";

    public static EvidenceRecord BuildSuccess(
        string paymentId,
        PassportPaymentResponse response,
        int? httpStatus,
        string commitSha,
        DateTime executedAtUtc,
        string? automatedTestReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paymentId);
        ArgumentNullException.ThrowIfNull(response);

        var requestSanitized = new Dictionary<string, object?>
        {
            ["payment_id_present"]     = true,
            ["payment_id_fingerprint"] = Fingerprint.Compute(paymentId),
            ["payment_id_source"]      = PaymentIdSourceM5CreatePrivateArtifact,
        };

        var hasError = response.Error is not null;

        var responseSanitized = new Dictionary<string, object?>
        {
            ["request_executed"]       = true,
            ["request_accepted"]       = true,
            ["status"]                 = response.Status,
            ["amount_present"]         = response.Amount is not null,
            ["currency"]               = response.Amount?.Currency,
            ["error_present"]          = hasError,
            ["safe_error_code"]        = hasError ? response.Error!.ErrorCode : null,
            ["safe_error_message"]     = hasError ? response.Error!.ErrorDescription : null,
            ["retry_count"]            = 0,
            ["payment_create_calls"]   = 0,
            ["payment_retrieve_calls"] = 1,
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
        string? paymentIdAttempted,
        string reason, int? httpStatus, string commitSha, DateTime executedAtUtc,
        string? safeErrorCode = null, string? safeErrorMessage = null) => new(
        CaseId: CaseId,
        ExecutedAtUtc: FormatTimestamp(executedAtUtc),
        Environment: EvidenceRecord.EnvironmentSandbox,
        BackendCommitSha: commitSha,
        Operation: Operation,
        HttpStatus: null,
        Result: EvidenceRecord.ResultFail,
        RequestSanitized: !string.IsNullOrWhiteSpace(paymentIdAttempted)
            ? new Dictionary<string, object?>
              {
                  ["payment_id_present"]     = true,
                  ["payment_id_fingerprint"] = Fingerprint.Compute(paymentIdAttempted),
                  ["payment_id_source"]      = PaymentIdSourceM5CreatePrivateArtifact,
              }
            : new Dictionary<string, object?>(),
        ResponseSanitized: BuildFailureResponseSanitized(httpStatus, safeErrorCode, safeErrorMessage),
        AutomatedTestReference: null,
        Notes: EvidenceRecord.NotePrefixPassportHttpFailure + reason,
        ReviewStatus: EvidenceRecord.ReviewPendingPassportReview);

    private static Dictionary<string, object?> BuildFailureResponseSanitized(
        int? httpStatus, string? safeErrorCode, string? safeErrorMessage) => new()
    {
        ["request_executed"]       = true,
        ["request_accepted"]       = false,
        ["observed_http_status"]   = httpStatus,
        ["safe_error_code"]        = safeErrorCode,
        ["safe_error_message"]     = safeErrorMessage,
        ["retry_count"]            = 0,
        ["payment_create_calls"]   = 0,
        ["payment_retrieve_calls"] = 1,
    };

    private static string FormatTimestamp(DateTime executedAtUtc) =>
        executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
}
