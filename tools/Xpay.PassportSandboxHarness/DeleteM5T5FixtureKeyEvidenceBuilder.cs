namespace Xpay.PassportSandboxHarness;

// XPAY-613 — construye el EvidenceRecord saneado de TRAZABILIDAD para la
// FASE B (PREP-DELETE) del fixture nuevo de M5-T5-E. Función PURA — no
// hace I/O, no llama a Passport, no escribe archivos.
//
// CRÍTICO: igual que CreateM5T5DeletedKeyFixtureEvidenceBuilder (Fase A),
// esta evidencia NUNCA representa que el caso de certificación M5-T5-E se
// ejecutó — sólo documenta que el fixture quedó eliminado. Por eso incluye
// explícitamente certification_test_result=NOT_YET_EXECUTED y
// fixture_phase=DELETED_NOT_TESTED. La Fase C (Resolve real sobre esta key
// ya eliminada) es la que produce la evidencia final del caso.
//
// A DIFERENCIA de SuspendM5T5FixtureKeyEvidenceBuilder (M5-T5-D), no
// existe returned_status: DELETE /v1/keys/{key_id} devuelve 204 No Content
// sin body. Se registra delete_acknowledged=true, que significa
// exactamente "Passport completó la operación sin error" — nunca un estado
// inventado.
public static class DeleteM5T5FixtureKeyEvidenceBuilder
{
    public const string CaseId = "M5-T5-E-KEY-PREP-DELETE";

    // Placeholder literal — NUNCA el key_id real interpolado aquí (mismo
    // criterio que DeleteKeyEvidenceBuilder, M3-T5).
    public const string Operation = "DELETE /v1/keys/{key_id}";

    public static EvidenceRecord BuildSuccess(
        string keyIdFingerprint,
        string keyValueFingerprint,
        int? httpStatus,
        string commitSha,
        DateTime executedAtUtc,
        string? automatedTestReference)
    {
        var responseSanitized = new Dictionary<string, object?>
        {
            ["passport_response_received"] = true,
            ["delete_acknowledged"]         = true,
            ["response_body_expected"]      = false, // 204 No Content por contrato.
            ["retry_count"]                 = 0,
            ["oauth_real_token_requests"]  = 1,
            ["create_key_real_calls"]      = 0,
            ["suspend_key_real_calls"]     = 0,
            ["activate_key_real_calls"]    = 0,
            ["delete_key_real_calls"]      = 1,
            ["list_keys_real_calls"]       = 0,
            ["resolve_real_calls"]          = 0,
            ["payment_real_calls"]          = 0,
            ["retrieve_real_calls"]         = 0,
            ["repeat_guard_consumed"]      = true,
            ["certification_test_result"] = "NOT_YET_EXECUTED",
            ["fixture_phase"]               = "DELETED_NOT_TESTED",
        };

        return new EvidenceRecord(
            CaseId: CaseId,
            ExecutedAtUtc: FormatTimestamp(executedAtUtc),
            Environment: EvidenceRecord.EnvironmentSandbox,
            BackendCommitSha: commitSha,
            Operation: Operation,
            HttpStatus: httpStatus,
            Result: EvidenceRecord.ResultPass,
            RequestSanitized: BuildRequestSanitized(keyIdFingerprint, keyValueFingerprint),
            ResponseSanitized: responseSanitized,
            AutomatedTestReference: automatedTestReference,
            Notes: "M5-T5-E-KEY-PREP-DELETE: Fase B del fixture nuevo de M5-T5-E (Fase A Create -> Fase B " +
                   "Delete). Operación irreversible por contrato de Passport (XPAY-292). NO representa que el " +
                   "caso de certificación M5-T5-E se haya ejecutado — el Resolve real sobre esta key ya " +
                   "eliminada requiere una Fase C separada y autorizada explícitamente.",
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
        ["fixture_source"]                = "M5_T5_E_PHASE_A_PRIVATE_CAPTURE",
        ["fixture_key_id_fingerprint"]    = keyIdFingerprint,
        ["fixture_key_value_fingerprint"] = keyValueFingerprint,
        ["key_type"]                       = "BCODE",
    };

    private static Dictionary<string, object?> BuildFailureResponseSanitized(
        int? httpStatus, string? safeErrorCode, string? safeErrorMessage) => new()
    {
        ["passport_response_received"] = true,
        ["delete_acknowledged"]         = false,
        ["observed_http_status"]        = httpStatus,
        ["safe_error_code"]             = safeErrorCode,
        ["safe_error_message"]          = safeErrorMessage,
        ["oauth_real_token_requests"]  = 1,
        ["create_key_real_calls"]      = 0,
        ["suspend_key_real_calls"]     = 0,
        ["activate_key_real_calls"]    = 0,
        ["delete_key_real_calls"]      = 1,
        ["list_keys_real_calls"]       = 0,
        ["resolve_real_calls"]          = 0,
        ["payment_real_calls"]          = 0,
        ["retrieve_real_calls"]         = 0,
        ["repeat_guard_consumed"]      = true,
        ["certification_test_result"] = "NOT_YET_EXECUTED",
        ["fixture_phase"]               = "DELETE_REJECTED",
    };

    private static string FormatTimestamp(DateTime executedAtUtc) =>
        executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
}
