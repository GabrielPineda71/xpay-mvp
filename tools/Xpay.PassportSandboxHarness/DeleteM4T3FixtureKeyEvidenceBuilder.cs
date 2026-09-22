namespace Xpay.PassportSandboxHarness;

// XPAY-492 — construye el EvidenceRecord saneado para el SEGUNDO paso de
// preparación de M4-T3-B (Delete Key — sobre la llave desechable dedicada).
// Función PURA — no hace I/O, no llama a Passport, no escribe archivos (eso
// es EvidenceWriter).
//
// case_id DELIBERADAMENTE distinto de "M3-T5"/"M3-T7" (DeleteKeyEvidenceBuilder/
// DeleteAlreadyDeletedKeyEvidenceBuilder), de "M4-T3-B-KEY-PREP-CREATE" (el
// paso anterior) y de "M4-T3-B" (la evidencia del Create QR final) — cada
// paso de esta secuencia produce su propio expediente, nunca conflatados.
//
// DIFERENCIA CLAVE frente a Create/CreateM4T3DeletedFixtureKeyEvidenceBuilder
// (mismo motivo que DeleteKeyEvidenceBuilder, M3-T5): Delete Key exitoso
// responde 204 No Content SIN body — `response_sanitized` queda
// deliberadamente VACÍO; nunca se fabrica `status`/`id`/`deleted_at` que
// Passport no devolvió.
//
// Política de saneamiento — misma disciplina que DeleteKeyEvidenceBuilder
// (XPAY-334): key_id (el enviado) SÓLO fingerprint — NUNCA el valor real,
// NUNCA interpolado en `operation` (placeholder literal "{key_id}").
public static class DeleteM4T3FixtureKeyEvidenceBuilder
{
    public const string CaseId = "M4-T3-B-KEY-PREP-DELETE";

    // Placeholder literal — NUNCA el key_id real interpolado aquí.
    public const string Operation = "DELETE /v1/keys/{key_id}";

    public static EvidenceRecord BuildSuccess(
        string keyId,
        int? httpStatus,
        string commitSha,
        DateTime executedAtUtc,
        string? automatedTestReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);

        var requestSanitized = new Dictionary<string, object?>
        {
            ["key_id_fingerprint"] = Fingerprint.Compute(keyId),
        };

        // Deliberadamente vacío — 204 No Content no trae ningún campo que
        // saneando pueda conservarse; inventar uno sería fabricar datos que
        // Passport nunca devolvió.
        var responseSanitized = new Dictionary<string, object?>();

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
            Notes: "M4-T3-B-KEY-PREP-DELETE: llave desechable eliminada. El key_id_fingerprint de este registro debe coincidir con el de M4-T3-B-KEY-PREP-CREATE — misma llave, ciclo Create→Delete completo.",
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
    // filtra el response body ni el key_id.
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
