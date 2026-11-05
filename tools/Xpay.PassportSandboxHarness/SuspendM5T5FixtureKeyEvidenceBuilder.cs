using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-576 — construye el EvidenceRecord saneado de TRAZABILIDAD para la
// FASE 2 (Suspend Key) del fixture nuevo de M5-T5-D. Función PURA — no
// hace I/O, no llama a Passport, no escribe archivos.
//
// CRÍTICO: igual que CreateM5T5SuspendedKeyFixtureEvidenceBuilder
// (XPAY-575), esta evidencia NUNCA representa que el caso de
// certificación M5-T5-D se ejecutó — sólo documenta que el fixture quedó
// suspendido. Por eso incluye explícitamente
// certification_test_result=NOT_YET_EXECUTED y
// fixture_phase=SUSPENDED_NOT_TESTED. La Fase 3 (Resolve real sobre esta
// key ya suspendida) es la que produce la evidencia final del caso.
public static class SuspendM5T5FixtureKeyEvidenceBuilder
{
    public const string CaseId = "M5-T5-D-KEY-PREP-SUSPEND";

    // Placeholder literal — NUNCA el key_id real interpolado aquí (mismo
    // criterio que SuspendKeyEvidenceBuilder, M3-T3).
    public const string Operation = "PATCH /v1/keys/{key_id}/suspend";

    public static EvidenceRecord BuildSuccess(
        string keyIdFingerprint,
        string keyValueFingerprint,
        PassportKeyResponse response,
        int? httpStatus,
        string commitSha,
        DateTime executedAtUtc,
        string? automatedTestReference)
    {
        ArgumentNullException.ThrowIfNull(response);

        var requestSanitized = BuildRequestSanitized(keyIdFingerprint, keyValueFingerprint);

        var responseSanitized = new Dictionary<string, object?>
        {
            ["passport_response_received"] = true,
            ["returned_status"]             = response.Status,
            ["retry_count"]                 = 0,
            ["oauth_real_token_requests"]  = 1,
            ["create_key_real_calls"]      = 0,
            ["suspend_key_real_calls"]     = 1,
            ["resolve_real_calls"]          = 0,
            ["payment_real_calls"]          = 0,
            ["retrieve_real_calls"]         = 0,
            ["delete_key_real_calls"]      = 0,
            ["repeat_guard_consumed"]      = true,
            ["certification_test_result"] = "NOT_YET_EXECUTED",
            ["fixture_phase"]               = "SUSPENDED_NOT_TESTED",
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
            Notes: "M5-T5-D-KEY-PREP-SUSPEND: Fase 2 del fixture nuevo de M5-T5-D (XPAY-575 Create -> XPAY-576 " +
                   "Suspend). NO representa que el caso de certificación M5-T5-D se haya ejecutado — el Resolve " +
                   "real sobre esta key ya suspendida requiere una Fase 3 separada y autorizada explícitamente.",
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
        string keyIdFingerprint, string keyValueFingerprint, string reason, int? httpStatus,
        string commitSha, DateTime executedAtUtc, string? safeErrorCode = null, string? safeErrorMessage = null) => new(
        CaseId: CaseId,
        ExecutedAtUtc: FormatTimestamp(executedAtUtc),
        Environment: EvidenceRecord.EnvironmentSandbox,
        BackendCommitSha: commitSha,
        Operation: Operation,
        HttpStatus: null,
        Result: EvidenceRecord.ResultFail,
        RequestSanitized: BuildRequestSanitized(keyIdFingerprint, keyValueFingerprint),
        ResponseSanitized: BuildFailureResponseSanitized(httpStatus, safeErrorCode, safeErrorMessage),
        AutomatedTestReference: null,
        Notes: EvidenceRecord.NotePrefixPassportHttpFailure + reason,
        ReviewStatus: EvidenceRecord.ReviewPendingPassportReview);

    private static Dictionary<string, object?> BuildRequestSanitized(string keyIdFingerprint, string keyValueFingerprint) => new()
    {
        ["fixture_source"]                = "XPAY_575_PRIVATE_CAPTURE",
        ["fixture_key_id_fingerprint"]    = keyIdFingerprint,
        ["fixture_key_value_fingerprint"] = keyValueFingerprint,
        ["key_type"]                       = "BCODE",
    };

    private static Dictionary<string, object?> BuildFailureResponseSanitized(
        int? httpStatus, string? safeErrorCode, string? safeErrorMessage) => new()
    {
        ["passport_response_received"] = true,
        ["observed_http_status"]        = httpStatus,
        ["safe_error_code"]             = safeErrorCode,
        ["safe_error_message"]          = safeErrorMessage,
        ["oauth_real_token_requests"]  = 1,
        ["create_key_real_calls"]      = 0,
        ["suspend_key_real_calls"]     = 1,
        ["resolve_real_calls"]          = 0,
        ["payment_real_calls"]          = 0,
        ["retrieve_real_calls"]         = 0,
        ["delete_key_real_calls"]      = 0,
        ["repeat_guard_consumed"]      = true,
        ["certification_test_result"] = "NOT_YET_EXECUTED",
        ["fixture_phase"]               = "SUSPEND_REJECTED",
    };

    private static string FormatTimestamp(DateTime executedAtUtc) =>
        executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
}
