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

    // ══════════════════════════════════════════════════════════════════════
    // XPAY-325 — create-key (M3-T1)
    // ══════════════════════════════════════════════════════════════════════

    private const string ValidTargetAccountId = "synthetic-account-id-001";
    private const string ValidTargetKeyType   = "BCODE";
    private const string ValidTargetKeyValue  = "0000000000";

    private static IConfiguration ConfigWithTargets(
        string? baseUrl = ValidSandboxUrl, string? apiKey = "synthetic-key", string? apiSecret = "synthetic-secret",
        string? accountId = ValidTargetAccountId, string? newKeyType = ValidTargetKeyType, string? newKeyValue = ValidTargetKeyValue)
    {
        var dict = new Dictionary<string, string?>();
        if (baseUrl is not null) dict[PassportOptions.EnvBaseUrl] = baseUrl;
        if (apiKey is not null) dict[PassportOptions.EnvClientId] = apiKey;
        if (apiSecret is not null) dict[PassportOptions.EnvClientSecret] = apiSecret;
        if (accountId is not null) dict[HarnessTargetConfig.EnvAccountId] = accountId;
        if (newKeyType is not null) dict[HarnessTargetConfig.EnvNewKeyType] = newKeyType;
        if (newKeyValue is not null) dict[HarnessTargetConfig.EnvNewKeyValue] = newKeyValue;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    // A. create-key dry-run: cero HTTP (Outcome nunca llega a Execute/ReadyToExecute).
    [Fact]
    public void Prepare_CreateKeyNoFlags_ReturnsDryRun()
    {
        var decision = HarnessOrchestrator.Prepare(new[] { "create-key" }, ConfigWithTargets());
        Assert.Equal(HarnessCommand.CreateKey, decision.Command);
        Assert.Equal(HarnessOrchestrator.Outcome.DryRun, decision.Outcome);
    }

    // C. configuración genérica faltante bloquea antes de HTTP.
    [Theory]
    [InlineData(null, "k", "s")]
    [InlineData(ValidSandboxUrl, null, "s")]
    [InlineData(ValidSandboxUrl, "k", null)]
    public void Prepare_CreateKey_MissingGenericConfig_ReturnsAbortedConfigMissing(
        string? baseUrl, string? apiKey, string? apiSecret)
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "create-key" }, ConfigWithTargets(baseUrl, apiKey, apiSecret));
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedConfigMissing, decision.Outcome);
    }

    // C (extendido) — targets específicos de create-key faltantes bloquean antes de HTTP.
    [Theory]
    [InlineData(null, ValidTargetKeyType, ValidTargetKeyValue)]
    [InlineData(ValidTargetAccountId, null, ValidTargetKeyValue)]
    [InlineData(ValidTargetAccountId, ValidTargetKeyType, null)]
    public void Prepare_CreateKey_MissingTarget_ReturnsAbortedTargetMissing(
        string? accountId, string? newKeyType, string? newKeyValue)
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "create-key" },
            ConfigWithTargets(accountId: accountId, newKeyType: newKeyType, newKeyValue: newKeyValue));
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedTargetMissing, decision.Outcome);
    }

    // D. host no-Sandbox bloquea antes de HTTP (incluso con targets completos).
    [Fact]
    public void Prepare_CreateKey_NonSandboxHost_ReturnsAbortedNonSandboxHost()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "create-key" }, ConfigWithTargets(baseUrl: "https://evil.example.com"));
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedNonSandboxHost, decision.Outcome);
    }

    [Fact]
    public void Prepare_CreateKey_ExecuteWithoutConfirm_ReturnsAbortedMissingConfirmation()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "create-key", "--execute" }, ConfigWithTargets());
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedMissingConfirmation, decision.Outcome);
    }

    [Fact]
    public void Prepare_CreateKey_ConfirmCreateCustomerDoesNotAuthorizeCreateKey()
    {
        // XPAY-325 — --confirm-create-customer NUNCA debe autorizar create-key:
        // cada comando mutante tiene su propia bandera de confirmación.
        var decision = HarnessOrchestrator.Prepare(
            new[] { "create-key", "--execute", "--confirm-create-customer" }, ConfigWithTargets());
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedMissingConfirmation, decision.Outcome);
    }

    // B. --execute + --confirm-create-key => ReadyToExecute (nunca invocado realmente en XPAY-325).
    [Fact]
    public void Prepare_CreateKey_ExecuteAndConfirm_ReturnsReadyToExecute()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "create-key", "--execute", "--confirm-create-key" }, ConfigWithTargets());
        Assert.Equal(HarnessOrchestrator.Outcome.ReadyToExecute, decision.Outcome);
    }

    // ══════════════════════════════════════════════════════════════════════
    // XPAY-326 — suspend-key (M3-T3)
    // ══════════════════════════════════════════════════════════════════════

    private const string ValidTargetKeyId = "synthetic-remote-key-id-001";

    private static IConfiguration ConfigWithSuspendTarget(
        string? baseUrl = ValidSandboxUrl, string? apiKey = "synthetic-key", string? apiSecret = "synthetic-secret",
        string? newKeyId = ValidTargetKeyId)
    {
        var dict = new Dictionary<string, string?>();
        if (baseUrl is not null) dict[PassportOptions.EnvBaseUrl] = baseUrl;
        if (apiKey is not null) dict[PassportOptions.EnvClientId] = apiKey;
        if (apiSecret is not null) dict[PassportOptions.EnvClientSecret] = apiSecret;
        if (newKeyId is not null) dict[HarnessTargetConfig.EnvNewKeyId] = newKeyId;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    // A. suspend-key dry-run: cero HTTP (Outcome nunca llega a Execute/ReadyToExecute).
    [Fact]
    public void Prepare_SuspendKeyNoFlags_ReturnsDryRun()
    {
        var decision = HarnessOrchestrator.Prepare(new[] { "suspend-key" }, ConfigWithSuspendTarget());
        Assert.Equal(HarnessCommand.SuspendKey, decision.Command);
        Assert.Equal(HarnessOrchestrator.Outcome.DryRun, decision.Outcome);
    }

    // B. --execute solo (sin --confirm-suspend-key) => Aborted, nunca HTTP.
    [Fact]
    public void Prepare_SuspendKey_ExecuteWithoutConfirm_ReturnsAbortedMissingConfirmation()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "suspend-key", "--execute" }, ConfigWithSuspendTarget());
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedMissingConfirmation, decision.Outcome);
    }

    // C. --confirm-suspend-key solo (sin --execute) => sigue en DryRun, nunca Execute.
    [Fact]
    public void Prepare_SuspendKey_ConfirmWithoutExecute_ReturnsDryRun()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "suspend-key", "--confirm-suspend-key" }, ConfigWithSuspendTarget());
        Assert.Equal(HarnessOrchestrator.Outcome.DryRun, decision.Outcome);
    }

    // D. --confirm-create-key NUNCA autoriza suspend-key (banderas específicas por comando).
    [Fact]
    public void Prepare_SuspendKey_ConfirmCreateKeyDoesNotAuthorizeSuspendKey()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "suspend-key", "--execute", "--confirm-create-key" }, ConfigWithSuspendTarget());
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedMissingConfirmation, decision.Outcome);
    }

    // E. PASSPORT_TEST_NEW_KEY_ID ausente => AbortedTargetMissing, nunca HTTP.
    [Fact]
    public void Prepare_SuspendKey_MissingKeyId_ReturnsAbortedTargetMissing()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "suspend-key" }, ConfigWithSuspendTarget(newKeyId: null));
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedTargetMissing, decision.Outcome);
    }

    // F. host no-Sandbox bloquea antes de HTTP (incluso con key_id presente).
    [Fact]
    public void Prepare_SuspendKey_NonSandboxHost_ReturnsAbortedNonSandboxHost()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "suspend-key" }, ConfigWithSuspendTarget(baseUrl: "https://evil.example.com"));
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedNonSandboxHost, decision.Outcome);
    }

    // G. --execute + --confirm-suspend-key => ReadyToExecute (Program.cs/HarnessApp deciden qué hacer).
    [Fact]
    public void Prepare_SuspendKey_ExecuteAndConfirm_ReturnsReadyToExecute()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "suspend-key", "--execute", "--confirm-suspend-key" }, ConfigWithSuspendTarget());
        Assert.Equal(HarnessOrchestrator.Outcome.ReadyToExecute, decision.Outcome);
    }

    // ══════════════════════════════════════════════════════════════════════
    // XPAY-332 — activate-key (M3-T4) — mirror exacto de suspend-key (M3-T3),
    // misma variable target (PASSPORT_TEST_NEW_KEY_ID) reutilizada.
    // ══════════════════════════════════════════════════════════════════════

    private static IConfiguration ConfigWithActivateTarget(
        string? baseUrl = ValidSandboxUrl, string? apiKey = "synthetic-key", string? apiSecret = "synthetic-secret",
        string? newKeyId = ValidTargetKeyId)
    {
        var dict = new Dictionary<string, string?>();
        if (baseUrl is not null) dict[PassportOptions.EnvBaseUrl] = baseUrl;
        if (apiKey is not null) dict[PassportOptions.EnvClientId] = apiKey;
        if (apiSecret is not null) dict[PassportOptions.EnvClientSecret] = apiSecret;
        if (newKeyId is not null) dict[HarnessTargetConfig.EnvNewKeyId] = newKeyId;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    // A. activate-key dry-run: cero HTTP.
    [Fact]
    public void Prepare_ActivateKeyNoFlags_ReturnsDryRun()
    {
        var decision = HarnessOrchestrator.Prepare(new[] { "activate-key" }, ConfigWithActivateTarget());
        Assert.Equal(HarnessCommand.ActivateKey, decision.Command);
        Assert.Equal(HarnessOrchestrator.Outcome.DryRun, decision.Outcome);
    }

    // B. --execute solo (sin --confirm-activate-key) => Aborted, nunca HTTP.
    [Fact]
    public void Prepare_ActivateKey_ExecuteWithoutConfirm_ReturnsAbortedMissingConfirmation()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "activate-key", "--execute" }, ConfigWithActivateTarget());
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedMissingConfirmation, decision.Outcome);
    }

    // C. --confirm-activate-key solo (sin --execute) => sigue en DryRun.
    [Fact]
    public void Prepare_ActivateKey_ConfirmWithoutExecute_ReturnsDryRun()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "activate-key", "--confirm-activate-key" }, ConfigWithActivateTarget());
        Assert.Equal(HarnessOrchestrator.Outcome.DryRun, decision.Outcome);
    }

    // D. --confirm-suspend-key NUNCA autoriza activate-key (banderas específicas por comando).
    [Fact]
    public void Prepare_ActivateKey_ConfirmSuspendKeyDoesNotAuthorizeActivateKey()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "activate-key", "--execute", "--confirm-suspend-key" }, ConfigWithActivateTarget());
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedMissingConfirmation, decision.Outcome);
    }

    // E. PASSPORT_TEST_NEW_KEY_ID ausente => AbortedTargetMissing, nunca HTTP.
    [Fact]
    public void Prepare_ActivateKey_MissingKeyId_ReturnsAbortedTargetMissing()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "activate-key" }, ConfigWithActivateTarget(newKeyId: null));
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedTargetMissing, decision.Outcome);
    }

    // F. host no-Sandbox bloquea antes de HTTP (incluso con key_id presente).
    [Fact]
    public void Prepare_ActivateKey_NonSandboxHost_ReturnsAbortedNonSandboxHost()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "activate-key" }, ConfigWithActivateTarget(baseUrl: "https://evil.example.com"));
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedNonSandboxHost, decision.Outcome);
    }

    // G. --execute + --confirm-activate-key => ReadyToExecute.
    [Fact]
    public void Prepare_ActivateKey_ExecuteAndConfirm_ReturnsReadyToExecute()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "activate-key", "--execute", "--confirm-activate-key" }, ConfigWithActivateTarget());
        Assert.Equal(HarnessOrchestrator.Outcome.ReadyToExecute, decision.Outcome);
    }

    // ══════════════════════════════════════════════════════════════════════
    // XPAY-334 — delete-key (M3-T5) — mirror exacto de suspend-key/
    // activate-key, misma variable target (PASSPORT_TEST_NEW_KEY_ID).
    // ══════════════════════════════════════════════════════════════════════

    private static IConfiguration ConfigWithDeleteTarget(
        string? baseUrl = ValidSandboxUrl, string? apiKey = "synthetic-key", string? apiSecret = "synthetic-secret",
        string? newKeyId = ValidTargetKeyId)
    {
        var dict = new Dictionary<string, string?>();
        if (baseUrl is not null) dict[PassportOptions.EnvBaseUrl] = baseUrl;
        if (apiKey is not null) dict[PassportOptions.EnvClientId] = apiKey;
        if (apiSecret is not null) dict[PassportOptions.EnvClientSecret] = apiSecret;
        if (newKeyId is not null) dict[HarnessTargetConfig.EnvNewKeyId] = newKeyId;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    // A. delete-key dry-run: cero HTTP.
    [Fact]
    public void Prepare_DeleteKeyNoFlags_ReturnsDryRun()
    {
        var decision = HarnessOrchestrator.Prepare(new[] { "delete-key" }, ConfigWithDeleteTarget());
        Assert.Equal(HarnessCommand.DeleteKey, decision.Command);
        Assert.Equal(HarnessOrchestrator.Outcome.DryRun, decision.Outcome);
    }

    // B. --execute solo (sin --confirm-delete-key) => Aborted, nunca HTTP.
    [Fact]
    public void Prepare_DeleteKey_ExecuteWithoutConfirm_ReturnsAbortedMissingConfirmation()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "delete-key", "--execute" }, ConfigWithDeleteTarget());
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedMissingConfirmation, decision.Outcome);
    }

    // C. --confirm-suspend-key NUNCA autoriza delete-key.
    [Fact]
    public void Prepare_DeleteKey_ConfirmSuspendKeyDoesNotAuthorizeDeleteKey()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "delete-key", "--execute", "--confirm-suspend-key" }, ConfigWithDeleteTarget());
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedMissingConfirmation, decision.Outcome);
    }

    // D. --confirm-activate-key NUNCA autoriza delete-key.
    [Fact]
    public void Prepare_DeleteKey_ConfirmActivateKeyDoesNotAuthorizeDeleteKey()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "delete-key", "--execute", "--confirm-activate-key" }, ConfigWithDeleteTarget());
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedMissingConfirmation, decision.Outcome);
    }

    // E. --confirm-create-key NUNCA autoriza delete-key.
    [Fact]
    public void Prepare_DeleteKey_ConfirmCreateKeyDoesNotAuthorizeDeleteKey()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "delete-key", "--execute", "--confirm-create-key" }, ConfigWithDeleteTarget());
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedMissingConfirmation, decision.Outcome);
    }

    // F. PASSPORT_TEST_NEW_KEY_ID ausente => AbortedTargetMissing, nunca HTTP.
    [Fact]
    public void Prepare_DeleteKey_MissingKeyId_ReturnsAbortedTargetMissing()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "delete-key" }, ConfigWithDeleteTarget(newKeyId: null));
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedTargetMissing, decision.Outcome);
    }

    // host no-Sandbox bloquea antes de HTTP (incluso con key_id presente).
    [Fact]
    public void Prepare_DeleteKey_NonSandboxHost_ReturnsAbortedNonSandboxHost()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "delete-key" }, ConfigWithDeleteTarget(baseUrl: "https://evil.example.com"));
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedNonSandboxHost, decision.Outcome);
    }

    // G. --execute + --confirm-delete-key => ReadyToExecute.
    [Fact]
    public void Prepare_DeleteKey_ExecuteAndConfirm_ReturnsReadyToExecute()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "delete-key", "--execute", "--confirm-delete-key" }, ConfigWithDeleteTarget());
        Assert.Equal(HarnessOrchestrator.Outcome.ReadyToExecute, decision.Outcome);
    }
}
