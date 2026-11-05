using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-577 — prueba, a nivel de ResolveM5T5SuspendedKeyExecutor (sin red),
// la FASE 3 (Resolve real) y ejecución final de M5-T5-D: el key_type/
// key_value se leen EXCLUSIVAMENTE del artifact CREATED privado de
// XPAY-575; se exige una correlación Create->Suspend válida con XPAY-576
// (mismos fingerprints, status SUSPENDED confirmado) ANTES de HTTP; invoca
// ÚNICAMENTE IPassportKeyClient.ResolveKeyAsync (nunca Create/Suspend/
// Activate/Delete/ListKeys, y estructuralmente nunca Payment/Retrieve —
// este executor no conoce IPassportPaymentClient); y el repeat guard
// PERMANENTE de esta Fase 3 impide una segunda ejecución real.
public class ResolveM5T5SuspendedKeyExecutorTests
{
    private sealed class FakeKeyClient : IPassportKeyClient
    {
        private readonly PassportResolveKeyResponse? _successResponse;
        private readonly Exception? _exceptionToThrow;

        public int ResolveCallCount { get; private set; }
        public int OtherMethodCallCount { get; private set; }
        public PassportResolveKeyRequest? LastRequest { get; private set; }

        public FakeKeyClient(PassportResolveKeyResponse? successResponse = null, Exception? exceptionToThrow = null)
        {
            _successResponse = successResponse;
            _exceptionToThrow = exceptionToThrow;
        }

        public Task<PassportResolveKeyResponse> ResolveKeyAsync(PassportResolveKeyRequest request, CancellationToken cancellationToken = default)
        {
            ResolveCallCount++;
            LastRequest = request;
            if (_exceptionToThrow is not null) throw _exceptionToThrow;
            return Task.FromResult(_successResponse ?? new PassportResolveKeyResponse
            {
                Id = "synthetic-unexpected-resolution-id", ResolvedAt = "2026-01-01T00:00:00Z", ExpiresAt = "2026-01-01T00:30:00Z",
            });
        }

        public Task<PassportKeyResponse> CreateKeyAsync(PassportCreateKeyRequest request, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("CreateKeyAsync NUNCA debe invocarse en la Fase 3."); }

        public Task<PassportKeyResponse> SuspendKeyAsync(string keyId, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("SuspendKeyAsync NUNCA debe invocarse en la Fase 3."); }

        public Task<PassportKeyResponse> ActivateKeyAsync(string keyId, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("ActivateKeyAsync NUNCA debe invocarse en la Fase 3."); }

        public Task DeleteKeyAsync(string keyId, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("DeleteKeyAsync NUNCA debe invocarse en la Fase 3."); }

        public Task<PassportListKeysResponse> ListKeysAsync(string accountId, PassportKeyType keyType, string keyValue, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("ListKeysAsync NUNCA debe invocarse en la Fase 3."); }
    }

    private static readonly DateTime FixedTime = new(2026, 1, 1, 0, 30, 0, DateTimeKind.Utc);

    private static readonly string SyntheticRepoRoot =
        Path.Combine(Path.GetTempPath(), "xpay-m5-t5-d-resolve-synthetic-repo-root-for-tests");

    private static string NewTempDir(string prefix) =>
        Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));

    private static ICommitShaProvider CommitSha() =>
        new FixedCommitShaProvider("synthetic-m5-t5-d-resolve-commit-sha-000000000000000000000000000000");

    private const string SyntheticActiveKeyValue = "synthetic-active-0268-key-value-never-used-here";
    private const string SyntheticFixtureKeyValue = "0091234567";
    private const string SyntheticFixtureKeyId = "synthetic-fixture-key-id-from-xpay-575";
    private const string SyntheticCustomerId = "synthetic-customer-id";
    private static readonly string FixtureKeyIdFingerprint = Fingerprint.Compute(SyntheticFixtureKeyId);
    private static readonly string FixtureKeyValueFingerprint = Fingerprint.Compute(SyntheticFixtureKeyValue);

    private static IConfiguration CreateConfig(
        string? privateCaptureDir, string? customerId = SyntheticCustomerId, string? activeKeyValue = SyntheticActiveKeyValue)
    {
        var dict = new Dictionary<string, string?>();
        if (privateCaptureDir is not null) dict[M5PaymentPrivateCapture.EnvPrivateCaptureDir] = privateCaptureDir;
        if (customerId is not null) dict[HarnessTargetConfig.EnvCustomerId] = customerId;
        if (activeKeyValue is not null) dict[HarnessTargetConfig.EnvBrebKeyValue] = activeKeyValue;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static void PrepareCreatedArtifact(
        string dir, string keyId = SyntheticFixtureKeyId, string keyValue = SyntheticFixtureKeyValue,
        string state = "CREATED", string keyType = "BCODE")
    {
        M5PaymentPrivateCapture.EnsureDirectory(dir);
        var content = new Dictionary<string, object?>
        {
            ["purpose"] = "M5-T5-D-SUSPENDED-KEY-FIXTURE", ["state"] = state, ["key_type"] = keyType,
            ["key_value"] = keyValue, ["key_value_fingerprint"] = Fingerprint.Compute(keyValue),
            ["key_id"] = keyId, ["key_id_fingerprint"] = Fingerprint.Compute(keyId),
        };
        M5PaymentPrivateCapture.Write(dir, $"{CreateM5T5SuspendedKeyFixtureExecutor.FixtureCaseIdPrefix}-CREATED", "2026-01-01T00-15-00Z", content);
    }

    private static void PrepareSuspendMarker(
        string dir, string? keyIdFingerprint = null, string? keyValueFingerprint = null,
        string outcome = "EXPECTED_SUSPEND_SUCCESS", string returnedStatus = "SUSPENDED")
    {
        M5PaymentPrivateCapture.EnsureDirectory(dir);
        var content = new Dictionary<string, object?>
        {
            ["case_id"] = SuspendM5T5FixtureKeyExecutor.SuspendGuardCaseId,
            ["key_id_fingerprint"] = keyIdFingerprint ?? FixtureKeyIdFingerprint,
            ["key_value_fingerprint"] = keyValueFingerprint ?? FixtureKeyValueFingerprint,
            ["outcome"] = outcome,
            ["returned_status"] = returnedStatus,
        };
        M5PaymentPrivateCapture.Write(dir, SuspendM5T5FixtureKeyExecutor.SuspendGuardCaseId, "MARKER", content);
    }

    private static void PrepareValidFixtureChain(string dir)
    {
        PrepareCreatedArtifact(dir);
        PrepareSuspendMarker(dir);
    }

    private static Task<KeyOperationResult> Execute(IConfiguration config, FakeKeyClient client, DateTime? executedAtUtc = null) =>
        ResolveM5T5SuspendedKeyExecutor.ExecuteAsync(
            config, client, CommitSha(), executedAtUtc ?? FixedTime, automatedTestReference: null,
            repositoryRootOverride: SyntheticRepoRoot);

    // ══════════════════════════════════════════════════════════════════
    // GUARDS ANTES DE HTTP
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MissingPrivateCaptureDir_LocalBlocked_NoHttp()
    {
        var config = CreateConfig(privateCaptureDir: null);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.ResolveCallCount);
    }

    [Fact]
    public async Task NoCreatedArtifact_LocalBlocked_NoHttp()
    {
        var dir = NewTempDir("xpay-m5-t5-d-resolve-no-artifact");
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.ResolveCallCount);
    }

    [Fact]
    public async Task CreatedArtifactExists_ButNoSuspendMarker_LocalBlocked_NoHttp()
    {
        var dir = NewTempDir("xpay-m5-t5-d-resolve-no-suspend-marker");
        PrepareCreatedArtifact(dir);
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.ResolveCallCount);
    }

    [Fact]
    public async Task SuspendMarker_FingerprintMismatch_LocalBlocked_NoHttp()
    {
        var dir = NewTempDir("xpay-m5-t5-d-resolve-mismatch");
        PrepareCreatedArtifact(dir);
        PrepareSuspendMarker(dir, keyIdFingerprint: "DIFFERENT0000"); // no corresponde al mismo fixture.
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.ResolveCallCount);
    }

    [Fact]
    public async Task SuspendMarker_ReturnedStatusNotSuspended_LocalBlocked_NoHttp()
    {
        var dir = NewTempDir("xpay-m5-t5-d-resolve-not-suspended");
        PrepareCreatedArtifact(dir);
        PrepareSuspendMarker(dir, returnedStatus: "ACTIVE"); // Suspend nunca confirmó SUSPENDED.
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.ResolveCallCount);
    }

    [Fact]
    public async Task SuspendMarker_OutcomeRejected_LocalBlocked_NoHttp()
    {
        var dir = NewTempDir("xpay-m5-t5-d-resolve-suspend-rejected");
        PrepareCreatedArtifact(dir);
        PrepareSuspendMarker(dir, outcome: "SUSPEND_REJECTED", returnedStatus: "SUSPENDED");
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.ResolveCallCount);
    }

    [Fact]
    public async Task MissingCustomerId_LocalBlocked_NoHttp()
    {
        var dir = NewTempDir("xpay-m5-t5-d-resolve-no-customer");
        PrepareValidFixtureChain(dir);
        var config = CreateConfig(privateCaptureDir: dir, customerId: null);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.ResolveCallCount);
    }

    [Fact]
    public async Task FixtureEqualsActiveKey_LocalBlocked_NoHttp()
    {
        var dir = NewTempDir("xpay-m5-t5-d-resolve-equals-active");
        PrepareCreatedArtifact(dir, keyValue: SyntheticActiveKeyValue);
        PrepareSuspendMarker(dir, keyValueFingerprint: Fingerprint.Compute(SyntheticActiveKeyValue));
        var config = CreateConfig(privateCaptureDir: dir, activeKeyValue: SyntheticActiveKeyValue);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.ResolveCallCount);
    }

    // ══════════════════════════════════════════════════════════════════
    // REQUEST — USA EXACTAMENTE EL FIXTURE CORRELACIONADO
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ValidChain_Success_CallsResolveExactlyOnce_WithFixtureKeyValue_NeverOtherMethod()
    {
        var dir = NewTempDir("xpay-m5-t5-d-resolve-success");
        PrepareValidFixtureChain(dir);
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient(exceptionToThrow: new PassportTransportException(
            "sintético", statusCode: 404, safeErrorCode: "not_found", safeErrorMessage: "key suspended (sintético)"));

        var result = await Execute(config, client);

        Assert.Equal(1, client.ResolveCallCount);
        Assert.Equal(0, client.OtherMethodCallCount);
        Assert.NotNull(client.LastRequest);
        Assert.Equal(SyntheticFixtureKeyValue, client.LastRequest!.Key.KeyValue);
        Assert.Equal(PassportKeyType.BCODE, client.LastRequest.Key.KeyType);
        Assert.Equal(SyntheticCustomerId, client.LastRequest.CustomerId);
        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        Assert.Equal("M5-T5-SUSPENDED-KEY", result.Evidence!.CaseId);
        Assert.Equal(EvidenceRecord.ResultFail, result.Evidence.Result);
    }

    [Fact]
    public async Task ExpectedRejection_MentioningSuspend_ConfirmsCurrentSuspension()
    {
        var dir = NewTempDir("xpay-m5-t5-d-resolve-suspension-signal");
        PrepareValidFixtureChain(dir);
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient(exceptionToThrow: new PassportTransportException(
            "sintético", statusCode: 400, safeErrorCode: "key_suspended", safeErrorMessage: "the key is suspended"));

        var result = await Execute(config, client);

        Assert.Equal(true, result.Evidence!.ResponseSanitized["current_suspension_confirmed"]);
    }

    [Fact]
    public async Task ExpectedRejection_GenericError_LeavesSuspensionUnknown()
    {
        var dir = NewTempDir("xpay-m5-t5-d-resolve-generic-error");
        PrepareValidFixtureChain(dir);
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient(exceptionToThrow: new PassportTransportException(
            "sintético", statusCode: 404, safeErrorCode: "not_found", safeErrorMessage: "resource not found"));

        var result = await Execute(config, client);

        Assert.Null(result.Evidence!.ResponseSanitized["current_suspension_confirmed"]);
    }

    // ══════════════════════════════════════════════════════════════════
    // RESULTADO INESPERADO — PASSPORT RESUELVE
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UnexpectedSuccess_ReturnsSuccess_MarkedUnexpected_NeverContainsRawResolutionId()
    {
        var dir = NewTempDir("xpay-m5-t5-d-resolve-unexpected-success");
        PrepareValidFixtureChain(dir);
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient(successResponse: new PassportResolveKeyResponse { Id = "synthetic-unexpected-resolution-id" });

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal(EvidenceRecord.ResultPass, result.Evidence!.Result);
        Assert.Equal("UNEXPECTED_RESOLVE_SUCCESS", result.Evidence.ResponseSanitized["certification_case_result"]);
        var json = System.Text.Json.JsonSerializer.Serialize(result.Evidence.ResponseSanitized);
        Assert.DoesNotContain("synthetic-unexpected-resolution-id", json);
        Assert.Contains("UNEXPECTED_RESOLVE_SUCCESS", result.Evidence.Notes);
    }

    // ══════════════════════════════════════════════════════════════════
    // SANEAMIENTO
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PublicEvidence_NeverContainsRawKeyIdKeyValueOrCustomerId()
    {
        var dir = NewTempDir("xpay-m5-t5-d-resolve-sanitize");
        PrepareValidFixtureChain(dir);
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient(exceptionToThrow: new PassportTransportException(
            "sintético", statusCode: 400, safeErrorCode: "invalid", safeErrorMessage: "invalid key"));

        var result = await Execute(config, client);

        var json = System.Text.Json.JsonSerializer.Serialize(result.Evidence!.RequestSanitized) +
                   System.Text.Json.JsonSerializer.Serialize(result.Evidence.ResponseSanitized);
        Assert.DoesNotContain(SyntheticFixtureKeyId, json);
        Assert.DoesNotContain(SyntheticFixtureKeyValue, json);
        Assert.DoesNotContain(SyntheticCustomerId, json);
    }

    // ══════════════════════════════════════════════════════════════════
    // REPEAT GUARD PERMANENTE DE FASE 3
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SecondExecution_AfterExpectedRejection_IsLocalBlocked_NoSecondCall()
    {
        var dir = NewTempDir("xpay-m5-t5-d-resolve-repeat-after-rejection");
        PrepareValidFixtureChain(dir);
        var config = CreateConfig(privateCaptureDir: dir);
        var firstClient = new FakeKeyClient(exceptionToThrow: new PassportTransportException(
            "sintético", statusCode: 404, safeErrorCode: "not_found", safeErrorMessage: "not found"));

        var first = await Execute(config, firstClient);
        Assert.Equal(KeyOperationOutcome.PassportFailure, first.Outcome);

        var secondClient = new FakeKeyClient();
        var second = await Execute(config, secondClient);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, second.Outcome);
        Assert.Equal(0, secondClient.ResolveCallCount);
        Assert.Contains("ALREADY_EXECUTED", second.Detail);
    }

    [Fact]
    public async Task SecondExecution_AfterUnexpectedSuccess_IsLocalBlocked_NoSecondCall()
    {
        var dir = NewTempDir("xpay-m5-t5-d-resolve-repeat-after-success");
        PrepareValidFixtureChain(dir);
        var config = CreateConfig(privateCaptureDir: dir);
        var firstClient = new FakeKeyClient();

        var first = await Execute(config, firstClient);
        Assert.Equal(KeyOperationOutcome.Success, first.Outcome);

        var secondClient = new FakeKeyClient();
        var second = await Execute(config, secondClient);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, second.Outcome);
        Assert.Equal(0, secondClient.ResolveCallCount);
    }

    [Fact]
    public async Task RepeatGuardMarker_WrittenAsFixedFilename()
    {
        var dir = NewTempDir("xpay-m5-t5-d-resolve-marker-filename");
        PrepareValidFixtureChain(dir);
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient(exceptionToThrow: new PassportTransportException(
            "sintético", statusCode: 404, safeErrorCode: "not_found", safeErrorMessage: "not found"));

        await Execute(config, client);

        var fullDir = Path.GetFullPath(dir, SyntheticRepoRoot);
        var expectedMarker = Path.Combine(fullDir, $"{ResolveM5T5SuspendedKeyExecutor.ResolveGuardCaseId}-MARKER.private.json");
        Assert.True(File.Exists(expectedMarker));
    }

    // ══════════════════════════════════════════════════════════════════
    // CLASIFICACIÓN DE SEÑAL DE SUSPENSIÓN (unidad pura)
    // ══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("key_suspended", "the key is suspended", true)]
    [InlineData("SUSPENDED_KEY", null, true)]
    [InlineData("not_found", "resource not found", null)]
    [InlineData(null, null, null)]
    public void ClassifySuspensionSignal_OnlyConfirmsOnExplicitMention(string? code, string? message, bool? expected)
    {
        Assert.Equal(expected, ResolveM5T5SuspendedKeyExecutor.ClassifySuspensionSignal(code, message));
    }

    // ══════════════════════════════════════════════════════════════════
    // DRY-RUN / CLI WIRING
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void DryRun_NeverReachesReadyToExecute_WithoutExecuteFlag()
    {
        var action = HarnessDecision.Decide(new[] { HarnessDecision.CommandResolveM5T5SuspendedKey }, out _);
        Assert.Equal(HarnessAction.DryRun, action);
    }

    [Fact]
    public void M3ConfirmFlag_NeverAuthorizesM5T5ResolveSuspended()
    {
        var action = HarnessDecision.Decide(
            new[] { HarnessDecision.CommandResolveM5T5SuspendedKey, HarnessDecision.FlagExecute,
                    HarnessDecision.FlagConfirmResolveKey },
            out var abortReason);
        Assert.Equal(HarnessAction.Aborted, action);
        Assert.NotNull(abortReason);
    }
}
