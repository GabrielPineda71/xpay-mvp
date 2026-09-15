using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-325 — fake local, NUNCA dependiente de un proceso Git real ni de un
// repositorio real en disco (FASE 8: "evita acoplar tests directamente al
// proceso Git real").
internal sealed class FixedCommitShaProvider : ICommitShaProvider
{
    private readonly string _sha;
    public FixedCommitShaProvider(string sha) => _sha = sha;
    public string GetCommitSha() => _sha;
}

public class CommitShaProviderTests
{
    [Fact]
    public void FixedCommitShaProvider_ReturnsConfiguredValue()
    {
        var provider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");
        Assert.Equal("synthetic-commit-sha-0000000000000000000000000000000000000000", provider.GetCommitSha());
    }

    [Fact]
    public void GitCommitShaProvider_EnvOverride_TakesPrecedenceOverGitProcess()
    {
        // Confirma la resolución vía variable de entorno sin depender del
        // estado real del repositorio Git.
        Environment.SetEnvironmentVariable("XPAY_BUILD_COMMIT_SHA", "synthetic-env-override-sha");
        try
        {
            var provider = new GitCommitShaProvider();
            Assert.Equal("synthetic-env-override-sha", provider.GetCommitSha());
        }
        finally
        {
            Environment.SetEnvironmentVariable("XPAY_BUILD_COMMIT_SHA", null);
        }
    }
}
