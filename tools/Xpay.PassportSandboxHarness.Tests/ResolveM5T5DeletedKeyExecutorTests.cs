using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-613 — prueba, SIN RED, la FASE C (RESOLVE-DELETED) y ejecución
// final de M5-T5-E: el key_type/key_value se leen EXCLUSIVAMENTE del
// artifact CREATED privado de la Fase A; se exige una correlación
// Create->Delete válida con la Fase B (mismos fingerprints, eliminación
// reconocida con 204) ANTES de HTTP; invoca ÚNICAMENTE
// IPassportKeyClient.ResolveKeyAsync (nunca Create/Suspend/Activate/
// Delete/ListKeys, y estructuralmente nunca Payment/Retrieve — este
// executor no conoce IPassportPaymentClient); y el repeat guard PERMANENTE
// de esta Fase C impide una segunda ejecución real.
public class ResolveM5T5DeletedKeyExecutorTests
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
        { OtherMethodCallCount++; throw new InvalidOperationException("CreateKeyAsync NUNCA debe invocarse en la Fase C."); }

        public Task<PassportKeyResponse> SuspendKeyAsync(string keyId, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("SuspendKeyAsync NUNCA debe invocarse en la Fase C."); }

        public Task<PassportKeyResponse> ActivateKeyAsync(string keyId, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("ActivateKeyAsync NUNCA debe invocarse en la Fase C."); }

        public Task DeleteKeyAsync(string keyId, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("DeleteKeyAsync NUNCA debe invocarse en la Fase C."); }

        public Task<PassportListKeysResponse> ListKeysAsync(string accountId, PassportKeyType keyType, string keyValue, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("ListKeysAsync NUNCA debe invocarse en la Fase C."); }
    }

    private static readonly DateTime FixedTime = new(2026, 1, 1, 0, 30, 0, DateTimeKind.Utc);

    private static readonly string SyntheticRepoRoot =
        Path.Combine(Path.GetTempPath(), "xpay-m5-t5-e-resolve-synthetic-repo-root-for-tests");

    private static string NewTempDir(string prefix) =>
        Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));

    private static ICommitShaProvider CommitSha() =>
        new FixedCommitShaProvider("synthetic-m5-t5-e-resolve-commit-sha-000000000000000000000000000000");

    private const string SyntheticActiveKeyValue = "synthetic-active-0268-key-value-never-used-here";
    private const string SyntheticFixtureKeyValue = "0091234567";
    private const string SyntheticFixtureKeyId = "synthetic-fixture-key-id-from-m5-t5-e-phase-a";
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
        string fullDir, string keyId = SyntheticFixtureKeyId, string keyValue = SyntheticFixtureKeyValue,
        string state = "CREATED", string keyType = "BCODE", string? purpose = null)
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
            fullDir, $"{CreateM5T5DeletedKeyFixtureExecutor.FixtureCaseIdPrefix}-CREATED", "2026-01-01T00-15-00Z", content);
    }

    private static void PrepareDeleteMarker(
        string fullDir, string? keyIdFingerprint = null, string? keyValueFingerprint = null,
        string outcome = "EXPECTED_DELETE_SUCCESS", bool deleteAcknowledged = true)
    {
        M5PaymentPrivateCapture.EnsureDirectory(fullDir);
        var content = new Dictionary<string, object?>
        {
            ["case_id"] = DeleteM5T5FixtureKeyExecutor.DeleteGuardCaseId,
            ["key_id_fingerprint"] = keyIdFingerprint ?? FixtureKeyIdFingerprint,
            ["key_value_fingerprint"] = keyValueFingerprint ?? FixtureKeyValueFingerprint,
            ["outcome"] = outcome,
            ["delete_acknowledged"] = deleteAcknowledged,
        };
        M5PaymentPrivateCapture.Write(fullDir, DeleteM5T5FixtureKeyExecutor.DeleteGuardCaseId, "MARKER", content);
    }

    private static void PrepareValidFixtureChain(string fullDir)
    {
        PrepareCreatedArtifact(fullDir);
        PrepareDeleteMarker(fullDir);
    }

    private static Task<KeyOperationResult> Execute(IConfiguration config, FakeKeyClient client) =>
        ResolveM5T5DeletedKeyExecutor.ExecuteAsync(
            config, client, CommitSha(), FixedTime, automatedTestReference: null,
            repositoryRootOverride: SyntheticRepoRoot);

    private static string FullDirFor(string dir) => Path.GetFullPath(dir, SyntheticRepoRoot);

    private static PassportTransportException NotFound() => new(
        "sintético", statusCode: 404, safeErrorCode: "resource_not_found", safeErrorMessage: "key not found.");

    // ══════════════════════════════════════════════════════════════════
    // CASO 13 — GUARDS DE ENTORNO ANTES DE HTTP
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MissingPrivateCaptureDir_LocalBlocked_NoHttp()
    {
        var client = new FakeKeyClient();

        var result = await Execute(CreateConfig(privateCaptureDir: null), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.ResolveCallCount);
        Assert.Null(result.Evidence);
    }

    [Fact]
    public async Task MissingCustomerId_LocalBlocked_NoHttp()
    {
        var dir = NewTempDir("xpay-m5-t5-e-resolve-no-customer");
        PrepareValidFixtureChain(FullDirFor(dir));
        var client = new FakeKeyClient();

        var result = await Execute(CreateConfig(dir, customerId: null), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.ResolveCallCount);
    }

    [Fact]
    public async Task NoCreatedArtifact_LocalBlocked_NoHttp()
    {
        var dir = NewTempDir("xpay-m5-t5-e-resolve-no-artifact");
        M5PaymentPrivateCapture.EnsureDirectory(FullDirFor(dir));
        var client = new FakeKeyClient();

        var result = await Execute(CreateConfig(dir), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.ResolveCallCount);
    }

    // ══════════════════════════════════════════════════════════════════
    // CASO 14 — CORRELACIÓN CREATE->DELETE OBLIGATORIA
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MissingDeleteMarker_LocalBlocked_NoHttp()
    {
        // Sin prueba de que la llave fue realmente eliminada, el caso
        // negativo no tiene sentido: fail-closed.
        var dir = NewTempDir("xpay-m5-t5-e-resolve-no-marker");
        PrepareCreatedArtifact(FullDirFor(dir));
        var client = new FakeKeyClient();

        var result = await Execute(CreateConfig(dir), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Contains("correlación Create->Delete", result.Detail);
        Assert.Equal(0, client.ResolveCallCount);
    }

    [Fact]
    public async Task DeleteMarkerForDifferentFixture_LocalBlocked_NoHttp()
    {
        var dir = NewTempDir("xpay-m5-t5-e-resolve-marker-mismatch");
        var fullDir = FullDirFor(dir);
        PrepareCreatedArtifact(fullDir);
        PrepareDeleteMarker(fullDir, keyIdFingerprint: Fingerprint.Compute("otro-key-id-completamente-distinto"));
        var client = new FakeKeyClient();

        var result = await Execute(CreateConfig(dir), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.ResolveCallCount);
    }

    [Fact]
    public async Task DeleteMarkerNotAcknowledged_LocalBlocked_NoHttp()
    {
        var dir = NewTempDir("xpay-m5-t5-e-resolve-marker-not-ack");
        var fullDir = FullDirFor(dir);
        PrepareCreatedArtifact(fullDir);
        PrepareDeleteMarker(fullDir, outcome: "DELETE_REJECTED", deleteAcknowledged: false);
        var client = new FakeKeyClient();

        var result = await Execute(CreateConfig(dir), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.ResolveCallCount);
    }

    [Fact]
    public async Task M5T5DSuspendMarker_NeverSatisfiesPhaseCCorrelation_NoHttp()
    {
        // Un marcador de Suspend de M5-T5-D (incluso con los MISMOS
        // fingerprints) nunca autoriza la Fase C de M5-T5-E: son archivos
        // de nombre distinto y la Fase C sólo lee el suyo.
        var dir = NewTempDir("xpay-m5-t5-e-resolve-d-marker");
        var fullDir = FullDirFor(dir);
        PrepareCreatedArtifact(fullDir);
        M5PaymentPrivateCapture.Write(
            fullDir, SuspendM5T5FixtureKeyExecutor.SuspendGuardCaseId, "MARKER",
            new Dictionary<string, object?>
            {
                ["key_id_fingerprint"] = FixtureKeyIdFingerprint,
                ["key_value_fingerprint"] = FixtureKeyValueFingerprint,
                ["outcome"] = "EXPECTED_SUSPEND_SUCCESS", ["returned_status"] = "SUSPENDED",
            });
        var client = new FakeKeyClient();

        var result = await Execute(CreateConfig(dir), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.ResolveCallCount);
    }

    // ══════════════════════════════════════════════════════════════════
    // CASO 15 — PROTECCIÓN DE LA LLAVE ACTIVA
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FixtureMatchingActiveCertificationKey_LocalBlocked_NoHttp()
    {
        var dir = NewTempDir("xpay-m5-t5-e-resolve-protected-key");
        var fullDir = FullDirFor(dir);
        PrepareCreatedArtifact(fullDir, keyValue: SyntheticActiveKeyValue);
        PrepareDeleteMarker(fullDir, keyValueFingerprint: Fingerprint.Compute(SyntheticActiveKeyValue));
        var client = new FakeKeyClient();

        var result = await Execute(CreateConfig(dir), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Contains("key activa de certificación", result.Detail);
        Assert.Equal(0, client.ResolveCallCount);
    }

    // ══════════════════════════════════════════════════════════════════
    // CASO 16 — RECHAZO ESPERADO (RESULTADO DESEADO DEL CASO NEGATIVO)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PassportRejection_ProducesExpectedProviderRejectionEvidence_WithFinalCaseId()
    {
        var dir = NewTempDir("xpay-m5-t5-e-resolve-rejected");
        var fullDir = FullDirFor(dir);
        PrepareValidFixtureChain(fullDir);
        var client = new FakeKeyClient(exceptionToThrow: NotFound());

        var result = await Execute(CreateConfig(dir), client);

        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        Assert.Equal(1, client.ResolveCallCount);
        Assert.Equal(0, client.OtherMethodCallCount);

        var evidence = result.Evidence!;
        Assert.Equal("M5-T5-DELETED-KEY", evidence.CaseId);
        Assert.Equal(EvidenceRecord.ResultFail, evidence.Result);
        Assert.Equal("EXPECTED_PROVIDER_REJECTION", evidence.ResponseSanitized["certification_case_result"]);
        Assert.Equal(true, evidence.ResponseSanitized["deletion_signal_confirmed"]);
        Assert.Equal(false, evidence.ResponseSanitized["resolution_returned"]);
        Assert.Equal(1, evidence.ResponseSanitized["resolve_real_calls"]);
        Assert.Equal(0, evidence.ResponseSanitized["payment_real_calls"]);
        Assert.Equal(0, evidence.ResponseSanitized["delete_key_real_calls"]);
    }

    [Fact]
    public async Task ResolveRequest_UsesKeyValueFromPrivateCapture_NeverKeyIdNorConfig()
    {
        var dir = NewTempDir("xpay-m5-t5-e-resolve-request-shape");
        PrepareValidFixtureChain(FullDirFor(dir));
        var client = new FakeKeyClient(exceptionToThrow: NotFound());

        await Execute(CreateConfig(dir), client);

        Assert.NotNull(client.LastRequest);
        Assert.Equal(SyntheticFixtureKeyValue, client.LastRequest!.Key.KeyValue);
        Assert.Equal(PassportKeyType.BCODE, client.LastRequest.Key.KeyType);
        Assert.Equal(SyntheticCustomerId, client.LastRequest.CustomerId);
        Assert.NotEqual(SyntheticActiveKeyValue, client.LastRequest.Key.KeyValue);
    }

    [Fact]
    public async Task PublicEvidence_NeverContainsRawFixtureValues()
    {
        var dir = NewTempDir("xpay-m5-t5-e-resolve-evidence-sanitized");
        PrepareValidFixtureChain(FullDirFor(dir));
        var client = new FakeKeyClient(exceptionToThrow: NotFound());

        var result = await Execute(CreateConfig(dir), client);

        var evidenceJson = System.Text.Json.JsonSerializer.Serialize(result.Evidence!.ResponseSanitized) +
                            System.Text.Json.JsonSerializer.Serialize(result.Evidence.RequestSanitized);
        Assert.DoesNotContain(SyntheticFixtureKeyValue, evidenceJson);
        Assert.DoesNotContain(SyntheticFixtureKeyId, evidenceJson);
        Assert.DoesNotContain(SyntheticCustomerId, evidenceJson);
    }

    // Clasificación LITERAL de la señal de eliminación — nunca inventada,
    // nunca false.
    [Theory]
    [InlineData(404, "resource_not_found", "key not found.", true)]
    [InlineData(404, null, null, true)]
    [InlineData(400, "resource_not_found", "whatever", true)]
    [InlineData(400, "invalid_request", "campo inválido", null)]
    [InlineData(500, null, null, null)]
    public void ClassifyDeletionSignal_IsLiteral_NeverFalse(int? httpStatus, string? code, string? message, bool? expected)
    {
        Assert.Equal(expected, ResolveM5T5DeletedKeyExecutor.ClassifyDeletionSignal(httpStatus, code, message));
    }

    // ══════════════════════════════════════════════════════════════════
    // RESOLUCIÓN INESPERADA — NUNCA SE LEE COMO ÉXITO DEL CASO NEGATIVO
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UnexpectedResolveSuccess_IsFlaggedForDirectorReview_AndNeverTriggersPayment()
    {
        var dir = NewTempDir("xpay-m5-t5-e-resolve-unexpected");
        PrepareValidFixtureChain(FullDirFor(dir));
        var client = new FakeKeyClient();

        var result = await Execute(CreateConfig(dir), client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Contains("UNEXPECTED_RESOLVE_SUCCESS", result.Detail);
        Assert.Equal("UNEXPECTED_RESOLVE_SUCCESS", result.Evidence!.ResponseSanitized["certification_case_result"]);
        Assert.Equal(0, result.Evidence.ResponseSanitized["payment_real_calls"]);
        Assert.Equal(0, result.Evidence.ResponseSanitized["retrieve_real_calls"]);
        Assert.Equal(0, client.OtherMethodCallCount);
    }

    // ══════════════════════════════════════════════════════════════════
    // REPEAT GUARD PERMANENTE DE LA FASE C
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SecondExecution_AfterRejection_IsLocalBlocked_NoSecondCall()
    {
        var dir = NewTempDir("xpay-m5-t5-e-resolve-repeat");
        PrepareValidFixtureChain(FullDirFor(dir));
        var config = CreateConfig(dir);

        var first = await Execute(config, new FakeKeyClient(exceptionToThrow: NotFound()));
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
        var dir = NewTempDir("xpay-m5-t5-e-resolve-repeat-after-success");
        PrepareValidFixtureChain(FullDirFor(dir));
        var config = CreateConfig(dir);

        var first = await Execute(config, new FakeKeyClient());
        Assert.Equal(KeyOperationOutcome.Success, first.Outcome);

        var secondClient = new FakeKeyClient();
        var second = await Execute(config, secondClient);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, second.Outcome);
        Assert.Equal(0, secondClient.ResolveCallCount);
    }
}
