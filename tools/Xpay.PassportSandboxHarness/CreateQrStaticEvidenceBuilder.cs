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
//     key_id REMOTO real de la Key ACTIVE de certificación,
//     PASSPORT_TEST_QR_KEY_ID desde XPAY-460 — antes PASSPORT_TEST_NEW_KEY_ID,
//     corregido por ser la llave DELETED de M3).
//   - customer_id (request): SOLO fingerprint — mismo criterio que
//     ResolveKeyEvidenceBuilder.
//   - type/channel (request): se conservan tal cual — no son sensibles, son
//     sólo la categoría de protocolo (mismo criterio que key_type en
//     Create/Suspend/Activate Key).
//   - amount_present / vat_present / inc_present / tip_present (request):
//     booleanos únicamente. M4-T1 nunca debe incluir amount (QR ESTÁTICO
//     sin monto). vat/inc/tip son siempre enviados por el executor
//     (confirmado por Passport para M4-T1, XPAY-458 — vat desde XPAY-360,
//     inc y tip agregados en XPAY-458). Todos se registran explícitamente
//     para que la evidencia sea auto-verificable sin depender de una
//     inspección externa del código. Nunca se exponen valores literales de
//     vat_type/vat_value/vat_base_value/inc_type/inc_value/tip_type/
//     tip_value (presentes o ausentes) — sólo la presencia/ausencia se
//     registra.
//   - qr_code_reference (request/response): SOLO fingerprint (nunca el
//     valor real, aunque no sea PII — mismo criterio conservador que otros
//     IDs opacos, XPAY-458).
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
            ["inc_present"]             = request.Inc is not null,
            ["tip_present"]             = request.Tip is not null,
            ["qr_code_reference_present"]    = request.QrCodeReference is not null,
            ["qr_code_reference_fingerprint"] = Fingerprint.Compute(request.QrCodeReference),
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
    //
    // XPAY-358 — safeErrorCode/safeErrorMessage son OPCIONALES y llegan
    // YA SANITIZADOS por PassportErrorBodySanitizer (vía
    // PassportTransportException) — esta función NUNCA sanitiza nada por
    // sí misma, sólo los registra tal cual si están presentes. httpStatus
    // se mantiene disponible (viene de PassportTransportException.
    // StatusCode) pero EvidenceRecord.HttpStatus permanece null POR DISEÑO
    // — decisión deliberada, no un olvido: ese campo tiene una regla
    // documentada repo-wide ("siempre null por diseño", ver
    // EvidenceRecord.cs) que aplica a TODOS los casos de certificación, no
    // sólo M4-T1 — cambiarla unilateralmente aquí sería inconsistente con
    // esa convención compartida. El status HTTP conocido se expone
    // únicamente dentro de response_sanitized (extensión libre ya
    // establecida, mismo patrón que M3-T6-INVALID/M3-T7).
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
