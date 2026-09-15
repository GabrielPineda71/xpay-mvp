using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-336 — orquesta la ejecución REAL de M3-T7 (intento de eliminar una
// llave YA eliminada por M3-T5): lee el key_id privado SOLO aquí (nunca en
// dry-run), resuelve el commit SHA, invoca
// IPassportKeyClient.DeleteKeyAsync EXACTAMENTE UNA VEZ, y clasifica el
// resultado a NIVEL TRANSPORTE únicamente (ver
// DeleteAlreadyDeletedKeyEvidenceBuilder para la distinción crítica entre
// "la llamada HTTP tuvo éxito/falló" y "el caso de certificación es
// correcto/incorrecto" — este executor NUNCA hace ese segundo juicio).
//
// DELIBERADAMENTE UN TIPO/ARCHIVO SEPARADO de DeleteKeyExecutor (M3-T5),
// aunque ambos invoquen el mismo IPassportKeyClient.DeleteKeyAsync sobre
// el mismo PASSPORT_TEST_NEW_KEY_ID: M3-T5 y M3-T7 son casos de
// certificación distintos y nunca deben compartir código de evidencia —
// esto es intencional, no una duplicación accidental a limpiar.
public static class DeleteAlreadyDeletedKeyExecutor
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
                "PASSPORT_TEST_NEW_KEY_ID ausente al momento de intentar Delete Already-Deleted Key (M3-T7).");
        }

        string commitSha;
        try
        {
            commitSha = commitShaProvider.GetCommitSha();
        }
        catch (Exception ex)
        {
            // XPAY-325/326/332/334/336 — sin un commit SHA válido no se
            // puede producir evidencia reproducible: se trata como
            // LOCAL_BLOCKED, nunca se intenta HTTP con un SHA no resuelto.
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"No se pudo resolver el commit SHA del backend: {ex.Message}");
        }

        try
        {
            // Exactamente UNA llamada — sin reintentos, sin segundo HTTP,
            // sin GET posterior. ÚNICAMENTE DeleteKeyAsync — nunca
            // Create/Suspend/Activate/Resolve/ListKeys. El resultado
            // (éxito o excepción) se clasifica a nivel transporte
            // ÚNICAMENTE — la interpretación de certificación queda para
            // revisión posterior (ver EvidenceBuilder).
            await keyClient.DeleteKeyAsync(keyId).ConfigureAwait(false);
            var evidence = DeleteAlreadyDeletedKeyEvidenceBuilder.BuildSuccess(
                keyId, httpStatus: null, commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            // Estas 3 excepciones ya son mensajes estáticos y saneados por
            // diseño (PassportHttpClient/PassportKeyClient): nunca
            // incluyen body, token ni Authorization, ni el key_id. Se
            // reutiliza el mensaje tal cual en `notes` — sin asumir que
            // este error es "el esperado" para M3-T7.
            var evidence = DeleteAlreadyDeletedKeyEvidenceBuilder.BuildPassportHttpFailure(
                ex.Message, httpStatus: null, commitSha, executedAtUtc);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }
}
