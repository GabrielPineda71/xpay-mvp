using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-351 — construye el EvidenceRecord saneado para M4-T1 (Create QR Code
// — STATIC) a partir del request real enviado y la respuesta real recibida
// (o su ausencia, en caso de fallo). Función PURA de transformación — no
// hace I/O, no llama a Passport, no escribe archivos (eso es
// responsabilidad de EvidenceWriter) — 100% testeable offline.
//
// Política de saneamiento:
//   - key_id (request): SOLO fingerprint — nunca el valor real (es el
//     key_id REMOTO real de la llave de certificación, PASSPORT_TEST_NEW_KEY_ID).
//   - customer_id (request): SOLO fingerprint — mismo criterio que
//     ResolveKeyEvidenceBuilder.
//   - type/channel (request): se conservan tal cual — no son sensibles, son
//     sólo la categoría de protocolo (mismo criterio que key_type en
//     Create/Suspend/Activate Key).
//   - amount_present / vat_present (request): booleanos únicamente — M4-T1
//     nunca debe incluir amount ni vat (QR ESTÁTICO, XPAY-357: vat pasó a
//     ser opcional a nivel de DTO y ya no se envía para este caso), pero se
//     registran explícitamente para que la evidencia sea auto-verificable
//     sin depender de una inspección externa del código. Nunca se inventan
//     valores de vat_type/vat_value/vat_base_value cuando vat está ausente
//     — sólo la presencia/ausencia se registra.
//   - id (response, campo raíz, el qr id remoto): SOLO fingerprint,
//     ETIQUETADO EXPLÍCITAMENTE como `qr_id_fingerprint` — NUNCA como
//     key_id/key_id_fingerprint ni resolution_id_fingerprint (son conceptos
//     distintos, mismo criterio de no-conflación ya aplicado en
//     ResolveKeyEvidenceBuilder para resolution_id).
//   - qr_code_data / qr_code_image (response): NUNCA se conserva el valor
//     literal — ambos pueden codificar el payload EMVCo completo de un QR
//     de pago (potencialmente sensible/reconstruible). Se registra
//     únicamente presencia + fingerprint (Fingerprint.Compute ya maneja
//     null/"" devolviendo el marcador ABSENT, nunca un hash real de cadena
//     vacía).
//   - key_id / customer_id (response, si Passport los refleja de vuelta):
//     mismo tratamiento que en el request — SOLO fingerprint.
public static class CreateQrStaticEvidenceBuilder
{
    public const string CaseId    = "M4-T1";
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
            ["vat_present"]             = request.Vat is not null,
            ["qr_code_reference_present"] = request.QrCodeReference is not null,
        };

        var responseSanitized = new Dictionary<string, object?>
        {
            // CRÍTICO: nunca "key_id_fingerprint" ni
            // "resolution_id_fingerprint" — este es el qr id remoto, un
            // concepto distinto de ambos (ver nota de clase).
            ["qr_id_fingerprint"]          = Fingerprint.Compute(response.Id),
            ["status"]                     = response.Status,
            ["type"]                       = response.Type,
            ["qr_code_data_present"]       = !string.IsNullOrEmpty(response.QrCodeData),
            ["qr_code_data_fingerprint"]   = Fingerprint.Compute(response.QrCodeData),
            ["qr_code_image_present"]      = !string.IsNullOrEmpty(response.QrCodeImage),
            ["qr_code_image_fingerprint"]  = Fingerprint.Compute(response.QrCodeImage),
            ["key_id_fingerprint"]         = Fingerprint.Compute(response.KeyId),
            ["customer_id_fingerprint"]    = Fingerprint.Compute(response.CustomerId),
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
            Notes: "M4-T1: NOT_EXECUTED_IN_SANDBOX hasta que esta evidencia provenga de una ejecución real autorizada — XPAY-351 es implementación offline únicamente.",
            ReviewStatus: EvidenceRecord.ReviewPendingPassportReview);
    }

    // XPAY-351 (mismo criterio XPAY-325/326/332/334/336/340/344) — bloqueo
    // LOCAL (target ausente, commit SHA no resoluble) ANTES de intentar
    // HTTP: nunca se finge como respuesta de Passport.
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
