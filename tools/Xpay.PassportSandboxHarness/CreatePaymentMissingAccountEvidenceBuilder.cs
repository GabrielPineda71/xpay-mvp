namespace Xpay.PassportSandboxHarness;

// XPAY-571 — construye el EvidenceRecord saneado para M5-T5-MISSING-ACCOUNT
// (demostración de que XPAY rechaza LOCALMENTE, antes de cualquier HTTP, un
// Create Bre-B Payment sin `account_id`). Función PURA de transformación —
// no hace I/O, no llama a Passport, no escribe archivos (eso es
// EvidenceWriter) — 100% testeable offline. Mismo patrón exacto que
// CreatePaymentMissingAmountEvidenceBuilder (M5-T5-MISSING-AMOUNT, XPAY-570).
//
// SEMÁNTICA CRÍTICA (igual que XPAY-570/M3-T6-MISSING): aquí el bloqueo
// LOCAL ES el resultado DESEADO y ESPERADO del subcaso — Result=PASS,
// nunca FAIL.
//
// Nota de saneamiento: a diferencia de CreatePaymentMissingAmountEvidenceBuilder
// (que sí incluye account_id_fingerprint, porque ahí account_id era un
// valor real presente), aquí no hay ningún account_id real que
// fingerprintear — el campo ausente ES account_id. Se usa
// Fingerprint.AbsentMarker explícitamente (nunca Fingerprint.Compute sobre
// una cadena vacía "por casualidad") para dejar constancia inequívoca de
// que no hubo ningún valor, ni siquiera uno redactado.
public static class CreatePaymentMissingAccountEvidenceBuilder
{
    public const string CaseId    = "M5-T5-MISSING-ACCOUNT";
    public const string Operation = "POST /v1/payments/breb — blocked before transport";

    public static EvidenceRecord BuildBlockedAsExpected(
        string commitSha,
        DateTime executedAtUtc,
        string? automatedTestReference)
    {
        var requestSanitized = new Dictionary<string, object?>
        {
            ["account_id_fingerprint"] = Fingerprint.AbsentMarker,
            ["missing_field"]          = "account_id",
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
            Notes: "XPAY rechazó localmente la solicitud Create Bre-B Payment por account_id ausente, " +
                   "antes de cualquier llamada HTTP a Passport (fail-closed por diseño).",
            ReviewStatus: EvidenceRecord.ReviewPendingPassportReview);
    }

    private static string FormatTimestamp(DateTime executedAtUtc) =>
        executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
}
