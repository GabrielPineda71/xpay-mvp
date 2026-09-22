using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-471 — construye el EvidenceRecord saneado para M4-T3-A (Create QR
// Code STATIC con llave Bre-B SUSPENDIDA). Función PURA — no hace I/O, no
// llama a Passport, no escribe archivos (eso es EvidenceWriter).
//
// CASO NEGATIVO — mismo criterio de no-interpretación ya establecido para
// M3-T6-INVALID/M3-T7 (ver docs/certificacion/passport-breb/README.md):
//   - `result=FAIL` a nivel EvidenceRecord (schema compartido, sólo admite
//     PASS/FAIL) significa ÚNICAMENTE que la llamada HTTP terminó en una
//     excepción de transporte/protocolo — NUNCA que el caso de
//     certificación "M4-T3-A" haya fallado. Un error podría ser
//     PRECISAMENTE el comportamiento correcto esperado (Passport
//     rechazando una llave suspendida).
//   - `result=PASS` (si Passport aceptara la operación pese a la llave
//     suspendida) tampoco implica que el caso de certificación haya
//     tenido éxito en el sentido esperado — podría ser un hallazgo
//     inesperado que amerita revisión.
//   - Este builder NUNCA decide "el error observado corresponde al
//     escenario ejecutado" — sólo registra transporte + diagnóstico
//     saneado. Esa interpretación es responsabilidad exclusiva de una
//     revisión contractual posterior (humana), nunca del harness.
//
// Política de saneamiento: idéntica a CreateQrStaticEvidenceBuilder (key_id/
// customer_id sólo fingerprint; type/channel literales; amount/vat/inc/tip/
// qr_code_reference sólo presencia+fingerprint cuando aplica; nunca
// qr_code_data/qr_code_image en texto).
public static class CreateQrStaticSuspendedKeyEvidenceBuilder
{
    public const string CaseId    = "M4-T3-A";
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
            // XPAY-471 — Passport ACEPTÓ la operación pese a la llave
            // suspendida usada como target. Esto es un ÉXITO a nivel
            // transporte, pero NO se afirma aquí que sea el resultado
            // "correcto" para el caso de certificación M4-T3-A (que espera
            // un error) — es exactamente lo opuesto de lo esperado y
            // requiere revisión explícita, nunca un juicio automático.
            Notes: "M4-T3-A: UNEXPECTED_SUCCESS_FOR_NEGATIVE_TEST_CASE — Passport aceptó la operación pese a que el target era una llave suspendida. Esto NO certifica automáticamente ni éxito ni fallo del caso M4-T3-A; requiere revisión contractual explícita.",
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

    // Fallo real de Passport (HTTP no-2xx, o error de protocolo). Nunca
    // filtra el response body, key_id ni customer_id.
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
        // XPAY-471 — CRÍTICO: `result=FAIL` aquí es ÚNICAMENTE el
        // resultado de TRANSPORTE (la llamada HTTP terminó en excepción).
        // NO afirma que el caso de certificación M4-T3-A haya fallado —
        // podría ser EXACTAMENTE el comportamiento correcto esperado
        // (Passport rechazando una llave suspendida). Si ese error
        // realmente corresponde al escenario "llave suspendida" (vs. algún
        // otro motivo no relacionado) requiere revisión contractual
        // explícita — nunca inferido automáticamente por este builder sólo
        // por tratarse de un HTTP 4xx.
        Notes: "M4-T3-A: CERTIFICATION_EXPECTED_ERROR_CANDIDATE (transport_result=FAIL). " +
               EvidenceRecord.NotePrefixPassportHttpFailure + reason +
               " — Si este error corresponde específicamente al escenario de llave suspendida " +
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
