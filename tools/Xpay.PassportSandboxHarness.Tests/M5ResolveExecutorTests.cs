using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-545 — prueba, a nivel del executor M5-T2-RESOLVE (sin red), que:
// reutiliza EXACTAMENTE los mismos targets de KEY_A que M3-T2 (nunca un
// segundo stack); nunca llama a ningún otro método de IPassportKeyClient;
// respeta TODOS los guards fail-closed antes de cualquier HTTP; el
// resolution_id crudo SÓLO llega al artefacto privado, nunca a stdout ni a
// evidence.json; el no-repeat guard bloquea mientras el artefacto esté
// vigente/no consumido y permite una nueva ejecución sólo tras consumo +
// vencimiento real; el lock cierra la ventana de concurrencia; y ninguna
// ruta de M3-T2 se usa para esta evidencia.
public class M5ResolveExecutorTests
{
    private sealed class FakeKeyClient : IPassportKeyClient
    {
        private readonly bool _throwOnResolve;
        private readonly PassportResolveKeyResponse? _resolveResponse;
        private readonly PassportTransportException? _exceptionToThrow;

        public int ResolveCallCount { get; private set; }
        public int OtherMethodCallCount { get; private set; }
        public PassportResolveKeyRequest? LastResolveRequest { get; private set; }

        public FakeKeyClient(
            bool throwOnResolve = false,
            PassportResolveKeyResponse? resolveResponse = null,
            PassportTransportException? exceptionToThrow = null)
        {
            _throwOnResolve = throwOnResolve;
            _resolveResponse = resolveResponse;
            _exceptionToThrow = exceptionToThrow;
        }

        public Task<PassportResolveKeyResponse> ResolveKeyAsync(
            PassportResolveKeyRequest request, CancellationToken cancellationToken = default)
        {
            ResolveCallCount++;
            LastResolveRequest = request;
            if (_exceptionToThrow is not null)
                throw _exceptionToThrow;
            if (_throwOnResolve)
                throw new PassportTransportException("Passport respondió con error HTTP (sintético).");
            return Task.FromResult(_resolveResponse ?? new PassportResolveKeyResponse
            {
                Id = "synthetic-resolution-id-001",
                ResolvedAt = "2026-01-01T00:00:00Z",
                ExpiresAt = "2026-01-01T00:30:00Z",
                Owner = new PassportResolveKeyOwnerResponse(),
                Participant = new PassportResolveKeyParticipantResponse(),
                Account = new PassportResolveKeyAccountResponse(),
            });
        }

        public Task<PassportKeyResponse> CreateKeyAsync(PassportCreateKeyRequest request, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("No debe invocarse desde M5ResolveExecutor."); }

        public Task<PassportKeyResponse> SuspendKeyAsync(string keyId, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("No debe invocarse desde M5ResolveExecutor."); }

        public Task<PassportKeyResponse> ActivateKeyAsync(string keyId, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("No debe invocarse desde M5ResolveExecutor."); }

        public Task DeleteKeyAsync(string keyId, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("No debe invocarse desde M5ResolveExecutor."); }

        public Task<PassportListKeysResponse> ListKeysAsync(string accountId, PassportKeyType keyType, string keyValue, CancellationToken cancellationToken = default)
        { OtherMethodCallCount++; throw new InvalidOperationException("No debe invocarse desde M5ResolveExecutor."); }
    }

    private static readonly DateTime FixedTime = new(2026, 1, 1, 0, 10, 0, DateTimeKind.Utc);

    private static readonly string SyntheticRepoRoot =
        Path.Combine(Path.GetTempPath(), "xpay-m5-resolve-synthetic-repo-root-for-tests");

    private static string NewTempDir(string prefix) =>
        Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));

    private static ICommitShaProvider CommitSha() =>
        new FixedCommitShaProvider("synthetic-m5-resolve-commit-sha-000000000000000000000000000000000000");

    private const string SyntheticCustomerId = "synthetic-customer-a-id";
    private const string SyntheticKeyType    = "BCODE";
    private const string SyntheticKeyValue   = "synthetic-key-a-value";

    private static IConfiguration CreateConfig(
        string? privateCaptureDir = "__AUTO_VALID__",
        string? customerId = SyntheticCustomerId,
        string? keyType = SyntheticKeyType,
        string? keyValue = SyntheticKeyValue)
    {
        var dict = new Dictionary<string, string?>();

        var resolvedDir = privateCaptureDir == "__AUTO_VALID__" ? NewTempDir("xpay-m5-resolve-private-capture") : privateCaptureDir;
        if (resolvedDir is not null) dict[M5PaymentPrivateCapture.EnvPrivateCaptureDir] = resolvedDir;

        if (customerId is not null) dict[HarnessTargetConfig.EnvCustomerId] = customerId;
        if (keyType is not null) dict[HarnessTargetConfig.EnvBrebKeyType] = keyType;
        if (keyValue is not null) dict[HarnessTargetConfig.EnvBrebKeyValue] = keyValue;

        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static Task<KeyOperationResult> Execute(IConfiguration config, FakeKeyClient client, DateTime? executedAtUtc = null) =>
        M5ResolveExecutor.ExecuteAsync(
            config, client, CommitSha(), executedAtUtc ?? FixedTime, automatedTestReference: null,
            repositoryRootOverride: SyntheticRepoRoot);

    // ══════════════════════════════════════════════════════════════════
    // ARCHITECTURE
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ExecuteAsync_SignatureExcludesProductDependencies()
    {
        var method = typeof(M5ResolveExecutor).GetMethod("ExecuteAsync");
        Assert.NotNull(method);
        var paramTypeNames = method!.GetParameters().Select(p => p.ParameterType.FullName).ToArray();
        Assert.DoesNotContain(paramTypeNames, n => n != null &&
            (n.Contains("BrebPaymentService") || n.Contains("PassportBrebRetiro") || n.Contains("DbContext") || n.Contains("Wallet")));
    }

    // ══════════════════════════════════════════════════════════════════
    // GUARDS
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
    public async Task PrivateCaptureDirInsideRepository_LocalBlocked_NoHttp()
    {
        var config = CreateConfig(privateCaptureDir: SyntheticRepoRoot);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.ResolveCallCount);
    }

    [Fact]
    public async Task MissingCustomerId_LocalBlocked_NoHttp()
    {
        var config = CreateConfig(customerId: null);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.ResolveCallCount);
    }

    [Fact]
    public async Task MissingKeyValue_LocalBlocked_NoHttp()
    {
        var config = CreateConfig(keyValue: null);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.ResolveCallCount);
    }

    [Fact]
    public async Task InvalidKeyType_LocalBlocked_NoHttp()
    {
        var config = CreateConfig(keyType: "MOBILE"); // sinónimo NO reconocido, a propósito
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.ResolveCallCount);
    }

    // ══════════════════════════════════════════════════════════════════
    // SUCCESS / FAILURE
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ValidState_Success_CallsResolveExactlyOnce_NoOtherMethod()
    {
        var config = CreateConfig();
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal(1, client.ResolveCallCount);
        Assert.Equal(0, client.OtherMethodCallCount);
        Assert.NotNull(result.Evidence);
        Assert.Equal("M5-T2-RESOLVE", result.Evidence!.CaseId);
    }

    [Fact]
    public async Task ValidState_Success_UsesSameKeyATargetsAsM3T2()
    {
        var config = CreateConfig();
        var client = new FakeKeyClient();

        await Execute(config, client);

        Assert.NotNull(client.LastResolveRequest);
        Assert.Equal(SyntheticCustomerId, client.LastResolveRequest!.CustomerId);
        Assert.Equal(SyntheticKeyValue, client.LastResolveRequest.Key.KeyValue);
        Assert.Equal(PassportKeyType.BCODE, client.LastResolveRequest.Key.KeyType);
    }

    [Fact]
    public async Task EvidenceNeverContainsRawResolutionIdOrKeyValue()
    {
        var config = CreateConfig();
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        var json = System.Text.Json.JsonSerializer.Serialize(result.Evidence!.ResponseSanitized);
        var reqJson = System.Text.Json.JsonSerializer.Serialize(result.Evidence.RequestSanitized);
        Assert.DoesNotContain("synthetic-resolution-id-001", json);
        Assert.DoesNotContain(SyntheticKeyValue, reqJson);
        Assert.DoesNotContain(SyntheticCustomerId, reqJson);
    }

    [Fact]
    public async Task PassportTransportException_ReturnsPassportFailure_NoRetry()
    {
        var config = CreateConfig();
        var client = new FakeKeyClient(throwOnResolve: true);

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        Assert.Equal(1, client.ResolveCallCount);
    }

    // ══════════════════════════════════════════════════════════════════
    // PRIVATE ARTIFACT / M3 ISOLATION
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SuccessfulExecution_WritesPrivateArtifactWithRawResolutionId()
    {
        var dir = NewTempDir("xpay-m5-resolve-artifact-assert");
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        var fullDir = Path.GetFullPath(dir, SyntheticRepoRoot);
        var files = Directory.GetFiles(fullDir, "M5-T2-RESOLVE-*.private.json");
        Assert.Single(files);
        var content = File.ReadAllText(files[0]);
        Assert.Contains("synthetic-resolution-id-001", content);
    }

    [Fact]
    public async Task SuccessfulExecution_NeverWritesUnderAnyM3Path()
    {
        // El artefacto privado y la evidencia sanitizada usan case_id
        // "M5-T2-RESOLVE" — nunca "M3-T2". Se verifica que ninguna ruta
        // generada contiene "M3-T2".
        var dir = NewTempDir("xpay-m5-resolve-m3-isolation");
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal("M5-T2-RESOLVE", result.Evidence!.CaseId);
        Assert.DoesNotContain("M3-T2", result.Evidence.CaseId);
        var fullDir = Path.GetFullPath(dir, SyntheticRepoRoot);
        var files = Directory.GetFiles(fullDir);
        Assert.All(files, f => Assert.DoesNotContain("M3-T2", Path.GetFileName(f)));
    }

    [Fact]
    public async Task SuccessfulExecution_LockReleased_NoResidualLockFile()
    {
        var dir = NewTempDir("xpay-m5-resolve-lock-release");
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        var lockPath = Path.Combine(Path.GetFullPath(dir, SyntheticRepoRoot), M5PaymentPrivateCapture.ResolveLockFileName);
        Assert.False(File.Exists(lockPath));
    }

    [Fact]
    public async Task PassportFailure_LockReleased_SafeToRetryLater()
    {
        var dir = NewTempDir("xpay-m5-resolve-lock-release-failure");
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient(throwOnResolve: true);

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        var lockPath = Path.Combine(Path.GetFullPath(dir, SyntheticRepoRoot), M5PaymentPrivateCapture.ResolveLockFileName);
        Assert.False(File.Exists(lockPath));
    }

    // ══════════════════════════════════════════════════════════════════
    // NO-REPEAT GUARD (vigente/no consumido vs consumido+vencido)
    // ══════════════════════════════════════════════════════════════════

    private static string PrepareUnconsumedArtifact(DateTime capturedAt)
    {
        var dir = NewTempDir("xpay-m5-resolve-repeat-unconsumed");
        M5PaymentPrivateCapture.EnsureDirectory(dir);
        var content = new Dictionary<string, object?>
        {
            ["case_id"] = "M5-T2-RESOLVE",
            ["captured_at_utc"] = capturedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["backend_commit_sha"] = "synthetic-sha",
            ["customer_id_fingerprint"] = "fp",
            ["key_value_fingerprint"] = "fp",
            ["resolution_id"] = "synthetic-old-resolution-id",
            ["resolved_at_raw"] = capturedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["expires_at_raw"] = capturedAt.AddMinutes(30).ToString("yyyy-MM-ddTHH:mm:ssZ"),
        };
        var ts = capturedAt.ToString("yyyy-MM-ddTHH-mm-ssZ");
        M5PaymentPrivateCapture.Write(dir, "M5-T2-RESOLVE", ts, content);
        return dir;
    }

    private static void MarkConsumed(string dir, DateTime capturedAt, DateTime consumedAt)
    {
        var (outcome, artifact) = M5ResolveArtifactReader.Resolve(dir);
        Assert.Equal(M5ResolveArtifactReader.ResolutionOutcome.Found, outcome);
        var markerPath = Path.Combine(dir, M5ResolveArtifactReader.ConsumedPrefix + artifact!.SourceFileNameSuffix);
        var content = new Dictionary<string, object?>
        {
            ["case_id"] = "M5-T2-RESOLVE",
            ["consumed_utc"] = consumedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["resolution_id_fingerprint"] = "fp",
        };
        File.WriteAllText(markerPath, System.Text.Json.JsonSerializer.Serialize(content));
    }

    [Fact]
    public async Task NoPriorArtifact_RepeatGuardPasses_ReachesSuccess()
    {
        var config = CreateConfig();
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal(1, client.ResolveCallCount);
    }

    [Fact]
    public async Task UnconsumedPriorArtifact_Blocked_ClientCallsZero()
    {
        var capturedAt = FixedTime.AddMinutes(-5);
        var dir = PrepareUnconsumedArtifact(capturedAt);
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.ResolveCallCount);
        Assert.Contains("ALREADY_EXECUTED", result.Detail);
    }

    [Fact]
    public async Task ConsumedButStillValidArtifact_Blocked_ClientCallsZero()
    {
        var capturedAt = FixedTime.AddMinutes(-5); // expira en +30min desde captured -> todavía vigente
        var dir = PrepareUnconsumedArtifact(capturedAt);
        MarkConsumed(dir, capturedAt, FixedTime.AddMinutes(-4));
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.ResolveCallCount);
    }

    [Fact]
    public async Task ConsumedAndExpiredArtifact_AllowsNewExecution()
    {
        var capturedAt = FixedTime.AddHours(-2); // expires_at = capturedAt+30min, ya venció respecto a FixedTime
        var dir = PrepareUnconsumedArtifact(capturedAt);
        MarkConsumed(dir, capturedAt, capturedAt.AddMinutes(1));
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal(1, client.ResolveCallCount);
    }

    [Fact]
    public async Task ConsumedButNoExpirationTimestamp_FailClosed_Blocked()
    {
        var dir = NewTempDir("xpay-m5-resolve-no-expiry");
        M5PaymentPrivateCapture.EnsureDirectory(dir);
        var capturedAt = FixedTime.AddHours(-5);
        var content = new Dictionary<string, object?>
        {
            ["case_id"] = "M5-T2-RESOLVE",
            ["captured_at_utc"] = capturedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["backend_commit_sha"] = "synthetic-sha",
            ["resolution_id"] = "synthetic-old-resolution-id",
            ["resolved_at_raw"] = null,
            ["expires_at_raw"] = null, // ausente a propósito — sin regla inventada, fail-closed
        };
        var ts = capturedAt.ToString("yyyy-MM-ddTHH-mm-ssZ");
        M5PaymentPrivateCapture.Write(dir, "M5-T2-RESOLVE", ts, content);
        MarkConsumed(dir, capturedAt, capturedAt.AddMinutes(1));

        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.ResolveCallCount);
    }

    [Fact]
    public async Task MultiplePriorArtifacts_Blocked_ClientCallsZero()
    {
        var dir = PrepareUnconsumedArtifact(FixedTime.AddMinutes(-10));
        var second = new Dictionary<string, object?>
        {
            ["case_id"] = "M5-T2-RESOLVE",
            ["captured_at_utc"] = FixedTime.AddMinutes(-9).ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["backend_commit_sha"] = "synthetic-sha-2",
            ["resolution_id"] = "synthetic-old-resolution-id-2",
        };
        M5PaymentPrivateCapture.Write(dir, "M5-T2-RESOLVE", FixedTime.AddMinutes(-9).ToString("yyyy-MM-ddTHH-mm-ssZ"), second);

        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.ResolveCallCount);
        Assert.Contains("AMBIGUOUS", result.Detail);
    }

    [Fact]
    public async Task NeverConfusesM5PaymentCreateArtifactWithM5Resolve()
    {
        // El directorio compartido puede contener artefactos de
        // M5-PAYMENT-CREATE — el glob específico de M5-T2-RESOLVE debe
        // ignorarlos por completo.
        var dir = NewTempDir("xpay-m5-resolve-shared-dir-isolation");
        M5PaymentPrivateCapture.EnsureDirectory(dir);
        var paymentContent = new Dictionary<string, object?> { ["case_id"] = "M5-PAYMENT-CREATE", ["payment_id"] = "synthetic-payment-id" };
        M5PaymentPrivateCapture.Write(dir, "M5-PAYMENT-CREATE", "2026-01-01T00-00-00Z", paymentContent);

        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        // No hay ningún artefacto M5-T2-RESOLVE todavía -> debe proceder con éxito.
        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal(1, client.ResolveCallCount);
    }

    // ══════════════════════════════════════════════════════════════════
    // LOCK / CONCURRENCIA
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LockAlreadyHeld_Blocked_ClientCallsZero()
    {
        var dir = NewTempDir("xpay-m5-resolve-lock-held");
        M5PaymentPrivateCapture.EnsureDirectory(dir);
        var lockOutcome = M5PaymentPrivateCapture.TryAcquireResolveLock(dir, out var lockPath);
        Assert.Equal(M5PaymentPrivateCapture.LockAcquisitionOutcome.Acquired, lockOutcome);

        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.ResolveCallCount);
        Assert.True(File.Exists(lockPath));
    }

    [Fact]
    public async Task ConcurrentAttempts_AtMostOneReachesPassport()
    {
        var dir = NewTempDir("xpay-m5-resolve-concurrency");
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakeKeyClient();

        var results = await Task.WhenAll(
            Execute(config, client),
            Execute(config, client));

        Assert.True(client.ResolveCallCount <= 1,
            $"Se esperaba como máximo 1 llamada real concurrente, se observaron {client.ResolveCallCount}.");
        Assert.Contains(results, r => r.Outcome == KeyOperationOutcome.LocalBlocked);
    }

    [Fact]
    public void DryRun_NeverReachesReadyToExecute_WithoutExecuteFlag()
    {
        var action = HarnessDecision.Decide(new[] { HarnessDecision.CommandResolveKeyM5T2 }, out _);
        Assert.Equal(HarnessAction.DryRun, action);
    }

    [Fact]
    public void M3ConfirmFlag_NeverAuthorizesM5T2Resolve()
    {
        var action = HarnessDecision.Decide(
            new[] { HarnessDecision.CommandResolveKeyM5T2, HarnessDecision.FlagExecute, HarnessDecision.FlagConfirmResolveKey },
            out var abortReason);
        Assert.Equal(HarnessAction.Aborted, action);
        Assert.NotNull(abortReason);
    }
}
