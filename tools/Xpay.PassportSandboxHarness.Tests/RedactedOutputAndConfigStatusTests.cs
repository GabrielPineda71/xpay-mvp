using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// 7. ninguna salida contiene secretos sintéticos.
public class RedactedOutputAndConfigStatusTests
{
    private const string SyntheticSecretLookingKey    = "SYNTH-API-KEY-abc123XYZ";
    private const string SyntheticSecretLookingSecret = "SYNTH-API-SECRET-doNotPrintMe987";

    [Fact]
    public void HarnessConfigStatus_RedactedLines_NeverContainConfiguredValues()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [PassportOptions.EnvBaseUrl]      = "https://api.paas.sandbox.co.passportfintech.com",
                [PassportOptions.EnvClientId]     = SyntheticSecretLookingKey,
                [PassportOptions.EnvClientSecret] = SyntheticSecretLookingSecret,
            })
            .Build();

        var status = HarnessConfigStatus.FromConfiguration(config);
        var lines = status.ToRedactedLines().ToList();

        Assert.True(status.AllPresent);
        foreach (var line in lines)
        {
            Assert.DoesNotContain(SyntheticSecretLookingKey, line);
            Assert.DoesNotContain(SyntheticSecretLookingSecret, line);
        }

        // Únicamente AVAILABLE/MISSING por variable — nunca el valor.
        Assert.All(lines, l => Assert.True(l.EndsWith("=AVAILABLE") || l.EndsWith("=MISSING")));
    }

    [Fact]
    public void RedactedResult_ToLines_NeverExposesForbiddenFields()
    {
        var result = new RedactedResult(
            Timestamp: "2026-09-14T00:00:00Z",
            CaseId: "M2-T1",
            Method: "POST",
            Path: "/v1/customers/business/link",
            Result: "DRY_RUN",
            Detail: "synthetic dry-run, no HTTP call executed",
            HttpStatus: null,
            DurationMs: null,
            RemoteIdFingerprint: null);

        var lines = result.ToLines().ToList();
        var joined = string.Join("\n", lines);

        // El propio record no tiene campos para Authorization/token/secret —
        // esto documenta esa garantía estructuralmente además de por texto.
        Assert.DoesNotContain("Authorization", joined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer", joined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SyntheticSecretLookingKey, joined);
        Assert.DoesNotContain(SyntheticSecretLookingSecret, joined);
    }
}
