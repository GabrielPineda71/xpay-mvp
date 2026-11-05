using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-572 — construye el EvidenceRecord saneado para
// M5-T5-INVALID-RESOLUTION (Create Bre-B Payment con resolution_id
// sintético, inválido/inexistente). Función PURA de transformación — no
// hace I/O, no llama a Passport, no escribe archivos (eso es
// EvidenceWriter) — 100% testeable offline.
//
// A DIFERENCIA de M5-T5-MISSING-AMOUNT/ACCOUNT (XPAY-570/571, bloqueo
// LOCAL antes de HTTP, Result=PASS por diseño): este subcaso SÍ alcanza
// Passport real — PassportPaymentClient.CreateBrebPaymentAsync sólo
// valida que resolution_id no esté vacío, nunca su validez semántica. Por
// tanto el resultado se clasifica con el MISMO criterio ya establecido
// por el proyecto para fallos de transporte reales (README.md — "Bloqueo
// local vs. fallo remoto", mismo patrón que M3-T6-INVALID/M3-T7):
// Result=FAIL cuando Passport responde con un error — un rechazo de
// Passport es precisamente el comportamiento ESPERADO y DESEADO que este
// caso busca observar y documentar, nunca un fallo del harness. La
// interpretación de certificación final requiere revisión de Passport,
// nunca codificada aquí como un veredicto.
public static class CreatePaymentInvalidResolutionEvidenceBuilder
{
    public const string CaseId    = "M5-T5-INVALID-RESOLUTION";
    public const string Operation = "POST /v1/payments/breb";

    // Passport respondió con un error de transporte real (401/403, o
    // cualquier respuesta non-2xx) — ÉXITO SEMÁNTICO esperado de este
    // subcaso negativo, pero Result=FAIL a nivel transporte, mismo
    // criterio ya usado por M3-T6-INVALID/M3-T7 (ver README §"Nota sobre
    // M3-T6-INVALID y M3-T7").
    public static EvidenceRecord BuildExpectedRejection(
        string accountId,
        string invalidResolutionId,
        string amountValue,
        int? httpStatus,
        string? safeErrorCode,
        string? safeErrorMessage,
        string commitSha,
        DateTime executedAtUtc,
        string? automatedTestReference)
    {
        var requestSanitized = BuildRequestSanitized(accountId, invalidResolutionId, amountValue);

        var responseSanitized = new Dictionary<string, object?>
        {
            ["request_executed"]         = true,
            ["request_accepted"]         = false,
            ["passport_response_received"] = true,
            ["observed_http_status"]     = httpStatus,
            ["safe_error_code"]          = safeErrorCode,
            ["safe_error_message"]       = safeErrorMessage,
            ["payment_id_present"]       = false,
            ["retry_count"]              = 0,
            ["oauth_real_token_requests"] = 1,
            ["payment_create_calls"]     = 1,
            ["resolve_real_calls"]       = 0,
            ["retrieve_real_calls"]      = 0,
            ["repeat_guard_consumed"]    = true,
            ["certification_case_result"] = "EXPECTED_PROVIDER_REJECTION",
        };

        return new EvidenceRecord(
            CaseId: CaseId,
            ExecutedAtUtc: FormatTimestamp(executedAtUtc),
            Environment: EvidenceRecord.EnvironmentSandbox,
            BackendCommitSha: commitSha,
            Operation: Operation,
            HttpStatus: null,
            Result: EvidenceRecord.ResultFail,
            RequestSanitized: requestSanitized,
            ResponseSanitized: responseSanitized,
            AutomatedTestReference: automatedTestReference,
            Notes: EvidenceRecord.NotePrefixPassportHttpFailure +
                   "resolution_id sintético/inexistente rechazado por Passport — " +
                   "ESTE ES EL RESULTADO ESPERADO Y DESEADO de M5-T5-INVALID-RESOLUTION, " +
                   "no un fallo del harness. Interpretación de certificación final pendiente de Passport.",
            ReviewStatus: EvidenceRecord.ReviewPendingPassportReview);
    }

    // Passport ACEPTÓ la request (2xx) — resultado INESPERADO que requiere
    // revisión inmediata del director. Nunca se interpreta aquí como
    // "éxito" del caso — el llamador debe detenerse y escalar.
    public static EvidenceRecord BuildUnexpectedAcceptance(
        string accountId,
        string invalidResolutionId,
        string amountValue,
        string? paymentId,
        string? paymentStatus,
        string commitSha,
        DateTime executedAtUtc,
        string? automatedTestReference)
    {
        var requestSanitized = BuildRequestSanitized(accountId, invalidResolutionId, amountValue);

        var responseSanitized = new Dictionary<string, object?>
        {
            ["request_executed"]          = true,
            ["request_accepted"]          = true,
            ["passport_response_received"] = true,
            ["status"]                    = paymentStatus,
            ["payment_id_present"]        = !string.IsNullOrWhiteSpace(paymentId),
            ["payment_id_fingerprint"]    = Fingerprint.Compute(paymentId),
            ["retry_count"]               = 0,
            ["oauth_real_token_requests"] = 1,
            ["payment_create_calls"]      = 1,
            ["resolve_real_calls"]        = 0,
            ["retrieve_real_calls"]       = 0,
            ["repeat_guard_consumed"]     = true,
            ["certification_case_result"] = "UNEXPECTED_ACCEPTANCE",
        };

        return new EvidenceRecord(
            CaseId: CaseId,
            ExecutedAtUtc: FormatTimestamp(executedAtUtc),
            Environment: EvidenceRecord.EnvironmentSandbox,
            BackendCommitSha: commitSha,
            Operation: Operation,
            HttpStatus: null,
            // PASS = "la llamada HTTP completó" — NUNCA se afirma que el
            // caso de certificación negativo tuvo éxito; ver
            // certification_case_result=UNEXPECTED_ACCEPTANCE arriba y las
            // notas explícitas — requiere revisión humana inmediata.
            Result: EvidenceRecord.ResultPass,
            RequestSanitized: requestSanitized,
            ResponseSanitized: responseSanitized,
            AutomatedTestReference: automatedTestReference,
            Notes: "UNEXPECTED_ACCEPTANCE: Passport aceptó un Create Bre-B Payment con resolution_id " +
                   "sintético/inexistente — comportamiento NO esperado por este caso negativo. " +
                   "Requiere revisión inmediata del director técnico antes de cualquier acción " +
                   "adicional (NO se ejecutó Retrieve ni ninguna llamada de seguimiento).",
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

    private static Dictionary<string, object?> BuildRequestSanitized(
        string accountId, string invalidResolutionId, string amountValue) => new()
    {
        ["account_id_present"]              = !string.IsNullOrWhiteSpace(accountId),
        ["account_id_fingerprint"]           = Fingerprint.Compute(accountId),
        ["resolution_id_present"]            = true,
        ["invalid_resolution_source"]        = "synthetic_local",
        ["invalid_resolution_fingerprint"]   = Fingerprint.Compute(invalidResolutionId),
        ["amount_value"]                     = amountValue,
        ["currency"]                         = PassportPaymentClient.CopCurrency,
    };

    private static string FormatTimestamp(DateTime executedAtUtc) =>
        executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
}
