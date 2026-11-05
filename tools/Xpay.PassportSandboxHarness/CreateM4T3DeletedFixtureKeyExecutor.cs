using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-492 — orquesta la ejecución REAL de la PREPARACIÓN de M4-T3-B (Create
// Key — llave desechable dedicada, todavía NO eliminada): lee el target
// privado SOLO aquí (nunca en dry-run — HarnessOrchestrator.Prepare sólo
// confirma su PRESENCIA, jamás su valor), resuelve el commit SHA, construye
// el request usando ÚNICAMENTE el DTO productivo (PassportCreateKeyRequest,
// SIN modificar código productivo), invoca IPassportKeyClient.CreateKeyAsync
// EXACTAMENTE UNA VEZ, y clasifica el resultado. Nunca imprime nada por sí
// misma — devuelve datos ya saneados (o ninguno, en LocalBlocked).
//
// Mirror ESTRUCTURAL de CreateM4T3SuspendedFixtureKeyExecutor (XPAY-474) —
// misma disciplina exacta, sólo cambia el display_name y el case_id de su
// evidence builder. Reutiliza DisposableBcodeGenerator SIN modificarlo (es
// agnóstico del destino final de la llave — suspendida o eliminada —, sólo
// genera un BCODE válido y fresco).
//
// Esta NO es la ejecución final de M4-T3-B (Create QR con llave eliminada
// — eso sigue siendo create-qr-static-deleted-key, adaptado en XPAY-492
// para leer PASSPORT_TEST_QR_DELETED_KEY_ID) — es sólo el PRIMER paso de
// preparación de su fixture desechable.
public static class CreateM4T3DeletedFixtureKeyExecutor
{
    // XPAY-492 — key_type: BCODE. Mismo criterio que CreateM4T3SuspendedFixtureKeyExecutor
    // (XPAY-474): único tipo con contrato de FORMATO confirmado en este
    // repositorio (^00[0-9]{8}$, XPAY-343/344).
    private const string CertificationKeyType = "BCODE";

    // XPAY-492 — display_name inequívoco y específico de ESTE fixture —
    // distinto del de M4-T3-A ("XPay M4-T3 Suspended QR Fixture") para que
    // ambas llaves desechables sean identificables sin ambigüedad en el
    // propio Passport Dashboard.
    private const string CertificationDisplayName = "XPay M4-T3 Deleted QR Fixture";

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

        // XPAY-492 — account_id proviene ÚNICAMENTE de
        // PASSPORT_TEST_ACCOUNT_ID (recurso COMPARTIDO y estable). Este
        // executor NUNCA lee PASSPORT_TEST_NEW_KEY_TYPE/_VALUE (contaminadas,
        // XPAY-487/488/491), PASSPORT_TEST_NEW_KEY_ID (contaminada/
        // abandonada), PASSPORT_TEST_QR_KEY_ID ni PASSPORT_TEST_QR_SUSPENDED_KEY_ID
        // (target exclusivo de M4-T3-A).
        var accountId = configuration[HarnessTargetConfig.EnvAccountId];

        // Defensa en profundidad: HarnessOrchestrator.Prepare ya debería
        // haber bloqueado esto (Outcome.AbortedTargetMissing) antes de que
        // el flujo llegue aquí.
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_ACCOUNT_ID ausente al momento de intentar Create M4-T3 Deleted Fixture Key.");
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

        // XPAY-492 — key_value NUNCA se lee del entorno: SIEMPRE
        // DisposableBcodeGenerator.Generate() — un BCODE VÁLIDO
        // (^00[0-9]{8}$), fresco, criptográficamente aleatorio, generado
        // AQUÍ MISMO (nunca antes — el dry-run jamás llega a esta línea).
        // Nunca derivado ni relacionado con PASSPORT_TEST_NEW_KEY_VALUE
        // (contaminada) ni con el fixture desechable de M4-T3-A.
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
            var evidence = CreateM4T3DeletedFixtureKeyEvidenceBuilder.BuildSuccess(
                request, response, httpStatus: null, commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            var (httpStatus, safeErrorCode, safeErrorMessage) = ex is PassportTransportException pte
                ? (pte.StatusCode, pte.SafeErrorCode, pte.SafeErrorMessage)
                : (null, null, null);

            var evidence = CreateM4T3DeletedFixtureKeyEvidenceBuilder.BuildPassportHttpFailure(
                ex.Message, httpStatus, commitSha, executedAtUtc, safeErrorCode, safeErrorMessage);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }
}
