using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-474 — orquesta la ejecución REAL de la PREPARACIÓN de M4-T3-A (Create
// Key — llave desechable dedicada, todavía NO suspendida): lee el target
// privado SOLO aquí (nunca en dry-run — HarnessOrchestrator.Prepare sólo
// confirma su PRESENCIA, jamás su valor), resuelve el commit SHA, construye
// el request usando ÚNICAMENTE el DTO productivo (PassportCreateKeyRequest,
// SIN modificar código productivo), invoca IPassportKeyClient.CreateKeyAsync
// EXACTAMENTE UNA VEZ, y clasifica el resultado. Nunca imprime nada por sí
// misma — devuelve datos ya saneados (o ninguno, en LocalBlocked).
//
// Esta NO es la ejecución final de M4-T3-A (Create QR con llave suspendida
// — eso sigue siendo create-qr-static-suspended-key, XPAY-471, sin cambios
// aquí) — es sólo el PRIMER paso de preparación de su fixture desechable.
public static class CreateM4T3SuspendedFixtureKeyExecutor
{
    // XPAY-474 — key_type: BCODE. Único tipo con contrato de FORMATO
    // confirmado en este repositorio (^00[0-9]{8}$, XPAY-343/344, ver
    // InvalidKeyValueGenerator.cs) y el mismo ya usado con éxito tanto en
    // la llave original de M3-T1 como en la llave activa protegida de
    // M4-T1/T2 (PASSPORT_TEST_QR_KEY_ID, "Business Entity Code").
    private const string CertificationKeyType = "BCODE";

    // XPAY-474 — display_name inequívoco y específico de este fixture —
    // NUNCA el genérico "XPay Certification Test Key" ya usado por
    // CreateKeyExecutor (M3-T1), para que ambas llaves sean identificables
    // sin ambigüedad en el propio Passport Dashboard.
    private const string CertificationDisplayName = "XPay M4-T3 Suspended QR Fixture";

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

        // XPAY-474 — account_id proviene ÚNICAMENTE de
        // PASSPORT_TEST_ACCOUNT_ID (recurso COMPARTIDO y estable, la cuenta
        // Sandbox misma — mismo criterio ya usado con
        // PASSPORT_TEST_CUSTOMER_ID). Este executor NUNCA lee
        // PASSPORT_TEST_NEW_KEY_TYPE/PASSPORT_TEST_NEW_KEY_VALUE (las de
        // M3) ni PASSPORT_TEST_QR_KEY_ID/PASSPORT_TEST_QR_SUSPENDED_KEY_ID
        // (esta operación CREA una llave, no depende de ninguna existente).
        var accountId = configuration[HarnessTargetConfig.EnvAccountId];

        // Defensa en profundidad: HarnessOrchestrator.Prepare ya debería
        // haber bloqueado esto (Outcome.AbortedTargetMissing) antes de que
        // el flujo llegue aquí.
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_ACCOUNT_ID ausente al momento de intentar Create M4-T3 Suspended Fixture Key.");
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

        // XPAY-474 — key_value NUNCA se lee del entorno: SIEMPRE
        // DisposableBcodeGenerator.Generate() — un BCODE VÁLIDO
        // (^00[0-9]{8}$), fresco, criptográficamente aleatorio, generado
        // AQUÍ MISMO (nunca antes — el dry-run jamás llega a esta línea,
        // así que nunca consume/genera un valor que luego no se usaría).
        var keyValue = DisposableBcodeGenerator.Generate();

        var request = new PassportCreateKeyRequest(
            AccountId: accountId,
            Key: new PassportKeyRequest(
                KeyType: Enum.Parse<PassportKeyType>(CertificationKeyType),
                KeyValue: keyValue))
        {
            DisplayName = CertificationDisplayName,
        };

        try
        {
            // Exactamente UNA llamada — sin reintentos, sin segundo HTTP.
            // ÚNICAMENTE CreateKeyAsync — nunca Suspend/Activate/Delete/List
            // Keys, nunca IPassportQrClient.
            var response = await keyClient.CreateKeyAsync(request).ConfigureAwait(false);
            var evidence = CreateM4T3SuspendedFixtureKeyEvidenceBuilder.BuildSuccess(
                request, response, httpStatus: null, commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            var (httpStatus, safeErrorCode, safeErrorMessage) = ex is PassportTransportException pte
                ? (pte.StatusCode, pte.SafeErrorCode, pte.SafeErrorMessage)
                : (null, null, null);

            var evidence = CreateM4T3SuspendedFixtureKeyEvidenceBuilder.BuildPassportHttpFailure(
                ex.Message, httpStatus, commitSha, executedAtUtc, safeErrorCode, safeErrorMessage);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }
}
