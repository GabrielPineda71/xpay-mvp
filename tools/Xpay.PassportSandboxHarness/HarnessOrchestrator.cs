using Microsoft.Extensions.Configuration;

namespace Xpay.PassportSandboxHarness;

// XPAY-312/325 — orquestación PURA de la decisión de qué hacer: parsea
// args, verifica presencia de config (sin exponer valores) y el guard de
// host Sandbox, ANTES de que Program.cs construya cualquier
// HttpClient/token provider real. Ninguna rama de este método realiza I/O
// de red — es 100% testeable offline con un IConfiguration en memoria.
public static class HarnessOrchestrator
{
    public enum Outcome
    {
        ShowHelp,
        AbortedMissingConfirmation,
        AbortedConfigMissing,
        AbortedTargetMissing, // XPAY-325 — faltan targets específicos del caso (p. ej. account_id/key nuevo)
        AbortedNonSandboxHost,
        DryRun,
        ReadyToExecute, // Program.cs decide qué hacer con esto; XPAY-312/325 nunca lo alcanzan en ejecución real.
    }

    public sealed record Decision(
        HarnessCommand Command,
        Outcome Outcome,
        HarnessConfigStatus ConfigStatus,
        HarnessTargetConfig? TargetConfig,
        string? Detail);

    public static Decision Prepare(string[] args, IConfiguration configuration)
    {
        var command = HarnessDecision.ParseCommand(args);
        var action  = HarnessDecision.Decide(args, out var abortReason);

        if (action == HarnessAction.ShowHelp)
            return new(command, Outcome.ShowHelp, HarnessConfigStatus.FromConfiguration(configuration), null, null);

        // El chequeo de config se hace SIEMPRE (incluso si action==Aborted por
        // falta de confirmación) porque el harness debe reportar el estado de
        // config de forma redactada en cualquier salida no-ShowHelp.
        var configStatus = HarnessConfigStatus.FromConfiguration(configuration);
        var targetConfig = command is HarnessCommand.CreateKey or HarnessCommand.SuspendKey
                                    or HarnessCommand.ActivateKey or HarnessCommand.DeleteKey
                                    or HarnessCommand.DeleteAlreadyDeletedKey or HarnessCommand.ResolveKey
                                    or HarnessCommand.CreateKeyMissing or HarnessCommand.CreateKeyInvalid
            ? HarnessTargetConfig.FromConfiguration(configuration)
            : null;

        if (action == HarnessAction.Aborted)
            return new(command, Outcome.AbortedMissingConfirmation, configStatus, targetConfig, abortReason);

        if (!configStatus.AllPresent)
            return new(command, Outcome.AbortedConfigMissing, configStatus, targetConfig,
                "Config incompleta — ver detalle AVAILABLE/MISSING por variable.");

        // XPAY-325 — para create-key, además de la config genérica, deben
        // estar presentes los targets específicos del caso (account_id de
        // la cuenta Sandbox ya provista, y key_type/key_value de la llave
        // NUEVA y desechable a crear) ANTES de construir ningún request.
        if (command == HarnessCommand.CreateKey && targetConfig is not null && !targetConfig.AllPresentForCreateKey)
            return new(command, Outcome.AbortedTargetMissing, configStatus, targetConfig,
                "Targets de create-key incompletos — ver detalle AVAILABLE/MISSING por variable.");

        // XPAY-326/332/334/336 — para suspend-key/activate-key/delete-key/
        // delete-already-deleted-key, debe estar presente el key_id
        // REMOTO real de la llave de certificación
        // (PASSPORT_TEST_NEW_KEY_ID) — nunca derivado del key_value ni de
        // un fingerprint, y nunca recuperado de nuevo vía List Keys aquí.
        // Para M3-T7 es intencional que ese key_id apunte a una llave YA
        // eliminada (XPAY-336) — no se sustituye ni se recupera otro.
        if (command is HarnessCommand.SuspendKey or HarnessCommand.ActivateKey
                     or HarnessCommand.DeleteKey or HarnessCommand.DeleteAlreadyDeletedKey
            && targetConfig is not null && !targetConfig.AllPresentForExistingKeyOperations)
        {
            var commandLabel = command switch
            {
                HarnessCommand.SuspendKey  => "suspend-key",
                HarnessCommand.ActivateKey => "activate-key",
                HarnessCommand.DeleteKey   => "delete-key",
                _                          => "delete-already-deleted-key",
            };
            return new(command, Outcome.AbortedTargetMissing, configStatus, targetConfig,
                $"Target de {commandLabel} incompleto — PASSPORT_TEST_NEW_KEY_ID ausente.");
        }

        // XPAY-340 — para resolve-key, target COMPLETAMENTE DISTINTO: los
        // recursos Bre-B de prueba ya provistos por Passport
        // (PASSPORT_TEST_CUSTOMER_ID/PASSPORT_TEST_BREB_KEY_TYPE/
        // PASSPORT_TEST_BREB_KEY), nunca PASSPORT_TEST_NEW_KEY_ID.
        if (command == HarnessCommand.ResolveKey && targetConfig is not null && !targetConfig.AllPresentForResolveKey)
            return new(command, Outcome.AbortedTargetMissing, configStatus, targetConfig,
                "Target de resolve-key incompleto — PASSPORT_TEST_CUSTOMER_ID/PASSPORT_TEST_BREB_KEY_TYPE/PASSPORT_TEST_BREB_KEY ausente(s).");

        // XPAY-344 — create-key-missing/create-key-invalid (M3-T6): sólo
        // requieren account_id + key_type (PASSPORT_TEST_NEW_KEY_VALUE
        // NUNCA se exige ni se lee para ninguno de los dos).
        if (command is HarnessCommand.CreateKeyMissing or HarnessCommand.CreateKeyInvalid
            && targetConfig is not null && !targetConfig.AllPresentForAccountAndKeyType)
        {
            var commandLabel = command == HarnessCommand.CreateKeyMissing ? "create-key-missing" : "create-key-invalid";
            return new(command, Outcome.AbortedTargetMissing, configStatus, targetConfig,
                $"Target de {commandLabel} incompleto — PASSPORT_TEST_ACCOUNT_ID/PASSPORT_TEST_NEW_KEY_TYPE ausente(s).");
        }

        var baseUrl = configuration[Xpay.Api.Integrations.Passport.PassportOptions.EnvBaseUrl];
        if (!SandboxHostGuard.IsAuthorizedSandboxHost(baseUrl))
            return new(command, Outcome.AbortedNonSandboxHost, configStatus, targetConfig,
                $"PASSPORT_BASE_URL no corresponde al host Sandbox autorizado ({SandboxHostGuard.AuthorizedSandboxHost}).");

        return action == HarnessAction.DryRun
            ? new(command, Outcome.DryRun, configStatus, targetConfig, null)
            : new(command, Outcome.ReadyToExecute, configStatus, targetConfig, null);
    }
}
