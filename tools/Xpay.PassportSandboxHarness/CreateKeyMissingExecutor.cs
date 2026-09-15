using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-344 — orquesta M3-T6-MISSING: demuestra que
// IPassportKeyClient.CreateKeyAsync (stack PRODUCTIVO real, sin segunda
// integración ni request manual) rechaza LOCALMENTE, antes de cualquier
// HTTP, un Create Key con key_value ausente.
//
// Lee account_id + key_type SOLO aquí (nunca en dry-run) — y
// DELIBERADAMENTE nunca lee PASSPORT_TEST_NEW_KEY_VALUE: el objetivo del
// subcaso es demostrar la ausencia de ese campo, por lo que siempre se
// construye el request con KeyValue=string.Empty, sin importar qué
// contenga el entorno.
//
// GARANTÍA ESTRUCTURAL (verificada por lectura de PassportKeyClient.
// CreateKeyAsync): `string.IsNullOrWhiteSpace(request.Key.KeyValue)` lanza
// ArgumentException de forma SÍNCRONA, ANTES de construir ningún
// HttpRequestMessage — por tanto esta llamada NUNCA alcanza
// IPassportHttpClient/OAuth/red real, sin necesidad de ningún mock que lo
// "simule": es el comportamiento real del código productivo.
public static class CreateKeyMissingExecutor
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
                "PASSPORT_TEST_ACCOUNT_ID ausente al momento de intentar Create Key Missing (M3-T6-MISSING).");
        }

        if (string.IsNullOrWhiteSpace(rawKeyType)
            || !Enum.TryParse<PassportKeyType>(rawKeyType, ignoreCase: false, out var keyType)
            || !Enum.IsDefined(keyType))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_NEW_KEY_TYPE no es un nombre válido de PassportKeyType (ID/PHONE/EMAIL/ALPHA/BCODE).");
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

        // KeyValue = string.Empty deliberado — ES el campo bajo prueba.
        var request = new PassportCreateKeyRequest(
            AccountId: accountId,
            Key: new PassportKeyRequest(keyType, string.Empty));

        try
        {
            await keyClient.CreateKeyAsync(request).ConfigureAwait(false);

            // Estructuralmente inalcanzable con el contrato productivo
            // actual (ver comentario de clase) — si alguna vez se
            // alcanzara, significaría que el guard cambió sin actualizar
            // este executor. Se trata como bloqueo operativo genuino, sin
            // evidencia persistida (nunca se afirma un PASS falso).
            return new(KeyOperationOutcome.LocalBlocked, null,
                "El guard productivo de CreateKeyAsync no rechazó key_value vacío como se esperaba — revisar contrato antes de continuar.");
        }
        catch (ArgumentException)
        {
            // ÉXITO del subcaso M3-T6-MISSING: el guard productivo
            // rechazó el request antes de cualquier HTTP.
            var evidence = CreateKeyMissingEvidenceBuilder.BuildBlockedAsExpected(
                accountId, keyType.ToString(), commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
    }
}
