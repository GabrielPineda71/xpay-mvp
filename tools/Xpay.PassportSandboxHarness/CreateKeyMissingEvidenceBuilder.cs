namespace Xpay.PassportSandboxHarness;

// XPAY-344 — construye el EvidenceRecord saneado para M3-T6-MISSING
// (demostración de que XPAY rechaza LOCALMENTE, antes de cualquier HTTP,
// un Create Key sin key_value). Función PURA de transformación — no hace
// I/O, no llama a Passport, no escribe archivos (eso es EvidenceWriter) —
// 100% testeable offline.
//
// SEMÁNTICA CRÍTICA (XPAY-344 §5): a diferencia de todos los demás casos
// (M3-T1/T3/T4/T5/T7), aquí el bloqueo LOCAL ES el resultado DESEADO y
// ESPERADO del subcaso — no un fallo operativo del harness. Por eso este
// builder NO reutiliza el patrón `BuildLocalBlocked` (Result=FAIL) usado
// en el resto del harness para bloqueos operativos genuinos (target
// ausente, commit SHA no resoluble): aquí se distingue explícitamente
// entre:
//   - BuildBlockedAsExpected: el guard productivo de PassportKeyClient.
//     CreateKeyAsync rechazó el request por key_value vacío — ÉXITO del
//     subcaso, Result=PASS.
//   - BuildLocalBlocked: bloqueo operativo GENUINO (target ausente, commit
//     SHA no resoluble) ANTES incluso de intentar construir el request —
//     esto sí es un fallo del harness, Result=FAIL, mismo criterio que el
//     resto del sistema.
//
// EvidenceRecord no tiene campos `transport_result`/`certification_case_
// result`/`validation_layer`/`missing_field`/`passport_http_attempted` —
// no se modifica el esquema; esa semántica se representa dentro de
// request_sanitized/response_sanitized (Dictionary<string,object?> de
// forma libre), exactamente como pide XPAY-344 §5.
public static class CreateKeyMissingEvidenceBuilder
{
    public const string CaseId    = "M3-T6-MISSING";
    public const string Operation = "POST /v1/keys — blocked before transport";

    public static EvidenceRecord BuildBlockedAsExpected(
        string accountId,
        string keyType,
        string commitSha,
        DateTime executedAtUtc,
        string? automatedTestReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyType);

        var requestSanitized = new Dictionary<string, object?>
        {
            ["account_id_fingerprint"] = Fingerprint.Compute(accountId),
            ["key_type"]               = keyType,
            ["missing_field"]          = "key_value",
            ["validation_layer"]       = "LOCAL",
        };

        var responseSanitized = new Dictionary<string, object?>
        {
            ["passport_http_attempted"]     = false,
            ["transport_result"]            = "NOT_ATTEMPTED",
            ["certification_case_result"]   = "PASS",
        };

        return new EvidenceRecord(
            CaseId: CaseId,
            ExecutedAtUtc: FormatTimestamp(executedAtUtc),
            Environment: EvidenceRecord.EnvironmentSandbox,
            BackendCommitSha: commitSha,
            Operation: Operation,
            HttpStatus: null,
            // Result=PASS: el bloqueo local ES el resultado deseado de
            // este subcaso — nunca "FAIL" aquí, para no leerse como que el
            // harness falló.
            Result: EvidenceRecord.ResultPass,
            RequestSanitized: requestSanitized,
            ResponseSanitized: responseSanitized,
            AutomatedTestReference: automatedTestReference,
            Notes: "XPAY rechazó localmente la solicitud Create Key por key_value ausente, " +
                   "antes de cualquier llamada HTTP a Passport (fail-closed por diseño).",
            ReviewStatus: EvidenceRecord.ReviewPendingPassportReview);
    }

    // Bloqueo LOCAL genuino (target ausente, commit SHA no resoluble) —
    // ANTES incluso de poder construir el request. A diferencia de
    // BuildBlockedAsExpected, esto SÍ es un fallo operativo del harness
    // (mismo criterio Result=FAIL que el resto del sistema).
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

    private static string FormatTimestamp(DateTime executedAtUtc) =>
        executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
}
