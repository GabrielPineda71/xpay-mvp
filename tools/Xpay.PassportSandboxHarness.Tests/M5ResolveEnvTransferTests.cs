using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-545 §5 — prueba M5ResolveEnvTransfer EXCLUSIVAMENTE con datos FAKE
// (ningún valor real, ningún archivo real de ~/.passport-sandbox.env,
// conforme exige el ticket). Cubre: transferencia exitosa sin imprimir el
// valor, backup creado antes de escribir, marcador de consumo escrito,
// bloqueo ante env preexistente inesperado, y los distintos estados de
// artefacto (ausente/ambiguo/malformado).
public class M5ResolveEnvTransferTests
{
    private static string NewTempDir(string prefix) =>
        Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));

    private static readonly DateTime FixedTime = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static string PrepareValidArtifact(string dir, string resolutionId = "fake-resolution-id-xyz")
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

    private static string PrepareFakeEnvFile(string? existingResolutionIdLine = null)
    {
        var dir = NewTempDir("xpay-m5-resolve-fake-env-dir");
        Directory.CreateDirectory(dir);
        var envPath = Path.Combine(dir, "fake-sandbox.env");
        var lines = "FAKE_UNRELATED_KEY=fake-value\nPASSPORT_BASE_URL=https://fake.invalid\n";
        if (existingResolutionIdLine is not null)
            lines += existingResolutionIdLine + "\n";
        File.WriteAllText(envPath, lines);
        return envPath;
    }

    [Fact]
    public void SuccessfulTransfer_WritesResolutionIdAndNeverPrintsIt()
    {
        var captureDir = NewTempDir("xpay-m5-resolve-transfer-success");
        PrepareValidArtifact(captureDir, "fake-resolution-id-should-not-appear-in-stdout");
        var envPath = PrepareFakeEnvFile();
        var backupDir = NewTempDir("xpay-m5-resolve-transfer-backup");

        var outcome = M5ResolveEnvTransfer.TryTransfer(captureDir, envPath, backupDir, FixedTime, out var detail);

        Assert.Equal(M5ResolveEnvTransfer.TransferOutcome.Transferred, outcome);
        var envContent = File.ReadAllText(envPath);
        Assert.Contains("PASSPORT_TEST_M5_PAYMENT_RESOLUTION_ID=fake-resolution-id-should-not-appear-in-stdout", envContent);
        // `detail` (lo único que un caller podría loguear) nunca contiene el valor.
        Assert.DoesNotContain("fake-resolution-id-should-not-appear-in-stdout", detail ?? string.Empty);
    }

    [Fact]
    public void SuccessfulTransfer_PreservesOtherEnvKeysUnchanged()
    {
        var captureDir = NewTempDir("xpay-m5-resolve-transfer-preserve");
        PrepareValidArtifact(captureDir);
        var envPath = PrepareFakeEnvFile();
        var backupDir = NewTempDir("xpay-m5-resolve-transfer-backup2");

        M5ResolveEnvTransfer.TryTransfer(captureDir, envPath, backupDir, FixedTime, out _);

        var envContent = File.ReadAllText(envPath);
        Assert.Contains("FAKE_UNRELATED_KEY=fake-value", envContent);
        Assert.Contains("PASSPORT_BASE_URL=https://fake.invalid", envContent);
    }

    [Fact]
    public void SuccessfulTransfer_CreatesBackupBeforeWriting()
    {
        var captureDir = NewTempDir("xpay-m5-resolve-transfer-backup-check");
        PrepareValidArtifact(captureDir);
        var envPath = PrepareFakeEnvFile();
        var backupDir = NewTempDir("xpay-m5-resolve-transfer-backup3");

        M5ResolveEnvTransfer.TryTransfer(captureDir, envPath, backupDir, FixedTime, out _);

        var backups = Directory.GetFiles(backupDir);
        Assert.Single(backups);
        var backupContent = File.ReadAllText(backups[0]);
        Assert.DoesNotContain("PASSPORT_TEST_M5_PAYMENT_RESOLUTION_ID", backupContent); // backup = estado ANTES de escribir
    }

    [Fact]
    public void SuccessfulTransfer_WritesConsumedMarker()
    {
        var captureDir = NewTempDir("xpay-m5-resolve-transfer-marker");
        PrepareValidArtifact(captureDir);
        var envPath = PrepareFakeEnvFile();
        var backupDir = NewTempDir("xpay-m5-resolve-transfer-backup4");

        M5ResolveEnvTransfer.TryTransfer(captureDir, envPath, backupDir, FixedTime, out _);

        var markers = Directory.GetFiles(captureDir, M5ResolveArtifactReader.ConsumedPrefix + "*");
        Assert.Single(markers);

        var (outcome, artifact) = M5ResolveArtifactReader.Resolve(captureDir);
        Assert.Equal(M5ResolveArtifactReader.ResolutionOutcome.Found, outcome);
        Assert.True(M5ResolveArtifactReader.HasConsumedMarker(captureDir, artifact!));
    }

    [Fact]
    public void OriginalArtifactNeverModified_OnlyMarkerWrittenSeparately()
    {
        var captureDir = NewTempDir("xpay-m5-resolve-transfer-immutable");
        var artifactPath = PrepareValidArtifact(captureDir, "fake-resolution-id-immutable");
        var beforeContent = File.ReadAllText(artifactPath);
        var envPath = PrepareFakeEnvFile();
        var backupDir = NewTempDir("xpay-m5-resolve-transfer-backup5");

        M5ResolveEnvTransfer.TryTransfer(captureDir, envPath, backupDir, FixedTime, out _);

        var afterContent = File.ReadAllText(artifactPath);
        Assert.Equal(beforeContent, afterContent);
    }

    [Fact]
    public void EnvAlreadyHasResolutionId_Blocked_NeverOverwrites()
    {
        var captureDir = NewTempDir("xpay-m5-resolve-transfer-existing-env");
        PrepareValidArtifact(captureDir, "fake-new-resolution-id");
        var envPath = PrepareFakeEnvFile(existingResolutionIdLine: "PASSPORT_TEST_M5_PAYMENT_RESOLUTION_ID=fake-existing-unexpected-value");
        var backupDir = NewTempDir("xpay-m5-resolve-transfer-backup6");

        var outcome = M5ResolveEnvTransfer.TryTransfer(captureDir, envPath, backupDir, FixedTime, out var detail);

        Assert.Equal(M5ResolveEnvTransfer.TransferOutcome.EnvAlreadyHasResolutionId, outcome);
        var envContent = File.ReadAllText(envPath);
        Assert.Contains("fake-existing-unexpected-value", envContent);
        Assert.DoesNotContain("fake-new-resolution-id", envContent);
        Assert.False(Directory.Exists(backupDir) && Directory.GetFiles(backupDir).Length > 0);
    }

    [Fact]
    public void NoArtifact_Blocked_ReturnsNoArtifactFound()
    {
        var captureDir = NewTempDir("xpay-m5-resolve-transfer-no-artifact");
        Directory.CreateDirectory(captureDir);
        var envPath = PrepareFakeEnvFile();
        var backupDir = NewTempDir("xpay-m5-resolve-transfer-backup7");

        var outcome = M5ResolveEnvTransfer.TryTransfer(captureDir, envPath, backupDir, FixedTime, out _);

        Assert.Equal(M5ResolveEnvTransfer.TransferOutcome.NoArtifactFound, outcome);
    }

    [Fact]
    public void MultipleArtifacts_Blocked_ReturnsAmbiguous()
    {
        var captureDir = NewTempDir("xpay-m5-resolve-transfer-ambiguous");
        PrepareValidArtifact(captureDir);
        var second = new Dictionary<string, object?> { ["case_id"] = "M5-T2-RESOLVE", ["resolution_id"] = "fake-second" };
        File.WriteAllText(Path.Combine(captureDir, "M5-T2-RESOLVE-2026-01-01T12-00-00Z.private.json"),
            System.Text.Json.JsonSerializer.Serialize(second));
        var envPath = PrepareFakeEnvFile();
        var backupDir = NewTempDir("xpay-m5-resolve-transfer-backup8");

        var outcome = M5ResolveEnvTransfer.TryTransfer(captureDir, envPath, backupDir, FixedTime, out _);

        Assert.Equal(M5ResolveEnvTransfer.TransferOutcome.ArtifactAmbiguous, outcome);
    }

    [Fact]
    public void MalformedArtifact_Blocked_ReturnsArtifactInvalid()
    {
        var captureDir = NewTempDir("xpay-m5-resolve-transfer-malformed");
        Directory.CreateDirectory(captureDir);
        File.WriteAllText(Path.Combine(captureDir, "M5-T2-RESOLVE-bad.private.json"), "not-valid-json{{{");
        var envPath = PrepareFakeEnvFile();
        var backupDir = NewTempDir("xpay-m5-resolve-transfer-backup9");

        var outcome = M5ResolveEnvTransfer.TryTransfer(captureDir, envPath, backupDir, FixedTime, out _);

        Assert.Equal(M5ResolveEnvTransfer.TransferOutcome.ArtifactInvalid, outcome);
    }

    [Fact]
    public void MissingEnvFile_Blocked_ReturnsEnvFileNotFound()
    {
        var captureDir = NewTempDir("xpay-m5-resolve-transfer-no-env");
        PrepareValidArtifact(captureDir);
        var nonExistentEnvPath = Path.Combine(NewTempDir("xpay-m5-resolve-transfer-no-env-dir"), "missing.env");
        var backupDir = NewTempDir("xpay-m5-resolve-transfer-backup10");

        var outcome = M5ResolveEnvTransfer.TryTransfer(captureDir, nonExistentEnvPath, backupDir, FixedTime, out _);

        Assert.Equal(M5ResolveEnvTransfer.TransferOutcome.EnvFileNotFound, outcome);
    }
}
