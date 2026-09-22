using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-489 — pruebas, a nivel de executor (sin red), de
// RecoverDeletedKeyIdExecutor: recuperación read-only, vía List Keys, del
// key_id histórico DELETED del ciclo M3.
public class RecoverDeletedKeyIdExecutorTests
{
    private sealed class FakeKeyClient : IPassportKeyClient
    {
        private readonly PassportListKeysResponse? _listResponse;
        private readonly PassportTransportException? _exceptionToThrow;
        private readonly bool _throwOnList;

        public int ListKeysCallCount { get; private set; }
        public int CreateKeyCallCount { get; private set; }
        public int SuspendKeyCallCount { get; private set; }
        public int ActivateKeyCallCount { get; private set; }
        public int DeleteKeyCallCount { get; private set; }
        public int ResolveKeyCallCount { get; private set; }
        public string? LastListAccountId { get; private set; }
        public PassportKeyType? LastListKeyType { get; private set; }
        public string? LastListKeyValue { get; private set; }

        public FakeKeyClient(
            PassportListKeysResponse? listResponse = null,
            bool throwOnList = false,
            PassportTransportException? exceptionToThrow = null)
        {
            _listResponse = listResponse;
            _throwOnList = throwOnList;
            _exceptionToThrow = exceptionToThrow;
        }

        public Task<PassportListKeysResponse> ListKeysAsync(
            string accountId, PassportKeyType keyType, string keyValue, CancellationToken cancellationToken = default)
        {
            ListKeysCallCount++;
            LastListAccountId = accountId;
            LastListKeyType = keyType;
            LastListKeyValue = keyValue;
            if (_exceptionToThrow is not null) throw _exceptionToThrow;
            if (_throwOnList) throw new PassportTransportException("Passport respondió con error HTTP 500 (sintético).");
            return Task.FromResult(_listResponse ?? new PassportListKeysResponse());
        }

        public Task<PassportKeyResponse> CreateKeyAsync(PassportCreateKeyRequest request, CancellationToken cancellationToken = default)
        {
            CreateKeyCallCount++;
            throw new InvalidOperationException("CreateKeyAsync NUNCA debe invocarse desde la recuperación read-only.");
        }

        public Task<PassportKeyResponse> SuspendKeyAsync(string keyId, CancellationToken cancellationToken = default)
        {
            SuspendKeyCallCount++;
            throw new InvalidOperationException("SuspendKeyAsync NUNCA debe invocarse desde la recuperación read-only.");
        }

        public Task<PassportKeyResponse> ActivateKeyAsync(string keyId, CancellationToken cancellationToken = default)
        {
            ActivateKeyCallCount++;
            throw new InvalidOperationException("ActivateKeyAsync NUNCA debe invocarse desde la recuperación read-only.");
        }

        public Task DeleteKeyAsync(string keyId, CancellationToken cancellationToken = default)
        {
            DeleteKeyCallCount++;
            throw new InvalidOperationException("DeleteKeyAsync NUNCA debe invocarse desde la recuperación read-only.");
        }

        public Task<PassportResolveKeyResponse> ResolveKeyAsync(PassportResolveKeyRequest request, CancellationToken cancellationToken = default)
        {
            ResolveKeyCallCount++;
            throw new InvalidOperationException("ResolveKeyAsync NUNCA debe invocarse desde la recuperación read-only.");
        }
    }

    private static readonly DateTime FixedTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static FixedCommitShaProvider CommitSha() =>
        new("synthetic-commit-sha-0000000000000000000000000000000000000000");

    private const string SyntheticAccountId = "synthetic-account-id-001";
    private const string SyntheticKeyValue  = "0012345678";

    private static IConfiguration CreateConfig(
        string? accountId = SyntheticAccountId,
        string? newKeyType = "BCODE", string? newKeyValue = SyntheticKeyValue,
        string? newKeyId = null, string? qrKeyId = null, string? qrSuspendedKeyId = null)
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

    private static PassportKeyResponse MatchingKey(string id, string status = "DELETED") => new()
    {
        Id = id,
        Status = status,
        AccountId = SyntheticAccountId,
        Key = new PassportKeyResponseDetail { KeyType = "BCODE", KeyValue = SyntheticKeyValue },
    };

    // ── Exactamente una llamada, ningún endpoint de mutación alcanzable ──

    [Fact]
    public async Task Recover_Success_CallsOnlyListKeysAsync_ExactlyOnce()
    {
        var client = new FakeKeyClient(listResponse: new PassportListKeysResponse
        {
            Keys = new List<PassportKeyResponse> { MatchingKey("remote-key-id-001") },
        });

        var result = await RecoverDeletedKeyIdExecutor.ExecuteAsync(CreateConfig(), client, CommitSha(), FixedTime);

        Assert.Equal(1, client.ListKeysCallCount);
        Assert.Equal(0, client.CreateKeyCallCount + client.SuspendKeyCallCount + client.ActivateKeyCallCount + client.DeleteKeyCallCount + client.ResolveKeyCallCount);
        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal("M4-T3-B-KEY-RECOVERY", result.Evidence!.CaseId);
        Assert.Equal("GET /v1/keys", result.Evidence.Operation);
    }

    [Fact]
    public async Task Recover_Success_UsesAccountIdKeyTypeKeyValue_AsSearchCriteria()
    {
        var client = new FakeKeyClient(listResponse: new PassportListKeysResponse
        {
            Keys = new List<PassportKeyResponse> { MatchingKey("remote-key-id-001") },
        });

        await RecoverDeletedKeyIdExecutor.ExecuteAsync(CreateConfig(), client, CommitSha(), FixedTime);

        Assert.Equal(SyntheticAccountId, client.LastListAccountId);
        Assert.Equal(PassportKeyType.BCODE, client.LastListKeyType);
        Assert.Equal(SyntheticKeyValue, client.LastListKeyValue);
    }

    // Regresión CRÍTICA: nunca lee PASSPORT_TEST_NEW_KEY_ID (el valor
    // contaminado que este comando existe para reemplazar), ni la llave
    // activa ni la suspendida — aunque estén presentes.
    [Fact]
    public async Task Recover_Success_NeverReadsNewKeyId_ActiveKey_OrSuspendedKey_EvenWhenAllPresent()
    {
        var client = new FakeKeyClient(listResponse: new PassportListKeysResponse
        {
            Keys = new List<PassportKeyResponse> { MatchingKey("remote-key-id-001") },
        });

        var result = await RecoverDeletedKeyIdExecutor.ExecuteAsync(
            CreateConfig(
                newKeyId: "synthetic-contaminated-new-key-id",
                qrKeyId: "synthetic-active-qr-key-id",
                qrSuspendedKeyId: "synthetic-suspended-key-id"),
            client, CommitSha(), FixedTime);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal(SyntheticAccountId, client.LastListAccountId);
        Assert.Equal(SyntheticKeyValue, client.LastListKeyValue);
    }

    // ── Selección fail-closed: match único / cero / ambiguo ─────────────

    [Fact]
    public async Task Recover_ExactlyOneMatch_EvidenceReportsMatchFound_StatusAndFingerprint()
    {
        var client = new FakeKeyClient(listResponse: new PassportListKeysResponse
        {
            Keys = new List<PassportKeyResponse> { MatchingKey("remote-key-id-001", status: "DELETED") },
        });

        var result = await RecoverDeletedKeyIdExecutor.ExecuteAsync(CreateConfig(), client, CommitSha(), FixedTime);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal(EvidenceRecord.ResultPass, result.Evidence!.Result);
        Assert.Equal(true, result.Evidence.ResponseSanitized["match_found"]);
        Assert.Equal(1, result.Evidence.ResponseSanitized["matches_count"]);
        Assert.Equal("DELETED", result.Evidence.ResponseSanitized["status"]);
        Assert.Equal("ExactlyOneMatch", result.Evidence.ResponseSanitized["selection_outcome"]);
        Assert.True(result.Evidence.ResponseSanitized.ContainsKey("id_fingerprint"));
    }

    [Fact]
    public async Task Recover_NoMatch_FailsClosed_ReportsMatchFoundFalse_NeverInventsAnything()
    {
        var client = new FakeKeyClient(listResponse: new PassportListKeysResponse
        {
            Keys = new List<PassportKeyResponse>(),
        });

        var result = await RecoverDeletedKeyIdExecutor.ExecuteAsync(CreateConfig(), client, CommitSha(), FixedTime);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome); // transporte OK — la búsqueda simplemente no encontró nada
        Assert.Equal(false, result.Evidence!.ResponseSanitized["match_found"]);
        Assert.Equal(0, result.Evidence.ResponseSanitized["matches_count"]);
        Assert.Equal("NoMatch", result.Evidence.ResponseSanitized["selection_outcome"]);
        Assert.False(result.Evidence.ResponseSanitized.ContainsKey("id_fingerprint"));
        Assert.False(result.Evidence.ResponseSanitized.ContainsKey("status"));
    }

    [Fact]
    public async Task Recover_MultipleMatches_FailsClosed_NeverSelectsFirst_NeverExposesCandidateDetails()
    {
        var client = new FakeKeyClient(listResponse: new PassportListKeysResponse
        {
            Keys = new List<PassportKeyResponse>
            {
                MatchingKey("remote-key-id-AAA", status: "DELETED"),
                MatchingKey("remote-key-id-BBB", status: "ACTIVE"),
            },
        });

        var result = await RecoverDeletedKeyIdExecutor.ExecuteAsync(CreateConfig(), client, CommitSha(), FixedTime);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal(false, result.Evidence!.ResponseSanitized["match_found"]);
        Assert.Equal("Ambiguous", result.Evidence.ResponseSanitized["selection_outcome"]);
        Assert.False(result.Evidence.ResponseSanitized.ContainsKey("id_fingerprint"));
        Assert.False(result.Evidence.ResponseSanitized.ContainsKey("status"));

        var evidenceJson = System.Text.Json.JsonSerializer.Serialize(result.Evidence);
        Assert.DoesNotContain("remote-key-id-AAA", evidenceJson);
        Assert.DoesNotContain("remote-key-id-BBB", evidenceJson);
    }

    // ── Bloqueo local / fail-closed de configuración ─────────────────────

    [Theory]
    [InlineData(null, "BCODE", SyntheticKeyValue)]
    [InlineData(SyntheticAccountId, null, SyntheticKeyValue)]
    [InlineData(SyntheticAccountId, "BCODE", null)]
    public async Task Recover_MissingAnyCriterion_IsLocalBlocked_NeverCallsClient(
        string? accountId, string? keyType, string? keyValue)
    {
        var client = new FakeKeyClient();

        var result = await RecoverDeletedKeyIdExecutor.ExecuteAsync(
            CreateConfig(accountId: accountId, newKeyType: keyType, newKeyValue: keyValue),
            client, CommitSha(), FixedTime);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.ListKeysCallCount);
        Assert.Null(result.Evidence);
    }

    [Fact]
    public async Task Recover_InvalidKeyType_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeKeyClient();

        var result = await RecoverDeletedKeyIdExecutor.ExecuteAsync(
            CreateConfig(newKeyType: "NOT-A-VALID-KEY-TYPE"), client, CommitSha(), FixedTime);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.ListKeysCallCount);
        Assert.Null(result.Evidence);
    }

    // ── Fallo real de Passport ────────────────────────────────────────────

    [Fact]
    public async Task Recover_PassportRejects_CallsOnlyOnce_ProducesFailEvidence()
    {
        var client = new FakeKeyClient(throwOnList: true);

        var result = await RecoverDeletedKeyIdExecutor.ExecuteAsync(CreateConfig(), client, CommitSha(), FixedTime);

        Assert.Equal(1, client.ListKeysCallCount);
        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        Assert.Equal("M4-T3-B-KEY-RECOVERY", result.Evidence!.CaseId);
        Assert.Equal(EvidenceRecord.ResultFail, result.Evidence.Result);
    }

    // ── Nunca expone valores crudos ───────────────────────────────────────

    [Fact]
    public async Task Recover_EvidenceNeverContainsRawIdentifiers()
    {
        const string realAccountId = "REAL-ACCOUNT-ID-must-never-appear-0000001";
        const string realKeyId     = "REAL-DELETED-KEY-ID-must-never-appear-0000002";
        const string realKeyValue  = "0099999999";
        var client = new FakeKeyClient(listResponse: new PassportListKeysResponse
        {
            Keys = new List<PassportKeyResponse>
            {
                new()
                {
                    Id = realKeyId, Status = "DELETED", AccountId = realAccountId,
                    Key = new PassportKeyResponseDetail { KeyType = "BCODE", KeyValue = realKeyValue },
                },
            },
        });

        var result = await RecoverDeletedKeyIdExecutor.ExecuteAsync(
            CreateConfig(accountId: realAccountId, newKeyValue: realKeyValue), client, CommitSha(), FixedTime);

        var evidenceJson = System.Text.Json.JsonSerializer.Serialize(result.Evidence);
        Assert.DoesNotContain(realAccountId, evidenceJson);
        Assert.DoesNotContain(realKeyId, evidenceJson);
        Assert.DoesNotContain(realKeyValue, evidenceJson);
        Assert.Contains("\"key_value\":\"REDACTED\"", evidenceJson);
        Assert.Contains("account_id_fingerprint", evidenceJson);
        Assert.Contains("id_fingerprint", evidenceJson);
    }

    [Fact]
    public void Operation_NeverInterpolatesAnyIdentifier()
    {
        Assert.Equal("GET /v1/keys", RecoverDeletedKeyIdEvidenceBuilder.Operation);
    }
}
