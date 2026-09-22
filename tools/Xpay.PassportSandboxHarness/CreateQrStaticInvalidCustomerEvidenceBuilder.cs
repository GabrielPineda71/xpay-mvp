using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-471 — construye el EvidenceRecord saneado para M4-T3-C (Create QR
// Code STATIC con customer_id INCORRECTO). Función PURA — no hace I/O, no
// llama a Passport, no escribe archivos.
//
// CASO NEGATIVO — mismo criterio de no-interpretación que M4-T3-A/B (ver
// CreateQrStaticSuspendedKeyEvidenceBuilder / README del expediente).
//
// Política de saneamiento: idéntica a CreateQrStaticEvidenceBuilder. El
// customer_id sintético inválido (InvalidCustomerIdGenerator) NO es
// secreto ni PII real, pero de todas formas se trata con el mismo criterio
// conservador (sólo fingerprint) que cualquier otro identificador — nunca
// se distingue en el saneamiento si un identificador es "real" o
// "sintético", exactamente para no crear una superficie donde ese detalle
// pudiera filtrarse por accidente.
public static class CreateQrStaticInvalidCustomerEvidenceBuilder
{
    public const string CaseId    = "M4-T3-C";
    public const string Operation = "POST /v1/qrcodes";

    public static EvidenceRecord BuildSuccess(
        PassportCreateQrCodeRequest request,
        PassportQrCodeResponse response,
        int? httpStatus,
        string commitSha,
        DateTime executedAtUtc,
        string? automatedTestReference)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);

        var requestSanitized = new Dictionary<string, object?>
        {
            ["key_id_fingerprint"]      = Fingerprint.Compute(request.KeyId),
            ["customer_id_fingerprint"] = Fingerprint.Compute(request.CustomerId),
            ["customer_id_synthetic"]   = true,
            ["type"]                    = request.Type.ToString(),
            ["channel"]                 = request.Channel.ToString(),
            ["amount_present"]          = request.Amount is not null,
            ["additional_info_present"] = request.AdditionalInfo is not null,
            ["vat_present"]             = request.Vat is not null,
            ["inc_present"]             = request.Inc is not null,
            ["tip_present"]             = request.Tip is not null,
            ["qr_code_reference_present"]     = request.QrCodeReference is not null,
            ["qr_code_reference_fingerprint"] = Fingerprint.Compute(request.QrCodeReference),
        };

        var responseSanitized = new Dictionary<string, object?>
        {
            ["qr_id_fingerprint"]         = Fingerprint.Compute(response.Id),
            ["status"]                    = response.Status,
            ["type"]                      = response.Type,
            ["qr_code_data_present"]      = !string.IsNullOrEmpty(response.QrCodeData),
            ["qr_code_data_fingerprint"]  = Fingerprint.Compute(response.QrCodeData),
            ["qr_code_image_present"]     = !string.IsNullOrEmpty(response.QrCodeImage),
            ["qr_code_image_fingerprint"] = Fingerprint.Compute(response.QrCodeImage),
            ["key_id_fingerprint"]        = Fingerprint.Compute(response.KeyId),
            ["customer_id_fingerprint"]   = Fingerprint.Compute(response.CustomerId),
            ["qr_code_reference_fingerprint"] = Fingerprint.Compute(response.QrCodeReference),
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
            Notes: "M4-T3-C: UNEXPECTED_SUCCESS_FOR_NEGATIVE_TEST_CASE — Passport aceptó la operación pese a que customer_id era un valor sintético inválido (nunca registrado). Esto NO certifica automáticamente ni éxito ni fallo del caso M4-T3-C; requiere revisión contractual explícita.",
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
        string reason, int? httpStatus, string commitSha, DateTime executedAtUtc,
        string? safeErrorCode = null, string? safeErrorMessage = null) => new(
        CaseId: CaseId,
        ExecutedAtUtc: FormatTimestamp(executedAtUtc),
        Environment: EvidenceRecord.EnvironmentSandbox,
        BackendCommitSha: commitSha,
        Operation: Operation,
        HttpStatus: null,
        Result: EvidenceRecord.ResultFail,
        RequestSanitized: new Dictionary<string, object?>(),
        ResponseSanitized: BuildFailureResponseSanitized(httpStatus, safeErrorCode, safeErrorMessage),
        AutomatedTestReference: null,
        Notes: "M4-T3-C: CERTIFICATION_EXPECTED_ERROR_CANDIDATE (transport_result=FAIL). " +
               EvidenceRecord.NotePrefixPassportHttpFailure + reason +
               " — Si este error corresponde específicamente al escenario de customer_id incorrecto " +
               "es una interpretación de certificación separada, no codificada aquí.",
        ReviewStatus: EvidenceRecord.ReviewPendingPassportReview);

    private static Dictionary<string, object?> BuildFailureResponseSanitized(
        int? httpStatus, string? safeErrorCode, string? safeErrorMessage)
    {
        var dict = new Dictionary<string, object?>();
        if (httpStatus is not null) dict["observed_http_status"] = httpStatus;
        if (safeErrorCode is not null) dict["error_code"] = safeErrorCode;
        if (safeErrorMessage is not null) dict["error_message"] = safeErrorMessage;
        return dict;
    }

    private static string FormatTimestamp(DateTime executedAtUtc) =>
        executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
}
