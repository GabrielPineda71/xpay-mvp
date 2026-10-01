using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-575 — prueba, a nivel de CreateM5T5SuspendedKeyFixtureExecutor (sin
// red), la FASE 1 (Create Key) del fixture nuevo de M5-T5-D: el key_value
// se genera localmente (nunca de config), se persiste privadamente ANTES
// de HTTP (estado PREPARED_NOT_CREATED) y de nuevo, correlacionado con el
// key_id real, tras éxito (estado CREATED); invoca ÚNICAMENTE
// IPassportKeyClient.CreateKeyAsync (nunca Suspend/Activate/Delete/
// Resolve/ListKeys); y el repeat guard PERMANENTE impide una segunda
// ejecución real sin importar el resultado de la primera.
public class CreateM5T5SuspendedKeyFixtureExecutorTests
{
    private sealed class FakeKeyClient : IPassportKeyClient
    {
        private readonly PassportKeyResponse? _successResponse;
        private readonly Exception? _exceptionToThrow;

        public int CreateKeyCallCount { get; private set; }
        public int OtherMethodCallCount { get; private set; }
        public PassportCreateKeyRequest? LastRequest { get; private set; }

        public FakeKeyClient(PassportKeyResponse? successResponse = null, Exception? exceptionToThrow = null)
        {
            _successResponse = successResponse;
            _exceptionToThrow = exceptionToThrow;
        }

        public Task<PassportKeyResponse> CreateKeyAsync(PassportCreateKeyRequest request, CancellationToken cancellationToken = default)
        {
            CreateKeyCallCount++;
            LastRequest = request;
            if (_exceptionToThrow is not null) throw _exceptionToThrow;
            return Task.FromResult(_successResponse ?? new PassportKeyResponse
            {
                Id = "synthetic-fixture-key-id-001", Status = "ACTIVE",
                AccountId = request.AccountId,
            });
        }

        public Task<PassportKeyResponse> SuspendKeyAsync(string keyId, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("SuspendKeyAsync NUNCA debe invocarse en la Fase 1."); }

        public Task<PassportKeyResponse> ActivateKeyAsync(string keyId, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("ActivateKeyAsync NUNCA debe invocarse en la Fase 1."); }

        public Task DeleteKeyAsync(string keyId, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("DeleteKeyAsync NUNCA debe invocarse en la Fase 1."); }

        public Task<PassportResolveKeyResponse> ResolveKeyAsync(PassportResolveKeyRequest request, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("ResolveKeyAsync NUNCA debe invocarse en la Fase 1."); }

        public Task<PassportListKeysResponse> ListKeysAsync(string accountId, PassportKeyType keyType, string keyValue, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("ListKeysAsync NUNCA debe invocarse en la Fase 1."); }
    }

    private static readonly DateTime FixedTime = new(2026, 1, 1, 0, 10, 0, DateTimeKind.Utc);

    private static readonly string SyntheticRepoRoot =
        Path.Combine(Path.GetTempPath(), "xpay-m5-t5-d-synthetic-repo-root-for-tests");

    private static string NewTempDir(string prefix) =>
        Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));

    private static ICommitShaProvider CommitSha() =>
        new FixedCommitShaProvider("synthetic-m5-t5-d-commit-sha-00000000000000000000000000000000000");

    private const string SyntheticAccountId = "synthetic-m5-t5-d-account-id";
    private const string SyntheticActiveKeyValue = "synthetic-active-0268-key-value-never-used-here";

    private static IConfiguration CreateConfig(
        string? privateCaptureDir = "__AUTO_VALID__",
        string? accountId = SyntheticAccountId,
        string? activeKeyValue = SyntheticActiveKeyValue)
    {
        var dict = new Dictionary<string, string?>();

        var resolvedDir = privateCaptureDir == "__AUTO_VALID__" ? NewTempDir("xpay-m5-t5-d-private-capture") : privateCaptureDir;
        if (resolvedDir is not null) dict[M5PaymentPrivateCapture.EnvPrivateCaptureDir] = resolvedDir;

        if (accountId is not null) dict[HarnessTargetConfig.EnvAccountId] = accountId;
        if (activeKeyValue is not null) dict[HarnessTargetConfig.EnvBrebKeyValue] = activeKeyValue;

        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static Task<KeyOperationResult> Execute(IConfiguration config, FakeKeyClient client, DateTime? executedAtUtc = null) =>
        CreateM5T5SuspendedKeyFixtureExecutor.ExecuteAsync(
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
        Assert.Equal(0, client.CreateKeyCallCount);
        Assert.Null(result.Evidence);
    }

    [Fact]
    public async Task PrivateCaptureDirInsideRepository_LocalBlocked_NoHttp()
    {
        var config = CreateConfig(privateCaptureDir: SyntheticRepoRoot);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateKeyCallCount);
    }

    [Fact]
    public async Task MissingAccountId_LocalBlocked_NoHttp()
    {
        var config = CreateConfig(accountId: null);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateKeyCallCount);
    }

    [Fact]
    public async Task CommitShaUnresolvable_IsLocalBlocked_NeverCallsClient()
    {
        var config = CreateConfig();
        var client = new FakeKeyClient();

        var result = await CreateM5T5SuspendedKeyFixtureExecutor.ExecuteAsync(
            config, client, new ThrowingCommitShaProvider(), FixedTime, repositoryRootOverride: SyntheticRepoRoot);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateKeyCallCount);
    }

    // ══════════════════════════════════════════════════════════════════
    // GENERACIÓN LOCAL DEL KEY_VALUE
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GeneratedKeyValue_NeverEqualsActiveKeyValue_AndIsBcodeShaped()
    {
        var dir = NewTempDir("xpay-m5-t5-d-generated-value");
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.NotNull(client.LastRequest);
        Assert.NotEqual(SyntheticActiveKeyValue, client.LastRequest!.Key.KeyValue);
        Assert.Matches("^00[0-9]{8}$", client.LastRequest.Key.KeyValue);
        Assert.Equal(SyntheticAccountId, client.LastRequest.AccountId);
    }

    // ══════════════════════════════════════════════════════════════════
    // PERSISTENCIA PRIVADA — PREPARED ANTES DE HTTP, CREATED DESPUÉS
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SuccessfulExecution_WritesPreparedAndCreatedRecords_WithFullCorrelation()
    {
        var dir = NewTempDir("xpay-m5-t5-d-prepared-created");
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient(successResponse: new PassportKeyResponse
        {
            Id = "synthetic-fixture-key-id-XYZ", Status = "ACTIVE",
        });

        var result = await Execute(config, client);
        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);

        var fullDir = Path.GetFullPath(dir, SyntheticRepoRoot);
        var preparedFiles = Directory.GetFiles(fullDir, "M5-T5-SUSPENDED-KEY-FIXTURE-PREPARED-*.private.json");
        var createdFiles = Directory.GetFiles(fullDir, "M5-T5-SUSPENDED-KEY-FIXTURE-CREATED-*.private.json");
        Assert.Single(preparedFiles);
        Assert.Single(createdFiles);

        var preparedContent = File.ReadAllText(preparedFiles[0]);
        var createdContent = File.ReadAllText(createdFiles[0]);

        Assert.Contains(client.LastRequest!.Key.KeyValue, preparedContent);
        Assert.Contains(client.LastRequest.Key.KeyValue, createdContent);
        Assert.Contains("synthetic-fixture-key-id-XYZ", createdContent);
        Assert.Contains("PREPARED_NOT_CREATED", preparedContent);
        Assert.Contains("\"state\": \"CREATED\"", createdContent);
    }

    [Fact]
    public async Task PreparedRecord_IsWrittenEvenIfPassportCallFails()
    {
        // Garantiza la corrección de la causa raíz de XPAY-574: el
        // key_value NUNCA depende únicamente de la respuesta HTTP para
        // sobrevivir.
        var dir = NewTempDir("xpay-m5-t5-d-prepared-survives-failure");
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient(exceptionToThrow: new PassportTransportException(
            "sintético", statusCode: 500, safeErrorCode: "internal_error", safeErrorMessage: "error sintético"));

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        var fullDir = Path.GetFullPath(dir, SyntheticRepoRoot);
        var preparedFiles = Directory.GetFiles(fullDir, "M5-T5-SUSPENDED-KEY-FIXTURE-PREPARED-*.private.json");
        Assert.Single(preparedFiles);
        Assert.Contains(client.LastRequest!.Key.KeyValue, File.ReadAllText(preparedFiles[0]));
    }

    // ══════════════════════════════════════════════════════════════════
    // SANEAMIENTO DE LA EVIDENCIA PÚBLICA
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PublicEvidence_NeverContainsRawKeyValueOrRawKeyId()
    {
        var dir = NewTempDir("xpay-m5-t5-d-evidence-sanitized");
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient(successResponse: new PassportKeyResponse
        {
            Id = "synthetic-fixture-key-id-for-sanitize-check", Status = "ACTIVE",
        });

        var result = await Execute(config, client);

        var evidenceJson = System.Text.Json.JsonSerializer.Serialize(result.Evidence!.ResponseSanitized) +
                            System.Text.Json.JsonSerializer.Serialize(result.Evidence.RequestSanitized);
        Assert.DoesNotContain(client.LastRequest!.Key.KeyValue, evidenceJson);
        Assert.DoesNotContain("synthetic-fixture-key-id-for-sanitize-check", evidenceJson);
        Assert.Equal("M5-T5-D-KEY-PREP-CREATE", result.Evidence.CaseId);
        Assert.Equal("NOT_YET_EXECUTED", result.Evidence.ResponseSanitized["certification_test_result"]);
        Assert.Equal("CREATED_NOT_SUSPENDED", result.Evidence.ResponseSanitized["fixture_phase"]);
    }

    // ══════════════════════════════════════════════════════════════════
    // NUNCA OTRO MÉTODO DEL CLIENTE
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SuccessfulExecution_CallsCreateKeyExactlyOnce_NeverAnyOtherMethod()
    {
        var config = CreateConfig();
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal(1, client.CreateKeyCallCount);
        Assert.Equal(0, client.OtherMethodCallCount);
    }

    // ══════════════════════════════════════════════════════════════════
    // REPEAT GUARD PERMANENTE
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SecondExecution_AfterSuccess_IsLocalBlocked_NoSecondCall()
    {
        var dir = NewTempDir("xpay-m5-t5-d-repeat-after-success");
        var config = CreateConfig(privateCaptureDir: dir);
        var firstClient = new FakeKeyClient();

        var first = await Execute(config, firstClient);
        Assert.Equal(KeyOperationOutcome.Success, first.Outcome);

        var secondClient = new FakeKeyClient();
        var second = await Execute(config, secondClient);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, second.Outcome);
        Assert.Equal(0, secondClient.CreateKeyCallCount);
        Assert.Contains("ALREADY_EXECUTED", second.Detail);
    }

    [Fact]
    public async Task SecondExecution_AfterFailure_IsLocalBlocked_NoSecondCall()
    {
        var dir = NewTempDir("xpay-m5-t5-d-repeat-after-failure");
        var config = CreateConfig(privateCaptureDir: dir);
        var firstClient = new FakeKeyClient(exceptionToThrow: new PassportTransportException(
            "sintético", statusCode: 500, safeErrorCode: "internal_error", safeErrorMessage: "error sintético"));

        var first = await Execute(config, firstClient);
        Assert.Equal(KeyOperationOutcome.PassportFailure, first.Outcome);

        var secondClient = new FakeKeyClient();
        var second = await Execute(config, secondClient);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, second.Outcome);
        Assert.Equal(0, secondClient.CreateKeyCallCount);
    }

    // ══════════════════════════════════════════════════════════════════
    // DRY-RUN / CLI WIRING
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void DryRun_NeverReachesReadyToExecute_WithoutExecuteFlag()
    {
        var action = HarnessDecision.Decide(new[] { HarnessDecision.CommandCreateM5T5SuspendedKeyFixture }, out _);
        Assert.Equal(HarnessAction.DryRun, action);
    }

    [Fact]
    public void M4ConfirmFlag_NeverAuthorizesM5T5SuspendedFixture()
    {
        var action = HarnessDecision.Decide(
            new[] { HarnessDecision.CommandCreateM5T5SuspendedKeyFixture, HarnessDecision.FlagExecute,
                    HarnessDecision.FlagConfirmCreateM4T3SuspendedFixtureKey },
            out var abortReason);
        Assert.Equal(HarnessAction.Aborted, action);
        Assert.NotNull(abortReason);
    }

    private sealed class ThrowingCommitShaProvider : ICommitShaProvider
    {
        public string GetCommitSha() => throw new InvalidOperationException("synthetic: commit SHA no resoluble.");
    }
}
