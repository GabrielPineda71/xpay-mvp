using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-344 — orquesta la ejecución REAL FUTURA de M3-T6-INVALID (Create
// Key con key_value de formato inválido para un key_type canónico). Lee
// account_id + key_type SOLO aquí (nunca en dry-run) — NUNCA lee
// PASSPORT_TEST_NEW_KEY_VALUE: el key_value inválido se genera
// INTERNAMENTE (InvalidKeyValueGenerator), nunca desde el entorno, nunca
// se persiste en ~/.passport-sandbox.env.
//
// XPAY-344 §8 — PROHIBIDO ejecutar --execute contra Sandbox en esta fase:
// este executor queda implementado y probado offline (fakes/mocks
// exclusivamente); su primera invocación real requiere autorización
// explícita separada en una fase posterior.
//
// Sólo BCODE está soportado (ver InvalidKeyValueGenerator) — cualquier
// otro key_type resulta en LocalBlocked, nunca se inventa un valor
// "inválido" sin contrato de formato confirmado.
public static class CreateKeyInvalidExecutor
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

        var accountId  = configuration[HarnessTargetConfig.EnvAccountId];
        var rawKeyType = configuration[HarnessTargetConfig.EnvNewKeyType];

        // Defensa en profundidad: HarnessOrchestrator.Prepare ya debería
        // haber bloqueado esto (Outcome.AbortedTargetMissing).
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_ACCOUNT_ID ausente al momento de intentar Create Key Invalid (M3-T6-INVALID).");
        }

        if (string.IsNullOrWhiteSpace(rawKeyType)
            || !Enum.TryParse<PassportKeyType>(rawKeyType, ignoreCase: false, out var keyType)
            || !Enum.IsDefined(keyType))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_NEW_KEY_TYPE no es un nombre válido de PassportKeyType (ID/PHONE/EMAIL/ALPHA/BCODE).");
        }

        // XPAY-344 §7 — sólo BCODE tiene contrato de formato confirmado
        // sin ambigüedad. Cualquier otro tipo se bloquea LOCALMENTE, nunca
        // se inventa una restricción.
        if (!InvalidKeyValueGenerator.TryGenerate(keyType, out var invalidValue, out var invalidDimension))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"PASSPORT_TEST_NEW_KEY_TYPE={keyType} no está soportado para generación de valor inválido " +
                $"(sólo {string.Join(", ", InvalidKeyValueGenerator.SupportedKeyTypes)} tiene(n) contrato de formato confirmado).");
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

        var request = new PassportCreateKeyRequest(
            AccountId: accountId,
            Key: new PassportKeyRequest(keyType, invalidValue!));

        try
        {
            // Exactamente UNA llamada — sin reintentos. ÚNICAMENTE
            // CreateKeyAsync — nunca Suspend/Activate/Delete/Resolve/
            // ListKeys.
            var response = await keyClient.CreateKeyAsync(request).ConfigureAwait(false);
            var evidence = CreateKeyInvalidEvidenceBuilder.BuildResult(
                accountId, keyType, invalidDimension, transportSucceeded: true, response,
                httpStatus: null, failureReason: null, commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            // Mensajes ya saneados por diseño: nunca incluyen body, token,
            // Authorization, account_id ni key_value.
            var evidence = CreateKeyInvalidEvidenceBuilder.BuildResult(
                accountId, keyType, invalidDimension, transportSucceeded: false, response: null,
                httpStatus: null, failureReason: ex.Message, commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }
}
