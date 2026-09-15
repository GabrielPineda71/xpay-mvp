using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-344 — prueba, a nivel de CreateKeyMissingExecutor (sin red), que
// M3-T6-MISSING invoca ÚNICAMENTE IPassportKeyClient.CreateKeyAsync
// (mismo cliente productivo, nunca uno alternativo) y que ningún otro
// método Passport se invoca jamás.
public class CreateKeyMissingExecutorTests
{
    // Reutiliza el stack productivo REAL (PassportKeyClient) para probar
    // el comportamiento real del guard — no un fake que "simule" el
    // rechazo, sino el código productivo genuino.
    private sealed class CallCountingKeyClient : IPassportKeyClient
    {
        private readonly IPassportKeyClient _inner;
        public int CreateKeyCallCount { get; private set; }

        public CallCountingKeyClient(IPassportKeyClient inner) => _inner = inner;

        public Task<PassportKeyResponse> CreateKeyAsync(
            PassportCreateKeyRequest request, CancellationToken cancellationToken = default)
        {
            CreateKeyCallCount++;
            return _inner.CreateKeyAsync(request, cancellationToken);
        }

        public Task<PassportKeyResponse> SuspendKeyAsync(string keyId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("SuspendKeyAsync NUNCA debe invocarse desde create-key-missing.");

        public Task<PassportKeyResponse> ActivateKeyAsync(string keyId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("ActivateKeyAsync NUNCA debe invocarse desde create-key-missing.");

        public Task DeleteKeyAsync(string keyId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("DeleteKeyAsync NUNCA debe invocarse desde create-key-missing.");

        public Task<PassportResolveKeyResponse> ResolveKeyAsync(
            PassportResolveKeyRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("ResolveKeyAsync NUNCA debe invocarse desde create-key-missing.");

        public Task<PassportListKeysResponse> ListKeysAsync(
            string accountId, PassportKeyType keyType, string keyValue, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("ListKeysAsync NUNCA debe invocarse desde create-key-missing.");
    }

    // Fake mínimo de IPassportKeyClient que reproduce EXACTAMENTE el guard
    // real de PassportKeyClient.CreateKeyAsync (ArgumentException para
    // KeyValue vacío) — sin tocar red — para poder envolverlo en el
    // contador de llamadas sin depender de PassportHttpClient real.
    private sealed class GuardOnlyKeyClient : IPassportKeyClient
    {
        public Task<PassportKeyResponse> CreateKeyAsync(
            PassportCreateKeyRequest request, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request.Key.KeyValue))
                throw new ArgumentException("key_value es requerido.", nameof(request));
            throw new InvalidOperationException("No debería alcanzarse — key_value siempre está vacío en este subcaso.");
        }

        public Task<PassportKeyResponse> SuspendKeyAsync(string keyId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("no aplica");
        public Task<PassportKeyResponse> ActivateKeyAsync(string keyId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("no aplica");
        public Task DeleteKeyAsync(string keyId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("no aplica");
        public Task<PassportResolveKeyResponse> ResolveKeyAsync(
            PassportResolveKeyRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("no aplica");
        public Task<PassportListKeysResponse> ListKeysAsync(
            string accountId, PassportKeyType keyType, string keyValue, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("no aplica");
    }

    private const string SyntheticAccountId = "synthetic-missing-executor-account-id-001";

    private static IConfiguration ConfigWithTarget(string? accountId = SyntheticAccountId, string? keyType = "BCODE")
    {
        var dict = new Dictionary<string, string?>();
        if (accountId is not null) dict[HarnessTargetConfig.EnvAccountId] = accountId;
        if (keyType is not null) dict[HarnessTargetConfig.EnvNewKeyType] = keyType;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public async Task ExecuteAsync_KeyValueAlwaysEmpty_TriggersGuard_CallsCreateKeyExactlyOnce_NeverOtherMethods()
    {
        var client = new CallCountingKeyClient(new GuardOnlyKeyClient());
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");
        var config = ConfigWithTarget();

        var result = await CreateKeyMissingExecutor.ExecuteAsync(
            config, client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(1, client.CreateKeyCallCount);
        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.NotNull(result.Evidence);
        Assert.Equal("M3-T6-MISSING", result.Evidence!.CaseId);
        Assert.Equal(EvidenceRecord.ResultPass, result.Evidence.Result);
    }

    [Fact]
    public async Task ExecuteAsync_MissingAccountId_IsLocalBlocked_NeverCallsClient()
    {
        var client = new CallCountingKeyClient(new GuardOnlyKeyClient());
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");
        var config = ConfigWithTarget(accountId: null);

        var result = await CreateKeyMissingExecutor.ExecuteAsync(
            config, client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateKeyCallCount);
        Assert.Null(result.Evidence);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidKeyType_IsLocalBlocked_NeverCallsClient()
    {
        var client = new CallCountingKeyClient(new GuardOnlyKeyClient());
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");
        var config = ConfigWithTarget(keyType: "MOBILE"); // inválido.

        var result = await CreateKeyMissingExecutor.ExecuteAsync(
            config, client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateKeyCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_CommitShaUnresolvable_IsLocalBlocked_NeverCallsClient()
    {
        var client = new CallCountingKeyClient(new GuardOnlyKeyClient());
        var throwingCommitShaProvider = new ThrowingCommitShaProvider();
        var config = ConfigWithTarget();

        var result = await CreateKeyMissingExecutor.ExecuteAsync(
            config, client, throwingCommitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateKeyCallCount);
    }

    private sealed class ThrowingCommitShaProvider : ICommitShaProvider
    {
        public string GetCommitSha() => throw new InvalidOperationException("synthetic: commit SHA no resoluble.");
    }
}
