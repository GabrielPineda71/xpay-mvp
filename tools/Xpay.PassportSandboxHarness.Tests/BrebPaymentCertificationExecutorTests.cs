using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-529 — prueba, a nivel de los dos executors M5 (sin red), que:
// cada uno respeta TODOS los guards fail-closed antes de cualquier HTTP,
// que el aislamiento arquitectónico frente a BrebPaymentService/Wallet/
// ledger/BD es estructuralmente imposible de violar, que la cadena privada
// Create→Retrieve funciona con payment_id sintético (nunca hardcoded en
// producción), que SUCCESS (PENDING/PROCESSING/SETTLED/REJECTED) y ERROR
// de Passport se capturan sin forzar un juicio de certificación, y que
// cada executor invoca ÚNICAMENTE el método Payment correspondiente como
// máximo una vez.
public class BrebPaymentCertificationExecutorTests
{
    private sealed class FakePaymentClient : IPassportPaymentClient
    {
        private readonly bool _throwOnCreate;
        private readonly bool _throwOnRetrieve;
        private readonly PassportTransportException? _exceptionToThrow;
        private readonly PassportPaymentResponse? _createResponse;
        private readonly PassportPaymentResponse? _retrieveResponse;

        public int CreateCallCount { get; private set; }
        public int RetrieveCallCount { get; private set; }
        public PassportCreatePaymentRequest? LastCreateRequest { get; private set; }
        public string? LastRetrievePaymentId { get; private set; }

        public FakePaymentClient(
            bool throwOnCreate = false,
            bool throwOnRetrieve = false,
            PassportPaymentResponse? createResponse = null,
            PassportPaymentResponse? retrieveResponse = null,
            PassportTransportException? exceptionToThrow = null)
        {
            _throwOnCreate = throwOnCreate;
            _throwOnRetrieve = throwOnRetrieve;
            _createResponse = createResponse;
            _retrieveResponse = retrieveResponse;
            _exceptionToThrow = exceptionToThrow;
        }

        public Task<PassportPaymentResponse> CreateBrebPaymentAsync(
            PassportCreatePaymentRequest request, CancellationToken cancellationToken = default)
        {
            CreateCallCount++;
            LastCreateRequest = request;
            if (_exceptionToThrow is not null)
                throw _exceptionToThrow;
            if (_throwOnCreate)
                throw new PassportTransportException("Passport respondió con error HTTP (sintético).");
            return Task.FromResult(_createResponse ?? new PassportPaymentResponse
            {
                Id = "synthetic-payment-id-001",
                Status = "PENDING",
            });
        }

        public Task<PassportPaymentResponse> RetrievePaymentAsync(
            string paymentId, CancellationToken cancellationToken = default)
        {
            RetrieveCallCount++;
            LastRetrievePaymentId = paymentId;
            if (_exceptionToThrow is not null)
                throw _exceptionToThrow;
            if (_throwOnRetrieve)
                throw new PassportTransportException("Passport respondió con error HTTP (sintético).");
            return Task.FromResult(_retrieveResponse ?? new PassportPaymentResponse
            {
                Id = paymentId,
                Status = "PROCESSING",
            });
        }
    }

    private const string SyntheticAccountId  = "synthetic-m5-operational-account-id";
    private const string SyntheticResolution = "synthetic-m5-resolution-id";
    private const string SyntheticAmount     = "1000.00";

    private static readonly DateTime FixedTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly string SyntheticRepoRoot =
        Path.Combine(Path.GetTempPath(), "xpay-m5-payment-synthetic-repo-root-for-tests");

    private static string NewTempDir(string prefix) =>
        Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));

    private static IConfiguration CreateConfig(
        string? accountId = SyntheticAccountId,
        string? resolutionId = SyntheticResolution,
        string? resolutionExpiresAt = null,
        string? amountValue = SyntheticAmount,
        string? privateCaptureDir = "__AUTO_VALID__")
    {
        var dict = new Dictionary<string, string?>();
        if (accountId is not null) dict[PassportOptions.EnvOperationalAccountId] = accountId;
        if (resolutionId is not null) dict[CreateBrebPaymentCertificationExecutor.EnvResolutionId] = resolutionId;
        if (resolutionExpiresAt is not null) dict[CreateBrebPaymentCertificationExecutor.EnvResolutionExpiresAtUtc] = resolutionExpiresAt;
        if (amountValue is not null) dict[CreateBrebPaymentCertificationExecutor.EnvAmountValue] = amountValue;

        var resolvedDir = privateCaptureDir == "__AUTO_VALID__" ? NewTempDir("xpay-m5-payment-private-capture") : privateCaptureDir;
        if (resolvedDir is not null) dict[M5PaymentPrivateCapture.EnvPrivateCaptureDir] = resolvedDir;

        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static ICommitShaProvider CommitSha() =>
        new FixedCommitShaProvider("synthetic-m5-commit-sha-0000000000000000000000000000000000000000");

    private static Task<KeyOperationResult> ExecuteCreate(IConfiguration config, FakePaymentClient client, DateTime? nowUtcOverride = null) =>
        CreateBrebPaymentCertificationExecutor.ExecuteAsync(
            config, client, CommitSha(), FixedTime, automatedTestReference: null,
            repositoryRootOverride: SyntheticRepoRoot, nowUtcOverride: nowUtcOverride);

    private static Task<KeyOperationResult> ExecuteRetrieve(IConfiguration config, FakePaymentClient client) =>
        RetrieveBrebPaymentCertificationExecutor.ExecuteAsync(
            config, client, CommitSha(), FixedTime, automatedTestReference: null,
            repositoryRootOverride: SyntheticRepoRoot);

    // ══════════════════════════════════════════════════════════════════
    // ARCHITECTURE — aislamiento estructural (XPAY-529 §2/§20 ARCHITECTURE)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void CreateExecutor_ExecuteAsync_SignatureExcludesProductDependencies()
    {
        // No existe ningún parámetro de tipo BrebPaymentService/
        // PassportBrebRetiro/DbContext/Wallet — estructuralmente imposible
        // de invocarlos desde este método, sin necesidad de auditar el
        // cuerpo entero cada vez.
        var method = typeof(CreateBrebPaymentCertificationExecutor).GetMethod("ExecuteAsync");
        Assert.NotNull(method);
        var paramTypeNames = method!.GetParameters().Select(p => p.ParameterType.FullName).ToArray();
        Assert.DoesNotContain(paramTypeNames, n => n != null && (n.Contains("BrebPaymentService") || n.Contains("PassportBrebRetiro") || n.Contains("DbContext") || n.Contains("Wallet")));
    }

    [Fact]
    public void RetrieveExecutor_ExecuteAsync_SignatureExcludesProductDependencies()
    {
        var method = typeof(RetrieveBrebPaymentCertificationExecutor).GetMethod("ExecuteAsync");
        Assert.NotNull(method);
        var paramTypeNames = method!.GetParameters().Select(p => p.ParameterType.FullName).ToArray();
        Assert.DoesNotContain(paramTypeNames, n => n != null && (n.Contains("BrebPaymentService") || n.Contains("PassportBrebRetiro") || n.Contains("DbContext") || n.Contains("Wallet")));
    }

    [Fact]
    public async Task CreateExecutor_NeverCallsRetrieve_OnlyCreate()
    {
        var client = new FakePaymentClient();
        await ExecuteCreate(CreateConfig(), client);

        Assert.Equal(1, client.CreateCallCount);
        Assert.Equal(0, client.RetrieveCallCount);
    }

    [Fact]
    public async Task RetrieveExecutor_NeverCallsCreate_OnlyRetrieve()
    {
        var captureDir = NewTempDir("xpay-m5-payment-chain");
        try
        {
            var createClient = new FakePaymentClient();
            await ExecuteCreate(CreateConfig(privateCaptureDir: captureDir), createClient);

            var retrieveClient = new FakePaymentClient();
            await ExecuteRetrieve(CreateConfig(privateCaptureDir: captureDir), retrieveClient);

            Assert.Equal(0, retrieveClient.CreateCallCount);
            Assert.Equal(1, retrieveClient.RetrieveCallCount);
        }
        finally
        {
            if (Directory.Exists(captureDir)) Directory.Delete(captureDir, recursive: true);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // CREATE — guards fail-closed (XPAY-529 §19 CREATE)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Create_MissingAccountId_IsLocalBlocked_ZeroHttp()
    {
        var client = new FakePaymentClient();
        var result = await ExecuteCreate(CreateConfig(accountId: null), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateCallCount);
        Assert.Null(result.Evidence);
    }

    [Fact]
    public async Task Create_MissingResolutionId_IsLocalBlocked_ZeroHttp()
    {
        var client = new FakePaymentClient();
        var result = await ExecuteCreate(CreateConfig(resolutionId: null), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateCallCount);
    }

    [Fact]
    public async Task Create_MissingAmount_IsLocalBlocked_ZeroHttp()
    {
        var client = new FakePaymentClient();
        var result = await ExecuteCreate(CreateConfig(amountValue: null), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateCallCount);
    }

    [Fact]
    public async Task Create_ExpiredResolution_IsLocalBlocked_ZeroHttp()
    {
        var client = new FakePaymentClient();
        var expiredAt = FixedTime.AddMinutes(-1).ToString("O");
        var result = await ExecuteCreate(
            CreateConfig(resolutionExpiresAt: expiredAt), client, nowUtcOverride: FixedTime);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateCallCount);
        Assert.Contains("vencida", result.Detail);
    }

    [Fact]
    public async Task Create_ValidFutureResolutionExpiry_ProceedsToHttp()
    {
        var client = new FakePaymentClient();
        var futureExpiry = FixedTime.AddMinutes(10).ToString("O");
        var result = await ExecuteCreate(
            CreateConfig(resolutionExpiresAt: futureExpiry), client, nowUtcOverride: FixedTime);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal(1, client.CreateCallCount);
    }

    [Fact]
    public async Task Create_NoExpiryMetadata_DoesNotBlockOnThatGuardAlone()
    {
        // XPAY-529 §16 — ausencia de metadata de vigencia NO bloquea por sí
        // sola (el gap se documenta, no se inventa un chequeo).
        var client = new FakePaymentClient();
        var result = await ExecuteCreate(CreateConfig(resolutionExpiresAt: null), client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal(1, client.CreateCallCount);
    }

    [Fact]
    public async Task Create_MalformedExpiryMetadata_IsLocalBlocked_ZeroHttp()
    {
        var client = new FakePaymentClient();
        var result = await ExecuteCreate(CreateConfig(resolutionExpiresAt: "not-a-date"), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateCallCount);
    }

    [Fact]
    public async Task Create_MissingPrivateCaptureDir_IsLocalBlocked_ZeroHttp()
    {
        var client = new FakePaymentClient();
        var result = await ExecuteCreate(CreateConfig(privateCaptureDir: null), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateCallCount);
    }

    [Fact]
    public async Task Create_PrivateCaptureDirInsideRepository_IsLocalBlocked_ZeroHttp()
    {
        var client = new FakePaymentClient();
        var insideRepoPath = Path.Combine(SyntheticRepoRoot, "docs", "certificacion", "passport-breb", "M5-private");
        var result = await ExecuteCreate(CreateConfig(privateCaptureDir: insideRepoPath), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateCallCount);
        Assert.DoesNotContain(insideRepoPath, result.Detail!);
    }

    [Fact]
    public async Task Create_CurrencyAlwaysCOP_NeverConfigurable()
    {
        var client = new FakePaymentClient();
        await ExecuteCreate(CreateConfig(), client);

        Assert.Equal(PassportPaymentClient.CopCurrency, client.LastCreateRequest!.Amount.Currency);
    }

    [Fact]
    public async Task Create_FakePending_CapturedFactually()
    {
        var client = new FakePaymentClient(createResponse: new PassportPaymentResponse { Id = "x", Status = "PENDING" });
        var result = await ExecuteCreate(CreateConfig(), client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal("PENDING", result.Evidence!.ResponseSanitized["status"]);
        Assert.Equal(EvidenceRecord.ResultPass, result.Evidence.Result);
    }

    [Fact]
    public async Task Create_FakeProcessing_CapturedFactually()
    {
        var client = new FakePaymentClient(createResponse: new PassportPaymentResponse { Id = "x", Status = "PROCESSING" });
        var result = await ExecuteCreate(CreateConfig(), client);

        Assert.Equal("PROCESSING", result.Evidence!.ResponseSanitized["status"]);
    }

    [Fact]
    public async Task Create_FakeSettled_CapturedFactually()
    {
        var client = new FakePaymentClient(createResponse: new PassportPaymentResponse { Id = "x", Status = "SETTLED" });
        var result = await ExecuteCreate(CreateConfig(), client);

        Assert.Equal("SETTLED", result.Evidence!.ResponseSanitized["status"]);
    }

    [Fact]
    public async Task Create_FakeRejected_CapturedFactually_NoJudgment()
    {
        var client = new FakePaymentClient(createResponse: new PassportPaymentResponse { Id = "x", Status = "REJECTED" });
        var result = await ExecuteCreate(CreateConfig(), client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome); // transporte = 2xx recibido, nunca reinterpretado.
        Assert.Equal("REJECTED", result.Evidence!.ResponseSanitized["status"]);
        Assert.Equal(EvidenceRecord.ResultPass, result.Evidence.Result); // Result=PASS = transporte, no veredicto.
    }

    [Fact]
    public async Task Create_FakeSafeError_CapturedSanitized()
    {
        var client = new FakePaymentClient(exceptionToThrow: new PassportTransportException(
            "sintético", statusCode: 400, safeErrorCode: "invalid_parameter_value", safeErrorMessage: "amount is invalid."));
        var result = await ExecuteCreate(CreateConfig(), client);

        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        Assert.False((bool)result.Evidence!.ResponseSanitized["request_accepted"]!);
        Assert.Equal(400, result.Evidence.ResponseSanitized["observed_http_status"]);
        Assert.Equal("invalid_parameter_value", result.Evidence.ResponseSanitized["safe_error_code"]);
    }

    [Fact]
    public async Task Create_NoRetry_ExactlyOneCallEvenOnFailure()
    {
        var client = new FakePaymentClient(throwOnCreate: true);
        await ExecuteCreate(CreateConfig(), client);

        Assert.Equal(1, client.CreateCallCount);
    }

    [Fact]
    public async Task Create_EvidenceContainsFingerprints_NeverRawIds()
    {
        var client = new FakePaymentClient();
        var result = await ExecuteCreate(CreateConfig(), client);

        var evidenceJson = System.Text.Json.JsonSerializer.Serialize(result.Evidence);
        Assert.DoesNotContain(SyntheticAccountId, evidenceJson);
        Assert.DoesNotContain(SyntheticResolution, evidenceJson);
        Assert.DoesNotContain("synthetic-payment-id-001", evidenceJson);
        Assert.Contains("account_id_fingerprint", evidenceJson);
        Assert.Contains("resolution_id_fingerprint", evidenceJson);
        Assert.Contains("payment_id_fingerprint", evidenceJson);
    }

    [Fact]
    public async Task Create_PrivateArtifactWritten_ContainsPaymentIdOutsideRepo()
    {
        var captureDir = NewTempDir("xpay-m5-payment-artifact-check");
        try
        {
            var client = new FakePaymentClient(createResponse: new PassportPaymentResponse { Id = "synthetic-payment-id-xyz", Status = "PENDING" });
            var result = await ExecuteCreate(CreateConfig(privateCaptureDir: captureDir), client);

            Assert.True((bool)result.Evidence!.ResponseSanitized["private_artifact_captured"]!);
            var files = Directory.GetFiles(captureDir, "*.private.json");
            Assert.Single(files);
            var content = File.ReadAllText(files[0]);
            Assert.Contains("synthetic-payment-id-xyz", content);
            // Nunca dentro del repositorio sintético.
            Assert.DoesNotContain(captureDir, System.Text.Json.JsonSerializer.Serialize(result.Evidence));
        }
        finally
        {
            if (Directory.Exists(captureDir)) Directory.Delete(captureDir, recursive: true);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // RETRIEVE — guards fail-closed (XPAY-529 §19 RETRIEVE)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Retrieve_MissingPrivateArtifactDir_IsLocalBlocked_ZeroHttp()
    {
        var client = new FakePaymentClient();
        var result = await ExecuteRetrieve(CreateConfig(privateCaptureDir: null), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.RetrieveCallCount);
    }

    [Fact]
    public async Task Retrieve_EmptyPrivateArtifactDir_IsLocalBlocked_ZeroHttp()
    {
        var captureDir = NewTempDir("xpay-m5-payment-empty-dir");
        Directory.CreateDirectory(captureDir);
        try
        {
            var client = new FakePaymentClient();
            var result = await ExecuteRetrieve(CreateConfig(privateCaptureDir: captureDir), client);

            Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
            Assert.Equal(0, client.RetrieveCallCount);
        }
        finally
        {
            Directory.Delete(captureDir, recursive: true);
        }
    }

    [Fact]
    public async Task Retrieve_IdentityMismatch_WrongAccountId_IsLocalBlocked_ZeroHttp()
    {
        var captureDir = NewTempDir("xpay-m5-payment-identity-mismatch");
        try
        {
            var createClient = new FakePaymentClient();
            await ExecuteCreate(CreateConfig(accountId: "account-A", privateCaptureDir: captureDir), createClient);

            var retrieveClient = new FakePaymentClient();
            // Cuenta operativa DISTINTA a la usada en el Create original.
            var result = await ExecuteRetrieve(CreateConfig(accountId: "account-B", privateCaptureDir: captureDir), retrieveClient);

            Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
            Assert.Equal(0, retrieveClient.RetrieveCallCount);
        }
        finally
        {
            if (Directory.Exists(captureDir)) Directory.Delete(captureDir, recursive: true);
        }
    }

    [Fact]
    public async Task Retrieve_UsesPaymentIdFromPrivateArtifact_NeverHardcoded()
    {
        var captureDir = NewTempDir("xpay-m5-payment-chain-id");
        try
        {
            var createClient = new FakePaymentClient(createResponse: new PassportPaymentResponse { Id = "synthetic-chained-payment-id", Status = "PENDING" });
            await ExecuteCreate(CreateConfig(privateCaptureDir: captureDir), createClient);

            var retrieveClient = new FakePaymentClient();
            await ExecuteRetrieve(CreateConfig(privateCaptureDir: captureDir), retrieveClient);

            Assert.Equal("synthetic-chained-payment-id", retrieveClient.LastRetrievePaymentId);
        }
        finally
        {
            if (Directory.Exists(captureDir)) Directory.Delete(captureDir, recursive: true);
        }
    }

    [Fact]
    public async Task Retrieve_FakePending_CapturedFactually()
    {
        var captureDir = NewTempDir("xpay-m5-payment-retrieve-pending");
        try
        {
            var createClient = new FakePaymentClient();
            await ExecuteCreate(CreateConfig(privateCaptureDir: captureDir), createClient);

            var retrieveClient = new FakePaymentClient(retrieveResponse: new PassportPaymentResponse { Id = "x", Status = "PENDING" });
            var result = await ExecuteRetrieve(CreateConfig(privateCaptureDir: captureDir), retrieveClient);

            Assert.Equal("PENDING", result.Evidence!.ResponseSanitized["status"]);
        }
        finally
        {
            if (Directory.Exists(captureDir)) Directory.Delete(captureDir, recursive: true);
        }
    }

    [Fact]
    public async Task Retrieve_FakeProcessing_CapturedFactually()
    {
        var captureDir = NewTempDir("xpay-m5-payment-retrieve-processing");
        try
        {
            var createClient = new FakePaymentClient();
            await ExecuteCreate(CreateConfig(privateCaptureDir: captureDir), createClient);

            var retrieveClient = new FakePaymentClient(retrieveResponse: new PassportPaymentResponse { Id = "x", Status = "PROCESSING" });
            var result = await ExecuteRetrieve(CreateConfig(privateCaptureDir: captureDir), retrieveClient);

            Assert.Equal("PROCESSING", result.Evidence!.ResponseSanitized["status"]);
        }
        finally
        {
            if (Directory.Exists(captureDir)) Directory.Delete(captureDir, recursive: true);
        }
    }

    [Fact]
    public async Task Retrieve_FakeSettled_CapturedFactually()
    {
        var captureDir = NewTempDir("xpay-m5-payment-retrieve-settled");
        try
        {
            var createClient = new FakePaymentClient();
            await ExecuteCreate(CreateConfig(privateCaptureDir: captureDir), createClient);

            var retrieveClient = new FakePaymentClient(retrieveResponse: new PassportPaymentResponse { Id = "x", Status = "SETTLED" });
            var result = await ExecuteRetrieve(CreateConfig(privateCaptureDir: captureDir), retrieveClient);

            Assert.Equal("SETTLED", result.Evidence!.ResponseSanitized["status"]);
        }
        finally
        {
            if (Directory.Exists(captureDir)) Directory.Delete(captureDir, recursive: true);
        }
    }

    [Fact]
    public async Task Retrieve_FakeRejectedWithEmbeddedError_CapturedFactually()
    {
        var captureDir = NewTempDir("xpay-m5-payment-retrieve-rejected");
        try
        {
            var createClient = new FakePaymentClient();
            await ExecuteCreate(CreateConfig(privateCaptureDir: captureDir), createClient);

            var retrieveClient = new FakePaymentClient(retrieveResponse: new PassportPaymentResponse
            {
                Id = "x",
                Status = "REJECTED",
                Error = new PassportPaymentErrorResponse { ErrorCode = "B002", ErrorDescription = "B002" },
            });
            var result = await ExecuteRetrieve(CreateConfig(privateCaptureDir: captureDir), retrieveClient);

            Assert.Equal("REJECTED", result.Evidence!.ResponseSanitized["status"]);
            Assert.True((bool)result.Evidence.ResponseSanitized["error_present"]!);
            Assert.Equal("B002", result.Evidence.ResponseSanitized["safe_error_code"]);
        }
        finally
        {
            if (Directory.Exists(captureDir)) Directory.Delete(captureDir, recursive: true);
        }
    }

    [Fact]
    public async Task Retrieve_FakeTransportError_CapturedSanitized()
    {
        var captureDir = NewTempDir("xpay-m5-payment-retrieve-error");
        try
        {
            var createClient = new FakePaymentClient();
            await ExecuteCreate(CreateConfig(privateCaptureDir: captureDir), createClient);

            var retrieveClient = new FakePaymentClient(exceptionToThrow: new PassportTransportException(
                "sintético", statusCode: 404, safeErrorCode: "resource_not_found", safeErrorMessage: "payment not found."));
            var result = await ExecuteRetrieve(CreateConfig(privateCaptureDir: captureDir), retrieveClient);

            Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
            Assert.Equal(404, result.Evidence!.ResponseSanitized["observed_http_status"]);
        }
        finally
        {
            if (Directory.Exists(captureDir)) Directory.Delete(captureDir, recursive: true);
        }
    }

    [Fact]
    public async Task Retrieve_NoRetry_ExactlyOneCallEvenOnFailure()
    {
        var captureDir = NewTempDir("xpay-m5-payment-retrieve-noretry");
        try
        {
            var createClient = new FakePaymentClient();
            await ExecuteCreate(CreateConfig(privateCaptureDir: captureDir), createClient);

            var retrieveClient = new FakePaymentClient(throwOnRetrieve: true);
            await ExecuteRetrieve(CreateConfig(privateCaptureDir: captureDir), retrieveClient);

            Assert.Equal(1, retrieveClient.RetrieveCallCount);
        }
        finally
        {
            if (Directory.Exists(captureDir)) Directory.Delete(captureDir, recursive: true);
        }
    }

    [Fact]
    public async Task Retrieve_EvidenceNeverContainsRawPaymentId()
    {
        var captureDir = NewTempDir("xpay-m5-payment-retrieve-sanitized");
        try
        {
            var createClient = new FakePaymentClient(createResponse: new PassportPaymentResponse { Id = "synthetic-retrieve-raw-id", Status = "PENDING" });
            await ExecuteCreate(CreateConfig(privateCaptureDir: captureDir), createClient);

            var retrieveClient = new FakePaymentClient();
            var result = await ExecuteRetrieve(CreateConfig(privateCaptureDir: captureDir), retrieveClient);

            var evidenceJson = System.Text.Json.JsonSerializer.Serialize(result.Evidence);
            Assert.DoesNotContain("synthetic-retrieve-raw-id", evidenceJson);
            Assert.Contains("payment_id_fingerprint", evidenceJson);
        }
        finally
        {
            if (Directory.Exists(captureDir)) Directory.Delete(captureDir, recursive: true);
        }
    }
}
