using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-332 — orquesta la ejecución REAL de M3-T4 (Activate Key): lee el
// key_id privado SOLO aquí (nunca en dry-run — HarnessOrchestrator.Prepare
// sólo confirma su PRESENCIA, jamás su valor), resuelve el commit SHA,
// invoca IPassportKeyClient.ActivateKeyAsync EXACTAMENTE UNA VEZ, y
// clasifica el resultado. Nunca imprime nada por sí misma — devuelve datos
// ya saneados (o ninguno, en LocalBlocked). 100% testeable inyectando un
// IPassportKeyClient/ICommitShaProvider fake. Mirror exacto de
// SuspendKeyExecutor (XPAY-326) — reutiliza el mismo KeyOperationResult/
// KeyOperationOutcome (ya diseñados en XPAY-326 para Suspend/Activate/
// futuro Delete), sin duplicar el tipo.
public static class ActivateKeyExecutor
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

        var keyId = configuration[HarnessTargetConfig.EnvNewKeyId];

        // Defensa en profundidad: HarnessOrchestrator.Prepare ya debería
        // haber bloqueado esto (Outcome.AbortedTargetMissing) antes de que
        // el flujo llegue aquí.
        if (string.IsNullOrWhiteSpace(keyId))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_NEW_KEY_ID ausente al momento de intentar Activate Key.");
        }

        string commitSha;
        try
        {
            commitSha = commitShaProvider.GetCommitSha();
        }
        catch (Exception ex)
        {
            // XPAY-325/326/332 — sin un commit SHA válido no se puede
            // producir evidencia reproducible: se trata como LOCAL_BLOCKED,
            // nunca se intenta HTTP con un SHA no resuelto.
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"No se pudo resolver el commit SHA del backend: {ex.Message}");
        }

        try
        {
            // Exactamente UNA llamada — sin reintentos, sin segundo HTTP.
            // ÚNICAMENTE ActivateKeyAsync — nunca Suspend/Delete/Create/
            // Resolve/ListKeys.
            var response = await keyClient.ActivateKeyAsync(keyId).ConfigureAwait(false);
            var evidence = ActivateKeyEvidenceBuilder.BuildSuccess(
                keyId, response, httpStatus: null, commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            // Estas 3 excepciones ya son mensajes estáticos y saneados por
            // diseño (PassportHttpClient/PassportKeyClient, sesiones
            // previas): nunca incluyen body, token ni Authorization, ni el
            // key_id. Se reutiliza el mensaje tal cual en `notes`.
            var evidence = ActivateKeyEvidenceBuilder.BuildPassportHttpFailure(
                ex.Message, httpStatus: null, commitSha, executedAtUtc);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }
}
