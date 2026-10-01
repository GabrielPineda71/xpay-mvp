namespace Xpay.PassportSandboxHarness;

// XPAY-570 — construye el EvidenceRecord saneado para M5-T5-MISSING-AMOUNT
// (demostración de que XPAY rechaza LOCALMENTE, antes de cualquier HTTP, un
// Create Bre-B Payment sin `amount`). Función PURA de transformación — no
// hace I/O, no llama a Passport, no escribe archivos (eso es
// EvidenceWriter) — 100% testeable offline. Mismo patrón exacto que
// CreateKeyMissingEvidenceBuilder (M3-T6-MISSING, XPAY-344).
//
// SEMÁNTICA CRÍTICA (igual que M3-T6-MISSING): aquí el bloqueo LOCAL ES el
// resultado DESEADO y ESPERADO del subcaso — Result=PASS, nunca FAIL. La
// distinción request/response entre "validación local" y "respuesta real
// de Passport" se representa dentro de request_sanitized/response_sanitized
// (Dictionary<string,object?> de forma libre) — el esquema de EvidenceRecord
// no se modifica.
public static class CreatePaymentMissingAmountEvidenceBuilder
{
    public const string CaseId    = "M5-T5-MISSING-AMOUNT";
    public const string Operation = "POST /v1/payments/breb — blocked before transport";

    public static EvidenceRecord BuildBlockedAsExpected(
        string accountId,
        string commitSha,
        DateTime executedAtUtc,
        string? automatedTestReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        var requestSanitized = new Dictionary<string, object?>
        {
            ["account_id_fingerprint"] = Fingerprint.Compute(accountId),
            ["missing_field"]          = "amount",
            ["validation_layer"]       = "LOCAL",
        };

        var responseSanitized = new Dictionary<string, object?>
        {
            ["passport_http_attempted"]   = false,
            ["transport_result"]          = "NOT_ATTEMPTED",
            ["certification_case_result"] = "PASS",
        };

        return new EvidenceRecord(
            CaseId: CaseId,
            ExecutedAtUtc: FormatTimestamp(executedAtUtc),
            Environment: EvidenceRecord.EnvironmentSandbox,
            BackendCommitSha: commitSha,
            Operation: Operation,
            HttpStatus: null,
            // Result=PASS: el bloqueo local ES el resultado deseado de este
            // subcaso — nunca "FAIL" aquí, para no leerse como que el
            // harness falló.
            Result: EvidenceRecord.ResultPass,
            RequestSanitized: requestSanitized,
            ResponseSanitized: responseSanitized,
            AutomatedTestReference: automatedTestReference,
            Notes: "XPAY rechazó localmente la solicitud Create Bre-B Payment por amount ausente, " +
                   "antes de cualquier llamada HTTP a Passport (fail-closed por diseño).",
            ReviewStatus: EvidenceRecord.ReviewPendingPassportReview);
    }

    private static string FormatTimestamp(DateTime executedAtUtc) =>
        executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
}
