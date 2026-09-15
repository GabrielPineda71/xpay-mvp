namespace Xpay.PassportSandboxHarness;

// XPAY-334 — construye el EvidenceRecord saneado para M3-T5 (Delete Key) a
// partir del key_id real enviado (o su ausencia, en caso de fallo).
// Función PURA de transformación — no hace I/O, no llama a Passport, no
// escribe archivos (eso es EvidenceWriter) — 100% testeable offline.
//
// DIFERENCIA CLAVE frente a SuspendKeyEvidenceBuilder/ActivateKeyEvidenceBuilder:
// Delete Key exitoso responde 204 No Content SIN body (contrato confirmado
// XPAY-292/293, `IPassportKeyClient.DeleteKeyAsync` devuelve `Task`, no
// `Task<PassportKeyResponse>` — la ausencia de excepción ES la
// confirmación de éxito). Por tanto `BuildSuccess` NO recibe ningún objeto
// de respuesta y `response_sanitized` queda deliberadamente VACÍO — nunca
// se fabrica `status`/`id`/`deleted_at` que Passport no devolvió.
//
// Política de saneamiento (misma que Suspend/Activate):
//   - key_id (el REAL enviado en el path): SOLO fingerprint
//     (Fingerprint.Compute) — NUNCA el valor real, NUNCA en `operation`
//     (placeholder literal "{key_id}", jamás el ID real interpolado).
public static class DeleteKeyEvidenceBuilder
{
    public const string CaseId = "M3-T5";

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
            Notes: null,
            ReviewStatus: EvidenceRecord.ReviewPendingPassportReview);
    }

    // XPAY-334 (mismo criterio XPAY-326/332) — bloqueo LOCAL (target
    // ausente, commit SHA no resoluble, u otra condición ANTES de intentar
    // HTTP): nunca se finge como respuesta de Passport.
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

    // XPAY-334 — fallo real de Passport (HTTP no-2xx, o error de protocolo).
    // Nunca filtra el response body ni el key_id.
    public static EvidenceRecord BuildPassportHttpFailure(
        string reason, int? httpStatus, string commitSha, DateTime executedAtUtc) => new(
        CaseId: CaseId,
        ExecutedAtUtc: FormatTimestamp(executedAtUtc),
        Environment: EvidenceRecord.EnvironmentSandbox,
        BackendCommitSha: commitSha,
        Operation: Operation,
        HttpStatus: httpStatus,
        Result: EvidenceRecord.ResultFail,
        RequestSanitized: new Dictionary<string, object?>(),
        ResponseSanitized: new Dictionary<string, object?>(),
        AutomatedTestReference: null,
        Notes: EvidenceRecord.NotePrefixPassportHttpFailure + reason,
        ReviewStatus: EvidenceRecord.ReviewPendingPassportReview);

    private static string FormatTimestamp(DateTime executedAtUtc) =>
        executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
}
