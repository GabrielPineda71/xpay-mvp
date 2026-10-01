using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-576 — prueba, a nivel de SuspendM5T5FixtureKeyExecutor (sin red),
// la FASE 2 (Suspend Key) del fixture nuevo de M5-T5-D: el key_id se lee
// EXCLUSIVAMENTE del artifact CREATED privado de XPAY-575 (nunca de
// config/env/CLI); invoca ÚNICAMENTE IPassportKeyClient.SuspendKeyAsync
// (nunca Create/Activate/Delete/Resolve/ListKeys); y el repeat guard
// PERMANENTE de esta Fase 2 impide una segunda Suspend real sin importar
// el resultado de la primera.
public class SuspendM5T5FixtureKeyExecutorTests
{
    private sealed class FakeKeyClient : IPassportKeyClient
    {
        private readonly PassportKeyResponse? _successResponse;
        private readonly Exception? _exceptionToThrow;

        public int SuspendCallCount { get; private set; }
        public int OtherMethodCallCount { get; private set; }
        public string? LastSuspendedKeyId { get; private set; }

        public FakeKeyClient(PassportKeyResponse? successResponse = null, Exception? exceptionToThrow = null)
        {
            _successResponse = successResponse;
            _exceptionToThrow = exceptionToThrow;
        }

        public Task<PassportKeyResponse> SuspendKeyAsync(string keyId, CancellationToken cancellationToken = default)
        {
            SuspendCallCount++;
            LastSuspendedKeyId = keyId;
            if (_exceptionToThrow is not null) throw _exceptionToThrow;
            return Task.FromResult(_successResponse ?? new PassportKeyResponse { Id = keyId, Status = "SUSPENDED" });
        }

        public Task<PassportKeyResponse> CreateKeyAsync(PassportCreateKeyRequest request, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("CreateKeyAsync NUNCA debe invocarse en la Fase 2."); }

        public Task<PassportKeyResponse> ActivateKeyAsync(string keyId, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("ActivateKeyAsync NUNCA debe invocarse en la Fase 2."); }

        public Task DeleteKeyAsync(string keyId, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("DeleteKeyAsync NUNCA debe invocarse en la Fase 2."); }

        public Task<PassportResolveKeyResponse> ResolveKeyAsync(PassportResolveKeyRequest request, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("ResolveKeyAsync NUNCA debe invocarse en la Fase 2."); }

        public Task<PassportListKeysResponse> ListKeysAsync(string accountId, PassportKeyType keyType, string keyValue, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("ListKeysAsync NUNCA debe invocarse en la Fase 2."); }
    }

    private static readonly DateTime FixedTime = new(2026, 1, 1, 0, 20, 0, DateTimeKind.Utc);

    private static readonly string SyntheticRepoRoot =
        Path.Combine(Path.GetTempPath(), "xpay-m5-t5-d-suspend-synthetic-repo-root-for-tests");

    private static string NewTempDir(string prefix) =>
        Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));

    private static ICommitShaProvider CommitSha() =>
        new FixedCommitShaProvider("synthetic-m5-t5-d-suspend-commit-sha-0000000000000000000000000000000");

    private const string SyntheticActiveKeyValue = "synthetic-active-0268-key-value-never-used-here";
    private const string SyntheticFixtureKeyValue = "0091234567";
    private const string SyntheticFixtureKeyId = "synthetic-fixture-key-id-from-xpay-575";

    private static IConfiguration CreateConfig(string? privateCaptureDir, string? activeKeyValue = SyntheticActiveKeyValue)
    {
        var dict = new Dictionary<string, string?>();
        if (privateCaptureDir is not null) dict[M5PaymentPrivateCapture.EnvPrivateCaptureDir] = privateCaptureDir;
        if (activeKeyValue is not null) dict[HarnessTargetConfig.EnvBrebKeyValue] = activeKeyValue;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static string PrepareCreatedArtifact(
        string dir, string keyId = SyntheticFixtureKeyId, string keyValue = SyntheticFixtureKeyValue,
        string state = "CREATED", string keyType = "BCODE", string timestampToken = "2026-01-01T00-15-00Z")
    {
        M5PaymentPrivateCapture.EnsureDirectory(dir);
        var content = new Dictionary<string, object?>
        {
            ["purpose"]              = "M5-T5-D-SUSPENDED-KEY-FIXTURE",
            ["state"]                 = state,
            ["key_type"]              = keyType,
            ["key_value"]             = keyValue,
            ["key_value_fingerprint"] = Fingerprint.Compute(keyValue),
            ["key_id"]                = keyId,
            ["key_id_fingerprint"]    = Fingerprint.Compute(keyId),
        };
        return M5PaymentPrivateCapture.Write(
            dir, $"{CreateM5T5SuspendedKeyFixtureExecutor.FixtureCaseIdPrefix}-CREATED", timestampToken, content);
    }

    private static Task<KeyOperationResult> Execute(IConfiguration config, FakeKeyClient client, DateTime? executedAtUtc = null) =>
        SuspendM5T5FixtureKeyExecutor.ExecuteAsync(
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
        Assert.Equal(0, client.SuspendCallCount);
        Assert.Null(result.Evidence);
    }

    [Fact]
    public async Task PrivateCaptureDirInsideRepository_LocalBlocked_NoHttp()
    {
        var config = CreateConfig(privateCaptureDir: SyntheticRepoRoot);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.SuspendCallCount);
    }

    [Fact]
    public async Task NoCreatedArtifact_LocalBlocked_NoHttp()
    {
        var dir = NewTempDir("xpay-m5-t5-d-suspend-no-artifact");
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.SuspendCallCount);
    }

    [Fact]
    public async Task MultipleCreatedArtifacts_LocalBlocked_Ambiguous_NoHttp()
    {
        var dir = NewTempDir("xpay-m5-t5-d-suspend-multiple");
        PrepareCreatedArtifact(dir, keyId: "id-1", keyValue: "0011111111", timestampToken: "2026-01-01T00-10-00Z");
        PrepareCreatedArtifact(dir, keyId: "id-2", keyValue: "0022222222", timestampToken: "2026-01-01T00-11-00Z");
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.SuspendCallCount);
        Assert.Contains("AMBIGUOUS", result.Detail);
    }

    [Fact]
    public async Task ArtifactStillPrepared_NotCreated_LocalBlocked_NoHttp()
    {
        var dir = NewTempDir("xpay-m5-t5-d-suspend-still-prepared");
        PrepareCreatedArtifact(dir, state: "PREPARED_NOT_CREATED");
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.SuspendCallCount);
    }

    [Fact]
    public async Task FixtureEqualsActiveKey_LocalBlocked_NoHttp()
    {
        var dir = NewTempDir("xpay-m5-t5-d-suspend-equals-active");
        PrepareCreatedArtifact(dir, keyValue: SyntheticActiveKeyValue);
        var config = CreateConfig(privateCaptureDir: dir, activeKeyValue: SyntheticActiveKeyValue);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.SuspendCallCount);
    }

    [Fact]
    public async Task CommitShaUnresolvable_IsLocalBlocked_NeverCallsClient()
    {
        var dir = NewTempDir("xpay-m5-t5-d-suspend-no-commit-sha");
        PrepareCreatedArtifact(dir);
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient();

        var result = await SuspendM5T5FixtureKeyExecutor.ExecuteAsync(
            config, client, new ThrowingCommitShaProvider(), FixedTime, repositoryRootOverride: SyntheticRepoRoot);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.SuspendCallCount);
    }

    // ══════════════════════════════════════════════════════════════════
    // ÉXITO — USA EXACTAMENTE EL KEY_ID DEL ARTIFACT XPAY-575
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ValidArtifact_Success_CallsSuspendExactlyOnce_WithCorrectKeyId_NeverOtherMethod()
    {
        var dir = NewTempDir("xpay-m5-t5-d-suspend-success");
        PrepareCreatedArtifact(dir);
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal(1, client.SuspendCallCount);
        Assert.Equal(0, client.OtherMethodCallCount);
        Assert.Equal(SyntheticFixtureKeyId, client.LastSuspendedKeyId);
        Assert.Equal("M5-T5-D-KEY-PREP-SUSPEND", result.Evidence!.CaseId);
        Assert.Equal(EvidenceRecord.ResultPass, result.Evidence.Result);
    }

    [Fact]
    public async Task PublicEvidence_NeverContainsRawKeyIdOrRawKeyValue()
    {
        var dir = NewTempDir("xpay-m5-t5-d-suspend-evidence-sanitized");
        PrepareCreatedArtifact(dir);
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        var json = System.Text.Json.JsonSerializer.Serialize(result.Evidence!.RequestSanitized) +
                   System.Text.Json.JsonSerializer.Serialize(result.Evidence.ResponseSanitized);
        Assert.DoesNotContain(SyntheticFixtureKeyId, json);
        Assert.DoesNotContain(SyntheticFixtureKeyValue, json);
        Assert.Equal("NOT_YET_EXECUTED", result.Evidence.ResponseSanitized["certification_test_result"]);
        Assert.Equal("SUSPENDED_NOT_TESTED", result.Evidence.ResponseSanitized["fixture_phase"]);
    }

    // ══════════════════════════════════════════════════════════════════
    // FALLO REAL DE PASSPORT
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PassportRejectsSuspend_ReturnsPassportFailure_WithSafeErrorFields()
    {
        var dir = NewTempDir("xpay-m5-t5-d-suspend-passport-failure");
        PrepareCreatedArtifact(dir);
        var config = CreateConfig(privateCaptureDir: dir);
        var exception = new PassportTransportException(
            "sintético", statusCode: 404, safeErrorCode: "not_found", safeErrorMessage: "key no encontrada (sintético)");
        var client = new FakeKeyClient(exceptionToThrow: exception);

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        Assert.Equal(1, client.SuspendCallCount);
        Assert.Equal(EvidenceRecord.ResultFail, result.Evidence!.Result);
        Assert.Equal("not_found", result.Evidence.ResponseSanitized["safe_error_code"]);
    }

    // ══════════════════════════════════════════════════════════════════
    // REPEAT GUARD PERMANENTE DE FASE 2
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SecondExecution_AfterSuccess_IsLocalBlocked_NoSecondCall()
    {
        var dir = NewTempDir("xpay-m5-t5-d-suspend-repeat-after-success");
        PrepareCreatedArtifact(dir);
        var config = CreateConfig(privateCaptureDir: dir);
        var firstClient = new FakeKeyClient();

        var first = await Execute(config, firstClient);
        Assert.Equal(KeyOperationOutcome.Success, first.Outcome);

        var secondClient = new FakeKeyClient();
        var second = await Execute(config, secondClient);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, second.Outcome);
        Assert.Equal(0, secondClient.SuspendCallCount);
        Assert.Contains("ALREADY_EXECUTED", second.Detail);
    }

    [Fact]
    public async Task SecondExecution_AfterFailure_IsLocalBlocked_NoSecondCall()
    {
        var dir = NewTempDir("xpay-m5-t5-d-suspend-repeat-after-failure");
        PrepareCreatedArtifact(dir);
        var config = CreateConfig(privateCaptureDir: dir);
        var firstClient = new FakeKeyClient(exceptionToThrow: new PassportTransportException(
            "sintético", statusCode: 500, safeErrorCode: "internal_error", safeErrorMessage: "error sintético"));

        var first = await Execute(config, firstClient);
        Assert.Equal(KeyOperationOutcome.PassportFailure, first.Outcome);

        var secondClient = new FakeKeyClient();
        var second = await Execute(config, secondClient);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, second.Outcome);
        Assert.Equal(0, secondClient.SuspendCallCount);
    }

    [Fact]
    public async Task RepeatGuardMarker_WrittenAsFixedFilename_NeverTimestamped()
    {
        var dir = NewTempDir("xpay-m5-t5-d-suspend-marker-filename");
        PrepareCreatedArtifact(dir);
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient();

        await Execute(config, client);

        var fullDir = Path.GetFullPath(dir, SyntheticRepoRoot);
        var expectedMarker = Path.Combine(fullDir, $"{SuspendM5T5FixtureKeyExecutor.SuspendGuardCaseId}-MARKER.private.json");
        Assert.True(File.Exists(expectedMarker));
    }

    [Fact]
    public async Task SuccessfulSuspend_NeverModifiesOrDeletesXpay575CreatedArtifact()
    {
        var dir = NewTempDir("xpay-m5-t5-d-suspend-preserves-source");
        var createdPath = PrepareCreatedArtifact(dir);
        var beforeContent = File.ReadAllText(createdPath);
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient();

        await Execute(config, client);

        Assert.True(File.Exists(createdPath));
        Assert.Equal(beforeContent, File.ReadAllText(createdPath));
    }

    // ══════════════════════════════════════════════════════════════════
    // DRY-RUN / CLI WIRING
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void DryRun_NeverReachesReadyToExecute_WithoutExecuteFlag()
    {
        var action = HarnessDecision.Decide(new[] { HarnessDecision.CommandSuspendM5T5FixtureKey }, out _);
        Assert.Equal(HarnessAction.DryRun, action);
    }

    [Fact]
    public void M3M4ConfirmFlags_NeverAuthorizeM5T5SuspendFixture()
    {
        var action = HarnessDecision.Decide(
            new[] { HarnessDecision.CommandSuspendM5T5FixtureKey, HarnessDecision.FlagExecute,
                    HarnessDecision.FlagConfirmSuspendKey },
            out var abortReason);
        Assert.Equal(HarnessAction.Aborted, action);
        Assert.NotNull(abortReason);
    }

    private sealed class ThrowingCommitShaProvider : ICommitShaProvider
    {
        public string GetCommitSha() => throw new InvalidOperationException("synthetic: commit SHA no resoluble.");
    }
}
