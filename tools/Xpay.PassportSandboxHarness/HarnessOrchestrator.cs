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
                                    or HarnessCommand.CreateQrStatic or HarnessCommand.DecodeQrStatic
                                    or HarnessCommand.CreateQrStaticSuspendedKey
                                    or HarnessCommand.CreateQrStaticDeletedKey
                                    or HarnessCommand.CreateQrStaticInvalidCustomer
                                    or HarnessCommand.CreateM4T3SuspendedFixtureKey
                                    or HarnessCommand.SuspendM4T3FixtureKey
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

        // XPAY-351/XPAY-460 — create-qr-static (M4-T1): target propio
        // (PASSPORT_TEST_QR_KEY_ID + PASSPORT_TEST_CUSTOMER_ID) — ver
        // HarnessTargetConfig.AllPresentForCreateQrStatic. XPAY-460:
        // PASSPORT_TEST_NEW_KEY_ID (llave DELETED de M3) NUNCA sirve de
        // fallback aquí, ni siquiera si está presente.
        if (command == HarnessCommand.CreateQrStatic && targetConfig is not null && !targetConfig.AllPresentForCreateQrStatic)
            return new(command, Outcome.AbortedTargetMissing, configStatus, targetConfig,
                "Target de create-qr-static incompleto — PASSPORT_TEST_QR_KEY_ID/PASSPORT_TEST_CUSTOMER_ID ausente(s).");

        // XPAY-465 — decode-qr-static (M4-T2): target propio
        // (PASSPORT_TEST_QR_DECODE_DATA_FILE + PASSPORT_TEST_CUSTOMER_ID) —
        // ver HarnessTargetConfig.AllPresentForDecodeQrStatic. Completamente
        // independiente del target de create-qr-static (M4-T1): ninguno de
        // los dos sirve de fallback del otro.
        if (command == HarnessCommand.DecodeQrStatic && targetConfig is not null && !targetConfig.AllPresentForDecodeQrStatic)
            return new(command, Outcome.AbortedTargetMissing, configStatus, targetConfig,
                "Target de decode-qr-static incompleto — PASSPORT_TEST_QR_DECODE_DATA_FILE/PASSPORT_TEST_CUSTOMER_ID ausente(s).");

        // XPAY-471 — create-qr-static-suspended-key (M4-T3-A): target propio
        // (PASSPORT_TEST_QR_SUSPENDED_KEY_ID + PASSPORT_TEST_CUSTOMER_ID).
        // NUNCA cae en fallback hacia PASSPORT_TEST_QR_KEY_ID ni
        // PASSPORT_TEST_NEW_KEY_ID — ver HarnessTargetConfig.
        // AllPresentForCreateQrStaticSuspendedKey.
        if (command == HarnessCommand.CreateQrStaticSuspendedKey && targetConfig is not null
            && !targetConfig.AllPresentForCreateQrStaticSuspendedKey)
            return new(command, Outcome.AbortedTargetMissing, configStatus, targetConfig,
                "Target de create-qr-static-suspended-key incompleto — PASSPORT_TEST_QR_SUSPENDED_KEY_ID/PASSPORT_TEST_CUSTOMER_ID ausente(s).");

        // XPAY-471 — create-qr-static-deleted-key (M4-T3-B): target propio
        // (PASSPORT_TEST_NEW_KEY_ID + PASSPORT_TEST_CUSTOMER_ID) — reutiliza
        // la llave DELETED de M3 únicamente como referencia histórica.
        if (command == HarnessCommand.CreateQrStaticDeletedKey && targetConfig is not null
            && !targetConfig.AllPresentForCreateQrStaticDeletedKey)
            return new(command, Outcome.AbortedTargetMissing, configStatus, targetConfig,
                "Target de create-qr-static-deleted-key incompleto — PASSPORT_TEST_NEW_KEY_ID/PASSPORT_TEST_CUSTOMER_ID ausente(s).");

        // XPAY-471 — create-qr-static-invalid-customer (M4-T3-C): target
        // propio (ÚNICAMENTE PASSPORT_TEST_QR_KEY_ID — el customer_id
        // siempre es sintético, generado internamente, nunca leído del
        // entorno).
        if (command == HarnessCommand.CreateQrStaticInvalidCustomer && targetConfig is not null
            && !targetConfig.AllPresentForCreateQrStaticInvalidCustomer)
            return new(command, Outcome.AbortedTargetMissing, configStatus, targetConfig,
                "Target de create-qr-static-invalid-customer incompleto — PASSPORT_TEST_QR_KEY_ID ausente.");

        // XPAY-474 — create-m4-t3-suspended-fixture-key: target propio
        // (ÚNICAMENTE PASSPORT_TEST_ACCOUNT_ID — key_type es constante
        // BCODE, key_value se genera internamente).
        if (command == HarnessCommand.CreateM4T3SuspendedFixtureKey && targetConfig is not null
            && !targetConfig.AllPresentForCreateM4T3SuspendedFixtureKey)
            return new(command, Outcome.AbortedTargetMissing, configStatus, targetConfig,
                "Target de create-m4-t3-suspended-fixture-key incompleto — PASSPORT_TEST_ACCOUNT_ID ausente.");

        // XPAY-474 — suspend-m4-t3-fixture-key: target propio (ÚNICAMENTE
        // PASSPORT_TEST_QR_SUSPENDED_KEY_ID). NUNCA cae en fallback hacia
        // PASSPORT_TEST_NEW_KEY_ID (M3) ni PASSPORT_TEST_QR_KEY_ID (llave
        // activa protegida M4-T1/T2).
        if (command == HarnessCommand.SuspendM4T3FixtureKey && targetConfig is not null
            && !targetConfig.AllPresentForSuspendM4T3FixtureKey)
            return new(command, Outcome.AbortedTargetMissing, configStatus, targetConfig,
                "Target de suspend-m4-t3-fixture-key incompleto — PASSPORT_TEST_QR_SUSPENDED_KEY_ID ausente.");

        var baseUrl = configuration[Xpay.Api.Integrations.Passport.PassportOptions.EnvBaseUrl];
        if (!SandboxHostGuard.IsAuthorizedSandboxHost(baseUrl))
            return new(command, Outcome.AbortedNonSandboxHost, configStatus, targetConfig,
                $"PASSPORT_BASE_URL no corresponde al host Sandbox autorizado ({SandboxHostGuard.AuthorizedSandboxHost}).");

        return action == HarnessAction.DryRun
            ? new(command, Outcome.DryRun, configStatus, targetConfig, null)
            : new(command, Outcome.ReadyToExecute, configStatus, targetConfig, null);
    }
}
