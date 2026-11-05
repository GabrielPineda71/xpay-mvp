using Microsoft.Extensions.Configuration;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-546 — prueba el comando dedicado configure-m5-payment-resolution:
// dry-run/execute/confirm-flag gating a nivel de decisión; el executor sólo
// delega en M5ResolveEnvTransfer (ya cubierto en detalle por
// M5ResolveEnvTransferTests.cs); nunca llama a IPassportKeyClient (no
// recibe ninguno como dependencia — estructuralmente imposible); nunca lee
// PASSPORT_TEST_CUSTOMER_ID/PASSPORT_TEST_BREB_KEY (no forman parte de su
// firma); home del usuario resuelto de forma segura (no hardcodeado) salvo
// override explícito en tests.
public class ConfigureM5PaymentResolutionExecutorTests
{
    private static string NewTempDir(string prefix) =>
        Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));

    private static readonly string SyntheticRepoRoot =
        Path.Combine(Path.GetTempPath(), "xpay-configure-m5-resolution-synthetic-repo-root");

    private static readonly DateTime FixedTime = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static string PrepareValidArtifact(string dir, string resolutionId = "fake-resolution-id-cli")
    {
        Directory.CreateDirectory(dir);
        var content = new Dictionary<string, object?>
        {
            ["case_id"] = "M5-T2-RESOLVE",
            ["captured_at_utc"] = "2026-01-01T11:00:00Z",
            ["backend_commit_sha"] = "fake-sha",
            ["resolution_id"] = resolutionId,
            ["resolved_at_raw"] = "2026-01-01T11:00:00Z",
            ["expires_at_raw"] = "2026-01-01T11:30:00Z",
        };
        var path = Path.Combine(dir, "M5-T2-RESOLVE-2026-01-01T11-00-00Z.private.json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(content));
        return path;
    }

    private static string PrepareFakeEnvFile()
    {
        var dir = NewTempDir("xpay-configure-m5-resolution-fake-env-dir");
        Directory.CreateDirectory(dir);
        var envPath = Path.Combine(dir, "fake-sandbox.env");
        File.WriteAllText(envPath, "FAKE_UNRELATED_KEY=fake-value\n");
        return envPath;
    }

    // ══════════════════════════════════════════════════════════════════
    // ARCHITECTURE — sin dependencia de Passport
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Execute_SignatureExcludesPassportClient()
    {
        var method = typeof(ConfigureM5PaymentResolutionExecutor).GetMethod("Execute");
        Assert.NotNull(method);
        var paramTypeNames = method!.GetParameters().Select(p => p.ParameterType.FullName).ToArray();
        Assert.DoesNotContain(paramTypeNames, n => n != null &&
            (n.Contains("IPassportKeyClient") || n.Contains("IPassportPaymentClient") || n.Contains("IPassportCustomerAccountClient")));
    }

    // ══════════════════════════════════════════════════════════════════
    // DECISION LAYER — dry-run / execute / confirm gating
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void DryRun_WithoutExecuteFlag_NeverReachesReadyToExecute()
    {
        var action = HarnessDecision.Decide(new[] { HarnessDecision.CommandConfigureM5PaymentResolution }, out _);
        Assert.Equal(HarnessAction.DryRun, action);
    }

    [Fact]
    public void ExecuteFlagWithoutConfirmFlag_Aborted_NotDryRunNotExecute()
    {
        var action = HarnessDecision.Decide(
            new[] { HarnessDecision.CommandConfigureM5PaymentResolution, HarnessDecision.FlagExecute },
            out var abortReason);
        Assert.Equal(HarnessAction.Aborted, action);
        Assert.NotNull(abortReason);
    }

    [Fact]
    public void ConfirmFlagWithoutExecuteFlag_DryRun()
    {
        var action = HarnessDecision.Decide(
            new[] { HarnessDecision.CommandConfigureM5PaymentResolution, HarnessDecision.FlagConfirmConfigureM5PaymentResolution },
            out _);
        Assert.Equal(HarnessAction.DryRun, action);
    }

    [Fact]
    public void OtherCommandsConfirmFlag_NeverAuthorizesThisCommand()
    {
        var action = HarnessDecision.Decide(
            new[] { HarnessDecision.CommandConfigureM5PaymentResolution, HarnessDecision.FlagExecute, HarnessDecision.FlagConfirmResolveKeyM5T2 },
            out var abortReason);
        Assert.Equal(HarnessAction.Aborted, action);
        Assert.NotNull(abortReason);
    }

    [Fact]
    public void BothFlagsPresent_Execute()
    {
        var action = HarnessDecision.Decide(
            new[] { HarnessDecision.CommandConfigureM5PaymentResolution, HarnessDecision.FlagExecute, HarnessDecision.FlagConfirmConfigureM5PaymentResolution },
            out _);
        Assert.Equal(HarnessAction.Execute, action);
    }

    // ══════════════════════════════════════════════════════════════════
    // EXECUTOR — delega correctamente en M5ResolveEnvTransfer
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidArtifact_Transferred_EnvContainsExactFakeValue()
    {
        var captureDir = NewTempDir("xpay-configure-m5-resolution-success");
        PrepareValidArtifact(captureDir, "fake-resolution-id-should-not-leak");
        var envPath = PrepareFakeEnvFile();
        var backupDir = NewTempDir("xpay-configure-m5-resolution-backup");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [M5PaymentPrivateCapture.EnvPrivateCaptureDir] = captureDir })
            .Build();

        var result = ConfigureM5PaymentResolutionExecutor.Execute(
            config, SyntheticRepoRoot, envFilePathOverride: envPath, backupDirOverride: backupDir, nowUtcOverride: FixedTime);

        Assert.Equal(ConfigureM5PaymentResolutionExecutor.Outcome.Transferred, result.Outcome);
        Assert.Null(result.Detail);
        var envContent = File.ReadAllText(envPath);
        Assert.Contains("PASSPORT_TEST_M5_PAYMENT_RESOLUTION_ID=fake-resolution-id-should-not-leak", envContent);
    }

    [Fact]
    public void DetailNeverContainsRawResolutionId_OnBlockedOutcomes()
    {
        var captureDir = NewTempDir("xpay-configure-m5-resolution-detail-safe");
        Directory.CreateDirectory(captureDir); // sin artefacto -> LocalBlocked
        var envPath = PrepareFakeEnvFile();
        var backupDir = NewTempDir("xpay-configure-m5-resolution-detail-safe-backup");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [M5PaymentPrivateCapture.EnvPrivateCaptureDir] = captureDir })
            .Build();

        var result = ConfigureM5PaymentResolutionExecutor.Execute(
            config, SyntheticRepoRoot, envFilePathOverride: envPath, backupDirOverride: backupDir, nowUtcOverride: FixedTime);

        Assert.Equal(ConfigureM5PaymentResolutionExecutor.Outcome.LocalBlocked, result.Outcome);
        Assert.DoesNotContain("fake-resolution-id", result.Detail ?? string.Empty);
    }

    [Fact]
    public void MissingPrivateCaptureDirConfig_LocalBlocked_NoFilesTouched()
    {
        var envPath = PrepareFakeEnvFile();
        var envBefore = File.ReadAllText(envPath);
        var backupDir = NewTempDir("xpay-configure-m5-resolution-missing-config-backup");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

        var result = ConfigureM5PaymentResolutionExecutor.Execute(
            config, SyntheticRepoRoot, envFilePathOverride: envPath, backupDirOverride: backupDir, nowUtcOverride: FixedTime);

        Assert.Equal(ConfigureM5PaymentResolutionExecutor.Outcome.LocalBlocked, result.Outcome);
        Assert.Equal(envBefore, File.ReadAllText(envPath));
        Assert.False(Directory.Exists(backupDir));
    }

    [Fact]
    public void PrivateCaptureDirInsideRepo_LocalBlocked_NoFilesTouched()
    {
        var envPath = PrepareFakeEnvFile();
        var envBefore = File.ReadAllText(envPath);
        var backupDir = NewTempDir("xpay-configure-m5-resolution-inside-repo-backup");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [M5PaymentPrivateCapture.EnvPrivateCaptureDir] = SyntheticRepoRoot })
            .Build();

        var result = ConfigureM5PaymentResolutionExecutor.Execute(
            config, SyntheticRepoRoot, envFilePathOverride: envPath, backupDirOverride: backupDir, nowUtcOverride: FixedTime);

        Assert.Equal(ConfigureM5PaymentResolutionExecutor.Outcome.LocalBlocked, result.Outcome);
        Assert.Equal(envBefore, File.ReadAllText(envPath));
    }

    [Fact]
    public void MissingPrivateCaptureDirConfig_NeverReachesUserProfileResolution()
    {
        // Sin overrides de envFilePath/backupDir: si el executor llegara a
        // intentar resolver Environment.SpecialFolder.UserProfile aquí,
        // arriesgaría tocar el ~/.passport-sandbox.env REAL del operador
        // desde un test — algo que este test verifica que NUNCA ocurre
        // cuando el destino privado ya falla en el primer guard (el guard
        // de destino privado corre ANTES de resolver el home del usuario).
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

        var result = ConfigureM5PaymentResolutionExecutor.Execute(config, SyntheticRepoRoot);

        Assert.Equal(ConfigureM5PaymentResolutionExecutor.Outcome.LocalBlocked, result.Outcome);
        Assert.Contains(M5PaymentPrivateCapture.EnvPrivateCaptureDir, result.Detail);
    }
}
