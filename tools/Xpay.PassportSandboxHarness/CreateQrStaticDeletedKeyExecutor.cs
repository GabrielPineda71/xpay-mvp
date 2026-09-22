using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-471 — orquesta la ejecución REAL de M4-T3-B (Create QR Code —
// STATIC — con llave Bre-B ELIMINADA): lee el target privado SOLO aquí,
// resuelve el commit SHA, construye el request usando ÚNICAMENTE el DTO
// productivo (mismo shape STATIC/MPOS que M4-T1 vía
// QrStaticCertificationFixture), invoca IPassportQrClient.CreateQrCodeAsync
// EXACTAMENTE UNA VEZ, y clasifica el resultado. Nunca imprime nada por sí
// misma.
//
// Caso negativo (mismo criterio de no-interpretación que M4-T3-A — ver
// CreateQrStaticDeletedKeyEvidenceBuilder).
public static class CreateQrStaticDeletedKeyExecutor
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

        // XPAY-492 (reemplaza el criterio original de XPAY-471) — key_id
        // proviene EXCLUSIVAMENTE de PASSPORT_TEST_QR_DELETED_KEY_ID: la
        // llave DESECHABLE dedicada de M4-T3-B (creada y eliminada
        // específicamente para este subcaso — ver
        // create-m4-t3-deleted-fixture-key/delete-m4-t3-fixture-key), NUNCA
        // la llave histórica del ciclo M3 (PASSPORT_TEST_NEW_KEY_ID).
        // XPAY-487/488/490/491 confirmaron que PASSPORT_TEST_NEW_KEY_ID y
        // PASSPORT_TEST_NEW_KEY_VALUE quedaron contaminados (ambos
        // coinciden hoy con la llave ACTIVA protegida) y que el
        // identificador histórico real no es recuperable localmente — ni
        // siquiera vía List Keys (XPAY-490, llamada real). Este executor
        // NUNCA muta la llave (nunca invoca Suspend/Activate/Delete/List
        // Keys), nunca la reverifica con una llamada adicional, y nunca cae
        // en fallback hacia PASSPORT_TEST_NEW_KEY_ID (contaminada/
        // abandonada), PASSPORT_TEST_QR_KEY_ID (llave ACTIVA protegida) ni
        // PASSPORT_TEST_QR_SUSPENDED_KEY_ID (target exclusivo de M4-T3-A).
        var keyId      = configuration[HarnessTargetConfig.EnvQrDeletedKeyId];
        // customer_id VÁLIDO — mismo recurso normal de certificación
        // (PASSPORT_TEST_CUSTOMER_ID), consistente con "debe usar el
        // customer_id válido configurado normalmente para el request QR".
        var customerId = configuration[HarnessTargetConfig.EnvCustomerId];

        // Defensa en profundidad: HarnessOrchestrator.Prepare ya debería
        // haber bloqueado esto (Outcome.AbortedTargetMissing) antes de que
        // el flujo llegue aquí.
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(customerId))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_QR_DELETED_KEY_ID/PASSPORT_TEST_CUSTOMER_ID ausente(s) al momento de intentar Create QR Static (M4-T3-B).");
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

        var request = QrStaticCertificationFixture.BuildRequest(keyId, customerId);

        try
        {
            // Exactamente UNA llamada — sin reintentos, sin segundo HTTP,
            // sin fallback. ÚNICAMENTE CreateQrCodeAsync — NUNCA
            // DeleteKeyAsync/ninguna mutación de key.
            var response = await qrClient.CreateQrCodeAsync(request).ConfigureAwait(false);
            var evidence = CreateQrStaticDeletedKeyEvidenceBuilder.BuildSuccess(
                request, response, httpStatus: null, commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            var (httpStatus, safeErrorCode, safeErrorMessage) = ex is PassportTransportException pte
                ? (pte.StatusCode, pte.SafeErrorCode, pte.SafeErrorMessage)
                : (null, null, null);

            var evidence = CreateQrStaticDeletedKeyEvidenceBuilder.BuildPassportHttpFailure(
                ex.Message, httpStatus, commitSha, executedAtUtc, safeErrorCode, safeErrorMessage);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }
}
