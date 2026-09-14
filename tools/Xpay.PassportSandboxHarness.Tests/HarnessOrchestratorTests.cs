using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-312 — tests OFFLINE de HarnessOrchestrator/HarnessDecision. Ningún
// test aquí construye un HttpClient real ni realiza I/O de red: sólo se
// verifica la decisión pura a partir de args + un IConfiguration en memoria.
public class HarnessOrchestratorTests
{
    private const string ValidSandboxUrl = "https://api.paas.sandbox.co.passportfintech.com";

    private static IConfiguration ConfigWith(
        string? baseUrl = ValidSandboxUrl, string? apiKey = "synthetic-key", string? apiSecret = "synthetic-secret")
    {
        var dict = new Dictionary<string, string?>();
        if (baseUrl is not null) dict[PassportOptions.EnvBaseUrl] = baseUrl;
        if (apiKey is not null) dict[PassportOptions.EnvClientId] = apiKey;
        if (apiSecret is not null) dict[PassportOptions.EnvClientSecret] = apiSecret;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    // 1. sin comando execute (sin argumentos) => NO HTTP.
    [Fact]
    public void Prepare_NoArgs_ReturnsShowHelp()
    {
        var decision = HarnessOrchestrator.Prepare(Array.Empty<string>(), ConfigWith());
        Assert.Equal(HarnessOrchestrator.Outcome.ShowHelp, decision.Outcome);
    }

    [Fact]
    public void Prepare_UnknownCommand_ReturnsShowHelp()
    {
        var decision = HarnessOrchestrator.Prepare(new[] { "resolve-key" }, ConfigWith());
        Assert.Equal(HarnessOrchestrator.Outcome.ShowHelp, decision.Outcome);
    }

    // 2. dry-run (comando válido, sin --execute) => NO HTTP.
    [Fact]
    public void Prepare_CreateCustomerNoFlags_ReturnsDryRun()
    {
        var decision = HarnessOrchestrator.Prepare(new[] { "create-customer" }, ConfigWith());
        Assert.Equal(HarnessOrchestrator.Outcome.DryRun, decision.Outcome);
    }

    // 3. config faltante => falla antes HTTP.
    [Theory]
    [InlineData(null, "k", "s")]
    [InlineData(ValidSandboxUrl, null, "s")]
    [InlineData(ValidSandboxUrl, "k", null)]
    public void Prepare_MissingConfig_ReturnsAbortedConfigMissing(string? baseUrl, string? apiKey, string? apiSecret)
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "create-customer" }, ConfigWith(baseUrl, apiKey, apiSecret));
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedConfigMissing, decision.Outcome);
    }

    // 4. host no-Sandbox => falla antes HTTP.
    [Theory]
    [InlineData("https://api.paas-sandbox.co.passportfintech.com")] // guion, host NO exacto
    [InlineData("https://api.passportfintech.com")]                  // host de producción hipotético
    [InlineData("https://evil.example.com")]
    public void Prepare_NonSandboxHost_ReturnsAbortedNonSandboxHost(string baseUrl)
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "create-customer" }, ConfigWith(baseUrl: baseUrl));
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedNonSandboxHost, decision.Outcome);
    }

    // 5. falta confirmación de create (--execute sin --confirm-create-customer) => NO HTTP.
    [Fact]
    public void Prepare_ExecuteWithoutConfirm_ReturnsAbortedMissingConfirmation()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "create-customer", "--execute" }, ConfigWith());
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedMissingConfirmation, decision.Outcome);
    }

    [Fact]
    public void Prepare_ConfirmWithoutExecute_ReturnsDryRun()
    {
        // Sólo --confirm-create-customer sin --execute NO es una autorización
        // válida por sí sola: debe seguir siendo dry-run, nunca Execute.
        var decision = HarnessOrchestrator.Prepare(
            new[] { "create-customer", "--confirm-create-customer" }, ConfigWith());
        Assert.Equal(HarnessOrchestrator.Outcome.DryRun, decision.Outcome);
    }

    [Fact]
    public void Prepare_ExecuteAndConfirm_ReturnsReadyToExecute()
    {
        // Confirma que la ÚNICA combinación que habilita Execute es la
        // esperada — este test NUNCA dispara una llamada HTTP real: sólo
        // verifica la decisión pura, Program.cs es quien construiría el
        // stack real, y XPAY-312 nunca invoca este outcome en ejecución real.
        var decision = HarnessOrchestrator.Prepare(
            new[] { "create-customer", "--execute", "--confirm-create-customer" }, ConfigWith());
        Assert.Equal(HarnessOrchestrator.Outcome.ReadyToExecute, decision.Outcome);
    }
}
