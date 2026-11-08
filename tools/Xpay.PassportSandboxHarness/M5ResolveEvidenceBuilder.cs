using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-545 — construye el EvidenceRecord saneado para M5-T2-RESOLVE
// (Resolve Key dedicado a la certificación de Payment B→A). Función PURA
// — sin I/O, sin red, sin escritura de archivos.
//
// DELIBERADAMENTE UN case_id DISTINTO de "M3-T2" ("M5-T2-RESOLVE") — esta
// evidencia se escribe en su PROPIO subdirectorio
// (docs/certificacion/passport-breb/M5-T2-RESOLVE/), NUNCA en
// docs/certificacion/passport-breb/M3-T2/ — evitando exactamente el
// problema detectado y corregido en XPAY-545 §7 (un archivo de esta
// familia apareciendo mezclado con la evidencia histórica de M3-T2, ya
// ACEPTADA y CONGELADA). M3-T2 permanece intacto y nunca se reutiliza
// como contenedor de esta evidencia.
//
// Misma política de saneamiento que ResolveKeyEvidenceBuilder (M3-T2,
// XPAY-340) — duplicada aquí deliberadamente en vez de reutilizar esa
// clase, conforme al principio ya establecido "duplicar antes que
// compartir riesgo" con executors/evidencia de casos ya CERRADOS:
//   - customer_id (request): SOLO fingerprint.
//   - key_type (request): se conserva tal cual (categoría, no sensible).
//   - key_value (request): SOLO fingerprint.
//   - resolution_id (response): SOLO fingerprint, etiquetado
//     explícitamente como resolution_id_fingerprint (nunca key_id).
//   - owner/participant/account (response): sólo presencia (booleano).
public static class M5ResolveEvidenceBuilder
{
    public const string CaseId    = "M5-T2-RESOLVE";
    public const string Operation = "POST /v1/resolve-key";

    public static EvidenceRecord BuildSuccess(
        PassportResolveKeyRequest request,
        PassportResolveKeyResponse response,
        int? httpStatus,
        string commitSha,
        DateTime executedAtUtc,
        string? automatedTestReference,
        bool privateArtifactCaptured)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);

        var requestSanitized = new Dictionary<string, object?>
        {
            ["customer_id_fingerprint"] = Fingerprint.Compute(request.CustomerId),
            ["key_type"]                = request.Key.KeyType.ToString(),
            ["key_value_fingerprint"]   = Fingerprint.Compute(request.Key.KeyValue),
        };

        var responseSanitized = new Dictionary<string, object?>
        {
            ["request_executed"]          = true,
            ["request_accepted"]          = true,
            ["resolution_id_fingerprint"] = Fingerprint.Compute(response.Id),
            ["owner_present"]             = response.Owner is not null,
            ["participant_present"]       = response.Participant is not null,
            ["account_present"]           = response.Account is not null,
            ["expires_at_present"]        = !string.IsNullOrWhiteSpace(response.ExpiresAt),
            ["private_artifact_captured"] = privateArtifactCaptured,
            ["retry_count"]               = 0,
            ["resolve_calls"]             = 1,
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
            Notes: "Dedicado a certificación M5-T2 (Payment B->A) — case_id distinto de M3-T2 a propósito; M3-T2 permanece congelado/ACCEPTED.",
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
