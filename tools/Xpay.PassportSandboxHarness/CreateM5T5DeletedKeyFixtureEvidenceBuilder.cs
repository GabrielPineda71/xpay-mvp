namespace Xpay.PassportSandboxHarness;

// XPAY-613 — construye el EvidenceRecord saneado de TRAZABILIDAD para la
// FASE A (PREP-CREATE) del fixture nuevo de M5-T5-E. Función PURA — no
// hace I/O, no llama a Passport, no escribe archivos.
//
// CRÍTICO: esta evidencia NUNCA representa que el caso de certificación
// M5-T5-E (deleted key) se ejecutó o pasó — sólo documenta la PREPARACIÓN
// de su fixture. Por eso incluye explícitamente
// certification_test_result=NOT_YET_EXECUTED y
// fixture_phase=CREATED_NOT_DELETED, mismo criterio ya establecido por
// CreateM5T5SuspendedKeyFixtureEvidenceBuilder (M5-T5-D-KEY-PREP-CREATE).
//
// Misma divergencia deliberada de la política uniforme "key_value siempre
// REDACTED" que XPAY-575: aquí SÍ se incluye key_value_fingerprint (nunca
// el valor crudo), porque la trazabilidad key_id<->key_value es
// exactamente la causa raíz que ese ticket corrigió — un fingerprint
// SHA-256 truncado no revela el valor original y permite correlacionar
// este fixture con su private capture sin abrir ese archivo.
public static class CreateM5T5DeletedKeyFixtureEvidenceBuilder
{
    public const string CaseId    = "M5-T5-E-KEY-PREP-CREATE";
    public const string Operation = "POST /v1/keys";

    public static EvidenceRecord BuildFixtureCreated(
        string accountId,
        string keyValueFingerprint,
        string? keyId,
        string? keyStatus,
        int? httpStatus,
        string commitSha,
        DateTime executedAtUtc,
        string? automatedTestReference,
        bool privateArtifactCaptured)
    {
        var requestSanitized = new Dictionary<string, object?>
        {
            ["key_type"]               = "BCODE",
            ["account_id_fingerprint"] = Fingerprint.Compute(accountId),
            ["key_value_fingerprint"]  = keyValueFingerprint,
            ["display_name_present"]   = true,
        };

        var responseSanitized = new Dictionary<string, object?>
        {
            ["status"]                       = keyStatus,
            ["key_id_present"]                = !string.IsNullOrWhiteSpace(keyId),
            ["key_id_fingerprint"]            = Fingerprint.Compute(keyId),
            ["private_artifact_captured"]      = privateArtifactCaptured,
            ["certification_test_result"]     = "NOT_YET_EXECUTED",
            ["fixture_phase"]                  = "CREATED_NOT_DELETED",
            ["delete_key_real_calls"]         = 0,
            ["resolve_real_calls"]             = 0,
            ["payment_real_calls"]             = 0,
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
            Notes: "M5-T5-E-KEY-PREP-CREATE: fixture desechable NUEVO creado para el futuro subcaso M5-T5-E " +
                   "(deleted key). NO representa que el caso de certificación se haya ejecutado — la llave " +
                   "todavía NO está eliminada. key_value preservado de forma recuperable en private capture: " +
                   "es precisamente lo que faltó en el fixture histórico de M4-T3-B (XPAY-492), cuyo key_value " +
                   "se perdió de forma irrecuperable y por eso no puede reutilizarse para este caso.",
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
        string keyValueFingerprint, string reason, int? httpStatus, string commitSha, DateTime executedAtUtc,
        string? safeErrorCode = null, string? safeErrorMessage = null) => new(
        CaseId: CaseId,
        ExecutedAtUtc: FormatTimestamp(executedAtUtc),
        Environment: EvidenceRecord.EnvironmentSandbox,
        BackendCommitSha: commitSha,
        Operation: Operation,
        HttpStatus: null,
        Result: EvidenceRecord.ResultFail,
        RequestSanitized: new Dictionary<string, object?> { ["key_value_fingerprint"] = keyValueFingerprint },
        ResponseSanitized: BuildFailureResponseSanitized(httpStatus, safeErrorCode, safeErrorMessage),
        AutomatedTestReference: null,
        Notes: EvidenceRecord.NotePrefixPassportHttpFailure + reason,
        ReviewStatus: EvidenceRecord.ReviewPendingPassportReview);

    private static Dictionary<string, object?> BuildFailureResponseSanitized(
        int? httpStatus, string? safeErrorCode, string? safeErrorMessage) => new()
    {
        ["observed_http_status"]       = httpStatus,
        ["safe_error_code"]            = safeErrorCode,
        ["safe_error_message"]         = safeErrorMessage,
        ["certification_test_result"] = "NOT_YET_EXECUTED",
        ["fixture_phase"]               = "CREATE_FAILED",
    };

    private static string FormatTimestamp(DateTime executedAtUtc) =>
        executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
}
