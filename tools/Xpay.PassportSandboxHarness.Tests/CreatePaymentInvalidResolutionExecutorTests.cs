using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-572 — prueba, a nivel de CreatePaymentInvalidResolutionExecutor (sin
// red), que M5-T5-INVALID-RESOLUTION: usa SIEMPRE el resolution_id
// sintético de InvalidResolutionIdGenerator (nunca uno leído de config);
// invoca ÚNICAMENTE IPassportPaymentClient.CreateBrebPaymentAsync (nunca
// RetrievePaymentAsync, bajo ninguna circunstancia — ni siquiera ante
// aceptación inesperada); respeta todos los guards fail-closed antes de
// cualquier HTTP; y el repeat guard PERMANENTE impide una segunda
// ejecución real, sin importar el resultado de la primera.
public class CreatePaymentInvalidResolutionExecutorTests
{
    private sealed class FakePaymentClient : IPassportPaymentClient
    {
        private readonly PassportPaymentResponse? _successResponse;
        private readonly Exception? _exceptionToThrow;

        public int CreateBrebPaymentCallCount { get; private set; }
        public int RetrieveCallCount { get; private set; }
        public PassportCreatePaymentRequest? LastRequest { get; private set; }

        public FakePaymentClient(PassportPaymentResponse? successResponse = null, Exception? exceptionToThrow = null)
        {
            _successResponse = successResponse;
            _exceptionToThrow = exceptionToThrow;
        }

        public Task<PassportPaymentResponse> CreateBrebPaymentAsync(
            PassportCreatePaymentRequest request, CancellationToken cancellationToken = default)
        {
            CreateBrebPaymentCallCount++;
            LastRequest = request;
            if (_exceptionToThrow is not null)
                throw _exceptionToThrow;
            return Task.FromResult(_successResponse ?? new PassportPaymentResponse
            {
                Id = "synthetic-unexpected-payment-id",
                Status = "PROCESSING",
                AccountId = request.AccountId,
                ResolutionId = request.ResolutionId,
            });
        }

        public Task<PassportPaymentResponse> RetrievePaymentAsync(
            string paymentId, CancellationToken cancellationToken = default)
        {
            RetrieveCallCount++;
            throw new InvalidOperationException("RetrievePaymentAsync NUNCA debe invocarse desde M5-T5-INVALID-RESOLUTION.");
        }
    }

    private static readonly DateTime FixedTime = new(2026, 1, 1, 0, 10, 0, DateTimeKind.Utc);

    private static readonly string SyntheticRepoRoot =
        Path.Combine(Path.GetTempPath(), "xpay-m5-t5-f-synthetic-repo-root-for-tests");

    private static string NewTempDir(string prefix) =>
        Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));

    private static ICommitShaProvider CommitSha() =>
        new FixedCommitShaProvider("synthetic-m5-t5-f-commit-sha-0000000000000000000000000000000000");

    private const string SyntheticAccountId = "synthetic-m5-t5-f-account-id";
    private const string SyntheticAmountValue = "1.00";

    private static IConfiguration CreateConfig(
        string? privateCaptureDir = "__AUTO_VALID__",
        string? accountId = SyntheticAccountId,
        string? amountValue = SyntheticAmountValue)
    {
        var dict = new Dictionary<string, string?>();

        var resolvedDir = privateCaptureDir == "__AUTO_VALID__" ? NewTempDir("xpay-m5-t5-f-private-capture") : privateCaptureDir;
        if (resolvedDir is not null) dict[M5PaymentPrivateCapture.EnvPrivateCaptureDir] = resolvedDir;

        if (accountId is not null) dict[PassportOptions.EnvOperationalAccountId] = accountId;
        if (amountValue is not null) dict[CreateBrebPaymentCertificationExecutor.EnvAmountValue] = amountValue;

        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static Task<KeyOperationResult> Execute(IConfiguration config, FakePaymentClient client, DateTime? executedAtUtc = null) =>
        CreatePaymentInvalidResolutionExecutor.ExecuteAsync(
            config, client, CommitSha(), executedAtUtc ?? FixedTime, automatedTestReference: null,
            repositoryRootOverride: SyntheticRepoRoot);

    // ══════════════════════════════════════════════════════════════════
    // GUARDS ANTES DE HTTP
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MissingPrivateCaptureDir_LocalBlocked_NoHttp()
    {
        var config = CreateConfig(privateCaptureDir: null);
        var client = new FakePaymentClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateBrebPaymentCallCount);
        Assert.Null(result.Evidence);
    }

    [Fact]
    public async Task PrivateCaptureDirInsideRepository_LocalBlocked_NoHttp()
    {
        var config = CreateConfig(privateCaptureDir: SyntheticRepoRoot);
        var client = new FakePaymentClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateBrebPaymentCallCount);
    }

    [Fact]
    public async Task MissingAccountId_LocalBlocked_NoHttp()
    {
        var config = CreateConfig(accountId: null);
        var client = new FakePaymentClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateBrebPaymentCallCount);
    }

    [Fact]
    public async Task MissingAmount_LocalBlocked_NoHttp()
    {
        var config = CreateConfig(amountValue: null);
        var client = new FakePaymentClient();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateBrebPaymentCallCount);
    }

    [Fact]
    public async Task CommitShaUnresolvable_IsLocalBlocked_NeverCallsClient()
    {
        var config = CreateConfig();
        var client = new FakePaymentClient();

        var result = await CreatePaymentInvalidResolutionExecutor.ExecuteAsync(
            config, client, new ThrowingCommitShaProvider(), FixedTime,
            repositoryRootOverride: SyntheticRepoRoot);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateBrebPaymentCallCount);
    }

    // ══════════════════════════════════════════════════════════════════
    // REQUEST — SIEMPRE EL RESOLUTION_ID SINTÉTICO, NUNCA UNO DE CONFIG
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Request_AlwaysUsesInvalidResolutionIdGenerator_NeverConfigValue()
    {
        var config = CreateConfig();
        var client = new FakePaymentClient();

        await Execute(config, client);

        Assert.NotNull(client.LastRequest);
        Assert.Equal(InvalidResolutionIdGenerator.Generate(), client.LastRequest!.ResolutionId);
        Assert.Equal(SyntheticAccountId, client.LastRequest.AccountId);
        Assert.Equal(SyntheticAmountValue, client.LastRequest.Amount.Value);
        Assert.Equal(PassportPaymentClient.CopCurrency, client.LastRequest.Amount.Currency);
    }

    // ══════════════════════════════════════════════════════════════════
    // RESULTADO ESPERADO — RECHAZO DE PASSPORT
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PassportRejectsInvalidResolution_ReturnsPassportFailure_WithSafeErrorFields()
    {
        var config = CreateConfig();
        var exception = new PassportTransportException(
            "Passport respondió con error HTTP (sintético).", statusCode: 400,
            safeErrorCode: "B002", safeErrorMessage: "resolution_id inválido (sintético, test).");
        var client = new FakePaymentClient(exceptionToThrow: exception);

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        Assert.Equal(1, client.CreateBrebPaymentCallCount);
        Assert.Equal(0, client.RetrieveCallCount);
        Assert.NotNull(result.Evidence);
        Assert.Equal("M5-T5-INVALID-RESOLUTION", result.Evidence!.CaseId);
        Assert.Equal(EvidenceRecord.ResultFail, result.Evidence.Result);
        Assert.Equal("B002", result.Evidence.ResponseSanitized["safe_error_code"]);
        Assert.Contains("EXPECTED_PROVIDER_REJECTION", result.Evidence.ResponseSanitized["certification_case_result"] as string);
    }

    // ══════════════════════════════════════════════════════════════════
    // RESULTADO INESPERADO — PASSPORT ACEPTA
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PassportUnexpectedlyAccepts_ReturnsSuccess_MarkedUnexpectedAcceptance_NeverRetrieves()
    {
        var config = CreateConfig();
        var client = new FakePaymentClient(successResponse: new PassportPaymentResponse
        {
            Id = "synthetic-unexpected-payment-id", Status = "PROCESSING",
        });

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal(1, client.CreateBrebPaymentCallCount);
        Assert.Equal(0, client.RetrieveCallCount);
        Assert.NotNull(result.Evidence);
        Assert.Equal(EvidenceRecord.ResultPass, result.Evidence!.Result);
        Assert.Equal("UNEXPECTED_ACCEPTANCE", result.Evidence.ResponseSanitized["certification_case_result"]);
        Assert.Contains("UNEXPECTED_ACCEPTANCE", result.Evidence.Notes);
        Assert.DoesNotContain("synthetic-unexpected-payment-id",
            System.Text.Json.JsonSerializer.Serialize(result.Evidence.ResponseSanitized));
    }

    // ══════════════════════════════════════════════════════════════════
    // SANEAMIENTO — NUNCA IDs CRUDOS EN LA EVIDENCIA PÚBLICA
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EvidenceNeverContainsRawAccountIdOrSyntheticResolutionId()
    {
        var config = CreateConfig();
        var client = new FakePaymentClient();

        var result = await Execute(config, client);

        var reqJson = System.Text.Json.JsonSerializer.Serialize(result.Evidence!.RequestSanitized);
        Assert.DoesNotContain(SyntheticAccountId, reqJson);
        Assert.DoesNotContain(InvalidResolutionIdGenerator.Generate(), reqJson);
    }

    // ══════════════════════════════════════════════════════════════════
    // REPEAT GUARD PERMANENTE — NUNCA UNA SEGUNDA LLAMADA REAL
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SecondExecution_AfterExpectedRejection_IsLocalBlocked_NoSecondCall()
    {
        var dir = NewTempDir("xpay-m5-t5-f-repeat-after-rejection");
        var config = CreateConfig(privateCaptureDir: dir);
        var firstClient = new FakePaymentClient(exceptionToThrow: new PassportTransportException(
            "sintético", statusCode: 400, safeErrorCode: "B002", safeErrorMessage: "inválido"));

        var first = await Execute(config, firstClient);
        Assert.Equal(KeyOperationOutcome.PassportFailure, first.Outcome);
        Assert.Equal(1, firstClient.CreateBrebPaymentCallCount);

        var secondClient = new FakePaymentClient();
        var second = await Execute(config, secondClient);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, second.Outcome);
        Assert.Equal(0, secondClient.CreateBrebPaymentCallCount);
        Assert.Contains("ALREADY_EXECUTED", second.Detail);
    }

    [Fact]
    public async Task SecondExecution_AfterUnexpectedAcceptance_IsLocalBlocked_NoSecondCall()
    {
        var dir = NewTempDir("xpay-m5-t5-f-repeat-after-acceptance");
        var config = CreateConfig(privateCaptureDir: dir);
        var firstClient = new FakePaymentClient(successResponse: new PassportPaymentResponse { Id = "x", Status = "PROCESSING" });

        var first = await Execute(config, firstClient);
        Assert.Equal(KeyOperationOutcome.Success, first.Outcome);

        var secondClient = new FakePaymentClient();
        var second = await Execute(config, secondClient);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, second.Outcome);
        Assert.Equal(0, secondClient.CreateBrebPaymentCallCount);
    }

    [Fact]
    public async Task RepeatGuardMarker_WrittenAsFixedFilename_NeverTimestamped()
    {
        var dir = NewTempDir("xpay-m5-t5-f-marker-filename");
        var config = CreateConfig(privateCaptureDir: dir);
        var client = new FakePaymentClient(exceptionToThrow: new PassportTransportException(
            "sintético", statusCode: 400, safeErrorCode: "B002", safeErrorMessage: "inválido"));

        await Execute(config, client);

        var fullDir = Path.GetFullPath(dir, SyntheticRepoRoot);
        var expectedMarker = Path.Combine(fullDir,
            $"{CreatePaymentInvalidResolutionExecutor.RepeatGuardCaseId}-MARKER.private.json");
        Assert.True(File.Exists(expectedMarker));
    }

    private sealed class ThrowingCommitShaProvider : ICommitShaProvider
    {
        public string GetCommitSha() => throw new InvalidOperationException("synthetic: commit SHA no resoluble.");
    }
}
