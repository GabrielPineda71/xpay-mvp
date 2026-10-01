using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-613 — prueba, SIN RED, la FASE B (PREP-DELETE) del fixture nuevo de
// M5-T5-E: el key_id se lee EXCLUSIVAMENTE del artifact CREATED privado de
// la Fase A (nunca de env, nunca de CLI, nunca hardcodeado); invoca
// ÚNICAMENTE IPassportKeyClient.DeleteKeyAsync (nunca Create/Suspend/
// Activate/Resolve/ListKeys); el marcador de consumo registra
// delete_acknowledged=true SÓLO si Passport completó sin error; y el
// repeat guard PERMANENTE impide un segundo Delete real — crítico porque
// la operación es irreversible.
public class DeleteM5T5FixtureKeyExecutorTests
{
    private sealed class FakeKeyClient : IPassportKeyClient
    {
        private readonly Exception? _exceptionToThrow;

        public int DeleteKeyCallCount { get; private set; }
        public int OtherMethodCallCount { get; private set; }
        public string? LastDeletedKeyId { get; private set; }

        public FakeKeyClient(Exception? exceptionToThrow = null) => _exceptionToThrow = exceptionToThrow;

        public Task DeleteKeyAsync(string keyId, CancellationToken cancellationToken = default)
        {
            DeleteKeyCallCount++;
            LastDeletedKeyId = keyId;
            if (_exceptionToThrow is not null) throw _exceptionToThrow;
            return Task.CompletedTask; // 204 No Content, sin body.
        }

        public Task<PassportKeyResponse> CreateKeyAsync(PassportCreateKeyRequest request, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("CreateKeyAsync NUNCA debe invocarse en la Fase B."); }

        public Task<PassportKeyResponse> SuspendKeyAsync(string keyId, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("SuspendKeyAsync NUNCA debe invocarse en la Fase B."); }

        public Task<PassportKeyResponse> ActivateKeyAsync(string keyId, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("ActivateKeyAsync NUNCA debe invocarse en la Fase B."); }

        public Task<PassportResolveKeyResponse> ResolveKeyAsync(PassportResolveKeyRequest request, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("ResolveKeyAsync NUNCA debe invocarse en la Fase B."); }

        public Task<PassportListKeysResponse> ListKeysAsync(string accountId, PassportKeyType keyType, string keyValue, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("ListKeysAsync NUNCA debe invocarse en la Fase B."); }
    }

    private static readonly DateTime FixedTime = new(2026, 1, 1, 0, 20, 0, DateTimeKind.Utc);

    private static readonly string SyntheticRepoRoot =
        Path.Combine(Path.GetTempPath(), "xpay-m5-t5-e-delete-synthetic-repo-root-for-tests");

    private static string NewTempDir(string prefix) =>
        Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));

    private static ICommitShaProvider CommitSha() =>
        new FixedCommitShaProvider("synthetic-m5-t5-e-delete-commit-sha-0000000000000000000000000000000");

    private const string SyntheticActiveKeyValue = "synthetic-active-0268-key-value-never-used-here";
    private const string SyntheticFixtureKeyValue = "0091234567";
    private const string SyntheticFixtureKeyId = "synthetic-fixture-key-id-from-m5-t5-e-phase-a";

    private static IConfiguration CreateConfig(
        string? privateCaptureDir, string? activeKeyValue = SyntheticActiveKeyValue)
    {
        var dict = new Dictionary<string, string?>();
        if (privateCaptureDir is not null) dict[M5PaymentPrivateCapture.EnvPrivateCaptureDir] = privateCaptureDir;
        if (activeKeyValue is not null) dict[HarnessTargetConfig.EnvBrebKeyValue] = activeKeyValue;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static void PrepareCreatedArtifact(
        string fullDir, string keyId = SyntheticFixtureKeyId, string keyValue = SyntheticFixtureKeyValue,
        string state = "CREATED", string keyType = "BCODE", string? purpose = null,
        string timestampToken = "2026-01-01T00-15-00Z")
    {
        M5PaymentPrivateCapture.EnsureDirectory(fullDir);
        var content = new Dictionary<string, object?>
        {
            ["purpose"] = purpose ?? CreateM5T5DeletedKeyFixtureExecutor.FixturePurpose,
            ["state"] = state, ["key_type"] = keyType,
            ["key_value"] = keyValue, ["key_value_fingerprint"] = Fingerprint.Compute(keyValue),
            ["key_id"] = keyId, ["key_id_fingerprint"] = Fingerprint.Compute(keyId),
        };
        M5PaymentPrivateCapture.Write(
            fullDir, $"{CreateM5T5DeletedKeyFixtureExecutor.FixtureCaseIdPrefix}-CREATED", timestampToken, content);
    }

    private static Task<KeyOperationResult> Execute(IConfiguration config, FakeKeyClient client) =>
        DeleteM5T5FixtureKeyExecutor.ExecuteAsync(
            config, client, CommitSha(), FixedTime, automatedTestReference: null,
            repositoryRootOverride: SyntheticRepoRoot);

    private static string FullDirFor(string dir) => Path.GetFullPath(dir, SyntheticRepoRoot);

    private static string MarkerPath(string fullDir) =>
        Path.Combine(fullDir, $"{DeleteM5T5FixtureKeyExecutor.DeleteGuardCaseId}-MARKER.private.json");

    // ══════════════════════════════════════════════════════════════════
    // CASO 7 — GUARDS ANTES DE HTTP
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MissingPrivateCaptureDir_LocalBlocked_NoHttp()
    {
        var client = new FakeKeyClient();

        var result = await Execute(CreateConfig(privateCaptureDir: null), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.DeleteKeyCallCount);
        Assert.Null(result.Evidence);
    }

    [Fact]
    public async Task NoCreatedArtifact_LocalBlocked_NoHttp()
    {
        var dir = NewTempDir("xpay-m5-t5-e-delete-no-artifact");
        M5PaymentPrivateCapture.EnsureDirectory(FullDirFor(dir));
        var client = new FakeKeyClient();

        var result = await Execute(CreateConfig(dir), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.DeleteKeyCallCount);
    }

    [Fact]
    public async Task MultipleCreatedArtifacts_LocalBlockedAmbiguous_NoHttp()
    {
        var dir = NewTempDir("xpay-m5-t5-e-delete-ambiguous");
        var fullDir = FullDirFor(dir);
        PrepareCreatedArtifact(fullDir, timestampToken: "2026-01-01T00-15-00Z");
        PrepareCreatedArtifact(fullDir, keyId: "otro-key-id", keyValue: "0098765432", timestampToken: "2026-01-01T00-16-00Z");
        var client = new FakeKeyClient();

        var result = await Execute(CreateConfig(dir), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Contains("AMBIGUOUS", result.Detail);
        Assert.Equal(0, client.DeleteKeyCallCount);
    }

    [Fact]
    public async Task PreparedButNotCreatedArtifact_LocalBlocked_NoHttp()
    {
        var dir = NewTempDir("xpay-m5-t5-e-delete-prepared-only");
        PrepareCreatedArtifact(FullDirFor(dir), state: "PREPARED_NOT_CREATED");
        var client = new FakeKeyClient();

        var result = await Execute(CreateConfig(dir), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.DeleteKeyCallCount);
    }

    // ══════════════════════════════════════════════════════════════════
    // CASO 8 — AISLAMIENTO RESPECTO A M5-T5-D Y A FIXTURES HISTÓRICOS
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ArtifactWithM5T5DPurpose_NeverSatisfiesPhaseB_NoHttp()
    {
        // Un artifact que, por un error de operación, llevara el purpose de
        // M5-T5-D nunca autoriza un Delete de M5-T5-E: el fixture de D debe
        // permanecer SUSPENDIDO, no eliminado (su evidencia ya fue generada).
        var dir = NewTempDir("xpay-m5-t5-e-delete-wrong-purpose");
        PrepareCreatedArtifact(FullDirFor(dir), purpose: "M5-T5-D-SUSPENDED-KEY-FIXTURE");
        var client = new FakeKeyClient();

        var result = await Execute(CreateConfig(dir), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.DeleteKeyCallCount);
    }

    [Fact]
    public async Task SuspendedFixtureArtifactOfM5T5D_IsInvisibleToPhaseB_NoHttp()
    {
        // El artefacto de M5-T5-D vive en el MISMO directorio compartido y
        // bajo su propio prefijo: la Fase B de E no lo ve en absoluto.
        var dir = NewTempDir("xpay-m5-t5-e-delete-d-artifact-only");
        var fullDir = FullDirFor(dir);
        M5PaymentPrivateCapture.EnsureDirectory(fullDir);
        M5PaymentPrivateCapture.Write(
            fullDir, $"{CreateM5T5SuspendedKeyFixtureExecutor.FixtureCaseIdPrefix}-CREATED", "2026-01-01T00-15-00Z",
            new Dictionary<string, object?>
            {
                ["purpose"] = "M5-T5-D-SUSPENDED-KEY-FIXTURE", ["state"] = "CREATED", ["key_type"] = "BCODE",
                ["key_value"] = "0090000001", ["key_id"] = "key-id-de-m5-t5-d",
            });
        var client = new FakeKeyClient();

        var result = await Execute(CreateConfig(dir), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.DeleteKeyCallCount);
        Assert.Null(client.LastDeletedKeyId);
    }

    // ══════════════════════════════════════════════════════════════════
    // CASO 9 — PROTECCIÓN DE LA LLAVE ACTIVA DE CERTIFICACIÓN
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FixtureMatchingActiveCertificationKey_LocalBlocked_NoHttp()
    {
        var dir = NewTempDir("xpay-m5-t5-e-delete-protected-key");
        PrepareCreatedArtifact(FullDirFor(dir), keyValue: SyntheticActiveKeyValue);
        var client = new FakeKeyClient();

        var result = await Execute(CreateConfig(dir), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Contains("key activa de certificación", result.Detail);
        Assert.Equal(0, client.DeleteKeyCallCount);
    }

    // ══════════════════════════════════════════════════════════════════
    // CASO 10 — ÉXITO: UNA SOLA LLAMADA, MARCADOR CORRELACIONADO
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Success_CallsDeleteKeyExactlyOnce_WithKeyIdFromPrivateCapture_NeverAnyOtherMethod()
    {
        var dir = NewTempDir("xpay-m5-t5-e-delete-success");
        PrepareCreatedArtifact(FullDirFor(dir));
        var client = new FakeKeyClient();

        var result = await Execute(CreateConfig(dir), client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal(1, client.DeleteKeyCallCount);
        Assert.Equal(0, client.OtherMethodCallCount);
        Assert.Equal(SyntheticFixtureKeyId, client.LastDeletedKeyId);
    }

    [Fact]
    public async Task Success_WritesMarkerWithAcknowledgedDeletion_AndMatchingFingerprints()
    {
        var dir = NewTempDir("xpay-m5-t5-e-delete-marker");
        var fullDir = FullDirFor(dir);
        PrepareCreatedArtifact(fullDir);
        var client = new FakeKeyClient();

        await Execute(CreateConfig(dir), client);

        Assert.True(File.Exists(MarkerPath(fullDir)));
        var marker = M5T5DeletedFixtureArtifactReader.ResolveDeleteMarker(fullDir);
        Assert.NotNull(marker);
        Assert.Equal("EXPECTED_DELETE_SUCCESS", marker!.Outcome);
        Assert.True(marker.DeleteAcknowledged);
        Assert.Equal(Fingerprint.Compute(SyntheticFixtureKeyId), marker.KeyIdFingerprint);
        Assert.Equal(Fingerprint.Compute(SyntheticFixtureKeyValue), marker.KeyValueFingerprint);

        // El marcador es FINGERPRINT-ONLY: nunca duplica valores crudos.
        var markerContent = File.ReadAllText(MarkerPath(fullDir));
        Assert.DoesNotContain(SyntheticFixtureKeyValue, markerContent);
        Assert.DoesNotContain(SyntheticFixtureKeyId, markerContent);
    }

    [Fact]
    public async Task Success_PreservesTheCreatedArtifact_SoPhaseCStillHasTheKeyValue()
    {
        // Delete elimina la llave en Passport, NUNCA el registro privado:
        // la Fase C necesita ese key_value y Resolve no acepta key_id.
        var dir = NewTempDir("xpay-m5-t5-e-delete-preserves-artifact");
        var fullDir = FullDirFor(dir);
        PrepareCreatedArtifact(fullDir);
        var client = new FakeKeyClient();

        await Execute(CreateConfig(dir), client);

        var (outcome, artifact) = M5T5DeletedFixtureArtifactReader.Resolve(fullDir);
        Assert.Equal(M5T5DeletedFixtureArtifactReader.ResolutionOutcome.Found, outcome);
        Assert.Equal(SyntheticFixtureKeyValue, artifact!.KeyValue);
    }

    [Fact]
    public async Task PublicEvidence_IsSanitized_AndNeverClaimsTheCaseRan()
    {
        var dir = NewTempDir("xpay-m5-t5-e-delete-evidence");
        PrepareCreatedArtifact(FullDirFor(dir));
        var client = new FakeKeyClient();

        var result = await Execute(CreateConfig(dir), client);

        var evidenceJson = System.Text.Json.JsonSerializer.Serialize(result.Evidence!.ResponseSanitized) +
                            System.Text.Json.JsonSerializer.Serialize(result.Evidence.RequestSanitized);
        Assert.DoesNotContain(SyntheticFixtureKeyValue, evidenceJson);
        Assert.DoesNotContain(SyntheticFixtureKeyId, evidenceJson);
        Assert.Equal("M5-T5-E-KEY-PREP-DELETE", result.Evidence.CaseId);
        Assert.Equal("NOT_YET_EXECUTED", result.Evidence.ResponseSanitized["certification_test_result"]);
        Assert.Equal("DELETED_NOT_TESTED", result.Evidence.ResponseSanitized["fixture_phase"]);
        Assert.Equal(1, result.Evidence.ResponseSanitized["delete_key_real_calls"]);
        Assert.Equal(0, result.Evidence.ResponseSanitized["resolve_real_calls"]);
        Assert.Equal(0, result.Evidence.ResponseSanitized["payment_real_calls"]);
    }

    // ══════════════════════════════════════════════════════════════════
    // CASO 11 — RECHAZO DEL PROVEEDOR: NUNCA SE AFIRMA LA ELIMINACIÓN
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PassportRejection_MarksConsumedWithoutAcknowledgingDeletion()
    {
        var dir = NewTempDir("xpay-m5-t5-e-delete-rejected");
        var fullDir = FullDirFor(dir);
        PrepareCreatedArtifact(fullDir);
        var client = new FakeKeyClient(exceptionToThrow: new PassportTransportException(
            "sintético", statusCode: 409, safeErrorCode: "conflict", safeErrorMessage: "rechazo sintético"));

        var result = await Execute(CreateConfig(dir), client);

        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        var marker = M5T5DeletedFixtureArtifactReader.ResolveDeleteMarker(fullDir);
        Assert.Equal("DELETE_REJECTED", marker!.Outcome);
        Assert.False(marker.DeleteAcknowledged);
    }

    // ══════════════════════════════════════════════════════════════════
    // CASO 12 — REPEAT GUARD PERMANENTE DE LA FASE B (IRREVERSIBILIDAD)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SecondExecution_AfterSuccess_IsLocalBlocked_NoSecondCall()
    {
        var dir = NewTempDir("xpay-m5-t5-e-delete-repeat-success");
        PrepareCreatedArtifact(FullDirFor(dir));
        var config = CreateConfig(dir);

        var first = await Execute(config, new FakeKeyClient());
        Assert.Equal(KeyOperationOutcome.Success, first.Outcome);

        var secondClient = new FakeKeyClient();
        var second = await Execute(config, secondClient);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, second.Outcome);
        Assert.Equal(0, secondClient.DeleteKeyCallCount);
        Assert.Contains("ALREADY_EXECUTED", second.Detail);
    }

    [Fact]
    public async Task SecondExecution_AfterRejection_IsLocalBlocked_NoSecondCall()
    {
        var dir = NewTempDir("xpay-m5-t5-e-delete-repeat-failure");
        PrepareCreatedArtifact(FullDirFor(dir));
        var config = CreateConfig(dir);

        var first = await Execute(config, new FakeKeyClient(exceptionToThrow: new PassportTransportException(
            "sintético", statusCode: 500, safeErrorCode: "internal_error", safeErrorMessage: "error sintético")));
        Assert.Equal(KeyOperationOutcome.PassportFailure, first.Outcome);

        var secondClient = new FakeKeyClient();
        var second = await Execute(config, secondClient);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, second.Outcome);
        Assert.Equal(0, secondClient.DeleteKeyCallCount);
    }

    [Fact]
    public async Task PhaseBGuard_IsIndependentFromM5T5DSuspendMarker()
    {
        // El marcador de Suspend de M5-T5-D presente en el mismo directorio
        // NUNCA bloquea ni autoriza la Fase B de M5-T5-E.
        var dir = NewTempDir("xpay-m5-t5-e-delete-independent-marker");
        var fullDir = FullDirFor(dir);
        PrepareCreatedArtifact(fullDir);
        M5PaymentPrivateCapture.Write(
            fullDir, SuspendM5T5FixtureKeyExecutor.SuspendGuardCaseId, "MARKER",
            new Dictionary<string, object?> { ["outcome"] = "EXPECTED_SUSPEND_SUCCESS", ["returned_status"] = "SUSPENDED" });
        var client = new FakeKeyClient();

        var result = await Execute(CreateConfig(dir), client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal(1, client.DeleteKeyCallCount);
    }
}
