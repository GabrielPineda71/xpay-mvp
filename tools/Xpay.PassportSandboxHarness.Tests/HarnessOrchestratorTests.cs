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
        // XPAY-340 — este literal usaba "resolve-key" como ejemplo de
        // comando NO reconocido; ahora que XPAY-340 lo implementó como
        // comando real, se usa un literal genuinamente desconocido para
        // preservar la intención original del test sin tocar su
        // comportamiento.
        var decision = HarnessOrchestrator.Prepare(new[] { "totally-unknown-command" }, ConfigWith());
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

    // ══════════════════════════════════════════════════════════════════════
    // XPAY-336 — delete-already-deleted-key (M3-T7) — comando INDEPENDIENTE
    // de delete-key (M3-T5), aunque reutilice el mismo target
    // (PASSPORT_TEST_NEW_KEY_ID, ahora intencionalmente apuntando a una
    // llave ya eliminada).
    // ══════════════════════════════════════════════════════════════════════

    private static IConfiguration ConfigWithDeleteAlreadyDeletedTarget(
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

    // A. delete-already-deleted-key dry-run: cero HTTP.
    [Fact]
    public void Prepare_DeleteAlreadyDeletedKeyNoFlags_ReturnsDryRun()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "delete-already-deleted-key" }, ConfigWithDeleteAlreadyDeletedTarget());
        Assert.Equal(HarnessCommand.DeleteAlreadyDeletedKey, decision.Command);
        Assert.Equal(HarnessOrchestrator.Outcome.DryRun, decision.Outcome);
    }

    // B. --execute solo (sin confirmación específica) => Aborted, nunca HTTP.
    [Fact]
    public void Prepare_DeleteAlreadyDeletedKey_ExecuteWithoutConfirm_ReturnsAbortedMissingConfirmation()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "delete-already-deleted-key", "--execute" }, ConfigWithDeleteAlreadyDeletedTarget());
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedMissingConfirmation, decision.Outcome);
    }

    // C/D. Ninguna otra confirmación (incluyendo --confirm-delete-key)
    // autoriza M3-T7.
    [Theory]
    [InlineData("--confirm-delete-key")]
    [InlineData("--confirm-suspend-key")]
    [InlineData("--confirm-activate-key")]
    [InlineData("--confirm-create-key")]
    public void Prepare_DeleteAlreadyDeletedKey_OtherConfirmations_DoNotAuthorize(string wrongConfirmFlag)
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "delete-already-deleted-key", "--execute", wrongConfirmFlag }, ConfigWithDeleteAlreadyDeletedTarget());
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedMissingConfirmation, decision.Outcome);
    }

    // Y viceversa: --confirm-delete-already-deleted-key NUNCA autoriza delete-key (M3-T5).
    [Fact]
    public void Prepare_DeleteKey_ConfirmDeleteAlreadyDeletedKeyDoesNotAuthorizeDeleteKey()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "delete-key", "--execute", "--confirm-delete-already-deleted-key" }, ConfigWithDeleteTarget());
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedMissingConfirmation, decision.Outcome);
    }

    // E. PASSPORT_TEST_NEW_KEY_ID ausente => AbortedTargetMissing, nunca HTTP.
    [Fact]
    public void Prepare_DeleteAlreadyDeletedKey_MissingKeyId_ReturnsAbortedTargetMissing()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "delete-already-deleted-key" }, ConfigWithDeleteAlreadyDeletedTarget(newKeyId: null));
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedTargetMissing, decision.Outcome);
    }

    // host no-Sandbox bloquea antes de HTTP (incluso con key_id presente).
    [Fact]
    public void Prepare_DeleteAlreadyDeletedKey_NonSandboxHost_ReturnsAbortedNonSandboxHost()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "delete-already-deleted-key" }, ConfigWithDeleteAlreadyDeletedTarget(baseUrl: "https://evil.example.com"));
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedNonSandboxHost, decision.Outcome);
    }

    // --execute + --confirm-delete-already-deleted-key => ReadyToExecute.
    [Fact]
    public void Prepare_DeleteAlreadyDeletedKey_ExecuteAndConfirm_ReturnsReadyToExecute()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "delete-already-deleted-key", "--execute", "--confirm-delete-already-deleted-key" },
            ConfigWithDeleteAlreadyDeletedTarget());
        Assert.Equal(HarnessOrchestrator.Outcome.ReadyToExecute, decision.Outcome);
    }

    // ══════════════════════════════════════════════════════════════════════
    // XPAY-340 — resolve-key (M3-T2) — target COMPLETAMENTE DISTINTO
    // (recursos Bre-B ya provistos por Passport: PASSPORT_TEST_CUSTOMER_ID/
    // PASSPORT_TEST_BREB_KEY_TYPE/PASSPORT_TEST_BREB_KEY — nunca
    // PASSPORT_TEST_NEW_KEY_ID).
    // ══════════════════════════════════════════════════════════════════════

    private const string ValidCustomerId  = "synthetic-customer-id-001";
    private const string ValidBrebKeyType = "BCODE";
    private const string ValidBrebKeyValue = "0099999999";

    private static IConfiguration ConfigWithResolveTarget(
        string? baseUrl = ValidSandboxUrl, string? apiKey = "synthetic-key", string? apiSecret = "synthetic-secret",
        string? customerId = ValidCustomerId, string? brebKeyType = ValidBrebKeyType, string? brebKeyValue = ValidBrebKeyValue)
    {
        var dict = new Dictionary<string, string?>();
        if (baseUrl is not null) dict[PassportOptions.EnvBaseUrl] = baseUrl;
        if (apiKey is not null) dict[PassportOptions.EnvClientId] = apiKey;
        if (apiSecret is not null) dict[PassportOptions.EnvClientSecret] = apiSecret;
        if (customerId is not null) dict[HarnessTargetConfig.EnvCustomerId] = customerId;
        if (brebKeyType is not null) dict[HarnessTargetConfig.EnvBrebKeyType] = brebKeyType;
        if (brebKeyValue is not null) dict[HarnessTargetConfig.EnvBrebKeyValue] = brebKeyValue;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    // A. resolve-key dry-run: cero HTTP.
    [Fact]
    public void Prepare_ResolveKeyNoFlags_ReturnsDryRun()
    {
        var decision = HarnessOrchestrator.Prepare(new[] { "resolve-key" }, ConfigWithResolveTarget());
        Assert.Equal(HarnessCommand.ResolveKey, decision.Command);
        Assert.Equal(HarnessOrchestrator.Outcome.DryRun, decision.Outcome);
    }

    // C. --execute solo (sin confirmación) => Aborted, nunca HTTP.
    [Fact]
    public void Prepare_ResolveKey_ExecuteWithoutConfirm_ReturnsAbortedMissingConfirmation()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "resolve-key", "--execute" }, ConfigWithResolveTarget());
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedMissingConfirmation, decision.Outcome);
    }

    // D/F. Ninguna otra confirmación autoriza resolve-key.
    [Theory]
    [InlineData("--confirm-create-key")]
    [InlineData("--confirm-suspend-key")]
    [InlineData("--confirm-activate-key")]
    [InlineData("--confirm-delete-key")]
    [InlineData("--confirm-delete-already-deleted-key")]
    public void Prepare_ResolveKey_OtherConfirmations_DoNotAuthorize(string wrongConfirmFlag)
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "resolve-key", "--execute", wrongConfirmFlag }, ConfigWithResolveTarget());
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedMissingConfirmation, decision.Outcome);
    }

    // E. --confirm-resolve-key NUNCA autoriza otra operación (viceversa).
    [Fact]
    public void Prepare_SuspendKey_ConfirmResolveKeyDoesNotAuthorizeSuspendKey()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "suspend-key", "--execute", "--confirm-resolve-key" }, ConfigWithSuspendTarget());
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedMissingConfirmation, decision.Outcome);
    }

    // G. PASSPORT_TEST_CUSTOMER_ID ausente => AbortedTargetMissing.
    [Fact]
    public void Prepare_ResolveKey_MissingCustomerId_ReturnsAbortedTargetMissing()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "resolve-key" }, ConfigWithResolveTarget(customerId: null));
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedTargetMissing, decision.Outcome);
    }

    // H. PASSPORT_TEST_BREB_KEY_TYPE ausente => AbortedTargetMissing.
    [Fact]
    public void Prepare_ResolveKey_MissingBrebKeyType_ReturnsAbortedTargetMissing()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "resolve-key" }, ConfigWithResolveTarget(brebKeyType: null));
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedTargetMissing, decision.Outcome);
    }

    // I. PASSPORT_TEST_BREB_KEY ausente => AbortedTargetMissing.
    [Fact]
    public void Prepare_ResolveKey_MissingBrebKeyValue_ReturnsAbortedTargetMissing()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "resolve-key" }, ConfigWithResolveTarget(brebKeyValue: null));
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedTargetMissing, decision.Outcome);
    }

    // host no-Sandbox bloquea antes de HTTP.
    [Fact]
    public void Prepare_ResolveKey_NonSandboxHost_ReturnsAbortedNonSandboxHost()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "resolve-key" }, ConfigWithResolveTarget(baseUrl: "https://evil.example.com"));
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedNonSandboxHost, decision.Outcome);
    }

    // --execute + --confirm-resolve-key => ReadyToExecute.
    [Fact]
    public void Prepare_ResolveKey_ExecuteAndConfirm_ReturnsReadyToExecute()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "resolve-key", "--execute", "--confirm-resolve-key" }, ConfigWithResolveTarget());
        Assert.Equal(HarnessOrchestrator.Outcome.ReadyToExecute, decision.Outcome);
    }

    // ══════════════════════════════════════════════════════════════════════
    // XPAY-344 — create-key-missing / create-key-invalid (M3-T6) — target
    // COMPARTIDO (account_id + key_type), deliberadamente SIN exigir
    // PASSPORT_TEST_NEW_KEY_VALUE (ninguno de los dos lo lee).
    // ══════════════════════════════════════════════════════════════════════

    private static IConfiguration ConfigWithMissingInvalidTarget(
        string? baseUrl = ValidSandboxUrl, string? apiKey = "synthetic-key", string? apiSecret = "synthetic-secret",
        string? accountId = ValidTargetAccountId, string? newKeyType = ValidTargetKeyType)
    {
        var dict = new Dictionary<string, string?>();
        if (baseUrl is not null) dict[PassportOptions.EnvBaseUrl] = baseUrl;
        if (apiKey is not null) dict[PassportOptions.EnvClientId] = apiKey;
        if (apiSecret is not null) dict[PassportOptions.EnvClientSecret] = apiSecret;
        if (accountId is not null) dict[HarnessTargetConfig.EnvAccountId] = accountId;
        if (newKeyType is not null) dict[HarnessTargetConfig.EnvNewKeyType] = newKeyType;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    // ── create-key-missing ──────────────────────────────────────────────

    [Fact]
    public void Prepare_CreateKeyMissingNoFlags_ReturnsDryRun()
    {
        var decision = HarnessOrchestrator.Prepare(new[] { "create-key-missing" }, ConfigWithMissingInvalidTarget());
        Assert.Equal(HarnessCommand.CreateKeyMissing, decision.Command);
        Assert.Equal(HarnessOrchestrator.Outcome.DryRun, decision.Outcome);
    }

    [Fact]
    public void Prepare_CreateKeyMissing_ExecuteWithoutConfirm_ReturnsAbortedMissingConfirmation()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "create-key-missing", "--execute" }, ConfigWithMissingInvalidTarget());
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedMissingConfirmation, decision.Outcome);
    }

    [Theory]
    [InlineData("--confirm-create-key")]
    [InlineData("--confirm-create-key-invalid")]
    [InlineData("--confirm-suspend-key")]
    public void Prepare_CreateKeyMissing_OtherConfirmations_DoNotAuthorize(string wrongConfirmFlag)
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "create-key-missing", "--execute", wrongConfirmFlag }, ConfigWithMissingInvalidTarget());
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedMissingConfirmation, decision.Outcome);
    }

    // Viceversa: --confirm-create-key-missing NUNCA autoriza create-key.
    [Fact]
    public void Prepare_CreateKey_ConfirmCreateKeyMissingDoesNotAuthorizeCreateKey()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "create-key", "--execute", "--confirm-create-key-missing" }, ConfigWithTargets());
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedMissingConfirmation, decision.Outcome);
    }

    [Fact]
    public void Prepare_CreateKeyMissing_MissingAccountId_ReturnsAbortedTargetMissing()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "create-key-missing" }, ConfigWithMissingInvalidTarget(accountId: null));
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedTargetMissing, decision.Outcome);
    }

    [Fact]
    public void Prepare_CreateKeyMissing_MissingKeyType_ReturnsAbortedTargetMissing()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "create-key-missing" }, ConfigWithMissingInvalidTarget(newKeyType: null));
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedTargetMissing, decision.Outcome);
    }

    [Fact]
    public void Prepare_CreateKeyMissing_NonSandboxHost_ReturnsAbortedNonSandboxHost()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "create-key-missing" }, ConfigWithMissingInvalidTarget(baseUrl: "https://evil.example.com"));
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedNonSandboxHost, decision.Outcome);
    }

    [Fact]
    public void Prepare_CreateKeyMissing_ExecuteAndConfirm_ReturnsReadyToExecute()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "create-key-missing", "--execute", "--confirm-create-key-missing" }, ConfigWithMissingInvalidTarget());
        Assert.Equal(HarnessOrchestrator.Outcome.ReadyToExecute, decision.Outcome);
    }

    // ── create-key-invalid ──────────────────────────────────────────────

    [Fact]
    public void Prepare_CreateKeyInvalidNoFlags_ReturnsDryRun()
    {
        var decision = HarnessOrchestrator.Prepare(new[] { "create-key-invalid" }, ConfigWithMissingInvalidTarget());
        Assert.Equal(HarnessCommand.CreateKeyInvalid, decision.Command);
        Assert.Equal(HarnessOrchestrator.Outcome.DryRun, decision.Outcome);
    }

    [Fact]
    public void Prepare_CreateKeyInvalid_ExecuteWithoutConfirm_ReturnsAbortedMissingConfirmation()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "create-key-invalid", "--execute" }, ConfigWithMissingInvalidTarget());
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedMissingConfirmation, decision.Outcome);
    }

    [Theory]
    [InlineData("--confirm-create-key")]
    [InlineData("--confirm-create-key-missing")]
    [InlineData("--confirm-suspend-key")]
    public void Prepare_CreateKeyInvalid_OtherConfirmations_DoNotAuthorize(string wrongConfirmFlag)
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "create-key-invalid", "--execute", wrongConfirmFlag }, ConfigWithMissingInvalidTarget());
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedMissingConfirmation, decision.Outcome);
    }

    [Fact]
    public void Prepare_CreateKeyInvalid_MissingAccountId_ReturnsAbortedTargetMissing()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "create-key-invalid" }, ConfigWithMissingInvalidTarget(accountId: null));
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedTargetMissing, decision.Outcome);
    }

    [Fact]
    public void Prepare_CreateKeyInvalid_MissingKeyType_ReturnsAbortedTargetMissing()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "create-key-invalid" }, ConfigWithMissingInvalidTarget(newKeyType: null));
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedTargetMissing, decision.Outcome);
    }

    [Fact]
    public void Prepare_CreateKeyInvalid_NonSandboxHost_ReturnsAbortedNonSandboxHost()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "create-key-invalid" }, ConfigWithMissingInvalidTarget(baseUrl: "https://evil.example.com"));
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedNonSandboxHost, decision.Outcome);
    }

    [Fact]
    public void Prepare_CreateKeyInvalid_ExecuteAndConfirm_ReturnsReadyToExecute()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "create-key-invalid", "--execute", "--confirm-create-key-invalid" }, ConfigWithMissingInvalidTarget());
        Assert.Equal(HarnessOrchestrator.Outcome.ReadyToExecute, decision.Outcome);
    }

    // ══════════════════════════════════════════════════════════════════════
    // XPAY-351 — create-qr-static (M4-T1) — target propio: key_id REMOTO
    // real de la llave de certificación (PASSPORT_TEST_NEW_KEY_ID, mismo
    // target que suspend/activate/delete-key) + PASSPORT_TEST_CUSTOMER_ID
    // (mismo recurso ya usado por resolve-key). Ninguna variable nueva.
    // ══════════════════════════════════════════════════════════════════════

    private static IConfiguration ConfigWithCreateQrStaticTarget(
        string? baseUrl = ValidSandboxUrl, string? apiKey = "synthetic-key", string? apiSecret = "synthetic-secret",
        string? newKeyId = ValidTargetKeyId, string? customerId = ValidCustomerId)
    {
        var dict = new Dictionary<string, string?>();
        if (baseUrl is not null) dict[PassportOptions.EnvBaseUrl] = baseUrl;
        if (apiKey is not null) dict[PassportOptions.EnvClientId] = apiKey;
        if (apiSecret is not null) dict[PassportOptions.EnvClientSecret] = apiSecret;
        if (newKeyId is not null) dict[HarnessTargetConfig.EnvNewKeyId] = newKeyId;
        if (customerId is not null) dict[HarnessTargetConfig.EnvCustomerId] = customerId;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    // A. create-qr-static dry-run: cero HTTP.
    [Fact]
    public void Prepare_CreateQrStaticNoFlags_ReturnsDryRun()
    {
        var decision = HarnessOrchestrator.Prepare(new[] { "create-qr-static" }, ConfigWithCreateQrStaticTarget());
        Assert.Equal(HarnessCommand.CreateQrStatic, decision.Command);
        Assert.Equal(HarnessOrchestrator.Outcome.DryRun, decision.Outcome);
    }

    // C. --execute solo (sin confirmación) => Aborted, nunca HTTP.
    [Fact]
    public void Prepare_CreateQrStatic_ExecuteWithoutConfirm_ReturnsAbortedMissingConfirmation()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "create-qr-static", "--execute" }, ConfigWithCreateQrStaticTarget());
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedMissingConfirmation, decision.Outcome);
    }

    // E. confirmación sin --execute => no ejecuta (permanece DryRun).
    [Fact]
    public void Prepare_CreateQrStatic_ConfirmWithoutExecute_ReturnsDryRun()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "create-qr-static", "--confirm-create-qr-static" }, ConfigWithCreateQrStaticTarget());
        Assert.Equal(HarnessOrchestrator.Outcome.DryRun, decision.Outcome);
    }

    // D/S. Ninguna confirmación de M3 autoriza create-qr-static.
    [Theory]
    [InlineData("--confirm-create-key")]
    [InlineData("--confirm-suspend-key")]
    [InlineData("--confirm-activate-key")]
    [InlineData("--confirm-delete-key")]
    [InlineData("--confirm-delete-already-deleted-key")]
    [InlineData("--confirm-resolve-key")]
    [InlineData("--confirm-create-key-missing")]
    [InlineData("--confirm-create-key-invalid")]
    public void Prepare_CreateQrStatic_OtherConfirmations_DoNotAuthorize(string wrongConfirmFlag)
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "create-qr-static", "--execute", wrongConfirmFlag }, ConfigWithCreateQrStaticTarget());
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedMissingConfirmation, decision.Outcome);
    }

    // R. --confirm-create-qr-static NUNCA autoriza ningún comando de M3.
    [Theory]
    [InlineData("create-key")]
    [InlineData("suspend-key")]
    [InlineData("activate-key")]
    [InlineData("delete-key")]
    [InlineData("delete-already-deleted-key")]
    [InlineData("resolve-key")]
    [InlineData("create-key-missing")]
    [InlineData("create-key-invalid")]
    public void Prepare_M3Commands_ConfirmCreateQrStaticDoesNotAuthorizeThem(string m3Command)
    {
        // Config que satisface TODOS los targets posibles simultáneamente,
        // para que el único motivo de rechazo posible sea la confirmación
        // incorrecta (nunca un AbortedTargetMissing/AbortedConfigMissing
        // enmascarando el resultado real que se quiere probar).
        var dict = new Dictionary<string, string?>
        {
            [PassportOptions.EnvBaseUrl] = ValidSandboxUrl,
            [PassportOptions.EnvClientId] = "synthetic-key",
            [PassportOptions.EnvClientSecret] = "synthetic-secret",
            [HarnessTargetConfig.EnvAccountId] = ValidTargetAccountId,
            [HarnessTargetConfig.EnvNewKeyType] = ValidTargetKeyType,
            [HarnessTargetConfig.EnvNewKeyValue] = ValidTargetKeyValue,
            [HarnessTargetConfig.EnvNewKeyId] = ValidTargetKeyId,
            [HarnessTargetConfig.EnvCustomerId] = ValidCustomerId,
            [HarnessTargetConfig.EnvBrebKeyType] = ValidBrebKeyType,
            [HarnessTargetConfig.EnvBrebKeyValue] = ValidBrebKeyValue,
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var decision = HarnessOrchestrator.Prepare(
            new[] { m3Command, "--execute", "--confirm-create-qr-static" }, config);
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedMissingConfirmation, decision.Outcome);
    }

    // G. PASSPORT_TEST_NEW_KEY_ID ausente => AbortedTargetMissing.
    [Fact]
    public void Prepare_CreateQrStatic_MissingNewKeyId_ReturnsAbortedTargetMissing()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "create-qr-static" }, ConfigWithCreateQrStaticTarget(newKeyId: null));
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedTargetMissing, decision.Outcome);
    }

    // H. PASSPORT_TEST_CUSTOMER_ID ausente => AbortedTargetMissing.
    [Fact]
    public void Prepare_CreateQrStatic_MissingCustomerId_ReturnsAbortedTargetMissing()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "create-qr-static" }, ConfigWithCreateQrStaticTarget(customerId: null));
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedTargetMissing, decision.Outcome);
    }

    // host no-Sandbox bloquea antes de HTTP.
    [Fact]
    public void Prepare_CreateQrStatic_NonSandboxHost_ReturnsAbortedNonSandboxHost()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "create-qr-static" }, ConfigWithCreateQrStaticTarget(baseUrl: "https://evil.example.com"));
        Assert.Equal(HarnessOrchestrator.Outcome.AbortedNonSandboxHost, decision.Outcome);
    }

    // --execute + --confirm-create-qr-static => ReadyToExecute.
    [Fact]
    public void Prepare_CreateQrStatic_ExecuteAndConfirm_ReturnsReadyToExecute()
    {
        var decision = HarnessOrchestrator.Prepare(
            new[] { "create-qr-static", "--execute", "--confirm-create-qr-static" }, ConfigWithCreateQrStaticTarget());
        Assert.Equal(HarnessOrchestrator.Outcome.ReadyToExecute, decision.Outcome);
    }
}
