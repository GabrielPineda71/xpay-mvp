using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-334 — orquesta la ejecución REAL de M3-T5 (Delete Key): lee el
// key_id privado SOLO aquí (nunca en dry-run — HarnessOrchestrator.Prepare
// sólo confirma su PRESENCIA, jamás su valor), resuelve el commit SHA,
// invoca IPassportKeyClient.DeleteKeyAsync EXACTAMENTE UNA VEZ, y
// clasifica el resultado. Nunca imprime nada por sí misma — devuelve datos
// ya saneados (o ninguno, en LocalBlocked). 100% testeable inyectando un
// IPassportKeyClient/ICommitShaProvider fake. Mirror de SuspendKeyExecutor/
// ActivateKeyExecutor (XPAY-326/332) — reutiliza el mismo
// KeyOperationResult/KeyOperationOutcome, sin duplicar el tipo.
//
// DIFERENCIA: DeleteKeyAsync devuelve `Task` (no `Task<PassportKeyResponse>`
// — 204 No Content sin body, contrato XPAY-292/293). La ausencia de
// excepción tras el `await` ES la confirmación de éxito; no hay ningún
// objeto de respuesta que pasarle a DeleteKeyEvidenceBuilder.BuildSuccess.
public static class DeleteKeyExecutor
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
                "PASSPORT_TEST_NEW_KEY_ID ausente al momento de intentar Delete Key.");
        }

        string commitSha;
        try
        {
            commitSha = commitShaProvider.GetCommitSha();
        }
        catch (Exception ex)
        {
            // XPAY-325/326/332/334 — sin un commit SHA válido no se puede
            // producir evidencia reproducible: se trata como LOCAL_BLOCKED,
            // nunca se intenta HTTP con un SHA no resuelto.
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"No se pudo resolver el commit SHA del backend: {ex.Message}");
        }

        try
        {
            // Exactamente UNA llamada — sin reintentos, sin segundo HTTP,
            // sin GET posterior de verificación. ÚNICAMENTE DeleteKeyAsync
            // — nunca Create/Suspend/Activate/Resolve/ListKeys. Sin
            // response body que leer (204 No Content) — la ausencia de
            // excepción ES el éxito.
            await keyClient.DeleteKeyAsync(keyId).ConfigureAwait(false);
            var evidence = DeleteKeyEvidenceBuilder.BuildSuccess(
                keyId, httpStatus: null, commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            // Estas 3 excepciones ya son mensajes estáticos y saneados por
            // diseño (PassportHttpClient/PassportKeyClient, sesiones
            // previas): nunca incluyen body, token ni Authorization, ni el
            // key_id. Se reutiliza el mensaje tal cual en `notes`.
            var evidence = DeleteKeyEvidenceBuilder.BuildPassportHttpFailure(
                ex.Message, httpStatus: null, commitSha, executedAtUtc);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }
}
