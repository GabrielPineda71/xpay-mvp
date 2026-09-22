using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-492 — pruebas, a nivel de executor (sin red), de los dos pasos de
// preparación de la llave desechable de M4-T3-B:
// CreateM4T3DeletedFixtureKeyExecutor y DeleteM4T3FixtureKeyExecutor.
// Mirror ESTRUCTURAL de CreateM4T3SuspendedFixtureKeyExecutorTests.cs
// (XPAY-474) — mismo criterio de consolidación en un solo archivo dado que
// comparten fixture de prueba.
public class CreateM4T3DeletedFixtureKeyExecutorTests
{
    private sealed class FakeKeyClient : IPassportKeyClient
    {
        private readonly bool _throwOnCreate;
        private readonly bool _throwOnDelete;
        private readonly PassportTransportException? _exceptionToThrow;
        private readonly PassportKeyResponse? _createResponse;

        public int CreateKeyCallCount { get; private set; }
        public int SuspendKeyCallCount { get; private set; }
        public int ActivateKeyCallCount { get; private set; }
        public int DeleteKeyCallCount { get; private set; }
        public int ResolveKeyCallCount { get; private set; }
        public int ListKeysCallCount { get; private set; }
        public PassportCreateKeyRequest? LastCreateRequest { get; private set; }
        public string? LastDeleteKeyId { get; private set; }

        public FakeKeyClient(
            bool throwOnCreate = false, bool throwOnDelete = false,
            PassportKeyResponse? createResponse = null,
            PassportTransportException? exceptionToThrow = null)
        {
            _throwOnCreate = throwOnCreate;
            _throwOnDelete = throwOnDelete;
            _createResponse = createResponse;
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
                Id = "synthetic-deleted-fixture-key-id", Status = "ACTIVE",
                Key = new PassportKeyResponseDetail { KeyType = "BCODE", KeyValue = "0012345678" },
            });
        }

        public Task<PassportKeyResponse> SuspendKeyAsync(string keyId, CancellationToken cancellationToken = default)
        {
            SuspendKeyCallCount++;
            throw new InvalidOperationException("SuspendKeyAsync NUNCA debe invocarse desde la preparación de M4-T3-B.");
        }

        public Task<PassportKeyResponse> ActivateKeyAsync(string keyId, CancellationToken cancellationToken = default)
        {
            ActivateKeyCallCount++;
            throw new InvalidOperationException("ActivateKeyAsync NUNCA debe invocarse desde la preparación de M4-T3-B.");
        }

        public Task DeleteKeyAsync(string keyId, CancellationToken cancellationToken = default)
        {
            DeleteKeyCallCount++;
            LastDeleteKeyId = keyId;
            if (_exceptionToThrow is not null) throw _exceptionToThrow;
            if (_throwOnDelete) throw new PassportTransportException("Passport respondió con error HTTP 400 (sintético).");
            return Task.CompletedTask;
        }

        public Task<PassportResolveKeyResponse> ResolveKeyAsync(PassportResolveKeyRequest request, CancellationToken cancellationToken = default)
        {
            ResolveKeyCallCount++;
            throw new InvalidOperationException("ResolveKeyAsync NUNCA debe invocarse desde la preparación de M4-T3-B.");
        }

        public Task<PassportListKeysResponse> ListKeysAsync(string accountId, PassportKeyType keyType, string keyValue, CancellationToken cancellationToken = default)
        {
            ListKeysCallCount++;
            throw new InvalidOperationException("ListKeysAsync NUNCA debe invocarse desde la preparación de M4-T3-B.");
        }
    }

    private static readonly DateTime FixedTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static FixedCommitShaProvider CommitSha() =>
        new("synthetic-commit-sha-0000000000000000000000000000000000000000");

    // ══════════════════════════════════════════════════════════════════════
    // CreateM4T3DeletedFixtureKeyExecutor
    // ══════════════════════════════════════════════════════════════════════

    private const string SyntheticAccountId = "synthetic-account-id-001";

    private static IConfiguration CreateConfig(
        string? accountId = SyntheticAccountId,
        string? newKeyType = null, string? newKeyValue = null, string? newKeyId = null,
        string? qrKeyId = null, string? qrSuspendedKeyId = null, string? qrDeletedKeyId = null)
    {
        var dict = new Dictionary<string, string?>();
        if (accountId is not null) dict[HarnessTargetConfig.EnvAccountId] = accountId;
        if (newKeyType is not null) dict[HarnessTargetConfig.EnvNewKeyType] = newKeyType;
        if (newKeyValue is not null) dict[HarnessTargetConfig.EnvNewKeyValue] = newKeyValue;
        if (newKeyId is not null) dict[HarnessTargetConfig.EnvNewKeyId] = newKeyId;
        if (qrKeyId is not null) dict[HarnessTargetConfig.EnvQrKeyId] = qrKeyId;
        if (qrSuspendedKeyId is not null) dict[HarnessTargetConfig.EnvQrSuspendedKeyId] = qrSuspendedKeyId;
        if (qrDeletedKeyId is not null) dict[HarnessTargetConfig.EnvQrDeletedKeyId] = qrDeletedKeyId;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public async Task CreateFixture_Success_CallsOnlyCreateKeyAsync_ExactlyOnce_UsesAccountIdOnly()
    {
        var client = new FakeKeyClient();

        var result = await CreateM4T3DeletedFixtureKeyExecutor.ExecuteAsync(
            CreateConfig(), client, CommitSha(), FixedTime);

        Assert.Equal(1, client.CreateKeyCallCount);
        Assert.Equal(0, client.SuspendKeyCallCount + client.ActivateKeyCallCount + client.DeleteKeyCallCount + client.ResolveKeyCallCount + client.ListKeysCallCount);
        Assert.Equal(SyntheticAccountId, client.LastCreateRequest!.AccountId);
        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal("M4-T3-B-KEY-PREP-CREATE", result.Evidence!.CaseId);
        Assert.Equal("POST /v1/keys", result.Evidence.Operation);
    }

    [Fact]
    public async Task CreateFixture_Success_UsesBcodeKeyType()
    {
        var client = new FakeKeyClient();

        await CreateM4T3DeletedFixtureKeyExecutor.ExecuteAsync(CreateConfig(), client, CommitSha(), FixedTime);

        Assert.Equal(PassportKeyType.BCODE, client.LastCreateRequest!.Key.KeyType);
    }

    [Fact]
    public async Task CreateFixture_Success_UsesDedicatedDisplayName_DistinctFromSuspendedFixture()
    {
        var client = new FakeKeyClient();

        await CreateM4T3DeletedFixtureKeyExecutor.ExecuteAsync(CreateConfig(), client, CommitSha(), FixedTime);

        Assert.Equal("XPay M4-T3 Deleted QR Fixture", client.LastCreateRequest!.DisplayName);
        Assert.NotEqual("XPay M4-T3 Suspended QR Fixture", client.LastCreateRequest.DisplayName);
        Assert.NotEqual("XPay Certification Test Key", client.LastCreateRequest.DisplayName);
    }

    [Fact]
    public async Task CreateFixture_Success_KeyValueMatchesValidBcodeFormat()
    {
        var client = new FakeKeyClient();

        await CreateM4T3DeletedFixtureKeyExecutor.ExecuteAsync(CreateConfig(), client, CommitSha(), FixedTime);

        var keyValue = client.LastCreateRequest!.Key.KeyValue;
        Assert.Matches("^00[0-9]{8}$", keyValue);
    }

    // Regresión CRÍTICA: incluso si TODAS las demás variables de llave están
    // presentes en el entorno (incluidas las contaminadas de M3), este
    // executor NUNCA las lee para construir el request.
    [Fact]
    public async Task CreateFixture_Success_NeverReadsAnyOtherKeyVariable_EvenWhenAllPresent()
    {
        const string contaminatedM3Value = "SHOULD-NEVER-BE-USED";
        var client = new FakeKeyClient();

        await CreateM4T3DeletedFixtureKeyExecutor.ExecuteAsync(
            CreateConfig(
                newKeyType: "PHONE", newKeyValue: contaminatedM3Value, newKeyId: "synthetic-contaminated-m3-key-id",
                qrKeyId: "synthetic-active-qr-key-id", qrSuspendedKeyId: "synthetic-suspended-key-id",
                qrDeletedKeyId: "synthetic-deleted-fixture-key-id-should-not-be-read-here"),
            client, CommitSha(), FixedTime);

        Assert.Equal(PassportKeyType.BCODE, client.LastCreateRequest!.Key.KeyType);
        Assert.NotEqual(contaminatedM3Value, client.LastCreateRequest.Key.KeyValue);
        Assert.Matches("^00[0-9]{8}$", client.LastCreateRequest.Key.KeyValue);
    }

    // Múltiples generaciones producen valores DISTINTOS — nunca un único
    // valor hardcodeado, y nunca coincide con el fixture de M4-T3-A.
    [Fact]
    public async Task CreateFixture_Success_GeneratesDifferentKeyValueEachExecution()
    {
        var client1 = new FakeKeyClient();
        var client2 = new FakeKeyClient();

        await CreateM4T3DeletedFixtureKeyExecutor.ExecuteAsync(CreateConfig(), client1, CommitSha(), FixedTime);
        await CreateM4T3DeletedFixtureKeyExecutor.ExecuteAsync(CreateConfig(), client2, CommitSha(), FixedTime);

        Assert.NotEqual(client1.LastCreateRequest!.Key.KeyValue, client2.LastCreateRequest!.Key.KeyValue);
    }

    [Fact]
    public async Task CreateFixture_MissingAccountId_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeKeyClient();

        var result = await CreateM4T3DeletedFixtureKeyExecutor.ExecuteAsync(
            CreateConfig(accountId: null), client, CommitSha(), FixedTime);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateKeyCallCount);
        Assert.Null(result.Evidence);
    }

    [Fact]
    public async Task CreateFixture_PassportRejects_CallsOnlyOnce_ProducesFailEvidence()
    {
        var client = new FakeKeyClient(throwOnCreate: true);

        var result = await CreateM4T3DeletedFixtureKeyExecutor.ExecuteAsync(
            CreateConfig(), client, CommitSha(), FixedTime);

        Assert.Equal(1, client.CreateKeyCallCount);
        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        Assert.Equal("M4-T3-B-KEY-PREP-CREATE", result.Evidence!.CaseId);
        Assert.Equal(EvidenceRecord.ResultFail, result.Evidence.Result);
    }

    [Fact]
    public async Task CreateFixture_EvidenceNeverContainsRawIdentifiers()
    {
        const string realAccountId = "REAL-ACCOUNT-ID-must-never-appear-0000010";
        const string realKeyId     = "REAL-DELETED-FIXTURE-KEY-ID-must-never-appear-0000011";
        var client = new FakeKeyClient(createResponse: new PassportKeyResponse
        {
            Id = realKeyId, Status = "ACTIVE", AccountId = realAccountId,
            Key = new PassportKeyResponseDetail { KeyType = "BCODE", KeyValue = "0087654321" },
        });

        var result = await CreateM4T3DeletedFixtureKeyExecutor.ExecuteAsync(
            CreateConfig(accountId: realAccountId), client, CommitSha(), FixedTime);

        var evidenceJson = System.Text.Json.JsonSerializer.Serialize(result.Evidence);
        Assert.DoesNotContain(realAccountId, evidenceJson);
        Assert.DoesNotContain(realKeyId, evidenceJson);
        Assert.DoesNotContain(client.LastCreateRequest!.Key.KeyValue, evidenceJson);
        Assert.Contains("\"key_value\":\"REDACTED\"", evidenceJson);
        Assert.Contains("account_id_fingerprint", evidenceJson);
        Assert.Contains("id_fingerprint", evidenceJson);
    }

    // ══════════════════════════════════════════════════════════════════════
    // DeleteM4T3FixtureKeyExecutor
    // ══════════════════════════════════════════════════════════════════════

    private const string SyntheticDeletedFixtureKeyId = "synthetic-deleted-fixture-key-id-to-delete-001";

    private static IConfiguration DeleteConfig(
        string? qrDeletedKeyId = SyntheticDeletedFixtureKeyId, string? newKeyId = null,
        string? qrKeyId = null, string? qrSuspendedKeyId = null)
    {
        var dict = new Dictionary<string, string?>();
        if (qrDeletedKeyId is not null) dict[HarnessTargetConfig.EnvQrDeletedKeyId] = qrDeletedKeyId;
        if (newKeyId is not null) dict[HarnessTargetConfig.EnvNewKeyId] = newKeyId;
        if (qrKeyId is not null) dict[HarnessTargetConfig.EnvQrKeyId] = qrKeyId;
        if (qrSuspendedKeyId is not null) dict[HarnessTargetConfig.EnvQrSuspendedKeyId] = qrSuspendedKeyId;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public async Task DeleteFixture_Success_CallsOnlyDeleteKeyAsync_ExactlyOnce_UsesQrDeletedKeyIdOnly()
    {
        var client = new FakeKeyClient();

        var result = await DeleteM4T3FixtureKeyExecutor.ExecuteAsync(
            DeleteConfig(), client, CommitSha(), FixedTime);

        Assert.Equal(1, client.DeleteKeyCallCount);
        Assert.Equal(0, client.CreateKeyCallCount + client.SuspendKeyCallCount + client.ActivateKeyCallCount + client.ResolveKeyCallCount + client.ListKeysCallCount);
        Assert.Equal(SyntheticDeletedFixtureKeyId, client.LastDeleteKeyId);
        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal("M4-T3-B-KEY-PREP-DELETE", result.Evidence!.CaseId);
    }

    // Regresión CRÍTICA: incluso si PASSPORT_TEST_NEW_KEY_ID (M3,
    // contaminada/abandonada), PASSPORT_TEST_QR_KEY_ID (activa) y
    // PASSPORT_TEST_QR_SUSPENDED_KEY_ID (M4-T3-A) están presentes, este
    // executor JAMÁS los usa como target.
    [Fact]
    public async Task DeleteFixture_Success_NeverTargetsAnyOtherKey_EvenWhenAllPresent()
    {
        const string contaminatedM3KeyId = "synthetic-contaminated-m3-key-id-should-never-be-targeted";
        const string activeKeyId         = "synthetic-active-qr-key-id-should-never-be-targeted";
        const string suspendedFixtureId  = "synthetic-suspended-fixture-key-id-should-never-be-targeted";
        var client = new FakeKeyClient();

        await DeleteM4T3FixtureKeyExecutor.ExecuteAsync(
            DeleteConfig(newKeyId: contaminatedM3KeyId, qrKeyId: activeKeyId, qrSuspendedKeyId: suspendedFixtureId),
            client, CommitSha(), FixedTime);

        Assert.Equal(SyntheticDeletedFixtureKeyId, client.LastDeleteKeyId);
        Assert.NotEqual(contaminatedM3KeyId, client.LastDeleteKeyId);
        Assert.NotEqual(activeKeyId, client.LastDeleteKeyId);
        Assert.NotEqual(suspendedFixtureId, client.LastDeleteKeyId);
    }

    [Fact]
    public async Task DeleteFixture_MissingQrDeletedKeyId_IsLocalBlocked_NeverCallsClient_NoFallback()
    {
        var client = new FakeKeyClient();

        var result = await DeleteM4T3FixtureKeyExecutor.ExecuteAsync(
            DeleteConfig(qrDeletedKeyId: null, newKeyId: "synthetic-contaminated-m3-key-id", qrKeyId: "synthetic-active-key-id"),
            client, CommitSha(), FixedTime);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.DeleteKeyCallCount);
        Assert.Null(result.Evidence);
    }

    [Fact]
    public async Task DeleteFixture_PassportRejects_CallsOnlyOnce_ProducesFailEvidence()
    {
        var client = new FakeKeyClient(throwOnDelete: true);

        var result = await DeleteM4T3FixtureKeyExecutor.ExecuteAsync(
            DeleteConfig(), client, CommitSha(), FixedTime);

        Assert.Equal(1, client.DeleteKeyCallCount);
        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        Assert.Equal("M4-T3-B-KEY-PREP-DELETE", result.Evidence!.CaseId);
        Assert.Equal(EvidenceRecord.ResultFail, result.Evidence.Result);
    }

    [Fact]
    public async Task DeleteFixture_EvidenceNeverContainsRawKeyId()
    {
        const string realKeyId = "REAL-DELETE-TARGET-FIXTURE-KEY-ID-must-never-appear-0000012";
        var client = new FakeKeyClient();

        var result = await DeleteM4T3FixtureKeyExecutor.ExecuteAsync(
            DeleteConfig(qrDeletedKeyId: realKeyId), client, CommitSha(), FixedTime);

        var evidenceJson = System.Text.Json.JsonSerializer.Serialize(result.Evidence);
        Assert.DoesNotContain(realKeyId, evidenceJson);
        Assert.DoesNotContain(realKeyId, DeleteM4T3FixtureKeyEvidenceBuilder.Operation);
        Assert.Contains("key_id_fingerprint", evidenceJson);
    }

    // ── Cadena de identidad: Create → Delete deben enlazar por fingerprint ──

    [Fact]
    public async Task CreateThenDelete_SameKeyId_ProduceSameFingerprint()
    {
        const string remoteKeyId = "synthetic-shared-remote-key-id-for-identity-chain";
        var createClient = new FakeKeyClient(createResponse: new PassportKeyResponse
        {
            Id = remoteKeyId, Status = "ACTIVE",
            Key = new PassportKeyResponseDetail { KeyType = "BCODE", KeyValue = "0011112222" },
        });
        var deleteClient = new FakeKeyClient();

        var createResult = await CreateM4T3DeletedFixtureKeyExecutor.ExecuteAsync(
            CreateConfig(), createClient, CommitSha(), FixedTime);
        var deleteResult = await DeleteM4T3FixtureKeyExecutor.ExecuteAsync(
            DeleteConfig(qrDeletedKeyId: remoteKeyId), deleteClient, CommitSha(), FixedTime);

        var createFingerprint = createResult.Evidence!.ResponseSanitized["id_fingerprint"];
        var deleteFingerprint = deleteResult.Evidence!.RequestSanitized["key_id_fingerprint"];
        Assert.Equal(createFingerprint, deleteFingerprint);
        Assert.Equal(Fingerprint.Compute(remoteKeyId), deleteFingerprint);
    }

    // ── Presupuesto de llamadas / sin reintentos — compartido por ambos ──

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BothExecutors_NeverExceedOneCallRegardlessOfOutcome(bool throwOnCall)
    {
        var createClient = new FakeKeyClient(throwOnCreate: throwOnCall);
        var deleteClient = new FakeKeyClient(throwOnDelete: throwOnCall);

        await CreateM4T3DeletedFixtureKeyExecutor.ExecuteAsync(CreateConfig(), createClient, CommitSha(), FixedTime);
        await DeleteM4T3FixtureKeyExecutor.ExecuteAsync(DeleteConfig(), deleteClient, CommitSha(), FixedTime);

        Assert.Equal(1, createClient.CreateKeyCallCount);
        Assert.Equal(1, deleteClient.DeleteKeyCallCount);
    }
}
