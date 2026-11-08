using System.Reflection;
using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-619 — prueba, SIN RED, el ejecutor del subcaso M5-T5-C ("Exceso de
// límites de Bre-B — transacción superior a $12 millones COP"):
//
//  - el monto es la constante de la clase (NUNCA del entorno) y pasa una
//    validación de formato y umbral estricta;
//  - el resolution_id se lee EXCLUSIVAMENTE de la captura privada del
//    Resolve positivo, y debe estar no consumido, vigente con margen y
//    correlacionado con la llave positiva;
//  - invoca ÚNICAMENTE IPassportPaymentClient.CreateBrebPaymentAsync, una
//    sola vez, nunca RetrievePaymentAsync, y estructuralmente nunca
//    IPassportKeyClient;
//  - el repeat guard se consume ANTES del HTTP, así que ninguna respuesta
//    — ni un error inesperado — habilita un segundo intento;
//  - un rechazo sin señal inequívoca de límite es INCONCLUSIVE, no PASS.
//
// El único colaborador remoto es un fake en memoria: ningún test abre un
// socket.
public class CreatePaymentOverLimitExecutorTests
{
    private sealed class FakePaymentClient : IPassportPaymentClient
    {
        private readonly PassportPaymentResponse? _response;
        private readonly Exception? _exceptionToThrow;

        public int CreateBrebPaymentCallCount { get; private set; }
        public int RetrievePaymentCallCount { get; private set; }
        public PassportCreatePaymentRequest? LastRequest { get; private set; }

        public FakePaymentClient(PassportPaymentResponse? response = null, Exception? exceptionToThrow = null)
        {
            _response = response;
            _exceptionToThrow = exceptionToThrow;
        }

        public Task<PassportPaymentResponse> CreateBrebPaymentAsync(
            PassportCreatePaymentRequest request, CancellationToken cancellationToken = default)
        {
            CreateBrebPaymentCallCount++;
            LastRequest = request;
            if (_exceptionToThrow is not null) throw _exceptionToThrow;
            return Task.FromResult(_response ?? new PassportPaymentResponse
            {
                Id = "synthetic-over-limit-payment-id", Status = "PROCESSING",
            });
        }

        public Task<PassportPaymentResponse> RetrievePaymentAsync(string paymentId, CancellationToken cancellationToken = default)
        {
            RetrievePaymentCallCount++;
            throw new InvalidOperationException("RetrievePaymentAsync NUNCA debe invocarse en M5-T5-C.");
        }
    }

    private static readonly DateTime FixedTime = new(2026, 1, 1, 12, 00, 0, DateTimeKind.Utc);

    private static readonly string SyntheticRepoRoot =
        Path.Combine(Path.GetTempPath(), "xpay-m5-t5-c-synthetic-repo-root-for-tests");

    private static string NewTempDir(string prefix) =>
        Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));

    private static ICommitShaProvider CommitSha() =>
        new FixedCommitShaProvider("synthetic-m5-t5-c-commit-sha-00000000000000000000000000000000");

    private const string SyntheticAccountId   = "synthetic-operational-account-id";
    private const string SyntheticPositiveKey = "0099999999";
    private const string SyntheticResolutionId = "synthetic-resolution-id-from-m5-t2";
    private const string ArtifactTimestamp     = "2026-01-01T11-50-00Z";

    private static IConfiguration CreateConfig(
        string? privateCaptureDir,
        string? accountId = SyntheticAccountId,
        string? positiveKeyValue = SyntheticPositiveKey)
    {
        var dict = new Dictionary<string, string?>();
        if (privateCaptureDir is not null) dict[M5PaymentPrivateCapture.EnvPrivateCaptureDir] = privateCaptureDir;
        if (accountId is not null) dict[PassportOptions.EnvOperationalAccountId] = accountId;
        if (positiveKeyValue is not null) dict[HarnessTargetConfig.EnvBrebKeyValue] = positiveKeyValue;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static string FullDirFor(string dir) => Path.GetFullPath(dir, SyntheticRepoRoot);

    // Captura privada del Resolve positivo, con la MISMA forma que escribe
    // M5ResolveExecutor.
    private static void PrepareResolveArtifact(
        string fullDir,
        string? expiresAtRaw = "2026-01-01T12-20-00Z",   // sobrescrito abajo por un ISO real
        string resolutionId = SyntheticResolutionId,
        string? keyValueFingerprint = null,
        string caseId = "M5-T2-RESOLVE",
        string timestamp = ArtifactTimestamp)
    {
        M5PaymentPrivateCapture.EnsureDirectory(fullDir);
        M5PaymentPrivateCapture.Write(fullDir, "M5-T2-RESOLVE", timestamp, new Dictionary<string, object?>
        {
            ["case_id"]                 = caseId,
            ["backend_commit_sha"]      = "synthetic-commit",
            ["captured_at_utc"]         = "2026-01-01T11:50:00Z",
            ["customer_id_fingerprint"] = Fingerprint.Compute("synthetic-customer"),
            ["key_value_fingerprint"]   = keyValueFingerprint ?? Fingerprint.Compute(SyntheticPositiveKey),
            ["resolution_id"]           = resolutionId,
            ["resolved_at_raw"]         = "2026-01-01T11:50:00.000Z",
            ["expires_at_raw"]          = expiresAtRaw,
        });
    }

    private static void PrepareConsumedMarker(string fullDir, string timestamp = ArtifactTimestamp)
    {
        M5PaymentPrivateCapture.EnsureDirectory(fullDir);
        M5PaymentPrivateCapture.Write(fullDir, "M5-T2-RESOLVE-CONSUMED", timestamp,
            new Dictionary<string, object?> { ["case_id"] = "M5-T2-RESOLVE-CONSUMED" });
    }

    // Vigente: 20 min por delante del FixedTime (margen exigido = 60 s).
    private const string ValidExpiresAt = "2026-01-01T12:20:00.000Z";

    private static Task<KeyOperationResult> Execute(IConfiguration config, FakePaymentClient client, DateTime? now = null) =>
        CreatePaymentOverLimitExecutor.ExecuteAsync(
            config, client, CommitSha(), now ?? FixedTime, automatedTestReference: null,
            repositoryRootOverride: SyntheticRepoRoot);

    private static (string dir, string fullDir) ValidSetup(string prefix)
    {
        var dir = NewTempDir(prefix);
        var fullDir = FullDirFor(dir);
        PrepareResolveArtifact(fullDir, expiresAtRaw: ValidExpiresAt);
        return (dir, fullDir);
    }

    // ══════════════════════════════════════════════════════════════════
    // 1 — CASE_ID EXCLUSIVO
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void CaseId_IsExclusive_AndNeverAPositiveOrOtherNegativeCase()
    {
        Assert.Equal("M5-T5-OVER-LIMIT", CreatePaymentOverLimitEvidenceBuilder.CaseId);
        Assert.NotEqual(CreateBrebPaymentCertificationEvidenceBuilder.CaseId, CreatePaymentOverLimitEvidenceBuilder.CaseId);
        Assert.NotEqual(ResolveM5T5DeletedKeyEvidenceBuilder.CaseId, CreatePaymentOverLimitEvidenceBuilder.CaseId);
        Assert.NotEqual(ResolveM5T5SuspendedKeyEvidenceBuilder.CaseId, CreatePaymentOverLimitEvidenceBuilder.CaseId);
        Assert.Equal("M5-T5-OVER-LIMIT-CONSUMED", CreatePaymentOverLimitExecutor.GuardCaseId);
    }

    // ══════════════════════════════════════════════════════════════════
    // 2-8 — VALIDACIÓN DEL MONTO
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Amount_12000001_00_IsAcceptedLocally()
    {
        Assert.Equal(CreatePaymentOverLimitExecutor.AmountValidation.Valid,
            CreatePaymentOverLimitExecutor.ValidateAmount("12000001.00", "COP"));
        Assert.Equal("12000001.00", CreatePaymentOverLimitExecutor.CertificationAmountValue);
    }

    [Theory]
    // 3 — exactamente el umbral NO es "superior a".
    [InlineData("12000000.00", CreatePaymentOverLimitExecutor.AmountValidation.NotAboveThreshold)]
    // 4 — montos inferiores.
    [InlineData("11999999.99", CreatePaymentOverLimitExecutor.AmountValidation.NotAboveThreshold)]
    [InlineData("1.00",        CreatePaymentOverLimitExecutor.AmountValidation.NotAboveThreshold)]
    [InlineData("0.00",        CreatePaymentOverLimitExecutor.AmountValidation.NotAboveThreshold)]
    // 5 — sin los dos decimales.
    [InlineData("12000001",    CreatePaymentOverLimitExecutor.AmountValidation.MalformedFormat)]
    // 6 — formato regional con coma.
    [InlineData("12000001,00",   CreatePaymentOverLimitExecutor.AmountValidation.MalformedFormat)]
    [InlineData("12.000.001,00", CreatePaymentOverLimitExecutor.AmountValidation.MalformedFormat)]
    [InlineData("12.000.001.00", CreatePaymentOverLimitExecutor.AmountValidation.MalformedFormat)]
    // 7 — más o menos de dos decimales.
    [InlineData("12000001.0",   CreatePaymentOverLimitExecutor.AmountValidation.MalformedFormat)]
    [InlineData("12000001.000", CreatePaymentOverLimitExecutor.AmountValidation.MalformedFormat)]
    [InlineData("12000001.",    CreatePaymentOverLimitExecutor.AmountValidation.MalformedFormat)]
    // formas adicionales que tampoco deben pasar
    [InlineData(" 12000001.00", CreatePaymentOverLimitExecutor.AmountValidation.MalformedFormat)]
    [InlineData("12000001.00 ", CreatePaymentOverLimitExecutor.AmountValidation.MalformedFormat)]
    [InlineData("+12000001.00", CreatePaymentOverLimitExecutor.AmountValidation.MalformedFormat)]
    [InlineData("-12000001.00", CreatePaymentOverLimitExecutor.AmountValidation.MalformedFormat)]
    [InlineData("1.2e7",        CreatePaymentOverLimitExecutor.AmountValidation.MalformedFormat)]
    [InlineData("12000001.00COP", CreatePaymentOverLimitExecutor.AmountValidation.MalformedFormat)]
    [InlineData("",   CreatePaymentOverLimitExecutor.AmountValidation.Missing)]
    [InlineData(null, CreatePaymentOverLimitExecutor.AmountValidation.Missing)]
    public void Amount_InvalidForms_AreBlockedLocally(string? value, CreatePaymentOverLimitExecutor.AmountValidation expected)
    {
        Assert.Equal(expected, CreatePaymentOverLimitExecutor.ValidateAmount(value, "COP"));
    }

    [Theory] // 8 — currency distinta de COP.
    [InlineData("USD")]
    [InlineData("cop")]
    [InlineData("")]
    [InlineData(null)]
    public void Amount_NonCopCurrency_IsBlockedLocally(string? currency)
    {
        Assert.Equal(CreatePaymentOverLimitExecutor.AmountValidation.UnsupportedCurrency,
            CreatePaymentOverLimitExecutor.ValidateAmount("12000001.00", currency));
    }

    [Fact]
    public void Amount_IsNeverReadFromEnvironment()
    {
        // El entorno trae el monto del caso POSITIVO (1.00). El ejecutor
        // debe ignorarlo por completo y usar su propia constante.
        var (dir, _) = ValidSetup("xpay-m5-t5-c-amount-not-from-env");
        var dict = new Dictionary<string, string?>
        {
            [M5PaymentPrivateCapture.EnvPrivateCaptureDir] = dir,
            [PassportOptions.EnvOperationalAccountId] = SyntheticAccountId,
            [HarnessTargetConfig.EnvBrebKeyValue] = SyntheticPositiveKey,
            ["PASSPORT_TEST_M5_PAYMENT_AMOUNT_VALUE"] = "1.00",
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var client = new FakePaymentClient();

        var result = Execute(config, client).GetAwaiter().GetResult();

        Assert.Equal(1, client.CreateBrebPaymentCallCount);
        Assert.Equal("12000001.00", client.LastRequest!.Amount.Value);
        Assert.NotEqual("1.00", client.LastRequest.Amount.Value);
        Assert.Equal("COP", client.LastRequest.Amount.Currency);
        Assert.NotNull(result.Evidence);
    }

    // ══════════════════════════════════════════════════════════════════
    // 9-13 — RESOLUCIÓN: FUENTE, VIGENCIA, CONSUMO, CORRELACIÓN
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MissingPrivateCaptureDir_LocalBlocked_NoPayment()
    {
        var client = new FakePaymentClient();
        var result = await Execute(CreateConfig(privateCaptureDir: null), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateBrebPaymentCallCount);
        Assert.Null(result.Evidence);
    }

    [Fact]
    public async Task MissingOperationalAccountId_LocalBlocked_NoPayment()
    {
        var (dir, _) = ValidSetup("xpay-m5-t5-c-no-account");
        var client = new FakePaymentClient();

        var result = await Execute(CreateConfig(dir, accountId: null), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateBrebPaymentCallCount);
    }

    [Fact] // 9
    public async Task AbsentResolution_BlocksPayment()
    {
        var dir = NewTempDir("xpay-m5-t5-c-no-resolution");
        M5PaymentPrivateCapture.EnsureDirectory(FullDirFor(dir));
        var client = new FakePaymentClient();

        var result = await Execute(CreateConfig(dir), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Contains("LOCAL_BLOCKED_INVALID_OR_EXPIRED_RESOLUTION", result.Detail);
        Assert.Contains("ArtifactMissing", result.Detail);
        Assert.Equal(0, client.CreateBrebPaymentCallCount);
    }

    [Fact] // 10
    public async Task ExpiredResolution_BlocksPayment()
    {
        var dir = NewTempDir("xpay-m5-t5-c-expired");
        PrepareResolveArtifact(FullDirFor(dir), expiresAtRaw: "2026-01-01T11:59:00.000Z"); // 1 min antes del now
        var client = new FakePaymentClient();

        var result = await Execute(CreateConfig(dir), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Contains("Expired", result.Detail);
        Assert.Equal(0, client.CreateBrebPaymentCallCount);
    }

    [Fact]
    public async Task ResolutionWithinSafetyMargin_BlocksPayment()
    {
        // Vigente en sentido estricto (+30 s) pero por debajo del margen
        // mínimo de 60 s — se bloquea para no gastar el único intento en
        // una resolución que puede vencer en vuelo.
        var dir = NewTempDir("xpay-m5-t5-c-margin");
        PrepareResolveArtifact(FullDirFor(dir), expiresAtRaw: "2026-01-01T12:00:30.000Z");
        var client = new FakePaymentClient();

        var result = await Execute(CreateConfig(dir), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Contains("Expired", result.Detail);
        Assert.Equal(0, client.CreateBrebPaymentCallCount);
    }

    [Theory] // 11
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-es-una-fecha")]
    [InlineData("31/12/2026")]
    public async Task UnparseableExpiresAt_BlocksPayment(string? expiresAtRaw)
    {
        var dir = NewTempDir("xpay-m5-t5-c-bad-expiry");
        PrepareResolveArtifact(FullDirFor(dir), expiresAtRaw: expiresAtRaw);
        var client = new FakePaymentClient();

        var result = await Execute(CreateConfig(dir), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Contains("ExpiresAtUnparseable", result.Detail);
        Assert.Equal(0, client.CreateBrebPaymentCallCount);
    }

    [Fact] // 12
    public async Task ConsumedResolution_BlocksPayment()
    {
        var dir = NewTempDir("xpay-m5-t5-c-consumed");
        var fullDir = FullDirFor(dir);
        PrepareResolveArtifact(fullDir, expiresAtRaw: ValidExpiresAt);
        PrepareConsumedMarker(fullDir);
        var client = new FakePaymentClient();

        var result = await Execute(CreateConfig(dir), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Contains("AlreadyConsumed", result.Detail);
        Assert.Equal(0, client.CreateBrebPaymentCallCount);
    }

    [Fact]
    public async Task ResolutionOfAnotherKey_BlocksPayment()
    {
        // La resolución debe pertenecer a la llave POSITIVA. Una resolución
        // de un fixture negativo (u otra llave cualquiera) no sirve.
        var dir = NewTempDir("xpay-m5-t5-c-key-mismatch");
        PrepareResolveArtifact(FullDirFor(dir), expiresAtRaw: ValidExpiresAt,
            keyValueFingerprint: Fingerprint.Compute("0091234567"));
        var client = new FakePaymentClient();

        var result = await Execute(CreateConfig(dir), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Contains("KeyCorrelationMismatch", result.Detail);
        Assert.Equal(0, client.CreateBrebPaymentCallCount);
    }

    [Fact]
    public async Task AmbiguousResolution_BlocksPayment()
    {
        var dir = NewTempDir("xpay-m5-t5-c-ambiguous");
        var fullDir = FullDirFor(dir);
        PrepareResolveArtifact(fullDir, expiresAtRaw: ValidExpiresAt, timestamp: "2026-01-01T11-50-00Z");
        PrepareResolveArtifact(fullDir, expiresAtRaw: ValidExpiresAt, timestamp: "2026-01-01T11-51-00Z",
            resolutionId: "otra-resolucion");
        var client = new FakePaymentClient();

        var result = await Execute(CreateConfig(dir), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Contains("ArtifactAmbiguous", result.Detail);
        Assert.Equal(0, client.CreateBrebPaymentCallCount);
    }

    [Fact]
    public async Task WrongCaseIdInArtifact_BlocksPayment()
    {
        var dir = NewTempDir("xpay-m5-t5-c-wrong-caseid");
        PrepareResolveArtifact(FullDirFor(dir), expiresAtRaw: ValidExpiresAt, caseId: "ALGO-DISTINTO");
        var client = new FakePaymentClient();

        var result = await Execute(CreateConfig(dir), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Contains("ArtifactInvalid", result.Detail);
        Assert.Equal(0, client.CreateBrebPaymentCallCount);
    }

    [Fact] // 13
    public async Task ValidResolution_ReachesThePaymentClient_WithResolutionIdFromTheArtifact()
    {
        var (dir, _) = ValidSetup("xpay-m5-t5-c-valid");
        var client = new FakePaymentClient();

        var result = await Execute(CreateConfig(dir), client);

        Assert.Equal(1, client.CreateBrebPaymentCallCount);
        Assert.Equal(SyntheticResolutionId, client.LastRequest!.ResolutionId);
        Assert.Equal(SyntheticAccountId, client.LastRequest.AccountId);
        Assert.Equal("12000001.00", client.LastRequest.Amount.Value);
        Assert.Null(client.LastRequest.DisplayName);
        Assert.NotNull(result.Evidence);
    }

    // ══════════════════════════════════════════════════════════════════
    // 14 — REPEAT GUARD
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SecondAttempt_IsLocalBlocked_NoSecondPayment()
    {
        var (dir, fullDir) = ValidSetup("xpay-m5-t5-c-repeat");
        var config = CreateConfig(dir);

        var first = await Execute(config, new FakePaymentClient());
        Assert.NotEqual(KeyOperationOutcome.LocalBlocked, first.Outcome);
        Assert.True(File.Exists(CreatePaymentOverLimitExecutor.GuardMarkerPath(fullDir)));

        var secondClient = new FakePaymentClient();
        var second = await Execute(config, secondClient);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, second.Outcome);
        Assert.Contains("ALREADY_EXECUTED", second.Detail);
        Assert.Equal(0, secondClient.CreateBrebPaymentCallCount);
    }

    [Fact]
    public async Task RepeatGuard_IsConsumedBeforeHttp_SoAnUnexpectedCrashCannotEnableASecondPayment()
    {
        // Un error NO contemplado (no Passport*) sale como excepción, pero
        // el marcador ya fue escrito antes del HTTP.
        var (dir, fullDir) = ValidSetup("xpay-m5-t5-c-guard-before-http");
        var config = CreateConfig(dir);
        var crashing = new FakePaymentClient(exceptionToThrow: new InvalidOperationException("fallo sintético inesperado"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Execute(config, crashing));

        Assert.Equal(1, crashing.CreateBrebPaymentCallCount);
        Assert.True(File.Exists(CreatePaymentOverLimitExecutor.GuardMarkerPath(fullDir)));

        var secondClient = new FakePaymentClient();
        var second = await Execute(config, secondClient);
        Assert.Equal(KeyOperationOutcome.LocalBlocked, second.Outcome);
        Assert.Equal(0, secondClient.CreateBrebPaymentCallCount);
    }

    [Fact]
    public async Task RepeatGuard_IsIndependentFromOtherNegativeCases()
    {
        var (dir, fullDir) = ValidSetup("xpay-m5-t5-c-independent-guards");
        // Marcadores de T5-D y T5-E presentes en el mismo directorio.
        M5PaymentPrivateCapture.Write(fullDir, SuspendM5T5FixtureKeyExecutor.SuspendGuardCaseId, "MARKER",
            new Dictionary<string, object?> { ["outcome"] = "EXPECTED_SUSPEND_SUCCESS" });
        M5PaymentPrivateCapture.Write(fullDir, DeleteM5T5FixtureKeyExecutor.DeleteGuardCaseId, "MARKER",
            new Dictionary<string, object?> { ["outcome"] = "EXPECTED_DELETE_SUCCESS" });
        var client = new FakePaymentClient();

        var result = await Execute(CreateConfig(dir), client);

        Assert.NotEqual(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(1, client.CreateBrebPaymentCallCount);
    }

    // ══════════════════════════════════════════════════════════════════
    // 15-16 — CLASIFICADOR DE SEÑAL DE LÍMITE
    // ══════════════════════════════════════════════════════════════════

    [Theory]
    // Códigos estructurados históricos (XPAY-611), reconocidos si aparecen.
    [InlineData(400, "U112", "Maximum amount check failed.", true)]
    [InlineData(400, "B107", null, true)]
    [InlineData(400, "B108", null, true)]
    [InlineData(400, "B109", null, true)]
    [InlineData(400, "u112", null, true)]
    // El código dentro del mensaje, como ocurrió en T5-D con U805.
    [InlineData(400, "generic_error", "Payment rejected: (U112)", true)]
    // Señal textual inequívoca SIN código estructurado — vía suficiente.
    [InlineData(400, "generic_error", "Maximum amount exceeded for this transaction", true)]
    [InlineData(400, "generic_error", "The amount exceeds the maximum allowed", true)]
    [InlineData(400, "generic_error", "amount limit reached", true)]
    [InlineData(400, "generic_error", "Excede el monto máximo permitido", true)]
    [InlineData(400, "generic_error", "El monto supera el límite autorizado", true)]
    [InlineData(400, "limit_exceeded", null, true)]
    // 16 — generic_error SIN señal de límite -> UNKNOWN, nunca PASS.
    [InlineData(400, "generic_error", null, null)]
    [InlineData(400, "generic_error", "", null)]
    [InlineData(400, "generic_error", "Something went wrong", null)]
    // Errores ajenos al monto -> UNKNOWN, nunca PASS.
    [InlineData(400, "invalid_parameter_value", "UUID version 4 expected", null)]
    [InlineData(400, "generic_error", "Key operation rejected: Key has been suspended by the Client. (U805)", null)]
    [InlineData(404, "resource_not_found", "key not found.", null)]
    [InlineData(401, "unauthorized", "invalid credentials", null)]
    [InlineData(500, null, null, null)]
    // Un token parecido pero no igual no debe disparar.
    [InlineData(400, "AB107XYZ", null, null)]
    public void ClassifyLimitSignal_IsConservative_AndNeverFalse(
        int? httpStatus, string? code, string? message, bool? expected)
    {
        Assert.Equal(expected, CreatePaymentOverLimitExecutor.ClassifyLimitSignal(httpStatus, code, message));
    }

    [Fact] // 15
    public async Task RejectionWithUnambiguousLimitSignal_IsPassOfTheCase()
    {
        var (dir, _) = ValidSetup("xpay-m5-t5-c-limit-rejection");
        var client = new FakePaymentClient(exceptionToThrow: new PassportTransportException(
            "sintético", statusCode: 400, safeErrorCode: "U112", safeErrorMessage: "Maximum amount check failed."));

        var result = await Execute(CreateConfig(dir), client);

        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        var e = result.Evidence!;
        Assert.Equal("M5-T5-OVER-LIMIT", e.CaseId);
        Assert.Equal(EvidenceRecord.ResultFail, e.Result);
        Assert.Equal(true, e.ResponseSanitized["limit_rejection"]);
        Assert.Equal("EXPECTED_LIMIT_REJECTION", e.ResponseSanitized["certification_case_result"]);
        Assert.Equal("PASS", e.ResponseSanitized["t5_c_result"]);
        Assert.Equal(false, e.ResponseSanitized["payment_accepted"]);
        Assert.Equal(1, e.ResponseSanitized["payment_real_calls"]);
        Assert.Equal(0, e.ResponseSanitized["resolve_real_calls"]);
        Assert.Equal(0, e.ResponseSanitized["retrieve_real_calls"]);
    }

    [Fact] // 16
    public async Task RejectionWithoutLimitSignal_IsInconclusive_NotPass()
    {
        var (dir, _) = ValidSetup("xpay-m5-t5-c-generic-rejection");
        var client = new FakePaymentClient(exceptionToThrow: new PassportTransportException(
            "sintético", statusCode: 400, safeErrorCode: "generic_error", safeErrorMessage: null));

        var result = await Execute(CreateConfig(dir), client);

        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        var e = result.Evidence!;
        Assert.Null(e.ResponseSanitized["limit_rejection"]);
        Assert.Equal("REJECTION_WITHOUT_LIMIT_SIGNAL", e.ResponseSanitized["certification_case_result"]);
        Assert.Equal("INCONCLUSIVE", e.ResponseSanitized["t5_c_result"]);
    }

    [Fact]
    public async Task RejectionInsideA200Body_IsTreatedAsRejection_NeverAsAcceptance()
    {
        var (dir, _) = ValidSetup("xpay-m5-t5-c-body-rejection");
        var client = new FakePaymentClient(response: new PassportPaymentResponse
        {
            Id = "synthetic-payment-id", Status = "REJECTED",
            Error = new PassportPaymentErrorResponse { ErrorCode = "U112", ErrorDescription = "Maximum amount check failed." },
        });

        var result = await Execute(CreateConfig(dir), client);

        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        Assert.Equal(true, result.Evidence!.ResponseSanitized["limit_rejection"]);
        Assert.Equal(false, result.Evidence.ResponseSanitized["payment_accepted"]);
        Assert.Equal("PASS", result.Evidence.ResponseSanitized["t5_c_result"]);
    }

    // ══════════════════════════════════════════════════════════════════
    // 17 — ACEPTACIÓN INESPERADA
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UnexpectedAcceptance_IsInconclusive_AndNeverRetrieves()
    {
        var (dir, _) = ValidSetup("xpay-m5-t5-c-accepted");
        var client = new FakePaymentClient(response: new PassportPaymentResponse
        {
            Id = "synthetic-accepted-payment-id", Status = "PROCESSING",
        });

        var result = await Execute(CreateConfig(dir), client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Contains("PAYMENT_ACCEPTED_UNEXPECTEDLY=YES", result.Detail);
        var e = result.Evidence!;
        Assert.Equal(true, e.ResponseSanitized["payment_accepted_unexpectedly"]);
        Assert.Null(e.ResponseSanitized["limit_rejection"]);
        Assert.Equal("UNEXPECTED_PAYMENT_ACCEPTANCE", e.ResponseSanitized["certification_case_result"]);
        Assert.Equal("INCONCLUSIVE", e.ResponseSanitized["t5_c_result"]);
        Assert.Equal("PROCESSING", e.ResponseSanitized["returned_status"]);
        Assert.Equal(0, e.ResponseSanitized["retrieve_real_calls"]);
        Assert.Equal(0, client.RetrievePaymentCallCount);
    }

    // ══════════════════════════════════════════════════════════════════
    // 18 — NINGÚN RESULTADO GENERA RETRY
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task NoOutcome_EverProducesMoreThanOnePaymentCall()
    {
        foreach (var client in new[]
        {
            new FakePaymentClient(), // aceptado
            new FakePaymentClient(exceptionToThrow: new PassportTransportException(
                "s", statusCode: 400, safeErrorCode: "U112", safeErrorMessage: "Maximum amount check failed.")),
            new FakePaymentClient(exceptionToThrow: new PassportTransportException(
                "s", statusCode: 500, safeErrorCode: "generic_error", safeErrorMessage: null)),
            new FakePaymentClient(exceptionToThrow: new PassportAuthenticationException("s")),
        })
        {
            var (dir, _) = ValidSetup("xpay-m5-t5-c-no-retry");
            await Execute(CreateConfig(dir), client);
            Assert.Equal(1, client.CreateBrebPaymentCallCount);
            Assert.Equal(0, client.RetrievePaymentCallCount);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // 19-20 — FLAGS EXCLUSIVAS
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void WithoutExecuteFlag_StaysInDryRun()
    {
        Assert.Equal(HarnessAction.DryRun,
            HarnessDecision.Decide([HarnessDecision.CommandCreatePaymentOverLimit], out _));
    }

    [Fact]
    public void WithExecuteButNoConfirmation_Aborts()
    {
        var action = HarnessDecision.Decide(
            [HarnessDecision.CommandCreatePaymentOverLimit, HarnessDecision.FlagExecute], out var reason);
        Assert.Equal(HarnessAction.Aborted, action);
        Assert.NotNull(reason);
    }

    [Theory] // 19 — ninguna flag ajena autoriza T5-C.
    [InlineData(HarnessDecision.FlagConfirmCreatePaymentMissingAmount)]
    [InlineData(HarnessDecision.FlagConfirmCreatePaymentMissingAccount)]
    [InlineData(HarnessDecision.FlagConfirmCreatePaymentInvalidResolution)]
    [InlineData(HarnessDecision.FlagConfirmSuspendM5T5FixtureKey)]
    [InlineData(HarnessDecision.FlagConfirmResolveM5T5SuspendedKey)]
    [InlineData(HarnessDecision.FlagConfirmDeleteM5T5FixtureKey)]
    [InlineData(HarnessDecision.FlagConfirmResolveM5T5DeletedKey)]
    [InlineData(HarnessDecision.FlagConfirmCreateM5T5DeletedKeyFixture)]
    public void AForeignFlag_NeverAuthorizesOverLimit(string foreignFlag)
    {
        var action = HarnessDecision.Decide(
            [HarnessDecision.CommandCreatePaymentOverLimit, HarnessDecision.FlagExecute, foreignFlag], out var reason);
        Assert.Equal(HarnessAction.Aborted, action);
        Assert.NotNull(reason);
    }

    [Theory] // 20 — la flag de T5-C no autoriza ningún otro caso.
    [InlineData(HarnessDecision.CommandCreatePaymentMissingAmount)]
    [InlineData(HarnessDecision.CommandCreatePaymentMissingAccount)]
    [InlineData(HarnessDecision.CommandCreatePaymentInvalidResolution)]
    [InlineData(HarnessDecision.CommandResolveM5T5DeletedKey)]
    [InlineData(HarnessDecision.CommandDeleteM5T5FixtureKey)]
    [InlineData(HarnessDecision.CommandResolveM5T5SuspendedKey)]
    public void TheOverLimitFlag_NeverAuthorizesAnotherCase(string otherCommand)
    {
        var action = HarnessDecision.Decide(
            [otherCommand, HarnessDecision.FlagExecute, HarnessDecision.FlagConfirmCreatePaymentOverLimit],
            out var reason);
        Assert.Equal(HarnessAction.Aborted, action);
        Assert.NotNull(reason);
    }

    [Fact]
    public void OnlyItsOwnFlag_ReachesExecute()
    {
        var action = HarnessDecision.Decide(
            [HarnessDecision.CommandCreatePaymentOverLimit, HarnessDecision.FlagExecute,
             HarnessDecision.FlagConfirmCreatePaymentOverLimit], out var reason);
        Assert.Equal(HarnessAction.Execute, action);
        Assert.Null(reason);
        Assert.Equal(HarnessCommand.CreatePaymentOverLimit,
            HarnessDecision.ParseCommand([HarnessDecision.CommandCreatePaymentOverLimit]));
    }

    // ══════════════════════════════════════════════════════════════════
    // 21 — DRY-RUN SIN HTTP
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void DryRun_NeverReachesTheExecutor_SoNoHttpAndNoGuardConsumption()
    {
        var dir = NewTempDir("xpay-m5-t5-c-dry-run");
        var fullDir = FullDirFor(dir);
        M5PaymentPrivateCapture.EnsureDirectory(fullDir);

        var action = HarnessDecision.Decide([HarnessDecision.CommandCreatePaymentOverLimit], out _);

        Assert.Equal(HarnessAction.DryRun, action);
        // El dry-run no escribe el marcador: el guard real sigue disponible.
        Assert.False(File.Exists(CreatePaymentOverLimitExecutor.GuardMarkerPath(fullDir)));
        // Y la validación pura del monto sí es segura de evaluar sin red.
        Assert.Equal(CreatePaymentOverLimitExecutor.AmountValidation.Valid,
            CreatePaymentOverLimitExecutor.ValidateAmount(
                CreatePaymentOverLimitExecutor.CertificationAmountValue, "COP"));
    }

    // ══════════════════════════════════════════════════════════════════
    // 22-23 — EVIDENCIA SANEADA Y ARTEFACTO PRIVADO
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PublicEvidence_NeverContainsSensitiveValues()
    {
        var (dir, _) = ValidSetup("xpay-m5-t5-c-sanitized");
        var client = new FakePaymentClient(response: new PassportPaymentResponse
        {
            Id = "synthetic-sensitive-payment-id", Status = "PROCESSING",
        });

        var result = await Execute(CreateConfig(dir), client);

        var json = System.Text.Json.JsonSerializer.Serialize(result.Evidence!.RequestSanitized)
                 + System.Text.Json.JsonSerializer.Serialize(result.Evidence.ResponseSanitized);
        Assert.DoesNotContain(SyntheticResolutionId, json);
        Assert.DoesNotContain(SyntheticAccountId, json);
        Assert.DoesNotContain(SyntheticPositiveKey, json);
        Assert.DoesNotContain("synthetic-sensitive-payment-id", json);
        // Sí lleva los fingerprints y el monto, que no son sensibles.
        Assert.Contains(Fingerprint.Compute(SyntheticResolutionId), json);
        Assert.Contains("12000001.00", json);
    }

    [Fact]
    public async Task PrivateArtifacts_StayOutsideTheRepository_AndKeepRawValuesOutOfPublicEvidence()
    {
        var (dir, fullDir) = ValidSetup("xpay-m5-t5-c-private");
        var client = new FakePaymentClient();

        await Execute(CreateConfig(dir), client);

        // El directorio privado está fuera del repositorio sintético.
        Assert.False(fullDir.StartsWith(SyntheticRepoRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        // Y un directorio DENTRO del repositorio es rechazado de plano.
        var insideRepo = await Execute(CreateConfig(SyntheticRepoRoot), new FakePaymentClient());
        Assert.Equal(KeyOperationOutcome.LocalBlocked, insideRepo.Outcome);

        // El marcador de guard es fingerprint-only: nunca el resolution_id crudo.
        var marker = File.ReadAllText(CreatePaymentOverLimitExecutor.GuardMarkerPath(fullDir));
        Assert.DoesNotContain(SyntheticResolutionId, marker);
        Assert.Contains(Fingerprint.Compute(SyntheticResolutionId), marker);
        Assert.Contains("ATTEMPTED", marker);
    }

    // ══════════════════════════════════════════════════════════════════
    // 24 — SEPARACIÓN ESTRUCTURAL DE CAPACIDADES
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void TheExecutor_CannotResolveRetrieveOrMutateKeys()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var t = typeof(CreatePaymentOverLimitExecutor);

        // No conoce IPassportKeyClient por ninguna vía: ni parámetro, ni
        // campo, ni retorno. Eso excluye Resolve, Create, Suspend,
        // Activate, Delete y List Keys de un golpe.
        foreach (var m in t.GetMethods(all))
        {
            foreach (var p in m.GetParameters())
                Assert.False(typeof(IPassportKeyClient).IsAssignableFrom(p.ParameterType),
                    $"{t.Name}.{m.Name} recibe IPassportKeyClient.");
            Assert.False(typeof(IPassportKeyClient).IsAssignableFrom(m.ReturnType),
                $"{t.Name}.{m.Name} devuelve IPassportKeyClient.");
        }
        foreach (var f in t.GetFields(all))
            Assert.False(typeof(IPassportKeyClient).IsAssignableFrom(f.FieldType),
                $"{t.Name} declara un campo IPassportKeyClient.");

        // Su única dependencia remota declarada es IPassportPaymentClient.
        var execute = t.GetMethod("ExecuteAsync", all);
        Assert.NotNull(execute);
        var remote = execute!.GetParameters().Select(p => p.ParameterType)
            .Where(x => x.Namespace == typeof(IPassportPaymentClient).Namespace && x.IsInterface).ToArray();
        Assert.Equal(new[] { typeof(IPassportPaymentClient) }, remote);
    }

    [Fact]
    public async Task TheExecutor_NeverCallsRetrievePayment_OnAnyOutcome()
    {
        foreach (var client in new[]
        {
            new FakePaymentClient(),
            new FakePaymentClient(response: new PassportPaymentResponse
            {
                Id = "x", Status = "REJECTED",
                Error = new PassportPaymentErrorResponse { ErrorCode = "U112", ErrorDescription = "Maximum amount check failed." },
            }),
            new FakePaymentClient(exceptionToThrow: new PassportTransportException(
                "s", statusCode: 400, safeErrorCode: "generic_error", safeErrorMessage: null)),
        })
        {
            var (dir, _) = ValidSetup("xpay-m5-t5-c-no-retrieve");
            await Execute(CreateConfig(dir), client);
            Assert.Equal(0, client.RetrievePaymentCallCount);
        }
    }

    [Fact]
    public void TheThreshold_IsTheAnnexFigure_NotTheUnverifiedApiLimit()
    {
        // El umbral del ejecutor es el único verificado en fuente primaria.
        // El 11,5M de la documentación de API NO se hardcodea (XPAY-618 no
        // pudo re-verificarlo en ninguna fuente local).
        Assert.Equal(12000000.00m, CreatePaymentOverLimitExecutor.MinCertificationThresholdCop);
        Assert.NotEqual(11500000.00m, CreatePaymentOverLimitExecutor.MinCertificationThresholdCop);
        // Y 11.500.001 no bastaría para este caso: el criterio es el Anexo.
        Assert.Equal(CreatePaymentOverLimitExecutor.AmountValidation.NotAboveThreshold,
            CreatePaymentOverLimitExecutor.ValidateAmount("11500001.00", "COP"));
    }
}
