using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-465 — orquesta la ejecución REAL de M4-T2 (Decode QR Code): lee los
// targets privados SOLO aquí (nunca en dry-run — HarnessOrchestrator.Prepare
// sólo confirma su PRESENCIA, jamás su valor ni el contenido del archivo de
// qr_code_data), resuelve el commit SHA, construye el request usando
// ÚNICAMENTE el DTO productivo (PassportDecodeQrCodeRequest), invoca
// IPassportQrClient.DecodeQrCodeAsync EXACTAMENTE UNA VEZ, y clasifica el
// resultado. Nunca imprime nada por sí misma — devuelve datos ya saneados
// (o ninguno, en LocalBlocked). 100% testeable inyectando un
// IPassportQrClient/ICommitShaProvider fake.
//
// Reutiliza el mismo tipo de resultado que CreateQrStaticExecutor/Resolve/
// Suspend/Activate/Delete Key (KeyOperationResult/KeyOperationOutcome).
public static class DecodeQrStaticExecutor
{
    public static async Task<KeyOperationResult> ExecuteAsync(
        IConfiguration configuration,
        IPassportQrClient qrClient,
        ICommitShaProvider commitShaProvider,
        DateTime executedAtUtc,
        string? automatedTestReference = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(qrClient);
        ArgumentNullException.ThrowIfNull(commitShaProvider);

        // XPAY-465 — customer_id proviene de PASSPORT_TEST_CUSTOMER_ID
        // (mismo recurso ya usado por create-qr-static/resolve-key).
        // qrDataFilePath es sólo la RUTA — su CONTENIDO (el qr_code_data
        // real) se lee más abajo, y ÚNICAMENTE aquí (nunca en dry-run/
        // Prepare, nunca se expone en ningún log/consola).
        var customerId     = configuration[HarnessTargetConfig.EnvCustomerId];
        var qrDataFilePath = configuration[HarnessTargetConfig.EnvQrDecodeDataFilePath];

        // Defensa en profundidad: HarnessOrchestrator.Prepare ya debería
        // haber bloqueado esto (Outcome.AbortedTargetMissing) antes de que
        // el flujo llegue aquí.
        if (string.IsNullOrWhiteSpace(customerId) || string.IsNullOrWhiteSpace(qrDataFilePath))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_QR_DECODE_DATA_FILE/PASSPORT_TEST_CUSTOMER_ID ausente(s) al momento de intentar Decode QR Static.");
        }

        // XPAY-465 — el qr_code_data real NUNCA vive en ~/.passport-sandbox.env
        // ni en ninguna variable de entorno: PASSPORT_TEST_QR_DECODE_DATA_FILE
        // apunta a un archivo local privado (fuera de Git, permisos
        // restrictivos — responsabilidad del operador al crearlo) que el
        // operador prepara manualmente. Este bloque es el ÚNICO lugar del
        // harness que lee su contenido — nunca se loguea, nunca se imprime,
        // nunca se incluye en evidencia (sólo su fingerprint, vía
        // DecodeQrStaticEvidenceBuilder). Cualquier error de lectura
        // (archivo inexistente, sin permisos, vacío) bloquea LOCALMENTE —
        // el mensaje de excepción nunca incluye contenido de archivo, sólo
        // la ruta (que no es sensible por sí sola) y el tipo de error.
        string qrCodeData;
        try
        {
            qrCodeData = await File.ReadAllTextAsync(qrDataFilePath).ConfigureAwait(false);
            qrCodeData = qrCodeData.Trim();
        }
        catch (Exception ex)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"No se pudo leer PASSPORT_TEST_QR_DECODE_DATA_FILE ('{qrDataFilePath}'): {ex.GetType().Name}.");
        }

        if (string.IsNullOrWhiteSpace(qrCodeData))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"PASSPORT_TEST_QR_DECODE_DATA_FILE ('{qrDataFilePath}') está vacío — se requiere el qr_code_data real.");
        }

        string commitSha;
        try
        {
            commitSha = commitShaProvider.GetCommitSha();
        }
        catch (Exception ex)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"No se pudo resolver el commit SHA del backend: {ex.Message}");
        }

        // XPAY-465 — contrato confirmado directamente desde
        // PassportDecodeQrCodeRequest.cs/PassportQrClient.ValidateDecodeRequest:
        // EXACTAMENTE customer_id + qr_code_data — nunca id, nunca
        // qr_code_reference (auditoría explícita del ticket XPAY-465 §1).
        var request = new PassportDecodeQrCodeRequest(
            CustomerId: customerId,
            QrCodeData: qrCodeData);

        try
        {
            // Exactamente UNA llamada — sin reintentos, sin segundo HTTP,
            // sin fallback. ÚNICAMENTE DecodeQrCodeAsync — nunca
            // CreateQrCodeAsync ni ningún método de IPassportKeyClient.
            var response = await qrClient.DecodeQrCodeAsync(request).ConfigureAwait(false);
            var evidence = DecodeQrStaticEvidenceBuilder.BuildSuccess(
                request, response, httpStatus: null, commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            // Mensajes ya saneados por diseño (PassportHttpClient/
            // PassportQrClient): nunca incluyen body, token ni
            // Authorization, ni customer_id/qr_code_data.
            var (httpStatus, safeErrorCode, safeErrorMessage) = ex is PassportTransportException pte
                ? (pte.StatusCode, pte.SafeErrorCode, pte.SafeErrorMessage)
                : (null, null, null);

            var evidence = DecodeQrStaticEvidenceBuilder.BuildPassportHttpFailure(
                ex.Message, httpStatus, commitSha, executedAtUtc, safeErrorCode, safeErrorMessage);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }
}
