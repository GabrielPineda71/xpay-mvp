using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-325 — clasificación del resultado de una ejecución real de M3-T1.
// LocalBlocked NUNCA produce evidencia persistible (Evidence=null): la
// operación no llegó a intentarse contra Passport (key_type inválido,
// commit SHA no resoluble, o targets ausentes como defensa en profundidad).
// Success/PassportFailure SÍ representan una interacción remota real (una
// respuesta, o una excepción de transporte/autenticación/protocolo tras
// haber llamado a Passport) y su Evidence debe persistirse.
public enum CreateKeyExecutionOutcome
{
    LocalBlocked,
    Success,
    PassportFailure,
}

public sealed record CreateKeyExecutionResult(
    CreateKeyExecutionOutcome Outcome,
    EvidenceRecord? Evidence,
    string? Detail);

// XPAY-325 — orquesta la ejecución REAL de M3-T1 (Create Key): lee los
// inputs privados SOLO aquí (nunca en dry-run — HarnessOrchestrator.Prepare
// sólo confirma su PRESENCIA, jamás su valor), valida key_type de forma
// ESTRICTA, construye el request real, invoca IPassportKeyClient
// EXACTAMENTE UNA VEZ, y clasifica el resultado. Nunca imprime nada por sí
// misma — devuelve datos ya saneados (o ninguno, en LocalBlocked). 100%
// testeable inyectando un IPassportKeyClient/ICommitShaProvider fake.
public static class CreateKeyExecutor
{
    public static async Task<CreateKeyExecutionResult> ExecuteAsync(
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
        var keyValue   = configuration[HarnessTargetConfig.EnvNewKeyValue];

        // XPAY-325 FASE 4 — mapeo ESTRICTO: únicamente nombres EXACTOS del
        // enum (ID/PHONE/EMAIL/ALPHA/BCODE, sensible a mayúsculas). NO se
        // normaliza MOBILE→PHONE ni ninguna otra tabla de sinónimos no
        // confirmada por Passport — un valor no reconocido es LOCAL_BLOCKED,
        // nunca se envía a HTTP.
        if (string.IsNullOrWhiteSpace(rawKeyType)
            || !Enum.TryParse<PassportKeyType>(rawKeyType, ignoreCase: false, out var keyType)
            || !Enum.IsDefined(keyType))
        {
            return new(CreateKeyExecutionOutcome.LocalBlocked, null,
                "PASSPORT_TEST_NEW_KEY_TYPE no es un nombre válido de PassportKeyType (ID/PHONE/EMAIL/ALPHA/BCODE).");
        }

        // Defensa en profundidad: HarnessOrchestrator.Prepare ya debería
        // haber bloqueado esto (Outcome.AbortedTargetMissing) antes de que
        // el flujo llegue aquí — este chequeo nunca debería activarse en la
        // práctica, pero nunca se asume.
        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(keyValue))
        {
            return new(CreateKeyExecutionOutcome.LocalBlocked, null,
                "account_id/key_value ausentes al momento de construir el request.");
        }

        string commitSha;
        try
        {
            commitSha = commitShaProvider.GetCommitSha();
        }
        catch (Exception ex)
        {
            // XPAY-325 FASE 9 — sin un commit SHA válido no se puede producir
            // evidencia reproducible: se trata como LOCAL_BLOCKED, nunca se
            // intenta HTTP con un SHA no resuelto.
            return new(CreateKeyExecutionOutcome.LocalBlocked, null,
                $"No se pudo resolver el commit SHA del backend: {ex.Message}");
        }

        var request = new PassportCreateKeyRequest(
            AccountId: accountId,
            Key: new PassportKeyRequest(keyType, keyValue))
        {
            DisplayName = "XPay Certification Test Key",
        };

        try
        {
            // Exactamente UNA llamada — sin reintentos, sin segundo HTTP.
            var response = await keyClient.CreateKeyAsync(request).ConfigureAwait(false);
            var evidence = CreateKeyEvidenceBuilder.BuildSuccess(
                request, response, httpStatus: null, commitSha, executedAtUtc, automatedTestReference);
            return new(CreateKeyExecutionOutcome.Success, evidence, null);
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            // XPAY-325 FASE 7 — estas 3 excepciones ya son mensajes
            // estáticos y saneados por diseño (PassportHttpClient/
            // PassportKeyClient, sesiones previas): nunca incluyen body,
            // token ni Authorization. Se reutiliza el mensaje tal cual en
            // `notes` — no se afirma un http_status que no se conoce.
            var evidence = CreateKeyEvidenceBuilder.BuildPassportHttpFailure(
                ex.Message, httpStatus: null, commitSha, executedAtUtc);
            return new(CreateKeyExecutionOutcome.PassportFailure, evidence, ex.Message);
        }
    }
}
