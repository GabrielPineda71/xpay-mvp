using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-506 — construye el EvidenceRecord saneado para M4-T4 (Create QR Code
// — DINÁMICO) a partir del request real enviado y la respuesta real
// recibida (o su ausencia, en caso de fallo). Función PURA de
// transformación — no hace I/O, no llama a Passport, no escribe archivos
// (eso es responsabilidad de EvidenceWriter) — 100% testeable offline.
//
// Mirror ESTRUCTURAL de CreateQrStaticEvidenceBuilder (M4-T1) — MISMA
// política de saneamiento exacta, sólo cambia CaseId y (por construcción
// del request) el literal capturado en el campo `type`.
//
//   - key_id / customer_id (request y response): SOLO fingerprint.
//   - type/channel (request): se conservan tal cual — no sensibles.
//   - amount_present / vat_present / inc_present / tip_present (request):
//     booleanos únicamente, nunca valores literales de vat_type/vat_value/
//     etc. M4-T4 siempre debe registrar vat_present=true (contrato
//     DYNAMIC) y amount_present=false (decisión de payload mínimo de este
//     caso, XPAY-506 §5) — ambos verificables por cualquier revisor sin
//     inspeccionar código.
//   - qr_code_reference (request/response): SOLO fingerprint.
//   - id (response, qr id remoto): SOLO fingerprint, etiquetado
//     EXPLÍCITAMENTE `qr_id_fingerprint` — nunca conflatado con
//     key_id_fingerprint/resolution_id_fingerprint.
//   - qr_code_data / qr_code_image (response): NUNCA el valor literal —
//     ambos pueden codificar el payload EMVCo/imagen completa de un QR de
//     pago real. Se registra únicamente presencia + fingerprint
//     (Fingerprint.Compute ya devuelve el marcador ABSENT para null/"").
//     Esto satisface el requisito de evidencia del Anexo ("Imagen del QR +
//     payload") de forma auditable y sanitizada: demuestra que Passport
//     efectivamente devolvió una imagen/payload, sin reproducirlos.
public static class CreateQrDynamicEvidenceBuilder
{
    public const string CaseId    = "M4-T4";
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
            // CRÍTICO: nunca "key_id_fingerprint" ni
            // "resolution_id_fingerprint" — este es el qr id remoto, un
            // concepto distinto de ambos.
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
            Notes: null,
            ReviewStatus: EvidenceRecord.ReviewPendingPassportReview);
    }

    // Bloqueo LOCAL (target ausente, commit SHA no resoluble) ANTES de
    // intentar HTTP: nunca se finge como respuesta de Passport.
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
        Notes: EvidenceRecord.NotePrefixPassportHttpFailure + reason,
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
