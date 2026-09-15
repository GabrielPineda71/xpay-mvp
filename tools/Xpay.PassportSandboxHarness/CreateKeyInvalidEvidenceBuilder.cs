using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-344 — construye el EvidenceRecord saneado para M3-T6-INVALID
// (Create Key con key_value de formato inválido — llega REALMENTE a
// Passport, a diferencia de M3-T6-MISSING). Función PURA — no hace I/O, no
// llama a Passport, no escribe archivos (eso es EvidenceWriter).
//
// SEMÁNTICA CRÍTICA (mismo criterio ya validado en M3-T7, XPAY-336): este
// builder NUNCA convierte el resultado de TRANSPORTE en un veredicto de
// certificación. `Result=PASS` significa ÚNICAMENTE "la llamada HTTP
// completó sin excepción" — podría ser el escenario INDESEADO (Passport
// aceptó un key_value inválido que debía rechazar). `Result=FAIL` significa
// ÚNICAMENTE "la llamada HTTP falló" — podría ser exactamente el rechazo
// documentalmente esperado (HTTP 400, confirmado contra documentación
// oficial de Passport en XPAY-344). La interpretación de certificación
// queda SIEMPRE en `PENDING_DIRECTOR_REVIEW` hasta revisión explícita.
public static class CreateKeyInvalidEvidenceBuilder
{
    public const string CaseId    = "M3-T6-INVALID";
    public const string Operation = "POST /v1/keys";

    // Documentado contra Passport (XPAY-343/344, fuente:
    // PASSPORT_OFFICIAL_DOCS) — "faltan campos requeridos o contienen
    // valores incorrectos" → HTTP 400. Se registra como EXPECTATIVA
    // documental únicamente, nunca como el status realmente observado
    // (que el stack actual no expone de forma estructurada — mismo
    // criterio ya documentado para `http_status=null` en el resto del
    // harness).
    private const int ExpectedHttpStatus = 400;
    private const string ExpectedHttpStatusProvenance = "PASSPORT_OFFICIAL_DOCS";
    private const string ExpectedErrorShape = "UNKNOWN";
    private const string CertificationInterpretationPending = "PENDING_DIRECTOR_REVIEW";

    public static EvidenceRecord BuildResult(
        string accountId,
        PassportKeyType keyType,
        string invalidDimension,
        bool transportSucceeded,
        PassportKeyResponse? response,
        int? httpStatus,
        string? failureReason,
        string commitSha,
        DateTime executedAtUtc,
        string? automatedTestReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(invalidDimension);

        var requestSanitized = new Dictionary<string, object?>
        {
            ["account_id_fingerprint"]           = Fingerprint.Compute(accountId),
            ["key_type"]                         = keyType.ToString(),
            ["invalid_dimension"]                = invalidDimension,
            // Nunca el literal — sólo su presencia.
            ["invalid_value_present"]             = true,
            ["expected_http_status"]             = ExpectedHttpStatus,
            ["expected_http_status_provenance"]  = ExpectedHttpStatusProvenance,
            ["expected_error_shape"]             = ExpectedErrorShape,
        };

        var responseSanitized = new Dictionary<string, object?>
        {
            ["passport_http_attempted"]        = true,
            ["transport_result"]               = transportSucceeded ? "SUCCESS" : "FAILURE",
            ["certification_interpretation"]   = CertificationInterpretationPending,
        };
        if (transportSucceeded && response is not null)
        {
            // Passport aceptó el valor inválido — se conserva la mínima
            // información saneada para investigar la anomalía, nunca el
            // key_value real.
            responseSanitized["status"]          = response.Status;
            responseSanitized["id_fingerprint"]  = Fingerprint.Compute(response.Id);
        }

        return new EvidenceRecord(
            CaseId: CaseId,
            ExecutedAtUtc: FormatTimestamp(executedAtUtc),
            Environment: EvidenceRecord.EnvironmentSandbox,
            BackendCommitSha: commitSha,
            Operation: Operation,
            HttpStatus: httpStatus,
            Result: transportSucceeded ? EvidenceRecord.ResultPass : EvidenceRecord.ResultFail,
            RequestSanitized: requestSanitized,
            ResponseSanitized: responseSanitized,
            AutomatedTestReference: automatedTestReference,
            Notes: transportSucceeded
                ? null
                : $"{EvidenceRecord.NotePrefixPassportHttpFailure}{failureReason} — interpretación de certificación pendiente: " +
                  "un rechazo aquí puede ser exactamente el comportamiento CORRECTO esperado de Passport ante un key_value inválido.",
            ReviewStatus: EvidenceRecord.ReviewPendingPassportReview);
    }

    // Bloqueo LOCAL genuino (target ausente, key_type no soportado para
    // generación de valor inválido, commit SHA no resoluble) — ANTES de
    // cualquier HTTP.
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
