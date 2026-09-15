using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-326 — construye el EvidenceRecord saneado para M3-T3 (Suspend Key) a
// partir del key_id real enviado y la respuesta real recibida (o su
// ausencia, en caso de fallo). Función PURA de transformación — no hace
// I/O, no llama a Passport, no escribe archivos (eso es EvidenceWriter) —
// 100% testeable offline.
//
// Política de saneamiento (misma disciplina que CreateKeyEvidenceBuilder,
// XPAY-325):
//   - key_id (el REAL enviado en el path, y el `id` remoto de la respuesta):
//     SOLO fingerprint (Fingerprint.Compute) — NUNCA el valor real, NUNCA
//     en `operation` (que usa un placeholder literal "{key_id}", jamás el
//     ID real interpolado).
//   - status remoto: se conserva tal cual (no es sensible).
public static class SuspendKeyEvidenceBuilder
{
    public const string CaseId = "M3-T3";

    // Placeholder literal — NUNCA el key_id real interpolado aquí.
    public const string Operation = "PATCH /v1/keys/{key_id}/suspend";

    public static EvidenceRecord BuildSuccess(
        string keyId,
        PassportKeyResponse response,
        int? httpStatus,
        string commitSha,
        DateTime executedAtUtc,
        string? automatedTestReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentNullException.ThrowIfNull(response);

        var requestSanitized = new Dictionary<string, object?>
        {
            ["key_id_fingerprint"] = Fingerprint.Compute(keyId),
        };

        var responseSanitized = new Dictionary<string, object?>
        {
            ["status"] = response.Status,
            ["id_fingerprint"] = Fingerprint.Compute(response.Id),
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

    // XPAY-326 (mismo criterio XPAY-325 FASE 12) — bloqueo LOCAL (target
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

    // XPAY-326 — fallo real de Passport (HTTP no-2xx, o error de protocolo
    // tras una respuesta 2xx inutilizable). Nunca filtra el response body ni
    // el key_id.
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
