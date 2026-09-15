using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-340 — orquesta la ejecución REAL de M3-T2 (Resolve Key): lee los
// targets privados SOLO aquí (nunca en dry-run — HarnessOrchestrator.
// Prepare sólo confirma su PRESENCIA, jamás su valor), valida key_type de
// forma ESTRICTA (mismo criterio que CreateKeyExecutor — sin normalizar
// MOBILE→PHONE), resuelve el commit SHA, invoca
// IPassportKeyClient.ResolveKeyAsync EXACTAMENTE UNA VEZ, y clasifica el
// resultado. Nunca imprime nada por sí misma — devuelve datos ya saneados
// (o ninguno, en LocalBlocked). 100% testeable inyectando un
// IPassportKeyClient/ICommitShaProvider fake.
//
// DIFERENCIA DE TARGET frente a Suspend/Activate/Delete/
// DeleteAlreadyDeleted: Resolve usa los recursos Bre-B de prueba YA
// provistos por Passport (PASSPORT_TEST_CUSTOMER_ID/
// PASSPORT_TEST_BREB_KEY_TYPE/PASSPORT_TEST_BREB_KEY), NUNCA
// PASSPORT_TEST_NEW_KEY_ID (la llave de certificación, ya eliminada desde
// M3-T5).
public static class ResolveKeyExecutor
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

        var customerId  = configuration[HarnessTargetConfig.EnvCustomerId];
        var rawKeyType  = configuration[HarnessTargetConfig.EnvBrebKeyType];
        var keyValue    = configuration[HarnessTargetConfig.EnvBrebKeyValue];

        // Defensa en profundidad: HarnessOrchestrator.Prepare ya debería
        // haber bloqueado esto (Outcome.AbortedTargetMissing) antes de que
        // el flujo llegue aquí.
        if (string.IsNullOrWhiteSpace(customerId) || string.IsNullOrWhiteSpace(keyValue))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_CUSTOMER_ID/PASSPORT_TEST_BREB_KEY ausente(s) al momento de intentar Resolve Key.");
        }

        // XPAY-289/324/340 — mapeo ESTRICTO: únicamente nombres EXACTOS
        // del enum (ID/PHONE/EMAIL/ALPHA/BCODE, sensible a mayúsculas). NO
        // se normaliza MOBILE→PHONE ni ninguna otra tabla de sinónimos no
        // confirmada por Passport — un valor no reconocido es
        // LOCAL_BLOCKED, nunca se envía a HTTP.
        if (string.IsNullOrWhiteSpace(rawKeyType)
            || !Enum.TryParse<PassportKeyType>(rawKeyType, ignoreCase: false, out var keyType)
            || !Enum.IsDefined(keyType))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_BREB_KEY_TYPE no es un nombre válido de PassportKeyType (ID/PHONE/EMAIL/ALPHA/BCODE).");
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

        var request = new PassportResolveKeyRequest(
            CustomerId: customerId,
            Key: new PassportKeyRequest(keyType, keyValue));

        try
        {
            // Exactamente UNA llamada — sin reintentos, sin segundo HTTP,
            // sin fallback. ÚNICAMENTE ResolveKeyAsync — nunca
            // Create/Suspend/Activate/Delete/ListKeys.
            var response = await keyClient.ResolveKeyAsync(request).ConfigureAwait(false);
            var evidence = ResolveKeyEvidenceBuilder.BuildSuccess(
                request, response, httpStatus: null, commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            // Mensajes ya saneados por diseño (PassportHttpClient/
            // PassportKeyClient): nunca incluyen body, token ni
            // Authorization, ni customer_id/key_value.
            var evidence = ResolveKeyEvidenceBuilder.BuildPassportHttpFailure(
                ex.Message, httpStatus: null, commitSha, executedAtUtc);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }
}
