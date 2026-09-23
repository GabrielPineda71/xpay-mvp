using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-517 — pruebas dedicadas de M4T5PrivateCapture, mirror estructural
// de M4T4PrivateCaptureTests.cs (XPAY-508/509).
public class M4T5PrivateCaptureTests
{
    private static readonly string SyntheticRepoRoot =
        Path.Combine(Path.GetTempPath(), "xpay-m4t5-capture-unit-synthetic-repo-root");

    [Fact]
    public void Validate_NullOrWhitespace_ReturnsMissing()
    {
        Assert.Equal(M4T5PrivateCapture.ValidationOutcome.Missing,
            M4T5PrivateCapture.Validate(null, SyntheticRepoRoot));
        Assert.Equal(M4T5PrivateCapture.ValidationOutcome.Missing,
            M4T5PrivateCapture.Validate("   ", SyntheticRepoRoot));
    }

    [Fact]
    public void Validate_PathEqualsRepositoryRoot_ReturnsInsideRepository()
    {
        Assert.Equal(M4T5PrivateCapture.ValidationOutcome.InsideRepository,
            M4T5PrivateCapture.Validate(SyntheticRepoRoot, SyntheticRepoRoot));
    }

    [Fact]
    public void Validate_PathNestedUnderRepositoryRoot_ReturnsInsideRepository()
    {
        var nested = Path.Combine(SyntheticRepoRoot, "docs", "certificacion", "passport-breb", "private");
        Assert.Equal(M4T5PrivateCapture.ValidationOutcome.InsideRepository,
            M4T5PrivateCapture.Validate(nested, SyntheticRepoRoot));
    }

    [Fact]
    public void Validate_SiblingDirectory_OutsideRepository_ReturnsValid()
    {
        var sibling = Path.Combine(Path.GetTempPath(), "xpay-m4t5-capture-unit-sibling-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(M4T5PrivateCapture.ValidationOutcome.Valid,
            M4T5PrivateCapture.Validate(sibling, SyntheticRepoRoot));
    }

    [Fact]
    public void Validate_SiblingWithSimilarNamePrefix_NeverMisclassifiedAsInsideRepository()
    {
        var confusablePrefix = SyntheticRepoRoot + "-but-actually-a-different-directory";
        Assert.Equal(M4T5PrivateCapture.ValidationOutcome.Valid,
            M4T5PrivateCapture.Validate(confusablePrefix, SyntheticRepoRoot));
    }

    [Fact]
    public void EnsureDirectory_CreatesDirectory_IfMissing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xpay-m4t5-ensure-dir-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.False(Directory.Exists(dir));
            M4T5PrivateCapture.EnsureDirectory(dir);
            Assert.True(Directory.Exists(dir));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Write_CreatesFile_WithExpectedContent()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xpay-m4t5-write-" + Guid.NewGuid().ToString("N"));
        try
        {
            M4T5PrivateCapture.EnsureDirectory(dir);
            var content = new Dictionary<string, object?> { ["synthetic_field"] = "synthetic_value" };

            var path = M4T5PrivateCapture.Write(dir, "M4-T5", "2026-01-01T00-00-00Z", content);

            Assert.True(File.Exists(path));
            Assert.Contains("synthetic_value", File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Write_NeverOverwritesExistingArtifact_ThrowsInstead()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xpay-m4t5-write-no-overwrite-" + Guid.NewGuid().ToString("N"));
        try
        {
            M4T5PrivateCapture.EnsureDirectory(dir);
            var content = new Dictionary<string, object?> { ["synthetic_field"] = "first" };
            M4T5PrivateCapture.Write(dir, "M4-T5", "2026-01-01T00-00-00Z", content);

            Assert.Throws<InvalidOperationException>(() =>
                M4T5PrivateCapture.Write(dir, "M4-T5", "2026-01-01T00-00-00Z", content));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    // XPAY-517 §12.P — el hallazgo central de este ticket: un campo JSON
    // NO modelado por PassportDecodeQrCodeResponse (aquí, un objeto
    // sintético "tip") sobrevive íntegro en el artefacto privado.
    [Fact]
    public void Write_PreservesUnmodeledJsonField_DemonstratingRawFidelity()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xpay-m4t5-write-raw-fidelity-" + Guid.NewGuid().ToString("N"));
        try
        {
            M4T5PrivateCapture.EnsureDirectory(dir);
            using var rawDoc = System.Text.Json.JsonDocument.Parse("""
                {
                  "status": "ACTIVE",
                  "tip": { "tip_type": "FIXED", "tip_value": "100.00" }
                }
                """);
            var content = new Dictionary<string, object?>
            {
                ["case_id"] = "M4-T5",
                ["raw_response"] = rawDoc.RootElement.Clone(),
            };

            var path = M4T5PrivateCapture.Write(dir, "M4-T5", "2026-01-01T00-00-00Z", content);

            var written = File.ReadAllText(path);
            using var writtenDoc = System.Text.Json.JsonDocument.Parse(written);
            var tip = writtenDoc.RootElement.GetProperty("raw_response").GetProperty("tip");
            Assert.Equal("FIXED", tip.GetProperty("tip_type").GetString());
            Assert.Equal("100.00", tip.GetProperty("tip_value").GetString());
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
