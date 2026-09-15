using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-340 — construye el EvidenceRecord saneado para M3-T2 (Resolve Key)
// a partir del request real enviado y la respuesta real recibida (o su
// ausencia, en caso de fallo). Función PURA de transformación — no hace
// I/O, no llama a Passport, no escribe archivos (eso es EvidenceWriter) —
// 100% testeable offline.
//
// Política de saneamiento:
//   - customer_id (request): SOLO fingerprint — nunca el valor real.
//   - key_type (request): se conserva tal cual (no es sensible, es sólo la
//     categoría — mismo criterio que Create/Suspend/Activate Key).
//   - key_value (request): SOLO fingerprint (a diferencia de Create Key,
//     que usa el literal "REDACTED" — aquí se sigue explícitamente la
//     instrucción XPAY-340 de preferir key_value_fingerprint, útil para
//     correlacionar la misma llave Bre-B entre varias evidencias sin
//     revelar su valor).
//   - resolution_id (response, campo raíz `id`): SOLO fingerprint, y
//     ETIQUETADO EXPLÍCITAMENTE como `resolution_id_fingerprint` — NUNCA
//     como key_id/key_id_fingerprint. Es un concepto distinto (confirmado
//     contractualmente en XPAY-310/324: el `id` de Resolve Key es el
//     resolution_id, reutilizable como idempotency key de un Payment; no
//     tiene relación con el key_id remoto de Create/Suspend/Activate/
//     Delete Key).
//   - owner/participant/account (response, objetos anidados con posible
//     PII: nombres, identificación, número de cuenta): NUNCA se conserva
//     ningún campo literal — sólo su presencia (booleano), mismo criterio
//     conservador ya aplicado a `display_name` en CreateKeyEvidenceBuilder.
public static class ResolveKeyEvidenceBuilder
{
    public const string CaseId    = "M3-T2";
    public const string Operation = "POST /v1/resolve-key";

    public static EvidenceRecord BuildSuccess(
        PassportResolveKeyRequest request,
        PassportResolveKeyResponse response,
        int? httpStatus,
        string commitSha,
        DateTime executedAtUtc,
        string? automatedTestReference)
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
            // CRÍTICO: nunca "key_id_fingerprint" — este es el
            // resolution_id, un concepto distinto (ver nota de clase).
            ["resolution_id_fingerprint"] = Fingerprint.Compute(response.Id),
            ["owner_present"]             = response.Owner is not null,
            ["participant_present"]      = response.Participant is not null,
            ["account_present"]          = response.Account is not null,
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

    // XPAY-340 (mismo criterio XPAY-325/326/332/334/336) — bloqueo LOCAL
    // (target ausente, commit SHA no resoluble, u otra condición ANTES de
    // intentar HTTP): nunca se finge como respuesta de Passport.
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
    // filtra el response body, customer_id ni key_value.
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
