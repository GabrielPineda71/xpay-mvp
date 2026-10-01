using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-577 — construye el EvidenceRecord saneado FINAL del subcaso
// M5-T5-D (envío/resolución hacia clave Bre-B suspendida). A DIFERENCIA
// de los JSON preparatorios de XPAY-575 (M5-T5-D-KEY-PREP-CREATE) y
// XPAY-576 (M5-T5-D-KEY-PREP-SUSPEND), ÉSTE es el candidato de evidencia
// de certificación del caso — por eso su case_id es "M5-T5-SUSPENDED-KEY"
// (el nombre del subcaso oficial, sin sufijo "-PREP-"), sin ningún campo
// que lo marque como preparatorio.
//
// Mismo criterio de clasificación transporte que M3-T6-INVALID/M3-T7/
// M5-T5-INVALID-RESOLUTION (XPAY-572): un rechazo real de Passport es el
// resultado ESPERADO de este caso negativo — Result=FAIL a nivel
// transporte, nunca leído como que el harness falló.
public static class ResolveM5T5SuspendedKeyEvidenceBuilder
{
    public const string CaseId    = "M5-T5-SUSPENDED-KEY";
    public const string Operation = "POST /v1/resolve-key";

    public static EvidenceRecord BuildExpectedRejection(
        string keyIdFingerprint,
        string keyValueFingerprint,
        int? httpStatus,
        string? safeErrorCode,
        string? safeErrorMessage,
        bool? currentSuspensionConfirmed,
        string commitSha,
        DateTime executedAtUtc,
        string? automatedTestReference)
    {
        var requestSanitized = BuildRequestSanitized(keyIdFingerprint, keyValueFingerprint);

        var responseSanitized = new Dictionary<string, object?>
        {
            ["passport_response_received"]      = true,
            ["observed_http_status"]            = httpStatus,
            ["safe_error_code"]                 = safeErrorCode,
            ["safe_error_message"]              = safeErrorMessage,
            ["resolution_returned"]             = false,
            ["current_suspension_confirmed"]    = currentSuspensionConfirmed, // true / null (=UNKNOWN) — nunca false por defecto.
            ["retry_count"]                      = 0,
            ["oauth_real_token_requests"]       = 1,
            ["create_key_real_calls"]           = 0,
            ["suspend_key_real_calls"]          = 0,
            ["resolve_real_calls"]               = 1,
            ["payment_real_calls"]               = 0,
            ["retrieve_real_calls"]              = 0,
            ["delete_key_real_calls"]           = 0,
            ["reactivate_key_real_calls"]       = 0,
            ["repeat_guard_consumed"]           = true,
            ["certification_case_result"]      = "EXPECTED_PROVIDER_REJECTION",
        };

        return new EvidenceRecord(
            CaseId: CaseId,
            ExecutedAtUtc: FormatTimestamp(executedAtUtc),
            Environment: EvidenceRecord.EnvironmentSandbox,
            BackendCommitSha: commitSha,
            Operation: Operation,
            HttpStatus: null,
            Result: EvidenceRecord.ResultFail,
            RequestSanitized: requestSanitized,
            ResponseSanitized: responseSanitized,
            AutomatedTestReference: automatedTestReference,
            Notes: EvidenceRecord.NotePrefixPassportHttpFailure +
                   "Resolve rechazado por Passport contra la key suspendida (fixture XPAY-575 creado, " +
                   "XPAY-576 suspendido y confirmado SUSPENDED antes de esta llamada) — ESTE ES EL RESULTADO " +
                   "ESPERADO Y DESEADO de M5-T5-D, no un fallo del harness. Interpretación de certificación " +
                   "final pendiente de Passport.",
            ReviewStatus: EvidenceRecord.ReviewPendingPassportReview);
    }

    // Passport ACEPTÓ el Resolve pese a la suspensión confirmada —
    // resultado INESPERADO que requiere revisión inmediata del director.
    // NUNCA se interpreta como éxito del caso negativo, y NUNCA dispara un
    // Payment (garantizado estructuralmente: este executor no conoce
    // IPassportPaymentClient).
    public static EvidenceRecord BuildUnexpectedSuccess(
        string keyIdFingerprint,
        string keyValueFingerprint,
        PassportResolveKeyResponse response,
        string commitSha,
        DateTime executedAtUtc,
        string? automatedTestReference)
    {
        var requestSanitized = BuildRequestSanitized(keyIdFingerprint, keyValueFingerprint);

        var responseSanitized = new Dictionary<string, object?>
        {
            ["passport_response_received"]  = true,
            ["resolution_returned"]         = true,
            ["resolution_fingerprint"]      = Fingerprint.Compute(response.Id),
            ["current_suspension_confirmed"] = false,
            ["retry_count"]                  = 0,
            ["oauth_real_token_requests"]   = 1,
            ["create_key_real_calls"]       = 0,
            ["suspend_key_real_calls"]      = 0,
            ["resolve_real_calls"]           = 1,
            ["payment_real_calls"]           = 0,
            ["retrieve_real_calls"]          = 0,
            ["delete_key_real_calls"]       = 0,
            ["reactivate_key_real_calls"]   = 0,
            ["repeat_guard_consumed"]       = true,
            ["certification_case_result"]  = "UNEXPECTED_RESOLVE_SUCCESS",
        };

        return new EvidenceRecord(
            CaseId: CaseId,
            ExecutedAtUtc: FormatTimestamp(executedAtUtc),
            Environment: EvidenceRecord.EnvironmentSandbox,
            BackendCommitSha: commitSha,
            Operation: Operation,
            HttpStatus: null,
            // PASS = "la llamada HTTP completó" — NUNCA se afirma que el
            // caso de certificación negativo tuvo éxito.
            Result: EvidenceRecord.ResultPass,
            RequestSanitized: requestSanitized,
            ResponseSanitized: responseSanitized,
            AutomatedTestReference: automatedTestReference,
            Notes: "UNEXPECTED_RESOLVE_SUCCESS: Passport resolvió la key pese a estar suspendida " +
                   "(fixture XPAY-575 creado, XPAY-576 suspendido y confirmado SUSPENDED antes de esta " +
                   "llamada) — comportamiento NO esperado por este caso negativo. Requiere revisión inmediata " +
                   "del director técnico. NO se creó ningún Payment, NO se transfirió la resolución a env, " +
                   "NO se ejecutó ninguna llamada de seguimiento.",
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

    private static Dictionary<string, object?> BuildRequestSanitized(string keyIdFingerprint, string keyValueFingerprint) => new()
    {
        ["fixture_create_completed"]           = true,
        ["fixture_suspend_completed"]          = true,
        ["fixture_key_id_fingerprint"]        = keyIdFingerprint,
        ["fixture_key_value_fingerprint"]     = keyValueFingerprint,
        ["key_type"]                            = "BCODE",
        ["historical_fixture_not_used"]       = true,
        ["active_0268_not_used"]                = true,
    };

    private static string FormatTimestamp(DateTime executedAtUtc) =>
        executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
}
