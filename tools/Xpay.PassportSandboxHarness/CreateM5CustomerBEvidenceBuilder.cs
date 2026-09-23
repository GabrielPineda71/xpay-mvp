using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-531 — construye el EvidenceRecord saneado para M5-T1-A (Create/Link
// Customer B, POST /v1/customers/business/link). Función PURA — sin I/O,
// sin red, sin escritura de archivos.
//
// AISLAMIENTO ARQUITECTÓNICO: esta evidencia describe ÚNICAMENTE la llamada
// directa harness → IPassportCustomerAccountClient → Passport — nunca pasa
// por ningún servicio de producto XPAY.
//
// Política de saneamiento — NUNCA valores crudos de identificación (razón
// social, email, teléfono, identificación, dirección): sólo presencia +
// fingerprint (o sólo presencia, cuando el campo es categórico/no sensible
// como merchant_category_code/address). customer_id de la respuesta: sólo
// presencia + fingerprint, nunca el valor crudo.
public static class CreateM5CustomerBEvidenceBuilder
{
    public const string CaseId    = "M5-T1-A";
    public const string Operation = "POST /v1/customers/business/link";

    public static EvidenceRecord BuildSuccess(
        PassportLinkMerchantRequest request,
        PassportCustomerResponse response,
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
            ["type"]                      = response.Type,
            ["customer_id_present"]       = !string.IsNullOrWhiteSpace(response.Id),
            ["customer_id_fingerprint"]   = Fingerprint.Compute(response.Id),
            ["private_artifact_captured"] = privateArtifactCaptured,
            ["retry_count"]               = 0,
            ["customer_create_calls"]     = 1,
            ["account_create_calls"]      = 0,
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
        PassportLinkMerchantRequest? requestAttempted,
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

    private static Dictionary<string, object?> BuildRequestSanitized(PassportLinkMerchantRequest request) => new()
    {
        ["business_name_present"]         = !string.IsNullOrWhiteSpace(request.BusinessName),
        ["business_name_fingerprint"]     = Fingerprint.Compute(request.BusinessName),
        ["email_present"]                 = !string.IsNullOrWhiteSpace(request.Email),
        ["email_fingerprint"]             = Fingerprint.Compute(request.Email),
        ["phone_present"]                 = !string.IsNullOrWhiteSpace(request.MobilePhoneNumber),
        ["phone_fingerprint"]             = Fingerprint.Compute(request.MobilePhoneNumber),
        ["identification_present"]        = !string.IsNullOrWhiteSpace(request.IdentificationNumber),
        ["identification_fingerprint"]    = Fingerprint.Compute(request.IdentificationNumber),
        ["merchant_category_code_present"] = !string.IsNullOrWhiteSpace(request.MerchantCategoryCode),
        ["address_present"]               = request.Address is not null,
        ["type"]                          = request.Type,
        ["identification_type"]           = request.IdentificationType,
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
        ["customer_create_calls"] = 1,
        ["account_create_calls"]  = 0,
    };

    private static string FormatTimestamp(DateTime executedAtUtc) =>
        executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
}
