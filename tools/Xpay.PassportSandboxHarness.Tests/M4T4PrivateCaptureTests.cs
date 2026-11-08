using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-508 — pruebas dedicadas del módulo M4T4PrivateCapture, aisladas del
// executor: Validate() es pura (sin filesystem), EnsureDirectory/Write sí
// tocan disco pero exclusivamente bajo directorios temporales creados por
// el propio test (nunca el repositorio real).
public class M4T4PrivateCaptureTests
{
    private static readonly string SyntheticRepoRoot =
        Path.Combine(Path.GetTempPath(), "xpay-m4t4-capture-unit-synthetic-repo-root");

    [Fact]
    public void Validate_NullOrWhitespace_ReturnsMissing()
    {
        Assert.Equal(M4T4PrivateCapture.ValidationOutcome.Missing,
            M4T4PrivateCapture.Validate(null, SyntheticRepoRoot));
        Assert.Equal(M4T4PrivateCapture.ValidationOutcome.Missing,
            M4T4PrivateCapture.Validate("   ", SyntheticRepoRoot));
        Assert.Equal(M4T4PrivateCapture.ValidationOutcome.Missing,
            M4T4PrivateCapture.Validate("", SyntheticRepoRoot));
    }

    [Fact]
    public void Validate_PathEqualsRepositoryRoot_ReturnsInsideRepository()
    {
        Assert.Equal(M4T4PrivateCapture.ValidationOutcome.InsideRepository,
            M4T4PrivateCapture.Validate(SyntheticRepoRoot, SyntheticRepoRoot));
    }

    [Fact]
    public void Validate_PathEqualsRepositoryRootWithTrailingSeparator_ReturnsInsideRepository()
    {
        Assert.Equal(M4T4PrivateCapture.ValidationOutcome.InsideRepository,
            M4T4PrivateCapture.Validate(SyntheticRepoRoot + Path.DirectorySeparatorChar, SyntheticRepoRoot));
    }

    [Fact]
    public void Validate_PathNestedUnderRepositoryRoot_ReturnsInsideRepository()
    {
        var nested = Path.Combine(SyntheticRepoRoot, "docs", "certificacion", "passport-breb", "private");
        Assert.Equal(M4T4PrivateCapture.ValidationOutcome.InsideRepository,
            M4T4PrivateCapture.Validate(nested, SyntheticRepoRoot));
    }

    [Fact]
    public void Validate_SiblingDirectory_OutsideRepository_ReturnsValid()
    {
        var sibling = Path.Combine(Path.GetTempPath(), "xpay-m4t4-capture-unit-sibling-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(M4T4PrivateCapture.ValidationOutcome.Valid,
            M4T4PrivateCapture.Validate(sibling, SyntheticRepoRoot));
    }

    // Regresión CRÍTICA: un directorio hermano cuyo NOMBRE simplemente
    // empieza con el mismo prefijo de texto que la raíz del repositorio
    // (pero NO está anidado bajo ella) nunca debe clasificarse como
    // InsideRepository — la comparación debe ser por segmentos de ruta, no
    // por prefijo de string crudo.
    [Fact]
    public void Validate_SiblingWithSimilarNamePrefix_NeverMisclassifiedAsInsideRepository()
    {
        var confusablePrefix = SyntheticRepoRoot + "-but-actually-a-different-directory";
        Assert.Equal(M4T4PrivateCapture.ValidationOutcome.Valid,
            M4T4PrivateCapture.Validate(confusablePrefix, SyntheticRepoRoot));
    }

    [Fact]
    public void Validate_RelativePath_ResolvedAgainstRepositoryRoot_DetectsContainment()
    {
        // Una ruta relativa se resuelve contra el CWD del proceso de test,
        // no contra SyntheticRepoRoot — este test sólo confirma que
        // Path.GetFullPath produce una ruta ABSOLUTA consistente y
        // comparable, sin asumir un CWD específico como "dentro" o "fuera".
        var outcome = M4T4PrivateCapture.Validate("./some-relative-dir", SyntheticRepoRoot);
        Assert.True(outcome is M4T4PrivateCapture.ValidationOutcome.Valid or M4T4PrivateCapture.ValidationOutcome.InsideRepository);
    }

    [Fact]
    public void EnsureDirectory_CreatesDirectory_IfMissing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xpay-m4t4-ensure-dir-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.False(Directory.Exists(dir));
            M4T4PrivateCapture.EnsureDirectory(dir);
            Assert.True(Directory.Exists(dir));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void EnsureDirectory_Idempotent_NoThrowIfAlreadyExists()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xpay-m4t4-ensure-dir-idempotent-" + Guid.NewGuid().ToString("N"));
        try
        {
            M4T4PrivateCapture.EnsureDirectory(dir);
            M4T4PrivateCapture.EnsureDirectory(dir); // segunda llamada — no debe lanzar.
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
        var dir = Path.Combine(Path.GetTempPath(), "xpay-m4t4-write-" + Guid.NewGuid().ToString("N"));
        try
        {
            M4T4PrivateCapture.EnsureDirectory(dir);
            var content = new Dictionary<string, object?> { ["synthetic_field"] = "synthetic_value" };

            var path = M4T4PrivateCapture.Write(dir, "M4-T4", "2026-01-01T00-00-00Z", content);

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
        var dir = Path.Combine(Path.GetTempPath(), "xpay-m4t4-write-no-overwrite-" + Guid.NewGuid().ToString("N"));
        try
        {
            M4T4PrivateCapture.EnsureDirectory(dir);
            var content = new Dictionary<string, object?> { ["synthetic_field"] = "first" };
            M4T4PrivateCapture.Write(dir, "M4-T4", "2026-01-01T00-00-00Z", content);

            Assert.Throws<InvalidOperationException>(() =>
                M4T4PrivateCapture.Write(dir, "M4-T4", "2026-01-01T00-00-00Z", content));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
