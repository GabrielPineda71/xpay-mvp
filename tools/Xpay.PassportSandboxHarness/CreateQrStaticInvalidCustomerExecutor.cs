using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-471 — orquesta la ejecución REAL de M4-T3-C (Create QR Code —
// STATIC — con customer_id INCORRECTO): lee el target privado SOLO aquí,
// resuelve el commit SHA, construye el request usando ÚNICAMENTE el DTO
// productivo (mismo shape STATIC/MPOS que M4-T1 vía
// QrStaticCertificationFixture), invoca IPassportQrClient.CreateQrCodeAsync
// EXACTAMENTE UNA VEZ, y clasifica el resultado. Nunca imprime nada por sí
// misma.
//
// Caso negativo (mismo criterio de no-interpretación que M4-T3-A/B — ver
// CreateQrStaticInvalidCustomerEvidenceBuilder).
public static class CreateQrStaticInvalidCustomerExecutor
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

        // XPAY-471 — key_id proviene de PASSPORT_TEST_QR_KEY_ID (la llave
        // ACTIVA de certificación, la MISMA de M4-T1/M4-T2) — usada
        // ÚNICAMENTE de forma READ/REFERENCE: este executor nunca la muta
        // (nunca Suspend/Activate/Delete), nunca la crea, nunca la
        // modifica. Es deliberadamente válida — lo único incorrecto en este
        // subcaso es customer_id.
        var keyId = configuration[HarnessTargetConfig.EnvQrKeyId];

        // Defensa en profundidad: HarnessOrchestrator.Prepare ya debería
        // haber bloqueado esto (Outcome.AbortedTargetMissing) antes de que
        // el flujo llegue aquí.
        if (string.IsNullOrWhiteSpace(keyId))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_QR_KEY_ID ausente al momento de intentar Create QR Static (M4-T3-C).");
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

        // XPAY-471 — customer_id NUNCA proviene de PASSPORT_TEST_CUSTOMER_ID
        // (el real) — SIEMPRE de InvalidCustomerIdGenerator.Generate(): un
        // valor sintético, determinista, con forma de UUID, nunca derivado
        // del customer_id real ni de su fingerprint (ver
        // InvalidCustomerIdGenerator para la justificación completa).
        var customerId = InvalidCustomerIdGenerator.Generate();

        var request = QrStaticCertificationFixture.BuildRequest(keyId, customerId);

        try
        {
            // Exactamente UNA llamada — sin reintentos, sin segundo HTTP,
            // sin fallback. ÚNICAMENTE CreateQrCodeAsync — NUNCA ninguna
            // mutación de key.
            var response = await qrClient.CreateQrCodeAsync(request).ConfigureAwait(false);
            var evidence = CreateQrStaticInvalidCustomerEvidenceBuilder.BuildSuccess(
                request, response, httpStatus: null, commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            var (httpStatus, safeErrorCode, safeErrorMessage) = ex is PassportTransportException pte
                ? (pte.StatusCode, pte.SafeErrorCode, pte.SafeErrorMessage)
                : (null, null, null);

            var evidence = CreateQrStaticInvalidCustomerEvidenceBuilder.BuildPassportHttpFailure(
                ex.Message, httpStatus, commitSha, executedAtUtc, safeErrorCode, safeErrorMessage);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }
}
