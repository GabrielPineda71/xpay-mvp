using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-325 — construye el EvidenceRecord saneado para M3-T1 (Create Key) a
// partir del request real enviado y la respuesta real recibida (o su
// ausencia, en caso de fallo). Función PURA de transformación — no hace
// I/O, no llama a Passport, no escribe archivos (eso es responsabilidad de
// EvidenceWriter) — 100% testeable offline.
//
// Política de saneamiento (XPAY-325 FASE 9/10):
//   - key_type: se conserva tal cual (no es sensible, es sólo la categoría).
//   - account_id / id / customer_id remotos: SOLO fingerprint (Fingerprint.Compute).
//   - key_value: SIEMPRE "REDACTED" — nunca el valor real, ni siquiera fingerprint
//     (el propio valor de la llave, a diferencia de un ID opaco de Passport,
//     puede ser PII directa según el key_type — teléfono, email, cédula).
//   - display_name: NUNCA se conserva el texto literal (política conservadora
//     explícita — XPAY-325 FASE 10: "No asumir que display_name es seguro"),
//     sólo si estaba presente o no.
public static class CreateKeyEvidenceBuilder
{
    public const string CaseId    = "M3-T1";
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
            ["key_type"]                = request.Key.KeyType.ToString(),
            ["account_id_fingerprint"]  = Fingerprint.Compute(request.AccountId),
            ["key_value"]               = "REDACTED",
            ["display_name_present"]    = request.DisplayName is not null,
        };

        var responseSanitized = new Dictionary<string, object?>
        {
            ["status"]                  = response.Status,
            ["key_type"]                = response.Key?.KeyType,
            ["id_fingerprint"]          = Fingerprint.Compute(response.Id),
            ["account_id_fingerprint"]  = Fingerprint.Compute(response.AccountId),
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

    // XPAY-325 FASE 12 — bloqueo LOCAL (config/host/guard u otra excepción
    // ANTES de intentar HTTP): nunca se finge como respuesta de Passport.
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

    // XPAY-325 FASE 12 — fallo real de Passport (HTTP no-2xx, o error de
    // protocolo tras una respuesta 2xx inutilizable).
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
