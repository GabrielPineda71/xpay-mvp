using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-619 — construye el EvidenceRecord saneado del subcaso M5-T5-C
// ("Exceso de límites de Bre-B — transacción superior a $12 millones COP").
// Función PURA — no hace I/O, no llama a Passport, no escribe archivos.
//
// Mismo criterio de clasificación transporte que el resto de los casos
// negativos (M3-T6-INVALID/M3-T7/M5-T5-INVALID-RESOLUTION/
// M5-T5-SUSPENDED-KEY/M5-T5-DELETED-KEY): un rechazo real de Passport es
// el resultado ESPERADO — Result=FAIL a nivel transporte, nunca leído
// como que el harness falló.
//
// SEPARACIÓN CRÍTICA entre dos cosas distintas:
//   - `limit_rejection`         -> true / null (UNKNOWN). NUNCA false.
//   - `certification_case_result` -> EXPECTED_LIMIT_REJECTION (PASS del
//     caso) sólo si limit_rejection==true; REJECTION_WITHOUT_LIMIT_SIGNAL
//     o UNEXPECTED_PAYMENT_ACCEPTANCE en otro caso, ambos INCONCLUSIVE.
// Un rechazo cuya causa no se puede atribuir al límite NO cierra el caso.
public static class CreatePaymentOverLimitEvidenceBuilder
{
    public const string CaseId    = "M5-T5-OVER-LIMIT";
    public const string Operation = "POST /v1/payments/breb";

    public static EvidenceRecord BuildExpectedRejection(
        string resolutionIdFingerprint,
        string accountIdFingerprint,
        int? httpStatus,
        string? safeErrorCode,
        string? safeErrorMessage,
        bool? limitRejection,
        string commitSha,
        DateTime executedAtUtc,
        string? automatedTestReference)
    {
        var confirmed = limitRejection == true;

        var responseSanitized = new Dictionary<string, object?>
        {
            ["passport_response_received"]  = true,
            ["request_attempted"]           = true,
            ["observed_http_status"]        = httpStatus,
            ["safe_error_code"]             = safeErrorCode,
            ["safe_error_message"]          = safeErrorMessage,
            ["payment_accepted"]            = false,
            ["payment_accepted_unexpectedly"] = false,
            // true / null (=UNKNOWN) — nunca false: la ausencia de una
            // señal reconocible de máximo de monto no prueba lo contrario.
            ["limit_rejection"]             = limitRejection,
            ["retry_count"]                 = 0,
            ["oauth_real_token_requests"]   = 1,
            ["payment_real_calls"]          = 1,
            ["resolve_real_calls"]          = 0,
            ["retrieve_real_calls"]         = 0,
            ["create_key_real_calls"]       = 0,
            ["suspend_key_real_calls"]      = 0,
            ["delete_key_real_calls"]       = 0,
            ["list_keys_real_calls"]        = 0,
            ["repeat_guard_consumed"]       = true,
            ["certification_case_result"]   = confirmed
                ? "EXPECTED_LIMIT_REJECTION"
                : "REJECTION_WITHOUT_LIMIT_SIGNAL",
            ["t5_c_result"]                 = confirmed ? "PASS" : "INCONCLUSIVE",
        };

        return new EvidenceRecord(
            CaseId: CaseId,
            ExecutedAtUtc: FormatTimestamp(executedAtUtc),
            Environment: EvidenceRecord.EnvironmentSandbox,
            BackendCommitSha: commitSha,
            Operation: Operation,
            HttpStatus: null,
            Result: EvidenceRecord.ResultFail,
            RequestSanitized: BuildRequestSanitized(resolutionIdFingerprint, accountIdFingerprint),
            ResponseSanitized: responseSanitized,
            AutomatedTestReference: automatedTestReference,
            Notes: EvidenceRecord.NotePrefixPassportHttpFailure + (confirmed
                ? "Payment rechazado por Passport con señal inequívoca de exceso de monto máximo — ESTE ES EL " +
                  "RESULTADO ESPERADO Y DESEADO de M5-T5-C, no un fallo del harness. Interpretación de " +
                  "certificación final pendiente de Passport."
                : "Payment rechazado por Passport, pero SIN señal inequívoca de que la causa fuera el límite de " +
                  "monto. NO se declara PASS del caso: T5_C_RESULT=INCONCLUSIVE. Requiere análisis del director " +
                  "antes de cualquier reintento, que este ejecutor no permite (repeat guard consumido)."),
            ReviewStatus: EvidenceRecord.ReviewPendingPassportReview);
    }

    // Passport ACEPTÓ un pago por encima del umbral certificable —
    // resultado INESPERADO. Nunca se interpreta como éxito del caso
    // negativo. No se consulta el ciclo de vida (el executor no conoce
    // RetrievePaymentAsync) ni se intenta un segundo Payment.
    public static EvidenceRecord BuildUnexpectedAcceptance(
        string resolutionIdFingerprint,
        string accountIdFingerprint,
        PassportPaymentResponse response,
        string commitSha,
        DateTime executedAtUtc,
        string? automatedTestReference)
    {
        ArgumentNullException.ThrowIfNull(response);

        var responseSanitized = new Dictionary<string, object?>
        {
            ["passport_response_received"]      = true,
            ["request_attempted"]               = true,
            ["payment_accepted"]                = true,
            ["payment_accepted_unexpectedly"]   = true,
            ["returned_status"]                 = response.Status,
            ["payment_id_present"]              = !string.IsNullOrWhiteSpace(response.Id),
            ["payment_id_fingerprint"]          = Fingerprint.Compute(response.Id),
            ["limit_rejection"]                 = null, // UNKNOWN — nunca false.
            ["retry_count"]                     = 0,
            ["oauth_real_token_requests"]       = 1,
            ["payment_real_calls"]              = 1,
            ["resolve_real_calls"]              = 0,
            ["retrieve_real_calls"]             = 0,
            ["create_key_real_calls"]           = 0,
            ["suspend_key_real_calls"]          = 0,
            ["delete_key_real_calls"]           = 0,
            ["list_keys_real_calls"]            = 0,
            ["repeat_guard_consumed"]           = true,
            ["certification_case_result"]       = "UNEXPECTED_PAYMENT_ACCEPTANCE",
            ["t5_c_result"]                     = "INCONCLUSIVE",
        };

        return new EvidenceRecord(
            CaseId: CaseId,
            ExecutedAtUtc: FormatTimestamp(executedAtUtc),
            Environment: EvidenceRecord.EnvironmentSandbox,
            BackendCommitSha: commitSha,
            Operation: Operation,
            HttpStatus: null,
            // PASS = "la llamada HTTP completó" — NUNCA se afirma que el
            // caso de certificación negativo tuvo éxito.
            Result: EvidenceRecord.ResultPass,
            RequestSanitized: BuildRequestSanitized(resolutionIdFingerprint, accountIdFingerprint),
            ResponseSanitized: responseSanitized,
            AutomatedTestReference: automatedTestReference,
            Notes: "UNEXPECTED_PAYMENT_ACCEPTANCE: Passport aceptó un pago de " +
                   CreatePaymentOverLimitExecutor.CertificationAmountValue + " COP, por encima del umbral " +
                   "certificable de 12.000.000 COP — comportamiento NO esperado por este caso negativo. " +
                   "T5_C_RESULT=INCONCLUSIVE, nunca PASS. NO se ejecutó Retrieve, NO se intentó un segundo " +
                   "Payment, NO se intentó ninguna compensación. El Anexo define Sandbox como entorno donde no " +
                   "se procesan fondos reales; el riesgo es de rastro de certificación, no financiero. Requiere " +
                   "revisión inmediata del director técnico.",
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
        ResponseSanitized: new Dictionary<string, object?>
        {
            ["request_attempted"]         = false,
            ["payment_real_calls"]        = 0,
            ["limit_rejection"]           = null,
            ["certification_case_result"] = "NOT_EXECUTED",
            ["t5_c_result"]               = "NOT_EXECUTED",
        },
        AutomatedTestReference: null,
        Notes: EvidenceRecord.NotePrefixLocalBlocked + reason,
        ReviewStatus: EvidenceRecord.ReviewPendingPassportReview);

    private static Dictionary<string, object?> BuildRequestSanitized(
        string resolutionIdFingerprint, string accountIdFingerprint) => new()
    {
        ["amount_value"]                  = CreatePaymentOverLimitExecutor.CertificationAmountValue,
        ["currency"]                      = PassportPaymentClient.CopCurrency,
        ["amount_above_annex_threshold"]  = true,
        ["annex_threshold_cop"]           = "12000000.00",
        ["resolution_id_fingerprint"]     = resolutionIdFingerprint,
        ["account_id_fingerprint"]        = accountIdFingerprint,
        ["resolution_source"]             = "M5_T2_RESOLVE_PRIVATE_CAPTURE",
        ["negative_fixture_not_used"]     = true,
        ["display_name_present"]          = false,
    };

    private static string FormatTimestamp(DateTime executedAtUtc) =>
        executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
}
