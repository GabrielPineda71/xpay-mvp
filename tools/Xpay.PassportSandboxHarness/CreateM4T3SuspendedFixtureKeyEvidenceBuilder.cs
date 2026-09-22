using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-474 — construye el EvidenceRecord saneado para la PREPARACIÓN de
// M4-T3-A (Create Key — llave desechable dedicada). Función PURA — no hace
// I/O, no llama a Passport, no escribe archivos (eso es EvidenceWriter).
//
// case_id DELIBERADAMENTE distinto de "M3-T1" (CreateKeyEvidenceBuilder) y
// de "M4-T3-A" (CreateQrStaticSuspendedKeyEvidenceBuilder, la evidencia del
// Create QR final) — esta es la evidencia de un paso de PREPARACIÓN, nunca
// debe confundirse con ninguno de los dos.
//
// Política de saneamiento — misma disciplina que CreateKeyEvidenceBuilder
// (XPAY-325):
//   - key_type: se conserva tal cual (no es sensible, es sólo la categoría).
//   - account_id / id remoto: SOLO fingerprint.
//   - key_value: SIEMPRE "REDACTED" — nunca el valor real, ni siquiera
//     fingerprint (mismo criterio conservador que CreateKeyEvidenceBuilder,
//     aplicado uniformemente sin importar que BCODE en sí no sea PII
//     clásica — la política no distingue por key_type).
//   - display_name: NUNCA el texto literal, sólo si estaba presente.
public static class CreateM4T3SuspendedFixtureKeyEvidenceBuilder
{
    public const string CaseId    = "M4-T3-A-KEY-PREP-CREATE";
    public const string Operation = "POST /v1/keys";

    public static EvidenceRecord BuildSuccess(
        PassportCreateKeyRequest request,
        PassportKeyResponse response,
        int? httpStatus,
        string commitSha,
        DateTime executedAtUtc,
        string? automatedTestReference)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);

        var requestSanitized = new Dictionary<string, object?>
        {
            ["key_type"]               = request.Key.KeyType.ToString(),
            ["account_id_fingerprint"] = Fingerprint.Compute(request.AccountId),
            ["key_value"]              = "REDACTED",
            ["display_name_present"]   = request.DisplayName is not null,
        };

        var responseSanitized = new Dictionary<string, object?>
        {
            ["status"]                 = response.Status,
            ["key_type"]               = response.Key?.KeyType,
            ["id_fingerprint"]         = Fingerprint.Compute(response.Id),
            ["account_id_fingerprint"] = Fingerprint.Compute(response.AccountId),
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
            // XPAY-474 — recordatorio explícito: este `id_fingerprint` es
            // el ÚNICO rastro del key_id real en todo el harness. El valor
            // real NUNCA se persiste aquí ni en ningún otro artefacto — el
            // operador debe recuperarlo manualmente desde Passport Sandbox
            // Dashboard (mismo protocolo ya usado para PASSPORT_TEST_QR_KEY_ID,
            // XPAY-460) y configurarlo él mismo en PASSPORT_TEST_QR_SUSPENDED_KEY_ID.
            Notes: "M4-T3-A-KEY-PREP-CREATE: llave desechable creada. El key_id real NO fue persistido — recupérelo manualmente desde Passport Sandbox Dashboard y configure PASSPORT_TEST_QR_SUSPENDED_KEY_ID usted mismo.",
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

    // Fallo real de Passport (HTTP no-2xx, o error de protocolo). Nunca
    // filtra el response body, account_id ni key_value.
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
