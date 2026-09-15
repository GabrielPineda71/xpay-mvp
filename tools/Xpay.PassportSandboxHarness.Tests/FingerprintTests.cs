using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

public class FingerprintTests
{
    [Fact]
    public void Compute_IsDeterministic_SameInputSameOutput()
    {
        var a = Fingerprint.Compute("synthetic-value-001");
        var b = Fingerprint.Compute("synthetic-value-001");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Compute_DifferentInputs_ProduceDifferentFingerprints()
    {
        var a = Fingerprint.Compute("synthetic-value-001");
        var b = Fingerprint.Compute("synthetic-value-002");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Compute_NeverReturnsOriginalValue()
    {
        const string original = "synthetic-sensitive-value-should-never-appear";
        var fingerprint = Fingerprint.Compute(original);
        Assert.NotEqual(original, fingerprint);
        Assert.DoesNotContain(original, fingerprint);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Compute_NullOrEmpty_ReturnsAbsentMarker(string? value)
    {
        Assert.Equal(Fingerprint.AbsentMarker, Fingerprint.Compute(value));
    }

    [Fact]
    public void Compute_RespectsRequestedLength()
    {
        var fp = Fingerprint.Compute("synthetic-value-001", length: 8);
        Assert.Equal(8, fp.Length);
    }

    [Fact]
    public void Compute_IsNotGetHashCodeBased()
    {
        // XPAY-325 FASE 9 — confirma empíricamente que el fingerprint NO
        // coincide con object.GetHashCode() (que ni siquiera es determinístico
        // entre procesos/ejecuciones .NET) — se usa SHA-256 real.
        var value = "synthetic-value-for-hashcode-check";
        var fingerprint = Fingerprint.Compute(value, length: 64);
        var notHashCode = value.GetHashCode().ToString("x8");
        Assert.DoesNotContain(notHashCode, fingerprint, StringComparison.OrdinalIgnoreCase);
    }
}
