using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-474 — orquesta la ejecución REAL del SEGUNDO paso de preparación de
// M4-T3-A (Suspend Key — sobre la llave desechable dedicada): lee el target
// privado SOLO aquí, resuelve el commit SHA, invoca
// IPassportKeyClient.SuspendKeyAsync EXACTAMENTE UNA VEZ (mismo cliente
// productivo genérico ya usado por SuspendKeyExecutor — M3-T3 — sin
// modificarlo), y clasifica el resultado. Nunca imprime nada por sí misma.
//
// Deliberadamente un ARCHIVO NUEVO en vez de reutilizar/parametrizar
// SuspendKeyExecutor (M3-T3): ese executor está hardcodeado a
// PASSPORT_TEST_NEW_KEY_ID y ya fue ejecutado realmente en producción de
// certificación — no se toca (XPAY-473 §9/10).
public static class SuspendM4T3FixtureKeyExecutor
{
    public static async Task<KeyOperationResult> ExecuteAsync(
        IConfiguration configuration,
        IPassportKeyClient keyClient,
        ICommitShaProvider commitShaProvider,
        DateTime executedAtUtc,
        string? automatedTestReference = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(keyClient);
        ArgumentNullException.ThrowIfNull(commitShaProvider);

        // XPAY-474 — keyId proviene EXCLUSIVAMENTE de
        // PASSPORT_TEST_QR_SUSPENDED_KEY_ID. PROHIBIDO cualquier fallback
        // hacia PASSPORT_TEST_NEW_KEY_ID (llave DELETED de M3) o
        // PASSPORT_TEST_QR_KEY_ID (llave ACTIVA protegida de M4-T1/T2) —
        // ver HarnessTargetConfig.AllPresentForSuspendM4T3FixtureKey.
        var keyId = configuration[HarnessTargetConfig.EnvQrSuspendedKeyId];

        // Defensa en profundidad: HarnessOrchestrator.Prepare ya debería
        // haber bloqueado esto (Outcome.AbortedTargetMissing) antes de que
        // el flujo llegue aquí.
        if (string.IsNullOrWhiteSpace(keyId))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_QR_SUSPENDED_KEY_ID ausente al momento de intentar Suspend M4-T3 Fixture Key.");
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

        try
        {
            // Exactamente UNA llamada — sin reintentos, sin segundo HTTP.
            // ÚNICAMENTE SuspendKeyAsync — nunca Create/Activate/Delete/List
            // Keys, nunca IPassportQrClient.
            var response = await keyClient.SuspendKeyAsync(keyId).ConfigureAwait(false);
            var evidence = SuspendM4T3FixtureKeyEvidenceBuilder.BuildSuccess(
                keyId, response, httpStatus: null, commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            var (httpStatus, safeErrorCode, safeErrorMessage) = ex is PassportTransportException pte
                ? (pte.StatusCode, pte.SafeErrorCode, pte.SafeErrorMessage)
                : (null, null, null);

            var evidence = SuspendM4T3FixtureKeyEvidenceBuilder.BuildPassportHttpFailure(
                ex.Message, httpStatus, commitSha, executedAtUtc, safeErrorCode, safeErrorMessage);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }
}
