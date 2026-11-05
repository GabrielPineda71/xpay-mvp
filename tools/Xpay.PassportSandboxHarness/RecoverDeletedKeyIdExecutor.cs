using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-489 — orquesta la ejecución REAL, READ-ONLY, de List Keys (GET
// /v1/keys) para intentar localizar de forma segura y determinística el
// key_id remoto de la llave histórica DELETED del ciclo M3 (M3-T1 → T3 →
// T4 → T5, confirmada eliminada; ver M3-T5/evidence, fingerprint
// 9654CF47D144) — PASSPORT_TEST_NEW_KEY_ID quedó contaminado (XPAY-487/488,
// apunta hoy al mismo valor que la llave ACTIVA protegida) y el key_id
// crudo nunca se persistió en ningún artefacto local recuperable.
//
// Reutiliza SIN modificar los building blocks ya existentes desde
// XPAY-327/328: IPassportKeyClient.ListKeysAsync (la única llamada HTTP) y
// KeySelector.SelectExactMatch (función pura de selección determinística
// account_id+key_type+key_value, fail-closed en 0 o >1 coincidencias).
//
// Este executor NUNCA:
//   - crea, suspende, activa, elimina, ni resuelve ninguna llave (sólo
//     invoca ListKeysAsync — ninguna otra API de IPassportKeyClient);
//   - escribe ~/.passport-sandbox.env ni ninguna variable de entorno;
//   - cae en fallback hacia otro criterio si no hay coincidencia exacta.
public static class RecoverDeletedKeyIdExecutor
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

        // XPAY-489 — criterios de búsqueda EXCLUSIVAMENTE:
        // PASSPORT_TEST_ACCOUNT_ID + PASSPORT_TEST_NEW_KEY_TYPE +
        // PASSPORT_TEST_NEW_KEY_VALUE — los tres datos que XPAY conserva
        // privadamente de la llave de M3-T1, sin depender del key_id
        // (justamente el valor que se perdió/contaminó). Deliberadamente
        // NUNCA lee PASSPORT_TEST_NEW_KEY_ID aquí — ese es el valor hoy no
        // confiable que este comando existe para validar/reemplazar, no
        // para reutilizar como entrada.
        var accountId = configuration[HarnessTargetConfig.EnvAccountId];
        var keyTypeRaw = configuration[HarnessTargetConfig.EnvNewKeyType];
        var keyValue = configuration[HarnessTargetConfig.EnvNewKeyValue];

        // Defensa en profundidad: HarnessOrchestrator.Prepare ya debería
        // haber bloqueado esto (Outcome.AbortedTargetMissing) antes de que
        // el flujo llegue aquí.
        if (string.IsNullOrWhiteSpace(accountId)
            || string.IsNullOrWhiteSpace(keyTypeRaw)
            || string.IsNullOrWhiteSpace(keyValue))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_ACCOUNT_ID/PASSPORT_TEST_NEW_KEY_TYPE/PASSPORT_TEST_NEW_KEY_VALUE ausente(s) al momento de intentar Recover Deleted Key Id.");
        }

        if (!Enum.TryParse<PassportKeyType>(keyTypeRaw, out var keyType) || !Enum.IsDefined(keyType))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_NEW_KEY_TYPE no es un PassportKeyType válido.");
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
            // Exactamente UNA llamada — sin reintentos, sin segundo HTTP.
            // ÚNICAMENTE ListKeysAsync — nunca Create/Suspend/Activate/
            // Delete/Resolve Key, nunca IPassportQrClient.
            var response = await keyClient.ListKeysAsync(accountId, keyType, keyValue).ConfigureAwait(false);

            // Selección PURA, fail-closed en 0 o >1 coincidencias — nunca
            // "la primera", nunca desambiguación heurística.
            var selection = KeySelector.SelectExactMatch(response, accountId, keyType, keyValue);

            var evidence = RecoverDeletedKeyIdEvidenceBuilder.BuildSuccess(
                accountId, keyType, selection, httpStatus: null, commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            var (httpStatus, safeErrorCode, safeErrorMessage) = ex is PassportTransportException pte
                ? (pte.StatusCode, pte.SafeErrorCode, pte.SafeErrorMessage)
                : (null, null, null);

            var evidence = RecoverDeletedKeyIdEvidenceBuilder.BuildPassportHttpFailure(
                ex.Message, httpStatus, commitSha, executedAtUtc, safeErrorCode, safeErrorMessage);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }
}
