using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-334 — prueba, a nivel de DeleteKeyExecutor (sin red, sin
// PassportHttpClient), que Delete Key invoca ÚNICAMENTE
// IPassportKeyClient.DeleteKeyAsync — NUNCA Create/Suspend/Activate/
// Resolve/ListKeys. Mirror exacto de ActivateKeyExecutorTests (XPAY-332).
public class DeleteKeyExecutorTests
{
    private sealed class FakeKeyClient : IPassportKeyClient
    {
        public int DeleteCallCount { get; private set; }
        public string? LastDeletedKeyId { get; private set; }

        public Task<PassportKeyResponse> CreateKeyAsync(
            PassportCreateKeyRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("CreateKeyAsync NUNCA debe invocarse desde delete-key.");

        public Task<PassportKeyResponse> SuspendKeyAsync(
            string keyId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("SuspendKeyAsync NUNCA debe invocarse desde delete-key.");

        public Task<PassportKeyResponse> ActivateKeyAsync(
            string keyId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("ActivateKeyAsync NUNCA debe invocarse desde delete-key.");

        public Task DeleteKeyAsync(string keyId, CancellationToken cancellationToken = default)
        {
            DeleteCallCount++;
            LastDeletedKeyId = keyId;
            return Task.CompletedTask; // 204 No Content — sin body, sin excepción.
        }

        public Task<PassportResolveKeyResponse> ResolveKeyAsync(
            PassportResolveKeyRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("ResolveKeyAsync NUNCA debe invocarse desde delete-key.");

        public Task<PassportListKeysResponse> ListKeysAsync(
            string accountId, PassportKeyType keyType, string keyValue, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("ListKeysAsync NUNCA debe invocarse desde delete-key.");
    }

    private const string SyntheticKeyId = "synthetic-delete-executor-key-id-001";

    private static IConfiguration ConfigWithKeyId(string? keyId = SyntheticKeyId)
    {
        var dict = new Dictionary<string, string?>();
        if (keyId is not null) dict[HarnessTargetConfig.EnvNewKeyId] = keyId;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public async Task ExecuteAsync_Success_CallsOnlyDeleteKeyAsync_ExactlyOnce()
    {
        var client = new FakeKeyClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");
        var config = ConfigWithKeyId();

        var result = await DeleteKeyExecutor.ExecuteAsync(
            config, client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal(1, client.DeleteCallCount);
        Assert.Equal(SyntheticKeyId, client.LastDeletedKeyId);

        // No lanzó ninguna InvalidOperationException de los otros 5
        // métodos fake — si Delete hubiese invocado cualquiera de ellos,
        // este test habría fallado con esa excepción en vez de llegar aquí.
        Assert.NotNull(result.Evidence);
        Assert.Equal("M3-T5", result.Evidence!.CaseId);
        Assert.Equal(EvidenceRecord.ResultPass, result.Evidence.Result);

        // response_sanitized vacío — 204 sin body, nada que inventar.
        Assert.Empty(result.Evidence.ResponseSanitized);
    }

    [Fact]
    public async Task ExecuteAsync_MissingKeyId_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeKeyClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");
        var config = ConfigWithKeyId(keyId: null);

        var result = await DeleteKeyExecutor.ExecuteAsync(
            config, client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.DeleteCallCount);
        Assert.Null(result.Evidence); // LocalBlocked nunca produce evidencia persistible.
    }

    [Fact]
    public async Task ExecuteAsync_CommitShaUnresolvable_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeKeyClient();
        var throwingCommitShaProvider = new ThrowingCommitShaProvider();
        var config = ConfigWithKeyId();

        var result = await DeleteKeyExecutor.ExecuteAsync(
            config, client, throwingCommitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.DeleteCallCount);
    }

    private sealed class ThrowingCommitShaProvider : ICommitShaProvider
    {
        public string GetCommitSha() => throw new InvalidOperationException("synthetic: commit SHA no resoluble.");
    }
}
