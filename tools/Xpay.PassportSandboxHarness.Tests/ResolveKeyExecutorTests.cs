using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-340 — prueba, a nivel de ResolveKeyExecutor (sin red, sin
// PassportHttpClient), que Resolve Key invoca ÚNICAMENTE
// IPassportKeyClient.ResolveKeyAsync — NUNCA Create/Suspend/Activate/
// Delete/ListKeys. Mirror del patrón ya usado en
// ActivateKeyExecutorTests/DeleteKeyExecutorTests (XPAY-332/334).
public class ResolveKeyExecutorTests
{
    private sealed class FakeKeyClient : IPassportKeyClient
    {
        public int ResolveCallCount { get; private set; }
        public PassportResolveKeyRequest? LastRequest { get; private set; }

        public Task<PassportKeyResponse> CreateKeyAsync(
            PassportCreateKeyRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("CreateKeyAsync NUNCA debe invocarse desde resolve-key.");

        public Task<PassportKeyResponse> SuspendKeyAsync(string keyId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("SuspendKeyAsync NUNCA debe invocarse desde resolve-key.");

        public Task<PassportKeyResponse> ActivateKeyAsync(string keyId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("ActivateKeyAsync NUNCA debe invocarse desde resolve-key.");

        public Task DeleteKeyAsync(string keyId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("DeleteKeyAsync NUNCA debe invocarse desde resolve-key.");

        public Task<PassportResolveKeyResponse> ResolveKeyAsync(
            PassportResolveKeyRequest request, CancellationToken cancellationToken = default)
        {
            ResolveCallCount++;
            LastRequest = request;
            return Task.FromResult(new PassportResolveKeyResponse
            {
                Id = "synthetic-resolution-id-executor-test",
                CustomerId = request.CustomerId,
                Key = new PassportKeyResponseDetail { KeyType = request.Key.KeyType.ToString(), KeyValue = request.Key.KeyValue },
            });
        }

        public Task<PassportListKeysResponse> ListKeysAsync(
            string accountId, PassportKeyType keyType, string keyValue, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("ListKeysAsync NUNCA debe invocarse desde resolve-key.");
    }

    private const string SyntheticCustomerId = "synthetic-resolve-executor-customer-id-001";
    private const string SyntheticKeyValue   = "synthetic-resolve-executor-key-value-001";

    private static IConfiguration ConfigWithTarget(
        string? customerId = SyntheticCustomerId, string? brebKeyType = "BCODE", string? brebKeyValue = SyntheticKeyValue)
    {
        var dict = new Dictionary<string, string?>();
        if (customerId is not null) dict[HarnessTargetConfig.EnvCustomerId] = customerId;
        if (brebKeyType is not null) dict[HarnessTargetConfig.EnvBrebKeyType] = brebKeyType;
        if (brebKeyValue is not null) dict[HarnessTargetConfig.EnvBrebKeyValue] = brebKeyValue;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public async Task ExecuteAsync_Success_CallsOnlyResolveKeyAsync_ExactlyOnce()
    {
        var client = new FakeKeyClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");
        var config = ConfigWithTarget();

        var result = await ResolveKeyExecutor.ExecuteAsync(
            config, client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal(1, client.ResolveCallCount);
        Assert.Equal(SyntheticCustomerId, client.LastRequest!.CustomerId);
        Assert.Equal(PassportKeyType.BCODE, client.LastRequest.Key.KeyType);
        Assert.Equal(SyntheticKeyValue, client.LastRequest.Key.KeyValue);

        // No lanzó ninguna InvalidOperationException de los otros 5
        // métodos fake — si Resolve hubiese invocado cualquiera de ellos,
        // este test habría fallado con esa excepción en vez de llegar aquí.
        Assert.NotNull(result.Evidence);
        Assert.Equal("M3-T2", result.Evidence!.CaseId);
        Assert.Equal(EvidenceRecord.ResultPass, result.Evidence.Result);
    }

    [Theory]
    [InlineData(null, "BCODE", SyntheticKeyValue)]
    [InlineData(SyntheticCustomerId, "BCODE", null)]
    public async Task ExecuteAsync_MissingTarget_IsLocalBlocked_NeverCallsClient(
        string? customerId, string brebKeyType, string? brebKeyValue)
    {
        var client = new FakeKeyClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");
        var config = ConfigWithTarget(customerId, brebKeyType, brebKeyValue);

        var result = await ResolveKeyExecutor.ExecuteAsync(
            config, client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.ResolveCallCount);
        Assert.Null(result.Evidence);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidKeyType_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeKeyClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");
        var config = ConfigWithTarget(brebKeyType: "MOBILE"); // inválido — no se normaliza a PHONE

        var result = await ResolveKeyExecutor.ExecuteAsync(
            config, client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.ResolveCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_CommitShaUnresolvable_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeKeyClient();
        var throwingCommitShaProvider = new ThrowingCommitShaProvider();
        var config = ConfigWithTarget();

        var result = await ResolveKeyExecutor.ExecuteAsync(
            config, client, throwingCommitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.ResolveCallCount);
    }

    private sealed class ThrowingCommitShaProvider : ICommitShaProvider
    {
        public string GetCommitSha() => throw new InvalidOperationException("synthetic: commit SHA no resoluble.");
    }
}
