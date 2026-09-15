using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-336 — prueba, a nivel de DeleteAlreadyDeletedKeyExecutor (sin red,
// sin PassportHttpClient), que M3-T7 invoca ÚNICAMENTE
// IPassportKeyClient.DeleteKeyAsync — NUNCA Create/Suspend/Activate/
// Resolve/ListKeys. Mirror exacto de DeleteKeyExecutorTests (XPAY-334).
public class DeleteAlreadyDeletedKeyExecutorTests
{
    private sealed class FakeKeyClient : IPassportKeyClient
    {
        public int DeleteCallCount { get; private set; }
        public string? LastDeletedKeyId { get; private set; }

        public Task<PassportKeyResponse> CreateKeyAsync(
            PassportCreateKeyRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("CreateKeyAsync NUNCA debe invocarse desde delete-already-deleted-key.");

        public Task<PassportKeyResponse> SuspendKeyAsync(
            string keyId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("SuspendKeyAsync NUNCA debe invocarse desde delete-already-deleted-key.");

        public Task<PassportKeyResponse> ActivateKeyAsync(
            string keyId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("ActivateKeyAsync NUNCA debe invocarse desde delete-already-deleted-key.");

        public Task DeleteKeyAsync(string keyId, CancellationToken cancellationToken = default)
        {
            DeleteCallCount++;
            LastDeletedKeyId = keyId;
            return Task.CompletedTask;
        }

        public Task<PassportResolveKeyResponse> ResolveKeyAsync(
            PassportResolveKeyRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("ResolveKeyAsync NUNCA debe invocarse desde delete-already-deleted-key.");

        public Task<PassportListKeysResponse> ListKeysAsync(
            string accountId, PassportKeyType keyType, string keyValue, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("ListKeysAsync NUNCA debe invocarse desde delete-already-deleted-key.");
    }

    // Excepción simulada — el fake lanza esta excepción saneada para
    // ejercitar la rama PassportFailure sin red real.
    private sealed class FailingFakeKeyClient : IPassportKeyClient
    {
        public int DeleteCallCount { get; private set; }

        public Task<PassportKeyResponse> CreateKeyAsync(
            PassportCreateKeyRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("CreateKeyAsync NUNCA debe invocarse.");

        public Task<PassportKeyResponse> SuspendKeyAsync(string keyId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("SuspendKeyAsync NUNCA debe invocarse.");

        public Task<PassportKeyResponse> ActivateKeyAsync(string keyId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("ActivateKeyAsync NUNCA debe invocarse.");

        public Task DeleteKeyAsync(string keyId, CancellationToken cancellationToken = default)
        {
            DeleteCallCount++;
            throw new PassportTransportException("Passport respondió con error HTTP 400 (sintético).");
        }

        public Task<PassportResolveKeyResponse> ResolveKeyAsync(
            PassportResolveKeyRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("ResolveKeyAsync NUNCA debe invocarse.");

        public Task<PassportListKeysResponse> ListKeysAsync(
            string accountId, PassportKeyType keyType, string keyValue, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("ListKeysAsync NUNCA debe invocarse.");
    }

    private const string SyntheticKeyId = "synthetic-delete-already-deleted-executor-key-id-001";

    private static IConfiguration ConfigWithKeyId(string? keyId = SyntheticKeyId)
    {
        var dict = new Dictionary<string, string?>();
        if (keyId is not null) dict[HarnessTargetConfig.EnvNewKeyId] = keyId;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public async Task ExecuteAsync_TransportSuccess_CallsOnlyDeleteKeyAsync_ExactlyOnce_ProducesM3T7Evidence()
    {
        var client = new FakeKeyClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");
        var config = ConfigWithKeyId();

        var result = await DeleteAlreadyDeletedKeyExecutor.ExecuteAsync(
            config, client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal(1, client.DeleteCallCount);
        Assert.Equal(SyntheticKeyId, client.LastDeletedKeyId);

        Assert.NotNull(result.Evidence);
        Assert.Equal("M3-T7", result.Evidence!.CaseId); // NUNCA "M3-T5".
        Assert.Equal(EvidenceRecord.ResultPass, result.Evidence.Result);
        Assert.Empty(result.Evidence.ResponseSanitized);

        // La nota debe dejar explícito que "PASS" aquí es sólo transporte,
        // no un veredicto de certificación.
        Assert.Contains("revisión contractual", result.Evidence.Notes!);
    }

    [Fact]
    public async Task ExecuteAsync_TransportFailure_ProducesM3T7FailEvidence_WithoutAsserting404Or409AsExpected()
    {
        var client = new FailingFakeKeyClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");
        var config = ConfigWithKeyId();

        var result = await DeleteAlreadyDeletedKeyExecutor.ExecuteAsync(
            config, client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        Assert.Equal(1, client.DeleteCallCount);

        Assert.NotNull(result.Evidence);
        Assert.Equal("M3-T7", result.Evidence!.CaseId);
        Assert.Equal(EvidenceRecord.ResultFail, result.Evidence.Result);
        Assert.StartsWith("PASSPORT_HTTP_FAILURE:", result.Evidence.Notes);
        Assert.Contains("revisión contractual", result.Evidence.Notes!);
    }

    [Fact]
    public async Task ExecuteAsync_MissingKeyId_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeKeyClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");
        var config = ConfigWithKeyId(keyId: null);

        var result = await DeleteAlreadyDeletedKeyExecutor.ExecuteAsync(
            config, client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.DeleteCallCount);
        Assert.Null(result.Evidence);
    }

    [Fact]
    public async Task ExecuteAsync_CommitShaUnresolvable_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeKeyClient();
        var throwingCommitShaProvider = new ThrowingCommitShaProvider();
        var config = ConfigWithKeyId();

        var result = await DeleteAlreadyDeletedKeyExecutor.ExecuteAsync(
            config, client, throwingCommitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.DeleteCallCount);
    }

    private sealed class ThrowingCommitShaProvider : ICommitShaProvider
    {
        public string GetCommitSha() => throw new InvalidOperationException("synthetic: commit SHA no resoluble.");
    }
}
