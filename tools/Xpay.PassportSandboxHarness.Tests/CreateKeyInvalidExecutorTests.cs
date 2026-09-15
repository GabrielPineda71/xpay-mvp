using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-344 — prueba, a nivel de CreateKeyInvalidExecutor (sin red), que
// M3-T6-INVALID invoca ÚNICAMENTE IPassportKeyClient.CreateKeyAsync
// exactamente una vez, que el key_value inválido nunca sale del proceso
// (ni en el request capturado por el fake ni en la evidencia), y que
// ningún otro tipo de llave distinto de BCODE se acepta sin contrato.
public class CreateKeyInvalidExecutorTests
{
    private sealed class FakeKeyClient : IPassportKeyClient
    {
        private readonly bool _throwOnCreate;
        public int CreateKeyCallCount { get; private set; }
        public PassportCreateKeyRequest? LastRequest { get; private set; }

        public FakeKeyClient(bool throwOnCreate = false) => _throwOnCreate = throwOnCreate;

        public Task<PassportKeyResponse> CreateKeyAsync(
            PassportCreateKeyRequest request, CancellationToken cancellationToken = default)
        {
            CreateKeyCallCount++;
            LastRequest = request;
            if (_throwOnCreate)
                throw new PassportTransportException("Passport respondió con error HTTP 400 (sintético).");
            return Task.FromResult(new PassportKeyResponse { Id = "synthetic-id", Status = "ACTIVE" });
        }

        public Task<PassportKeyResponse> SuspendKeyAsync(string keyId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("SuspendKeyAsync NUNCA debe invocarse desde create-key-invalid.");
        public Task<PassportKeyResponse> ActivateKeyAsync(string keyId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("ActivateKeyAsync NUNCA debe invocarse desde create-key-invalid.");
        public Task DeleteKeyAsync(string keyId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("DeleteKeyAsync NUNCA debe invocarse desde create-key-invalid.");
        public Task<PassportResolveKeyResponse> ResolveKeyAsync(
            PassportResolveKeyRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("ResolveKeyAsync NUNCA debe invocarse desde create-key-invalid.");
        public Task<PassportListKeysResponse> ListKeysAsync(
            string accountId, PassportKeyType keyType, string keyValue, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("ListKeysAsync NUNCA debe invocarse desde create-key-invalid.");
    }

    private const string SyntheticAccountId = "synthetic-invalid-executor-account-id-001";

    private static IConfiguration ConfigWithTarget(string? accountId = SyntheticAccountId, string? keyType = "BCODE")
    {
        var dict = new Dictionary<string, string?>();
        if (accountId is not null) dict[HarnessTargetConfig.EnvAccountId] = accountId;
        if (keyType is not null) dict[HarnessTargetConfig.EnvNewKeyType] = keyType;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public async Task ExecuteAsync_Success_CallsOnlyCreateKeyAsync_ExactlyOnce_WithInvalidBcodeShape()
    {
        var client = new FakeKeyClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");
        var config = ConfigWithTarget();

        var result = await CreateKeyInvalidExecutor.ExecuteAsync(
            config, client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(1, client.CreateKeyCallCount);
        Assert.Equal(SyntheticAccountId, client.LastRequest!.AccountId);
        Assert.Equal(PassportKeyType.BCODE, client.LastRequest.Key.KeyType);

        // El key_value generado internamente viola el formato BCODE
        // (^00[0-9]{8}$): longitud correcta y prefijo correcto, pero
        // contiene una letra — nunca coincide con un BCODE real válido.
        var generatedValue = client.LastRequest.Key.KeyValue;
        Assert.Equal(10, generatedValue.Length);
        Assert.StartsWith("00", generatedValue);
        Assert.False(generatedValue.All(char.IsDigit)); // inválido a propósito.

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.NotNull(result.Evidence);
        Assert.Equal("M3-T6-INVALID", result.Evidence!.CaseId);

        // El valor real generado NUNCA aparece en la evidencia.
        var evidenceJson = System.Text.Json.JsonSerializer.Serialize(result.Evidence);
        Assert.DoesNotContain(generatedValue, evidenceJson);
    }

    [Fact]
    public async Task ExecuteAsync_PassportRejects_CallsOnlyCreateKeyAsync_ExactlyOnce_ProducesFailEvidence()
    {
        var client = new FakeKeyClient(throwOnCreate: true);
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");
        var config = ConfigWithTarget();

        var result = await CreateKeyInvalidExecutor.ExecuteAsync(
            config, client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(1, client.CreateKeyCallCount);
        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        Assert.NotNull(result.Evidence);
        Assert.Equal("M3-T6-INVALID", result.Evidence!.CaseId);
        Assert.Equal(EvidenceRecord.ResultFail, result.Evidence.Result);
    }

    [Theory]
    [InlineData("ID")]
    [InlineData("PHONE")]
    [InlineData("EMAIL")]
    [InlineData("ALPHA")]
    public async Task ExecuteAsync_UnsupportedKeyType_IsLocalBlocked_NeverCallsClient(string unsupportedKeyType)
    {
        var client = new FakeKeyClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");
        var config = ConfigWithTarget(keyType: unsupportedKeyType);

        var result = await CreateKeyInvalidExecutor.ExecuteAsync(
            config, client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateKeyCallCount);
        Assert.Null(result.Evidence);
    }

    [Fact]
    public async Task ExecuteAsync_MissingAccountId_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeKeyClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");
        var config = ConfigWithTarget(accountId: null);

        var result = await CreateKeyInvalidExecutor.ExecuteAsync(
            config, client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateKeyCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_CommitShaUnresolvable_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeKeyClient();
        var throwingCommitShaProvider = new ThrowingCommitShaProvider();
        var config = ConfigWithTarget();

        var result = await CreateKeyInvalidExecutor.ExecuteAsync(
            config, client, throwingCommitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateKeyCallCount);
    }

    private sealed class ThrowingCommitShaProvider : ICommitShaProvider
    {
        public string GetCommitSha() => throw new InvalidOperationException("synthetic: commit SHA no resoluble.");
    }
}
