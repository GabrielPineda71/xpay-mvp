using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-474 — pruebas, a nivel de executor (sin red), de los dos pasos de
// preparación de la llave desechable de M4-T3-A:
// CreateM4T3SuspendedFixtureKeyExecutor y SuspendM4T3FixtureKeyExecutor.
// Consolidados en un solo archivo (mismo criterio ya usado en
// CreateQrStaticNegativeCasesExecutorTests.cs) dado que comparten fixture
// de prueba.
public class CreateM4T3SuspendedFixtureKeyExecutorTests
{
    private sealed class FakeKeyClient : IPassportKeyClient
    {
        private readonly bool _throwOnCreate;
        private readonly bool _throwOnSuspend;
        private readonly PassportTransportException? _exceptionToThrow;
        private readonly PassportKeyResponse? _createResponse;
        private readonly PassportKeyResponse? _suspendResponse;

        public int CreateKeyCallCount { get; private set; }
        public int SuspendKeyCallCount { get; private set; }
        public int ActivateKeyCallCount { get; private set; }
        public int DeleteKeyCallCount { get; private set; }
        public int ResolveKeyCallCount { get; private set; }
        public int ListKeysCallCount { get; private set; }
        public PassportCreateKeyRequest? LastCreateRequest { get; private set; }
        public string? LastSuspendKeyId { get; private set; }

        public FakeKeyClient(
            bool throwOnCreate = false, bool throwOnSuspend = false,
            PassportKeyResponse? createResponse = null, PassportKeyResponse? suspendResponse = null,
            PassportTransportException? exceptionToThrow = null)
        {
            _throwOnCreate = throwOnCreate;
            _throwOnSuspend = throwOnSuspend;
            _createResponse = createResponse;
            _suspendResponse = suspendResponse;
            _exceptionToThrow = exceptionToThrow;
        }

        public Task<PassportKeyResponse> CreateKeyAsync(PassportCreateKeyRequest request, CancellationToken cancellationToken = default)
        {
            CreateKeyCallCount++;
            LastCreateRequest = request;
            if (_exceptionToThrow is not null) throw _exceptionToThrow;
            if (_throwOnCreate) throw new PassportTransportException("Passport respondió con error HTTP 400 (sintético).");
            return Task.FromResult(_createResponse ?? new PassportKeyResponse
            {
                Id = "synthetic-fixture-key-id", Status = "ACTIVE",
                Key = new PassportKeyResponseDetail { KeyType = "BCODE", KeyValue = "0012345678" },
            });
        }

        public Task<PassportKeyResponse> SuspendKeyAsync(string keyId, CancellationToken cancellationToken = default)
        {
            SuspendKeyCallCount++;
            LastSuspendKeyId = keyId;
            if (_exceptionToThrow is not null) throw _exceptionToThrow;
            if (_throwOnSuspend) throw new PassportTransportException("Passport respondió con error HTTP 400 (sintético).");
            return Task.FromResult(_suspendResponse ?? new PassportKeyResponse { Id = keyId, Status = "SUSPENDED" });
        }

        public Task<PassportKeyResponse> ActivateKeyAsync(string keyId, CancellationToken cancellationToken = default)
        {
            ActivateKeyCallCount++;
            throw new InvalidOperationException("ActivateKeyAsync NUNCA debe invocarse desde la preparación de M4-T3-A.");
        }

        public Task DeleteKeyAsync(string keyId, CancellationToken cancellationToken = default)
        {
            DeleteKeyCallCount++;
            throw new InvalidOperationException("DeleteKeyAsync NUNCA debe invocarse desde la preparación de M4-T3-A.");
        }

        public Task<PassportResolveKeyResponse> ResolveKeyAsync(PassportResolveKeyRequest request, CancellationToken cancellationToken = default)
        {
            ResolveKeyCallCount++;
            throw new InvalidOperationException("ResolveKeyAsync NUNCA debe invocarse desde la preparación de M4-T3-A.");
        }

        public Task<PassportListKeysResponse> ListKeysAsync(string accountId, PassportKeyType keyType, string keyValue, CancellationToken cancellationToken = default)
        {
            ListKeysCallCount++;
            throw new InvalidOperationException("ListKeysAsync NUNCA debe invocarse desde la preparación de M4-T3-A.");
        }
    }

    private static readonly DateTime FixedTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static FixedCommitShaProvider CommitSha() =>
        new("synthetic-commit-sha-0000000000000000000000000000000000000000");

    // ══════════════════════════════════════════════════════════════════════
    // CreateM4T3SuspendedFixtureKeyExecutor
    // ══════════════════════════════════════════════════════════════════════

    private const string SyntheticAccountId = "synthetic-account-id-001";

    private static IConfiguration CreateConfig(
        string? accountId = SyntheticAccountId,
        string? newKeyType = null, string? newKeyValue = null, string? newKeyId = null,
        string? qrKeyId = null, string? qrSuspendedKeyId = null)
    {
        var dict = new Dictionary<string, string?>();
        if (accountId is not null) dict[HarnessTargetConfig.EnvAccountId] = accountId;
        if (newKeyType is not null) dict[HarnessTargetConfig.EnvNewKeyType] = newKeyType;
        if (newKeyValue is not null) dict[HarnessTargetConfig.EnvNewKeyValue] = newKeyValue;
        if (newKeyId is not null) dict[HarnessTargetConfig.EnvNewKeyId] = newKeyId;
        if (qrKeyId is not null) dict[HarnessTargetConfig.EnvQrKeyId] = qrKeyId;
        if (qrSuspendedKeyId is not null) dict[HarnessTargetConfig.EnvQrSuspendedKeyId] = qrSuspendedKeyId;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public async Task CreateFixture_Success_CallsOnlyCreateKeyAsync_ExactlyOnce_UsesAccountIdOnly()
    {
        var client = new FakeKeyClient();

        var result = await CreateM4T3SuspendedFixtureKeyExecutor.ExecuteAsync(
            CreateConfig(), client, CommitSha(), FixedTime);

        Assert.Equal(1, client.CreateKeyCallCount);
        Assert.Equal(0, client.SuspendKeyCallCount + client.ActivateKeyCallCount + client.DeleteKeyCallCount + client.ResolveKeyCallCount + client.ListKeysCallCount);
        Assert.Equal(SyntheticAccountId, client.LastCreateRequest!.AccountId);
        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal("M4-T3-A-KEY-PREP-CREATE", result.Evidence!.CaseId);
        Assert.Equal("POST /v1/keys", result.Evidence.Operation);
    }

    [Fact]
    public async Task CreateFixture_Success_UsesBcodeKeyType()
    {
        var client = new FakeKeyClient();

        await CreateM4T3SuspendedFixtureKeyExecutor.ExecuteAsync(CreateConfig(), client, CommitSha(), FixedTime);

        Assert.Equal(PassportKeyType.BCODE, client.LastCreateRequest!.Key.KeyType);
    }

    [Fact]
    public async Task CreateFixture_Success_UsesDedicatedDisplayName_NotM3Generic()
    {
        var client = new FakeKeyClient();

        await CreateM4T3SuspendedFixtureKeyExecutor.ExecuteAsync(CreateConfig(), client, CommitSha(), FixedTime);

        Assert.Equal("XPay M4-T3 Suspended QR Fixture", client.LastCreateRequest!.DisplayName);
        Assert.NotEqual("XPay Certification Test Key", client.LastCreateRequest.DisplayName);
    }

    [Fact]
    public async Task CreateFixture_Success_KeyValueMatchesValidBcodeFormat()
    {
        var client = new FakeKeyClient();

        await CreateM4T3SuspendedFixtureKeyExecutor.ExecuteAsync(CreateConfig(), client, CommitSha(), FixedTime);

        var keyValue = client.LastCreateRequest!.Key.KeyValue;
        Assert.Matches("^00[0-9]{8}$", keyValue);
    }

    // Regresión CRÍTICA: incluso si TODAS las demás variables de llave están
    // presentes en el entorno, este executor NUNCA las lee para construir
    // el request.
    [Fact]
    public async Task CreateFixture_Success_NeverReadsM3OrActiveOrSuspendedKeyVariables_EvenWhenAllPresent()
    {
        const string m3KeyType  = "PHONE";
        const string m3KeyValue = "SHOULD-NEVER-BE-USED";
        var client = new FakeKeyClient();

        await CreateM4T3SuspendedFixtureKeyExecutor.ExecuteAsync(
            CreateConfig(
                newKeyType: m3KeyType, newKeyValue: m3KeyValue, newKeyId: "synthetic-m3-key-id",
                qrKeyId: "synthetic-active-qr-key-id", qrSuspendedKeyId: "synthetic-suspended-key-id"),
            client, CommitSha(), FixedTime);

        Assert.Equal(PassportKeyType.BCODE, client.LastCreateRequest!.Key.KeyType);
        Assert.NotEqual(m3KeyValue, client.LastCreateRequest.Key.KeyValue);
        Assert.Matches("^00[0-9]{8}$", client.LastCreateRequest.Key.KeyValue);
    }

    // Múltiples generaciones producen valores DISTINTOS — nunca un único
    // valor hardcodeado.
    [Fact]
    public async Task CreateFixture_Success_GeneratesDifferentKeyValueEachExecution()
    {
        var client1 = new FakeKeyClient();
        var client2 = new FakeKeyClient();

        await CreateM4T3SuspendedFixtureKeyExecutor.ExecuteAsync(CreateConfig(), client1, CommitSha(), FixedTime);
        await CreateM4T3SuspendedFixtureKeyExecutor.ExecuteAsync(CreateConfig(), client2, CommitSha(), FixedTime);

        Assert.NotEqual(client1.LastCreateRequest!.Key.KeyValue, client2.LastCreateRequest!.Key.KeyValue);
    }

    [Fact]
    public async Task CreateFixture_MissingAccountId_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeKeyClient();

        var result = await CreateM4T3SuspendedFixtureKeyExecutor.ExecuteAsync(
            CreateConfig(accountId: null), client, CommitSha(), FixedTime);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateKeyCallCount);
        Assert.Null(result.Evidence);
    }

    [Fact]
    public async Task CreateFixture_PassportRejects_CallsOnlyOnce_ProducesFailEvidence()
    {
        var client = new FakeKeyClient(throwOnCreate: true);

        var result = await CreateM4T3SuspendedFixtureKeyExecutor.ExecuteAsync(
            CreateConfig(), client, CommitSha(), FixedTime);

        Assert.Equal(1, client.CreateKeyCallCount);
        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        Assert.Equal("M4-T3-A-KEY-PREP-CREATE", result.Evidence!.CaseId);
        Assert.Equal(EvidenceRecord.ResultFail, result.Evidence.Result);
    }

    [Fact]
    public async Task CreateFixture_EvidenceNeverContainsRawIdentifiers()
    {
        const string realAccountId = "REAL-ACCOUNT-ID-must-never-appear-0000001";
        const string realKeyId     = "REAL-FIXTURE-KEY-ID-must-never-appear-0000002";
        var client = new FakeKeyClient(createResponse: new PassportKeyResponse
        {
            Id = realKeyId, Status = "ACTIVE", AccountId = realAccountId,
            Key = new PassportKeyResponseDetail { KeyType = "BCODE", KeyValue = "0087654321" },
        });

        var result = await CreateM4T3SuspendedFixtureKeyExecutor.ExecuteAsync(
            CreateConfig(accountId: realAccountId), client, CommitSha(), FixedTime);

        var evidenceJson = System.Text.Json.JsonSerializer.Serialize(result.Evidence);
        Assert.DoesNotContain(realAccountId, evidenceJson);
        Assert.DoesNotContain(realKeyId, evidenceJson);
        // El key_value generado internamente tampoco debe aparecer crudo.
        Assert.DoesNotContain(client.LastCreateRequest!.Key.KeyValue, evidenceJson);
        Assert.Contains("\"key_value\":\"REDACTED\"", evidenceJson);
        Assert.Contains("account_id_fingerprint", evidenceJson);
        Assert.Contains("id_fingerprint", evidenceJson);
    }

    // ══════════════════════════════════════════════════════════════════════
    // SuspendM4T3FixtureKeyExecutor
    // ══════════════════════════════════════════════════════════════════════

    private const string SyntheticSuspendedKeyId = "synthetic-suspended-fixture-key-id-001";

    private static IConfiguration SuspendConfig(
        string? qrSuspendedKeyId = SyntheticSuspendedKeyId, string? newKeyId = null, string? qrKeyId = null)
    {
        var dict = new Dictionary<string, string?>();
        if (qrSuspendedKeyId is not null) dict[HarnessTargetConfig.EnvQrSuspendedKeyId] = qrSuspendedKeyId;
        if (newKeyId is not null) dict[HarnessTargetConfig.EnvNewKeyId] = newKeyId;
        if (qrKeyId is not null) dict[HarnessTargetConfig.EnvQrKeyId] = qrKeyId;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public async Task SuspendFixture_Success_CallsOnlySuspendKeyAsync_ExactlyOnce_UsesSuspendedKeyIdOnly()
    {
        var client = new FakeKeyClient();

        var result = await SuspendM4T3FixtureKeyExecutor.ExecuteAsync(
            SuspendConfig(), client, CommitSha(), FixedTime);

        Assert.Equal(1, client.SuspendKeyCallCount);
        Assert.Equal(0, client.CreateKeyCallCount + client.ActivateKeyCallCount + client.DeleteKeyCallCount + client.ResolveKeyCallCount + client.ListKeysCallCount);
        Assert.Equal(SyntheticSuspendedKeyId, client.LastSuspendKeyId);
        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal("M4-T3-A-KEY-PREP-SUSPEND", result.Evidence!.CaseId);
    }

    // Regresión CRÍTICA: incluso si PASSPORT_TEST_NEW_KEY_ID (M3) y
    // PASSPORT_TEST_QR_KEY_ID (activa protegida) están presentes, este
    // executor JAMÁS los usa como target.
    [Fact]
    public async Task SuspendFixture_Success_NeverTargetsM3OrActiveKey_EvenWhenBothPresent()
    {
        const string m3KeyId     = "synthetic-m3-deleted-key-id-should-never-be-targeted";
        const string activeKeyId = "synthetic-active-qr-key-id-should-never-be-targeted";
        var client = new FakeKeyClient();

        await SuspendM4T3FixtureKeyExecutor.ExecuteAsync(
            SuspendConfig(newKeyId: m3KeyId, qrKeyId: activeKeyId), client, CommitSha(), FixedTime);

        Assert.Equal(SyntheticSuspendedKeyId, client.LastSuspendKeyId);
        Assert.NotEqual(m3KeyId, client.LastSuspendKeyId);
        Assert.NotEqual(activeKeyId, client.LastSuspendKeyId);
    }

    [Fact]
    public async Task SuspendFixture_MissingSuspendedKeyId_IsLocalBlocked_NeverCallsClient_NoFallback()
    {
        var client = new FakeKeyClient();

        var result = await SuspendM4T3FixtureKeyExecutor.ExecuteAsync(
            SuspendConfig(qrSuspendedKeyId: null, newKeyId: "synthetic-m3-key-id", qrKeyId: "synthetic-active-key-id"),
            client, CommitSha(), FixedTime);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.SuspendKeyCallCount);
        Assert.Null(result.Evidence);
    }

    [Fact]
    public async Task SuspendFixture_PassportRejects_CallsOnlyOnce_ProducesFailEvidence()
    {
        var client = new FakeKeyClient(throwOnSuspend: true);

        var result = await SuspendM4T3FixtureKeyExecutor.ExecuteAsync(
            SuspendConfig(), client, CommitSha(), FixedTime);

        Assert.Equal(1, client.SuspendKeyCallCount);
        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        Assert.Equal("M4-T3-A-KEY-PREP-SUSPEND", result.Evidence!.CaseId);
        Assert.Equal(EvidenceRecord.ResultFail, result.Evidence.Result);
    }

    [Fact]
    public async Task SuspendFixture_EvidenceNeverContainsRawKeyId()
    {
        const string realKeyId = "REAL-SUSPENDED-FIXTURE-KEY-ID-must-never-appear-0000003";
        var client = new FakeKeyClient();

        var result = await SuspendM4T3FixtureKeyExecutor.ExecuteAsync(
            SuspendConfig(qrSuspendedKeyId: realKeyId), client, CommitSha(), FixedTime);

        var evidenceJson = System.Text.Json.JsonSerializer.Serialize(result.Evidence);
        Assert.DoesNotContain(realKeyId, evidenceJson);
        Assert.DoesNotContain(realKeyId, SuspendM4T3FixtureKeyEvidenceBuilder.Operation);
        Assert.Contains("key_id_fingerprint", evidenceJson);
    }

    // ── Presupuesto de llamadas / sin reintentos — compartido por ambos ──

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BothExecutors_NeverExceedOneCallRegardlessOfOutcome(bool throwOnCall)
    {
        var createClient = new FakeKeyClient(throwOnCreate: throwOnCall);
        var suspendClient = new FakeKeyClient(throwOnSuspend: throwOnCall);

        await CreateM4T3SuspendedFixtureKeyExecutor.ExecuteAsync(CreateConfig(), createClient, CommitSha(), FixedTime);
        await SuspendM4T3FixtureKeyExecutor.ExecuteAsync(SuspendConfig(), suspendClient, CommitSha(), FixedTime);

        Assert.Equal(1, createClient.CreateKeyCallCount);
        Assert.Equal(1, suspendClient.SuspendKeyCallCount);
    }

    // ── Generador ─────────────────────────────────────────────────────────

    [Fact]
    public void DisposableBcodeGenerator_MatchesValidBcodeFormat()
    {
        var value = DisposableBcodeGenerator.Generate();
        Assert.Matches("^00[0-9]{8}$", value);
        Assert.Equal(10, value.Length);
    }

    [Fact]
    public void DisposableBcodeGenerator_ProducesDifferentValuesAcrossCalls()
    {
        var values = Enumerable.Range(0, 20).Select(_ => DisposableBcodeGenerator.Generate()).ToList();
        Assert.Equal(values.Count, values.Distinct().Count());
    }
}
