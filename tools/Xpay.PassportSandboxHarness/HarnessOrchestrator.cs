using Microsoft.Extensions.Configuration;

namespace Xpay.PassportSandboxHarness;

// XPAY-312 — orquestación PURA de la decisión de qué hacer: parsea args,
// verifica presencia de config (sin exponer valores) y el guard de host
// Sandbox, ANTES de que Program.cs construya cualquier HttpClient/token
// provider real. Ninguna rama de este método realiza I/O de red — es
// 100% testeable offline con un IConfiguration en memoria.
public static class HarnessOrchestrator
{
    public enum Outcome
    {
        ShowHelp,
        AbortedMissingConfirmation,
        AbortedConfigMissing,
        AbortedNonSandboxHost,
        DryRun,
        ReadyToExecute, // Program.cs decide qué hacer con esto; XPAY-312 nunca lo alcanza en ejecución real.
    }

    public sealed record Decision(Outcome Outcome, HarnessConfigStatus ConfigStatus, string? Detail);

    public static Decision Prepare(string[] args, IConfiguration configuration)
    {
        var action = HarnessDecision.Decide(args, out var abortReason);

        if (action == HarnessAction.ShowHelp)
            return new(Outcome.ShowHelp, HarnessConfigStatus.FromConfiguration(configuration), null);

        // El chequeo de config se hace SIEMPRE (incluso si action==Aborted por
        // falta de --confirm) porque el harness debe reportar el estado de
        // config de forma redactada en cualquier salida no-ShowHelp.
        var configStatus = HarnessConfigStatus.FromConfiguration(configuration);

        if (action == HarnessAction.Aborted)
            return new(Outcome.AbortedMissingConfirmation, configStatus, abortReason);

        if (!configStatus.AllPresent)
            return new(Outcome.AbortedConfigMissing, configStatus,
                "Config incompleta — ver detalle AVAILABLE/MISSING por variable.");

        var baseUrl = configuration[Xpay.Api.Integrations.Passport.PassportOptions.EnvBaseUrl];
        if (!SandboxHostGuard.IsAuthorizedSandboxHost(baseUrl))
            return new(Outcome.AbortedNonSandboxHost, configStatus,
                $"PASSPORT_BASE_URL no corresponde al host Sandbox autorizado ({SandboxHostGuard.AuthorizedSandboxHost}).");

        return action == HarnessAction.DryRun
            ? new(Outcome.DryRun, configStatus, null)
            : new(Outcome.ReadyToExecute, configStatus, null);
    }
}
