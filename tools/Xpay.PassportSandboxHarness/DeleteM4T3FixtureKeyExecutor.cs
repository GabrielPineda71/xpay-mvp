using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-492 — orquesta la ejecución REAL del SEGUNDO paso de preparación de
// M4-T3-B (Delete Key — sobre la llave desechable dedicada): lee el target
// privado SOLO aquí, resuelve el commit SHA, invoca
// IPassportKeyClient.DeleteKeyAsync EXACTAMENTE UNA VEZ (mismo cliente
// productivo genérico ya usado por DeleteKeyExecutor — M3-T5 — sin
// modificarlo), y clasifica el resultado. Nunca imprime nada por sí misma.
//
// Deliberadamente un ARCHIVO NUEVO en vez de reutilizar/parametrizar
// DeleteKeyExecutor (M3-T5): ese executor está hardcodeado a
// PASSPORT_TEST_NEW_KEY_ID (la llave histórica de M3, hoy contaminada y
// abandonada como target de M4-T3-B — XPAY-487/488/490/491) y ya fue
// ejecutado realmente en producción de certificación — no se toca.
//
// DIFERENCIA (mismo motivo que DeleteKeyExecutor): DeleteKeyAsync devuelve
// `Task` (no `Task<PassportKeyResponse>` — 204 No Content sin body,
// contrato XPAY-292/293). La ausencia de excepción tras el `await` ES la
// confirmación de éxito; no hay ningún objeto de respuesta que pasarle a
// DeleteM4T3FixtureKeyEvidenceBuilder.BuildSuccess.
public static class DeleteM4T3FixtureKeyExecutor
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

        // XPAY-492 — keyId proviene EXCLUSIVAMENTE de
        // PASSPORT_TEST_QR_DELETED_KEY_ID. PROHIBIDO cualquier fallback
        // hacia PASSPORT_TEST_NEW_KEY_ID (llave contaminada/abandonada de
        // M3), PASSPORT_TEST_QR_KEY_ID (llave ACTIVA protegida de M4-T1/T2)
        // o PASSPORT_TEST_QR_SUSPENDED_KEY_ID (llave suspendida protegida de
        // M4-T3-A) — ver HarnessTargetConfig.AllPresentForDeleteM4T3FixtureKey.
        var keyId = configuration[HarnessTargetConfig.EnvQrDeletedKeyId];

        // Defensa en profundidad: HarnessOrchestrator.Prepare ya debería
        // haber bloqueado esto (Outcome.AbortedTargetMissing) antes de que
        // el flujo llegue aquí.
        if (string.IsNullOrWhiteSpace(keyId))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_QR_DELETED_KEY_ID ausente al momento de intentar Delete M4-T3 Fixture Key.");
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
            // Exactamente UNA llamada — sin reintentos, sin segundo HTTP,
            // sin GET posterior de verificación. ÚNICAMENTE DeleteKeyAsync —
            // nunca Create/Suspend/Activate/Resolve/List Keys. Sin response
            // body que leer (204 No Content) — la ausencia de excepción ES
            // el éxito.
            await keyClient.DeleteKeyAsync(keyId).ConfigureAwait(false);
            var evidence = DeleteM4T3FixtureKeyEvidenceBuilder.BuildSuccess(
                keyId, httpStatus: null, commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            var (httpStatus, safeErrorCode, safeErrorMessage) = ex is PassportTransportException pte
                ? (pte.StatusCode, pte.SafeErrorCode, pte.SafeErrorMessage)
                : (null, null, null);

            var evidence = DeleteM4T3FixtureKeyEvidenceBuilder.BuildPassportHttpFailure(
                ex.Message, httpStatus, commitSha, executedAtUtc, safeErrorCode, safeErrorMessage);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }
}
