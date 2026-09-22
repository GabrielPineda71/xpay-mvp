using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-471 — orquesta la ejecución REAL de M4-T3-A (Create QR Code —
// STATIC — con llave Bre-B SUSPENDIDA): lee el target privado SOLO aquí
// (nunca en dry-run), resuelve el commit SHA, construye el request usando
// ÚNICAMENTE el DTO productivo (PassportCreateQrCodeRequest, mismo shape
// STATIC/MPOS que M4-T1 vía QrStaticCertificationFixture), invoca
// IPassportQrClient.CreateQrCodeAsync EXACTAMENTE UNA VEZ, y clasifica el
// resultado. Nunca imprime nada por sí misma — devuelve datos ya saneados
// (o ninguno, en LocalBlocked).
//
// Este es un CASO NEGATIVO (M3-T6-INVALID/M3-T7 son el mismo criterio ya
// establecido): un HTTP 4xx es el resultado ESPERADO de la evidencia, no
// necesariamente un fallo — pero este executor NUNCA decide por sí mismo
// que un error "es el esperado" sólo por ser 4xx; sólo clasifica
// transporte/protocolo (Success/PassportFailure/LocalBlocked, igual que
// CreateQrStaticExecutor) — la interpretación de si el error observado
// corresponde específicamente al escenario "llave suspendida" es una
// revisión contractual separada (ver CreateQrStaticSuspendedKeyEvidenceBuilder).
public static class CreateQrStaticSuspendedKeyExecutor
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

        // XPAY-471 — key_id proviene EXCLUSIVAMENTE de
        // PASSPORT_TEST_QR_SUSPENDED_KEY_ID (llave DESECHABLE dedicada,
        // NO creada todavía — este ticket es únicamente soporte de código).
        // PROHIBIDO cualquier fallback hacia PASSPORT_TEST_QR_KEY_ID (llave
        // ACTIVA protegida de M4-T1/M4-T2) o PASSPORT_TEST_NEW_KEY_ID
        // (llave DELETED de M3, target exclusivo de M4-T3-B) — ver
        // HarnessTargetConfig.AllPresentForCreateQrStaticSuspendedKey.
        var keyId      = configuration[HarnessTargetConfig.EnvQrSuspendedKeyId];
        var customerId = configuration[HarnessTargetConfig.EnvCustomerId];

        // Defensa en profundidad: HarnessOrchestrator.Prepare ya debería
        // haber bloqueado esto (Outcome.AbortedTargetMissing) antes de que
        // el flujo llegue aquí.
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(customerId))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_QR_SUSPENDED_KEY_ID/PASSPORT_TEST_CUSTOMER_ID ausente(s) al momento de intentar Create QR Static (M4-T3-A).");
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

        // Mismo contrato STATIC/MPOS ya validado en M4-T1 — factorizado en
        // QrStaticCertificationFixture, sin tocar CreateQrStaticExecutor.cs.
        var request = QrStaticCertificationFixture.BuildRequest(keyId, customerId);

        try
        {
            // Exactamente UNA llamada — sin reintentos, sin segundo HTTP,
            // sin fallback. ÚNICAMENTE CreateQrCodeAsync.
            var response = await qrClient.CreateQrCodeAsync(request).ConfigureAwait(false);
            var evidence = CreateQrStaticSuspendedKeyEvidenceBuilder.BuildSuccess(
                request, response, httpStatus: null, commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            var (httpStatus, safeErrorCode, safeErrorMessage) = ex is PassportTransportException pte
                ? (pte.StatusCode, pte.SafeErrorCode, pte.SafeErrorMessage)
                : (null, null, null);

            var evidence = CreateQrStaticSuspendedKeyEvidenceBuilder.BuildPassportHttpFailure(
                ex.Message, httpStatus, commitSha, executedAtUtc, safeErrorCode, safeErrorMessage);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }
}
