using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-332 — prueba, a nivel de ActivateKeyExecutor (sin red, sin
// PassportHttpClient), que Activate Key invoca ÚNICAMENTE
// IPassportKeyClient.ActivateKeyAsync — NUNCA Suspend/Delete/Create/
// Resolve/ListKeys. Un IPassportKeyClient fake que lanza si se invoca
// cualquier método distinto de ActivateKeyAsync hace esta garantía
// verificable de forma directa y robusta (más simple y más fuerte que
// inspeccionar el HTTP subyacente).
public class ActivateKeyExecutorTests
{
    private sealed class FakeKeyClient : IPassportKeyClient
    {
        public int ActivateCallCount { get; private set; }
        public string? LastActivatedKeyId { get; private set; }

        public Task<PassportKeyResponse> CreateKeyAsync(
            PassportCreateKeyRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("CreateKeyAsync NUNCA debe invocarse desde activate-key.");

        public Task<PassportKeyResponse> SuspendKeyAsync(
            string keyId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("SuspendKeyAsync NUNCA debe invocarse desde activate-key.");

        public Task<PassportKeyResponse> ActivateKeyAsync(
            string keyId, CancellationToken cancellationToken = default)
        {
            ActivateCallCount++;
            LastActivatedKeyId = keyId;
            return Task.FromResult(new PassportKeyResponse { Id = keyId, Status = "ACTIVE" });
        }

        public Task DeleteKeyAsync(string keyId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("DeleteKeyAsync NUNCA debe invocarse desde activate-key.");

        public Task<PassportResolveKeyResponse> ResolveKeyAsync(
            PassportResolveKeyRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("ResolveKeyAsync NUNCA debe invocarse desde activate-key.");

        public Task<PassportListKeysResponse> ListKeysAsync(
            string accountId, PassportKeyType keyType, string keyValue, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("ListKeysAsync NUNCA debe invocarse desde activate-key.");
    }

    private const string SyntheticKeyId = "synthetic-activate-executor-key-id-001";

    private static IConfiguration ConfigWithKeyId(string? keyId = SyntheticKeyId)
    {
        var dict = new Dictionary<string, string?>();
        if (keyId is not null) dict[HarnessTargetConfig.EnvNewKeyId] = keyId;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public async Task ExecuteAsync_Success_CallsOnlyActivateKeyAsync_ExactlyOnce()
    {
        var client = new FakeKeyClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");
        var config = ConfigWithKeyId();

        var result = await ActivateKeyExecutor.ExecuteAsync(
            config, client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal(1, client.ActivateCallCount);
        Assert.Equal(SyntheticKeyId, client.LastActivatedKeyId);

        // No lanzó ninguna InvalidOperationException de los otros 5 métodos
        // fake — si Activate hubiese invocado cualquiera de ellos, este
        // test habría fallado con esa excepción en vez de llegar aquí.
        Assert.NotNull(result.Evidence);
        Assert.Equal("M3-T4", result.Evidence!.CaseId);
        Assert.Equal(EvidenceRecord.ResultPass, result.Evidence.Result);
    }

    [Fact]
    public async Task ExecuteAsync_MissingKeyId_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeKeyClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");
        var config = ConfigWithKeyId(keyId: null);

        var result = await ActivateKeyExecutor.ExecuteAsync(
            config, client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.ActivateCallCount);
        Assert.Null(result.Evidence); // LocalBlocked nunca produce evidencia persistible.
    }

    [Fact]
    public async Task ExecuteAsync_CommitShaUnresolvable_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeKeyClient();
        var throwingCommitShaProvider = new ThrowingCommitShaProvider();
        var config = ConfigWithKeyId();

        var result = await ActivateKeyExecutor.ExecuteAsync(
            config, client, throwingCommitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.ActivateCallCount);
    }

    private sealed class ThrowingCommitShaProvider : ICommitShaProvider
    {
        public string GetCommitSha() => throw new InvalidOperationException("synthetic: commit SHA no resoluble.");
    }
}
