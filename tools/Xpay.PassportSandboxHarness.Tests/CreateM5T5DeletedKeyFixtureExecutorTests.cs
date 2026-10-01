using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-613 — prueba, SIN RED, la FASE A (PREP-CREATE) del fixture nuevo de
// M5-T5-E: el key_value se genera localmente (nunca de config), se
// persiste privadamente ANTES de HTTP (estado PREPARED_NOT_CREATED) y de
// nuevo, correlacionado con el key_id real, tras éxito (estado CREATED);
// invoca ÚNICAMENTE IPassportKeyClient.CreateKeyAsync (nunca Suspend/
// Activate/Delete/Resolve/ListKeys); y el repeat guard PERMANENTE impide
// una segunda ejecución real sin importar el resultado de la primera.
//
// Ningún test de este archivo abre un socket: el único colaborador remoto
// es FakeKeyClient, en memoria.
public class CreateM5T5DeletedKeyFixtureExecutorTests
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
                Id = "synthetic-deleted-fixture-key-id-001", Status = "ACTIVE",
                AccountId = request.AccountId,
            });
        }

        public Task<PassportKeyResponse> SuspendKeyAsync(string keyId, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("SuspendKeyAsync NUNCA debe invocarse en la Fase A."); }

        public Task<PassportKeyResponse> ActivateKeyAsync(string keyId, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("ActivateKeyAsync NUNCA debe invocarse en la Fase A."); }

        public Task DeleteKeyAsync(string keyId, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("DeleteKeyAsync NUNCA debe invocarse en la Fase A."); }

        public Task<PassportResolveKeyResponse> ResolveKeyAsync(PassportResolveKeyRequest request, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("ResolveKeyAsync NUNCA debe invocarse en la Fase A."); }

        public Task<PassportListKeysResponse> ListKeysAsync(string accountId, PassportKeyType keyType, string keyValue, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("ListKeysAsync NUNCA debe invocarse en la Fase A."); }
    }

    private static readonly DateTime FixedTime = new(2026, 1, 1, 0, 10, 0, DateTimeKind.Utc);

    private static readonly string SyntheticRepoRoot =
        Path.Combine(Path.GetTempPath(), "xpay-m5-t5-e-create-synthetic-repo-root-for-tests");

    private static string NewTempDir(string prefix) =>
        Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));

    private static ICommitShaProvider CommitSha() =>
        new FixedCommitShaProvider("synthetic-m5-t5-e-create-commit-sha-0000000000000000000000000000000");

    private const string SyntheticAccountId = "synthetic-m5-t5-e-account-id";
    private const string SyntheticActiveKeyValue = "synthetic-active-0268-key-value-never-used-here";

    private static IConfiguration CreateConfig(
        string? privateCaptureDir = "__AUTO_VALID__",
        string? accountId = SyntheticAccountId,
        string? activeKeyValue = SyntheticActiveKeyValue)
    {
        var dict = new Dictionary<string, string?>();

        var resolvedDir = privateCaptureDir == "__AUTO_VALID__" ? NewTempDir("xpay-m5-t5-e-private-capture") : privateCaptureDir;
        if (resolvedDir is not null) dict[M5PaymentPrivateCapture.EnvPrivateCaptureDir] = resolvedDir;

        if (accountId is not null) dict[HarnessTargetConfig.EnvAccountId] = accountId;
        if (activeKeyValue is not null) dict[HarnessTargetConfig.EnvBrebKeyValue] = activeKeyValue;

        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static Task<KeyOperationResult> Execute(IConfiguration config, FakeKeyClient client, DateTime? executedAtUtc = null) =>
        CreateM5T5DeletedKeyFixtureExecutor.ExecuteAsync(
            config, client, CommitSha(), executedAtUtc ?? FixedTime, automatedTestReference: null,
            repositoryRootOverride: SyntheticRepoRoot);

    // ══════════════════════════════════════════════════════════════════
    // CASO 1 — GUARDS ANTES DE HTTP
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

        var result = await CreateM5T5DeletedKeyFixtureExecutor.ExecuteAsync(
            config, client, new ThrowingCommitShaProvider(), FixedTime, repositoryRootOverride: SyntheticRepoRoot);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateKeyCallCount);
    }

    // ══════════════════════════════════════════════════════════════════
    // CASO 2 — GENERACIÓN LOCAL DEL KEY_VALUE, NUNCA REUTILIZACIÓN
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GeneratedKeyValue_NeverEqualsActiveKeyValue_AndIsBcodeShaped()
    {
        var config = CreateConfig();
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.NotNull(client.LastRequest);
        Assert.NotEqual(SyntheticActiveKeyValue, client.LastRequest!.Key.KeyValue);
        Assert.Matches("^00[0-9]{8}$", client.LastRequest.Key.KeyValue);
        Assert.Equal(SyntheticAccountId, client.LastRequest.AccountId);
        Assert.Equal(PassportKeyType.BCODE, client.LastRequest.Key.KeyType);
    }

    [Fact]
    public async Task GeneratedKeyValue_IsFreshOnEachFixture_NeverDerivedFromConfig()
    {
        // Dos directorios privados independientes => dos fixtures
        // independientes. Nunca el mismo key_value: no se deriva de ninguna
        // variable de entorno compartida.
        var firstClient = new FakeKeyClient();
        var secondClient = new FakeKeyClient();

        await Execute(CreateConfig(), firstClient);
        await Execute(CreateConfig(), secondClient);

        Assert.NotEqual(firstClient.LastRequest!.Key.KeyValue, secondClient.LastRequest!.Key.KeyValue);
    }

    // ══════════════════════════════════════════════════════════════════
    // CASO 3 — PERSISTENCIA PRIVADA: PREPARED ANTES DE HTTP, CREATED DESPUÉS
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SuccessfulExecution_WritesPreparedAndCreatedRecords_WithFullCorrelation()
    {
        var dir = NewTempDir("xpay-m5-t5-e-prepared-created");
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient(successResponse: new PassportKeyResponse
        {
            Id = "synthetic-deleted-fixture-key-id-XYZ", Status = "ACTIVE",
        });

        var result = await Execute(config, client);
        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);

        var fullDir = Path.GetFullPath(dir, SyntheticRepoRoot);
        var preparedFiles = Directory.GetFiles(fullDir, "M5-T5-DELETED-KEY-FIXTURE-PREPARED-*.private.json");
        var createdFiles = Directory.GetFiles(fullDir, "M5-T5-DELETED-KEY-FIXTURE-CREATED-*.private.json");
        Assert.Single(preparedFiles);
        Assert.Single(createdFiles);

        var preparedContent = File.ReadAllText(preparedFiles[0]);
        var createdContent = File.ReadAllText(createdFiles[0]);

        Assert.Contains(client.LastRequest!.Key.KeyValue, preparedContent);
        Assert.Contains(client.LastRequest.Key.KeyValue, createdContent);
        Assert.Contains("synthetic-deleted-fixture-key-id-XYZ", createdContent);
        Assert.Contains("PREPARED_NOT_CREATED", preparedContent);
        Assert.Contains("\"state\": \"CREATED\"", createdContent);
        Assert.Contains(CreateM5T5DeletedKeyFixtureExecutor.FixturePurpose, createdContent);
    }

    [Fact]
    public async Task PreparedRecord_IsWrittenEvenIfPassportCallFails()
    {
        // Misma garantía que XPAY-575 mantiene para M5-T5-D: el key_value
        // NUNCA depende únicamente de la respuesta HTTP para sobrevivir —
        // es exactamente lo que faltó en el fixture histórico de M4-T3-B.
        var dir = NewTempDir("xpay-m5-t5-e-prepared-survives-failure");
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient(exceptionToThrow: new PassportTransportException(
            "sintético", statusCode: 500, safeErrorCode: "internal_error", safeErrorMessage: "error sintético"));

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        var fullDir = Path.GetFullPath(dir, SyntheticRepoRoot);
        var preparedFiles = Directory.GetFiles(fullDir, "M5-T5-DELETED-KEY-FIXTURE-PREPARED-*.private.json");
        Assert.Single(preparedFiles);
        Assert.Contains(client.LastRequest!.Key.KeyValue, File.ReadAllText(preparedFiles[0]));
    }

    // ══════════════════════════════════════════════════════════════════
    // CASO 4 — SANEAMIENTO DE LA EVIDENCIA PÚBLICA
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PublicEvidence_NeverContainsRawKeyValueOrRawKeyId()
    {
        var config = CreateConfig();
        var client = new FakeKeyClient(successResponse: new PassportKeyResponse
        {
            Id = "synthetic-deleted-key-id-for-sanitize-check", Status = "ACTIVE",
        });

        var result = await Execute(config, client);

        var evidenceJson = System.Text.Json.JsonSerializer.Serialize(result.Evidence!.ResponseSanitized) +
                            System.Text.Json.JsonSerializer.Serialize(result.Evidence.RequestSanitized);
        Assert.DoesNotContain(client.LastRequest!.Key.KeyValue, evidenceJson);
        Assert.DoesNotContain("synthetic-deleted-key-id-for-sanitize-check", evidenceJson);
        Assert.DoesNotContain(SyntheticAccountId, evidenceJson);
        Assert.Equal("M5-T5-E-KEY-PREP-CREATE", result.Evidence.CaseId);
        Assert.Equal("NOT_YET_EXECUTED", result.Evidence.ResponseSanitized["certification_test_result"]);
        Assert.Equal("CREATED_NOT_DELETED", result.Evidence.ResponseSanitized["fixture_phase"]);
    }

    [Fact]
    public async Task PublicEvidence_NeverClaimsTheCertificationCaseRan()
    {
        var config = CreateConfig();
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        // El case_id de la Fase A JAMÁS es el del subcaso final.
        Assert.NotEqual(ResolveM5T5DeletedKeyEvidenceBuilder.CaseId, result.Evidence!.CaseId);
        Assert.Equal(0, result.Evidence.ResponseSanitized["delete_key_real_calls"]);
        Assert.Equal(0, result.Evidence.ResponseSanitized["resolve_real_calls"]);
        Assert.Equal(0, result.Evidence.ResponseSanitized["payment_real_calls"]);
    }

    // ══════════════════════════════════════════════════════════════════
    // CASO 5 — NUNCA OTRO MÉTODO DEL CLIENTE
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
    // CASO 6 — REPEAT GUARD PERMANENTE DE LA FASE A
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SecondExecution_AfterSuccess_IsLocalBlocked_NoSecondCall()
    {
        var dir = NewTempDir("xpay-m5-t5-e-repeat-after-success");
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
        var dir = NewTempDir("xpay-m5-t5-e-repeat-after-failure");
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

    [Fact]
    public async Task RepeatGuard_IsIndependentFromM5T5DFixture()
    {
        // Un artefacto del fixture de M5-T5-D en el MISMO directorio
        // compartido NUNCA bloquea la Fase A de M5-T5-E (globs disjuntos).
        var dir = NewTempDir("xpay-m5-t5-e-independent-from-d");
        var fullDir = Path.GetFullPath(dir, SyntheticRepoRoot);
        M5PaymentPrivateCapture.EnsureDirectory(fullDir);
        M5PaymentPrivateCapture.Write(
            fullDir, $"{CreateM5T5SuspendedKeyFixtureExecutor.FixtureCaseIdPrefix}-CREATED", "2026-01-01T00-05-00Z",
            new Dictionary<string, object?> { ["purpose"] = "M5-T5-D-SUSPENDED-KEY-FIXTURE", ["state"] = "CREATED" });

        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal(1, client.CreateKeyCallCount);
    }

    private sealed class ThrowingCommitShaProvider : ICommitShaProvider
    {
        public string GetCommitSha() => throw new InvalidOperationException("synthetic: commit SHA no resoluble.");
    }
}
