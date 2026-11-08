using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-489 — construye el EvidenceRecord saneado para el intento de
// recuperación read-only, vía List Keys, del key_id de la llave histórica
// DELETED del ciclo M3 (fingerprint esperado 9654CF47D144, ver
// M3-T5/evidence). Función PURA — no hace I/O, no llama a Passport, no
// escribe archivos (eso es EvidenceWriter) y NUNCA escribe
// ~/.passport-sandbox.env — la eventual reparación de
// PASSPORT_TEST_NEW_KEY_ID es un paso manual explícitamente separado
// (XPAY-489, XPAY-488 §5).
//
// case_id DELIBERADAMENTE distinto de cualquier otro caso M3/M4 — esta no
// es evidencia de certificación de un caso del Anexo, es evidencia
// operativa interna de un intento de recuperación de configuración.
//
// Política de saneamiento — misma disciplina que el resto del expediente:
//   - key_type: se conserva literal (no es sensible).
//   - account_id: SOLO fingerprint.
//   - key_value (criterio de búsqueda): SIEMPRE "REDACTED", nunca el valor
//     real ni siquiera como fingerprint (mismo criterio conservador que
//     CreateKeyEvidenceBuilder).
//   - key_id de la llave encontrada (si hay ExactlyOneMatch): SOLO
//     fingerprint — NUNCA el valor real.
//   - Ambiguous (>1 coincidencia): NUNCA expone ningún detalle de los
//     candidatos individuales — sólo la cantidad. Exponer parcialmente
//     cualquier candidato facilitaría reconstruir información sensible por
//     descarte.
public static class RecoverDeletedKeyIdEvidenceBuilder
{
    public const string CaseId = "M4-T3-B-KEY-RECOVERY";
    public const string Operation = "GET /v1/keys";

    public static EvidenceRecord BuildSuccess(
        string accountId,
        PassportKeyType keyType,
        KeySelectionResult selection,
        int? httpStatus,
        string commitSha,
        DateTime executedAtUtc,
        string? automatedTestReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentNullException.ThrowIfNull(selection);

        var requestSanitized = new Dictionary<string, object?>
        {
            ["account_id_fingerprint"] = Fingerprint.Compute(accountId),
            ["key_type"]               = keyType.ToString(),
            ["key_value"]              = "REDACTED",
        };

        var responseSanitized = new Dictionary<string, object?>
        {
            ["selection_outcome"] = selection.Outcome.ToString(),
        };

        string notes;
        switch (selection.Outcome)
        {
            case KeySelectionOutcome.ExactlyOneMatch:
                responseSanitized["match_found"]    = true;
                responseSanitized["matches_count"]  = 1;
                responseSanitized["status"]         = selection.SelectedKey!.Status;
                responseSanitized["id_fingerprint"] = Fingerprint.Compute(selection.SelectedKey!.Id);
                notes =
                    "M4-T3-B-KEY-RECOVERY: coincidencia única encontrada. El campo `status` de esta " +
                    "evidencia refleja literalmente lo que Passport devolvió — este builder NO asume " +
                    "que un valor concreto (p.ej. \"DELETED\") sea el esperado; esa interpretación es " +
                    "responsabilidad del revisor humano. El key_id real NO fue persistido — este " +
                    "comando NUNCA escribe ~/.passport-sandbox.env; configure " +
                    "PASSPORT_TEST_NEW_KEY_ID usted mismo, de forma manual, si decide usar este " +
                    "resultado.";
                break;

            case KeySelectionOutcome.NoMatch:
                responseSanitized["match_found"]   = false;
                responseSanitized["matches_count"] = 0;
                notes =
                    "M4-T3-B-KEY-RECOVERY: NO_MATCH — ninguna llave en la respuesta de List Keys " +
                    "coincidió simultáneamente con account_id+key_type+key_value. Fail-closed: no se " +
                    "intenta ningún criterio alternativo ni fallback.";
                break;

            default: // Ambiguous
                responseSanitized["match_found"]   = false;
                responseSanitized["matches_count"] = ">1";
                notes =
                    "M4-T3-B-KEY-RECOVERY: AMBIGUOUS — más de una llave coincidió simultáneamente con " +
                    "account_id+key_type+key_value. Fail-closed: NUNCA se selecciona automáticamente " +
                    "la primera ni se expone ningún detalle de los candidatos individuales.";
                break;
        }

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
            Notes: notes,
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
