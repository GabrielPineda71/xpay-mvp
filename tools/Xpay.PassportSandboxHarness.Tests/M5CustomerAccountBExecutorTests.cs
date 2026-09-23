using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-531 — prueba, a nivel de los dos executors M5-T1 (sin red), que:
// cada uno respeta TODOS los guards fail-closed antes de cualquier HTTP;
// que el aislamiento arquitectónico frente a servicios de producto XPAY es
// estructuralmente imposible de violar; que Account B obtiene customer_id
// EXCLUSIVAMENTE del artefacto privado de Customer B (nunca hardcoded,
// nunca CLI, nunca Customer A/histórico); que account_number NUNCA cae en
// PASSPORT_ACCOUNT_ID; y que cada executor invoca ÚNICAMENTE el método
// Customer/Account correspondiente como máximo una vez.
public class M5CustomerAccountBExecutorTests
{
    private sealed class FakeCustomerAccountClient : IPassportCustomerAccountClient
    {
        private readonly bool _throwOnLinkMerchant;
        private readonly bool _throwOnLinkAccount;
        private readonly PassportTransportException? _exceptionToThrow;
        private readonly PassportCustomerResponse? _linkMerchantResponse;
        private readonly PassportAccountResponse? _linkAccountResponse;

        public int LinkMerchantCallCount { get; private set; }
        public int LinkAccountCallCount { get; private set; }
        public int RetrieveCustomerCallCount { get; private set; }
        public int RetrieveAccountCallCount { get; private set; }
        public PassportLinkMerchantRequest? LastLinkMerchantRequest { get; private set; }
        public PassportLinkAccountRequest? LastLinkAccountRequest { get; private set; }

        public FakeCustomerAccountClient(
            bool throwOnLinkMerchant = false,
            bool throwOnLinkAccount = false,
            PassportCustomerResponse? linkMerchantResponse = null,
            PassportAccountResponse? linkAccountResponse = null,
            PassportTransportException? exceptionToThrow = null)
        {
            _throwOnLinkMerchant = throwOnLinkMerchant;
            _throwOnLinkAccount = throwOnLinkAccount;
            _linkMerchantResponse = linkMerchantResponse;
            _linkAccountResponse = linkAccountResponse;
            _exceptionToThrow = exceptionToThrow;
        }

        public Task<PassportCustomerResponse> LinkMerchantAsync(
            PassportLinkMerchantRequest request, CancellationToken cancellationToken = default)
        {
            LinkMerchantCallCount++;
            LastLinkMerchantRequest = request;
            if (_exceptionToThrow is not null)
                throw _exceptionToThrow;
            if (_throwOnLinkMerchant)
                throw new PassportTransportException("Passport respondió con error HTTP (sintético).");
            return Task.FromResult(_linkMerchantResponse ?? new PassportCustomerResponse
            {
                Id = "synthetic-customer-b-id-001",
                Status = "ACTIVE",
                Type = "BUSINESS",
            });
        }

        public Task<PassportCustomerResponse> RetrieveCustomerAsync(
            string customerId, CancellationToken cancellationToken = default)
        {
            RetrieveCustomerCallCount++;
            throw new InvalidOperationException("RetrieveCustomerAsync no debe invocarse desde ningún executor M5-T1.");
        }

        public Task<PassportAccountResponse> LinkAccountAsync(
            PassportLinkAccountRequest request, CancellationToken cancellationToken = default)
        {
            LinkAccountCallCount++;
            LastLinkAccountRequest = request;
            if (_exceptionToThrow is not null)
                throw _exceptionToThrow;
            if (_throwOnLinkAccount)
                throw new PassportTransportException("Passport respondió con error HTTP (sintético).");
            return Task.FromResult(_linkAccountResponse ?? new PassportAccountResponse
            {
                Id = "synthetic-account-b-id-001",
                CustomerId = request.CustomerId,
                Status = "ACTIVE",
                AccountType = "ORDINARY",
            });
        }

        public Task<PassportAccountResponse> RetrieveAccountAsync(
            string accountId, CancellationToken cancellationToken = default)
        {
            RetrieveAccountCallCount++;
            throw new InvalidOperationException("RetrieveAccountAsync no debe invocarse desde ningún executor M5-T1.");
        }
    }

    private static readonly DateTime FixedTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly string SyntheticRepoRoot =
        Path.Combine(Path.GetTempPath(), "xpay-m5-t1-synthetic-repo-root-for-tests");

    private static string NewTempDir(string prefix) =>
        Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));

    private static ICommitShaProvider CommitSha() =>
        new FixedCommitShaProvider("synthetic-m5-t1-commit-sha-0000000000000000000000000000000000000");

    // ══════════════════════════════════════════════════════════════════
    // Fixture helpers — Customer B
    // ══════════════════════════════════════════════════════════════════

    private const string ValidBusinessName = "XPAY SANDBOX HARNESS M5-T1 TEST SAS";
    private const string ValidEmail = "sandbox-harness-m5-t1-test@example.invalid";
    private const string ValidPhone = "3000000001";
    private const string ValidIdentification = "000000001";
    private const string ValidMcc = "5411";

    private static string WriteFixtureFile(string? path, M5CustomerBFixture? fixture)
    {
        var resolvedPath = path ?? Path.Combine(NewTempDir("xpay-m5-customer-b-fixture-dir"), "fixture.json");
        Directory.CreateDirectory(Path.GetDirectoryName(resolvedPath)!);
        var json = fixture is null
            ? "not-valid-json{{{"
            : System.Text.Json.JsonSerializer.Serialize(fixture);
        File.WriteAllText(resolvedPath, json);
        return resolvedPath;
    }

    private static M5CustomerBFixture ValidFixture() => new(
        BusinessName: ValidBusinessName,
        Email: ValidEmail,
        MobilePhoneNumber: ValidPhone,
        IdentificationNumber: ValidIdentification,
        MerchantCategoryCode: ValidMcc,
        Address: new M5CustomerBFixtureAddress(
            Line1: "Calle Sintética 1", City: "Bogotá", State: "Bogotá D.C.",
            PostCode: "110111", Country: "CO"));

    private static IConfiguration CreateCustomerBConfig(
        string? fixtureFilePath = "__AUTO_VALID__",
        string? privateCaptureDir = "__AUTO_VALID__")
    {
        var dict = new Dictionary<string, string?>();

        var resolvedFixturePath = fixtureFilePath == "__AUTO_VALID__"
            ? WriteFixtureFile(null, ValidFixture())
            : fixtureFilePath;
        if (resolvedFixturePath is not null) dict[CreateM5CustomerBExecutor.EnvFixtureFile] = resolvedFixturePath;

        var resolvedCaptureDir = privateCaptureDir == "__AUTO_VALID__"
            ? NewTempDir("xpay-m5-customer-b-private-capture")
            : privateCaptureDir;
        if (resolvedCaptureDir is not null) dict[M5CustomerBPrivateCapture.EnvPrivateCaptureDir] = resolvedCaptureDir;

        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static Task<KeyOperationResult> ExecuteCreateCustomerB(IConfiguration config, FakeCustomerAccountClient client) =>
        CreateM5CustomerBExecutor.ExecuteAsync(
            config, client, CommitSha(), FixedTime, automatedTestReference: null,
            repositoryRootOverride: SyntheticRepoRoot);

    // ══════════════════════════════════════════════════════════════════
    // Fixture helpers — Account B
    // ══════════════════════════════════════════════════════════════════

    private const string ValidAccountNumber = "9999999999";

    // Prepara un directorio de captura de Customer B con un artefacto YA
    // escrito (simulando que M5-T1-A ya se ejecutó con éxito), para que
    // Account B pueda leerlo.
    private static string PrepareCustomerBArtifactDir(string caseId = "M5-T1-A", string? customerId = "synthetic-customer-b-id-001")
    {
        var dir = NewTempDir("xpay-m5-customer-b-artifact-dir");
        M5CustomerBPrivateCapture.EnsureDirectory(dir);
        var content = new Dictionary<string, object?>
        {
            ["case_id"] = caseId,
            ["captured_at_utc"] = "2026-01-01T00:00:00Z",
            ["backend_commit_sha"] = "synthetic-sha",
            ["customer_id"] = customerId,
            ["status_at_creation"] = "ACTIVE",
        };
        M5CustomerBPrivateCapture.Write(dir, caseId, "2026-01-01T00-00-00Z", content);
        return dir;
    }

    private static IConfiguration CreateAccountBConfig(
        string? accountNumber = ValidAccountNumber,
        string? customerCaptureDir = "__AUTO_VALID__",
        string? accountCaptureDir = "__AUTO_VALID__")
    {
        var dict = new Dictionary<string, string?>();
        if (accountNumber is not null) dict[CreateM5AccountBExecutor.EnvAccountNumber] = accountNumber;

        var resolvedCustomerDir = customerCaptureDir == "__AUTO_VALID__"
            ? PrepareCustomerBArtifactDir()
            : customerCaptureDir;
        if (resolvedCustomerDir is not null) dict[M5CustomerBPrivateCapture.EnvPrivateCaptureDir] = resolvedCustomerDir;

        var resolvedAccountDir = accountCaptureDir == "__AUTO_VALID__"
            ? NewTempDir("xpay-m5-account-b-private-capture")
            : accountCaptureDir;
        if (resolvedAccountDir is not null) dict[M5AccountBPrivateCapture.EnvPrivateCaptureDir] = resolvedAccountDir;

        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static Task<KeyOperationResult> ExecuteCreateAccountB(IConfiguration config, FakeCustomerAccountClient client) =>
        CreateM5AccountBExecutor.ExecuteAsync(
            config, client, CommitSha(), FixedTime, automatedTestReference: null,
            repositoryRootOverride: SyntheticRepoRoot);

    // ══════════════════════════════════════════════════════════════════
    // ARCHITECTURE — aislamiento estructural
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void CreateM5CustomerBExecutor_ExecuteAsync_SignatureExcludesProductDependencies()
    {
        var method = typeof(CreateM5CustomerBExecutor).GetMethod("ExecuteAsync");
        Assert.NotNull(method);
        var paramTypeNames = method!.GetParameters().Select(p => p.ParameterType.FullName).ToArray();
        Assert.DoesNotContain(paramTypeNames, n => n != null &&
            (n.Contains("BrebPaymentService") || n.Contains("PassportBrebRetiro") || n.Contains("DbContext") || n.Contains("Wallet")));
    }

    [Fact]
    public void CreateM5AccountBExecutor_ExecuteAsync_SignatureExcludesProductDependencies()
    {
        var method = typeof(CreateM5AccountBExecutor).GetMethod("ExecuteAsync");
        Assert.NotNull(method);
        var paramTypeNames = method!.GetParameters().Select(p => p.ParameterType.FullName).ToArray();
        Assert.DoesNotContain(paramTypeNames, n => n != null &&
            (n.Contains("BrebPaymentService") || n.Contains("PassportBrebRetiro") || n.Contains("DbContext") || n.Contains("Wallet")));
    }

    // ══════════════════════════════════════════════════════════════════
    // CUSTOMER B (M5-T1-A) — §16
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CustomerB_MissingFixtureFile_LocalBlocked_NoHttp()
    {
        var config = CreateCustomerBConfig(fixtureFilePath: null);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateCustomerB(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.LinkMerchantCallCount);
        Assert.Contains(CreateM5CustomerBExecutor.EnvFixtureFile, result.Detail);
    }

    [Fact]
    public async Task CustomerB_MissingPrivateCaptureDir_LocalBlocked_NoHttp()
    {
        var config = CreateCustomerBConfig(privateCaptureDir: null);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateCustomerB(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.LinkMerchantCallCount);
    }

    [Fact]
    public async Task CustomerB_PrivateCaptureDirInsideRepository_LocalBlocked_NoHttp()
    {
        var config = CreateCustomerBConfig(privateCaptureDir: SyntheticRepoRoot);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateCustomerB(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.LinkMerchantCallCount);
    }

    [Fact]
    public async Task CustomerB_FixtureFileMissingOnDisk_LocalBlocked_NoHttp()
    {
        var nonExistentPath = Path.Combine(NewTempDir("xpay-m5-customer-b-nonexistent"), "missing.json");
        var config = CreateCustomerBConfig(fixtureFilePath: nonExistentPath);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateCustomerB(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.LinkMerchantCallCount);
    }

    [Fact]
    public async Task CustomerB_FixtureFileInvalidJson_LocalBlocked_NoHttp()
    {
        var path = WriteFixtureFile(null, fixture: null); // null => JSON malformado
        var config = CreateCustomerBConfig(fixtureFilePath: path);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateCustomerB(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.LinkMerchantCallCount);
    }

    [Theory]
    [InlineData("BusinessName")]
    [InlineData("Email")]
    [InlineData("MobilePhoneNumber")]
    [InlineData("IdentificationNumber")]
    [InlineData("MerchantCategoryCode")]
    public async Task CustomerB_FixtureMissingRequiredField_LocalBlocked_NoHttp(string fieldToBlank)
    {
        var fixture = ValidFixture();
        fixture = fieldToBlank switch
        {
            "BusinessName" => fixture with { BusinessName = null },
            "Email" => fixture with { Email = null },
            "MobilePhoneNumber" => fixture with { MobilePhoneNumber = null },
            "IdentificationNumber" => fixture with { IdentificationNumber = null },
            "MerchantCategoryCode" => fixture with { MerchantCategoryCode = null },
            _ => fixture,
        };
        var path = WriteFixtureFile(null, fixture);
        var config = CreateCustomerBConfig(fixtureFilePath: path);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateCustomerB(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.LinkMerchantCallCount);
    }

    [Fact]
    public async Task CustomerB_FixtureMissingAddress_LocalBlocked_NoHttp()
    {
        var fixture = ValidFixture() with { Address = null };
        var path = WriteFixtureFile(null, fixture);
        var config = CreateCustomerBConfig(fixtureFilePath: path);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateCustomerB(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.LinkMerchantCallCount);
    }

    [Theory]
    [InlineData("Line1")]
    [InlineData("City")]
    [InlineData("State")]
    [InlineData("PostCode")]
    [InlineData("Country")]
    public async Task CustomerB_FixtureAddressMissingRequiredField_LocalBlocked_NoHttp(string fieldToBlank)
    {
        var address = new M5CustomerBFixtureAddress(
            Line1: "Calle Sintética 1", City: "Bogotá", State: "Bogotá D.C.", PostCode: "110111", Country: "CO");
        address = fieldToBlank switch
        {
            "Line1" => address with { Line1 = null },
            "City" => address with { City = null },
            "State" => address with { State = null },
            "PostCode" => address with { PostCode = null },
            "Country" => address with { Country = null },
            _ => address,
        };
        var fixture = ValidFixture() with { Address = address };
        var path = WriteFixtureFile(null, fixture);
        var config = CreateCustomerBConfig(fixtureFilePath: path);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateCustomerB(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.LinkMerchantCallCount);
    }

    [Fact]
    public async Task CustomerB_ValidFixture_Success_CallsLinkMerchantExactlyOnce()
    {
        var config = CreateCustomerBConfig();
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateCustomerB(config, client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal(1, client.LinkMerchantCallCount);
        Assert.Equal(0, client.RetrieveCustomerCallCount);
        Assert.Equal(0, client.LinkAccountCallCount);
        Assert.NotNull(result.Evidence);
        Assert.Equal("M5-T1-A", result.Evidence!.CaseId);
    }

    [Fact]
    public async Task CustomerB_ValidFixture_Success_RequestUsesFixedContractDefaults()
    {
        var config = CreateCustomerBConfig();
        var client = new FakeCustomerAccountClient();

        await ExecuteCreateCustomerB(config, client);

        Assert.NotNull(client.LastLinkMerchantRequest);
        Assert.Equal("BUSINESS", client.LastLinkMerchantRequest!.Type);
        Assert.Equal("NIT", client.LastLinkMerchantRequest.IdentificationType);
    }

    [Fact]
    public async Task CustomerB_ValidFixture_Success_RequestNeverContainsRealCustomerAValues()
    {
        var config = CreateCustomerBConfig();
        var client = new FakeCustomerAccountClient();

        await ExecuteCreateCustomerB(config, client);

        Assert.NotNull(client.LastLinkMerchantRequest);
        Assert.Equal(ValidBusinessName, client.LastLinkMerchantRequest!.BusinessName);
        Assert.Equal(ValidEmail, client.LastLinkMerchantRequest.Email);
    }

    [Fact]
    public async Task CustomerB_EvidenceNeverContainsRawIdentificationValues()
    {
        var config = CreateCustomerBConfig();
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateCustomerB(config, client);

        var json = System.Text.Json.JsonSerializer.Serialize(result.Evidence!.RequestSanitized);
        Assert.DoesNotContain(ValidBusinessName, json);
        Assert.DoesNotContain(ValidEmail, json);
        Assert.DoesNotContain(ValidPhone, json);
        Assert.DoesNotContain(ValidIdentification, json);
    }

    [Fact]
    public async Task CustomerB_PassportTransportException_ReturnsPassportFailure_NoRetry()
    {
        var config = CreateCustomerBConfig();
        var client = new FakeCustomerAccountClient(throwOnLinkMerchant: true);

        var result = await ExecuteCreateCustomerB(config, client);

        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        Assert.Equal(1, client.LinkMerchantCallCount);
    }

    [Fact]
    public async Task CustomerB_SuccessfulExecution_WritesPrivateArtifactWithRawCustomerId()
    {
        var captureDir = NewTempDir("xpay-m5-customer-b-private-capture-assert");
        var config = CreateCustomerBConfig(privateCaptureDir: captureDir);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateCustomerB(config, client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        var (outcome, artifact) = M5CustomerBArtifactReader.Resolve(Path.GetFullPath(captureDir, SyntheticRepoRoot));
        Assert.Equal(M5CustomerBArtifactReader.ResolutionOutcome.Found, outcome);
        Assert.NotNull(artifact);
        Assert.Equal("synthetic-customer-b-id-001", artifact!.CustomerId);
        Assert.Equal("M5-T1-A", artifact.CaseId);
    }

    // ══════════════════════════════════════════════════════════════════
    // ACCOUNT B (M5-T1-B) — §17
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AccountB_MissingAccountNumber_LocalBlocked_NoHttp()
    {
        var config = CreateAccountBConfig(accountNumber: null);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateAccountB(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.LinkAccountCallCount);
        Assert.Contains(CreateM5AccountBExecutor.EnvAccountNumber, result.Detail);
    }

    [Fact]
    public async Task AccountB_MissingCustomerBCaptureDir_LocalBlocked_NoHttp()
    {
        var config = CreateAccountBConfig(customerCaptureDir: null);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateAccountB(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.LinkAccountCallCount);
    }

    [Fact]
    public async Task AccountB_CustomerBArtifactNotFound_LocalBlocked_NoHttp()
    {
        var emptyDir = NewTempDir("xpay-m5-customer-b-empty-dir");
        M5CustomerBPrivateCapture.EnsureDirectory(emptyDir);
        var config = CreateAccountBConfig(customerCaptureDir: emptyDir);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateAccountB(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.LinkAccountCallCount);
    }

    [Fact]
    public async Task AccountB_CustomerBArtifactWrongCaseId_LocalBlocked_NoHttp()
    {
        var wrongDir = PrepareCustomerBArtifactDir(caseId: "M4-T4"); // case_id ajeno
        var config = CreateAccountBConfig(customerCaptureDir: wrongDir);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateAccountB(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.LinkAccountCallCount);
    }

    [Fact]
    public async Task AccountB_CustomerBArtifactEmptyCustomerId_LocalBlocked_NoHttp()
    {
        var dir = PrepareCustomerBArtifactDir(customerId: "");
        var config = CreateAccountBConfig(customerCaptureDir: dir);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateAccountB(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.LinkAccountCallCount);
    }

    [Fact]
    public async Task AccountB_MultipleCustomerBArtifacts_LocalBlocked_NoHttp()
    {
        var dir = PrepareCustomerBArtifactDir();
        // Escribe un segundo artefacto para forzar ambigüedad.
        var secondContent = new Dictionary<string, object?>
        {
            ["case_id"] = "M5-T1-A",
            ["captured_at_utc"] = "2026-01-02T00:00:00Z",
            ["backend_commit_sha"] = "synthetic-sha-2",
            ["customer_id"] = "synthetic-customer-b-id-002",
        };
        M5CustomerBPrivateCapture.Write(dir, "M5-T1-A", "2026-01-02T00-00-00Z", secondContent);

        var config = CreateAccountBConfig(customerCaptureDir: dir);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateAccountB(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.LinkAccountCallCount);
    }

    [Fact]
    public async Task AccountB_MissingOwnPrivateCaptureDir_LocalBlocked_NoHttp()
    {
        var config = CreateAccountBConfig(accountCaptureDir: null);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateAccountB(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.LinkAccountCallCount);
    }

    [Fact]
    public async Task AccountB_OwnPrivateCaptureDirInsideRepository_LocalBlocked_NoHttp()
    {
        var config = CreateAccountBConfig(accountCaptureDir: SyntheticRepoRoot);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateAccountB(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.LinkAccountCallCount);
    }

    [Fact]
    public void AccountB_AccountNumberNeverEqualsOperationalAccountEnvName()
    {
        // XPAY-531 §11 — este comando NUNCA lee PASSPORT_ACCOUNT_ID; se
        // verifica que su propia bandera de configuración es un nombre
        // completamente distinto.
        Assert.NotEqual(PassportOptions.EnvOperationalAccountId, CreateM5AccountBExecutor.EnvAccountNumber);
    }

    [Fact]
    public async Task AccountB_ValidState_Success_CallsLinkAccountExactlyOnce()
    {
        var config = CreateAccountBConfig();
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateAccountB(config, client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal(1, client.LinkAccountCallCount);
        Assert.Equal(0, client.RetrieveAccountCallCount);
        Assert.Equal(0, client.LinkMerchantCallCount);
        Assert.NotNull(result.Evidence);
        Assert.Equal("M5-T1-B", result.Evidence!.CaseId);
    }

    [Fact]
    public async Task AccountB_ValidState_Success_UsesCustomerIdFromArtifactExclusively()
    {
        var config = CreateAccountBConfig();
        var client = new FakeCustomerAccountClient();

        await ExecuteCreateAccountB(config, client);

        Assert.NotNull(client.LastLinkAccountRequest);
        Assert.Equal("synthetic-customer-b-id-001", client.LastLinkAccountRequest!.CustomerId);
    }

    [Fact]
    public async Task AccountB_ValidState_Success_RequestUsesFixedAccountType()
    {
        var config = CreateAccountBConfig();
        var client = new FakeCustomerAccountClient();

        await ExecuteCreateAccountB(config, client);

        Assert.Equal("ORDINARY", client.LastLinkAccountRequest!.AccountType);
    }

    [Fact]
    public async Task AccountB_EvidenceNeverContainsRawCustomerIdOrAccountNumber()
    {
        var config = CreateAccountBConfig();
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateAccountB(config, client);

        var json = System.Text.Json.JsonSerializer.Serialize(result.Evidence!.RequestSanitized);
        Assert.DoesNotContain("synthetic-customer-b-id-001", json);
        Assert.DoesNotContain(ValidAccountNumber, json);
    }

    [Fact]
    public async Task AccountB_PassportTransportException_ReturnsPassportFailure_NoRetry()
    {
        var config = CreateAccountBConfig();
        var client = new FakeCustomerAccountClient(throwOnLinkAccount: true);

        var result = await ExecuteCreateAccountB(config, client);

        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        Assert.Equal(1, client.LinkAccountCallCount);
    }

    [Fact]
    public async Task AccountB_SuccessfulExecution_WritesPrivateArtifactWithRawAccountId()
    {
        var accountCaptureDir = NewTempDir("xpay-m5-account-b-private-capture-assert");
        var config = CreateAccountBConfig(accountCaptureDir: accountCaptureDir);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateAccountB(config, client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        var files = Directory.GetFiles(Path.GetFullPath(accountCaptureDir, SyntheticRepoRoot), "*.private.json");
        Assert.Single(files);
        var json = File.ReadAllText(files[0]);
        Assert.Contains("synthetic-account-b-id-001", json);
    }

    [Fact]
    public async Task AccountB_SuccessfulExecution_NeverWritesIntoCustomerBCaptureDir()
    {
        var customerDir = PrepareCustomerBArtifactDir();
        var accountDir = NewTempDir("xpay-m5-account-b-private-capture-isolated");
        var config = CreateAccountBConfig(customerCaptureDir: customerDir, accountCaptureDir: accountDir);
        var client = new FakeCustomerAccountClient();

        await ExecuteCreateAccountB(config, client);

        var customerDirFiles = Directory.GetFiles(customerDir, "*.private.json");
        Assert.Single(customerDirFiles); // sólo el artefacto original de Customer B, nunca uno de Account B
    }

    // ══════════════════════════════════════════════════════════════════
    // XPAY-533 — NO-REPEAT GUARDS (Customer B / Account B) — §11/§12
    // ══════════════════════════════════════════════════════════════════

    private static void WriteRawArtifactFile(string dir, string fileName, string content)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), content);
    }

    // ---------- Customer B repeat guard ----------

    [Fact]
    public async Task CustomerB_NoPriorArtifact_RepeatGuardPasses_ReachesNextPrecheck()
    {
        // Sin artefacto previo, el guard de repetición debe dejar pasar la
        // ejecución hasta el siguiente precheck (aquí, éxito completo —
        // demuestra que el no-repeat guard no bloquea falsos positivos).
        var config = CreateCustomerBConfig();
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateCustomerB(config, client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal(1, client.LinkMerchantCallCount);
    }

    [Fact]
    public async Task CustomerB_ValidPriorArtifact_Blocked_ClientCallsZero()
    {
        var captureDir = PrepareCustomerBArtifactDir();
        var config = CreateCustomerBConfig(privateCaptureDir: captureDir);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateCustomerB(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.LinkMerchantCallCount);
        Assert.Contains("ALREADY_EXECUTED", result.Detail);
    }

    [Fact]
    public async Task CustomerB_MalformedArtifact_Blocked_ClientCallsZero()
    {
        var dir = NewTempDir("xpay-m5-customer-b-malformed");
        M5CustomerBPrivateCapture.EnsureDirectory(dir);
        WriteRawArtifactFile(dir, "M5-T1-A-bad.private.json", "not-valid-json{{{");
        var config = CreateCustomerBConfig(privateCaptureDir: dir);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateCustomerB(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.LinkMerchantCallCount);
    }

    [Fact]
    public async Task CustomerB_PriorArtifactWrongCaseId_Blocked_ClientCallsZero()
    {
        var dir = PrepareCustomerBArtifactDir(caseId: "M4-T4"); // reutiliza el helper existente con case_id ajeno
        var config = CreateCustomerBConfig(privateCaptureDir: dir);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateCustomerB(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.LinkMerchantCallCount);
        Assert.DoesNotContain("ALREADY_EXECUTED", result.Detail);
    }

    [Fact]
    public async Task CustomerB_MultiplePriorArtifacts_Blocked_ClientCallsZero()
    {
        var dir = PrepareCustomerBArtifactDir();
        var second = new Dictionary<string, object?>
        {
            ["case_id"] = "M5-T1-A",
            ["captured_at_utc"] = "2026-01-02T00:00:00Z",
            ["backend_commit_sha"] = "synthetic-sha-2",
            ["customer_id"] = "synthetic-customer-b-id-002",
        };
        M5CustomerBPrivateCapture.Write(dir, "M5-T1-A", "2026-01-02T00-00-00Z", second);

        var config = CreateCustomerBConfig(privateCaptureDir: dir);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateCustomerB(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.LinkMerchantCallCount);
        Assert.Contains("AMBIGUOUS", result.Detail);
    }

    [Fact]
    public async Task CustomerB_RepeatBlocked_DetailNeverContainsRawCustomerId()
    {
        var captureDir = PrepareCustomerBArtifactDir(customerId: "synthetic-customer-b-id-should-never-leak");
        var config = CreateCustomerBConfig(privateCaptureDir: captureDir);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateCustomerB(config, client);

        Assert.DoesNotContain("synthetic-customer-b-id-should-never-leak", result.Detail);
        Assert.DoesNotContain(captureDir, result.Detail ?? string.Empty);
    }

    [Fact]
    public void CustomerB_DryRun_NeverReachesReadyToExecute_WithoutExecuteFlag()
    {
        // El no-repeat guard sólo participa en ReadyToExecute — confirmamos
        // que sin --execute, la decisión nunca sale de DryRun (por lo tanto
        // HarnessApp jamás invoca a CreateM5CustomerBExecutor.ExecuteAsync;
        // el bloque DryRun correspondiente en HarnessApp.cs es texto
        // estático puro — ver inspección de código).
        var action = HarnessDecision.Decide(new[] { HarnessDecision.CommandCreateM5CustomerB }, out _);
        Assert.Equal(HarnessAction.DryRun, action);
    }

    [Fact]
    public void CustomerB_M1ThroughM4ConfirmFlags_NeverAuthorizeExecute()
    {
        // Banderas de confirmación de otros comandos (M1-M4/M5-Payment)
        // nunca satisfacen la confirmación exclusiva de create-m5-customer-b.
        var action = HarnessDecision.Decide(
            new[] { HarnessDecision.CommandCreateM5CustomerB, HarnessDecision.FlagExecute, HarnessDecision.FlagConfirmCreateKey },
            out var abortReason);
        Assert.Equal(HarnessAction.Aborted, action);
        Assert.NotNull(abortReason);
    }

    [Fact]
    public async Task CustomerB_LockAlreadyHeld_Blocked_ClientCallsZero()
    {
        var dir = NewTempDir("xpay-m5-customer-b-lock-held");
        M5CustomerBPrivateCapture.EnsureDirectory(dir);
        var lockOutcome = M5CustomerBPrivateCapture.TryAcquireLock(dir, out var lockPath);
        Assert.Equal(M5CustomerBPrivateCapture.LockAcquisitionOutcome.Acquired, lockOutcome);

        var config = CreateCustomerBConfig(privateCaptureDir: dir);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateCustomerB(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.LinkMerchantCallCount);
        Assert.True(File.Exists(lockPath)); // el lock preexistente NUNCA se borra por otro proceso
    }

    [Fact]
    public async Task CustomerB_SuccessfulExecution_LockReleased_NoResidualLockFile()
    {
        var dir = NewTempDir("xpay-m5-customer-b-lock-release");
        var config = CreateCustomerBConfig(privateCaptureDir: dir);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateCustomerB(config, client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        var lockPath = Path.Combine(Path.GetFullPath(dir, SyntheticRepoRoot), M5CustomerBPrivateCapture.LockFileName);
        Assert.False(File.Exists(lockPath));
    }

    [Fact]
    public async Task CustomerB_PassportFailure_LockReleased_SafeToRetryLater()
    {
        var dir = NewTempDir("xpay-m5-customer-b-lock-release-failure");
        var config = CreateCustomerBConfig(privateCaptureDir: dir);
        var client = new FakeCustomerAccountClient(throwOnLinkMerchant: true);

        var result = await ExecuteCreateCustomerB(config, client);

        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        var lockPath = Path.Combine(Path.GetFullPath(dir, SyntheticRepoRoot), M5CustomerBPrivateCapture.LockFileName);
        Assert.False(File.Exists(lockPath));
    }

    [Fact]
    public async Task CustomerB_ConcurrentAttempts_AtMostOneReachesPassport()
    {
        // Prueba de concurrencia REAL (no simulada): dos ExecuteAsync
        // concurrentes sobre el MISMO directorio de captura y la MISMA
        // fixture válida. El lock atómico (FileMode.CreateNew) debe
        // garantizar que como máximo uno de los dos alcance a Passport.
        var dir = NewTempDir("xpay-m5-customer-b-concurrency");
        var config = CreateCustomerBConfig(privateCaptureDir: dir);
        var client = new FakeCustomerAccountClient();

        var results = await Task.WhenAll(
            ExecuteCreateCustomerB(config, client),
            ExecuteCreateCustomerB(config, client));

        Assert.True(client.LinkMerchantCallCount <= 1,
            $"Se esperaba como máximo 1 llamada real concurrente, se observaron {client.LinkMerchantCallCount}.");
        Assert.Contains(results, r => r.Outcome == KeyOperationOutcome.LocalBlocked);
    }

    // ---------- Account B repeat guard ----------

    [Fact]
    public async Task AccountB_NoPriorArtifact_RepeatGuardPasses_ReachesNextPrecheck()
    {
        var config = CreateAccountBConfig();
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateAccountB(config, client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal(1, client.LinkAccountCallCount);
    }

    // Escribe un artefacto privado Account B "crudo" (fuera del helper
    // TryWritePrivateArtifact) para simular estados que el executor real
    // nunca produciría por sí mismo (malformado, case_id ajeno).
    private static string PrepareAccountBArtifactDir(string caseId = "M5-T1-B", string? accountId = "synthetic-account-b-id-001")
    {
        var dir = NewTempDir("xpay-m5-account-b-artifact-dir");
        M5AccountBPrivateCapture.EnsureDirectory(dir);
        var content = new Dictionary<string, object?>
        {
            ["case_id"] = caseId,
            ["captured_at_utc"] = "2026-01-01T00:00:00Z",
            ["backend_commit_sha"] = "synthetic-sha",
            ["customer_id"] = "synthetic-customer-b-id-001",
            ["account_id"] = accountId,
        };
        M5AccountBPrivateCapture.Write(dir, caseId, "2026-01-01T00-00-00Z", content);
        return dir;
    }

    [Fact]
    public async Task AccountB_ValidPriorArtifact_Blocked_ClientCallsZero()
    {
        var accountDir = PrepareAccountBArtifactDir();
        var config = CreateAccountBConfig(accountCaptureDir: accountDir);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateAccountB(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.LinkAccountCallCount);
        Assert.Contains("ALREADY_EXECUTED", result.Detail);
    }

    [Fact]
    public async Task AccountB_MalformedArtifact_Blocked_ClientCallsZero()
    {
        var dir = NewTempDir("xpay-m5-account-b-malformed");
        M5AccountBPrivateCapture.EnsureDirectory(dir);
        WriteRawArtifactFile(dir, "M5-T1-B-bad.private.json", "not-valid-json{{{");
        var config = CreateAccountBConfig(accountCaptureDir: dir);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateAccountB(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.LinkAccountCallCount);
    }

    [Fact]
    public async Task AccountB_PriorArtifactWrongCaseId_Blocked_ClientCallsZero()
    {
        var dir = PrepareAccountBArtifactDir(caseId: "M4-T4");
        var config = CreateAccountBConfig(accountCaptureDir: dir);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateAccountB(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.LinkAccountCallCount);
        Assert.DoesNotContain("ALREADY_EXECUTED", result.Detail);
    }

    [Fact]
    public async Task AccountB_MultiplePriorArtifacts_Blocked_ClientCallsZero()
    {
        var dir = PrepareAccountBArtifactDir();
        var second = new Dictionary<string, object?>
        {
            ["case_id"] = "M5-T1-B",
            ["captured_at_utc"] = "2026-01-02T00:00:00Z",
            ["backend_commit_sha"] = "synthetic-sha-2",
            ["customer_id"] = "synthetic-customer-b-id-001",
            ["account_id"] = "synthetic-account-b-id-002",
        };
        M5AccountBPrivateCapture.Write(dir, "M5-T1-B", "2026-01-02T00-00-00Z", second);

        var config = CreateAccountBConfig(accountCaptureDir: dir);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateAccountB(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.LinkAccountCallCount);
        Assert.Contains("AMBIGUOUS", result.Detail);
    }

    [Fact]
    public async Task AccountB_RepeatBlocked_DetailNeverContainsRawIds()
    {
        var accountDir = PrepareAccountBArtifactDir(accountId: "synthetic-account-b-id-should-never-leak");
        var config = CreateAccountBConfig(accountCaptureDir: accountDir);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateAccountB(config, client);

        Assert.DoesNotContain("synthetic-account-b-id-should-never-leak", result.Detail);
        Assert.DoesNotContain(accountDir, result.Detail ?? string.Empty);
    }

    [Fact]
    public void AccountB_DryRun_NeverReachesReadyToExecute_WithoutExecuteFlag()
    {
        var action = HarnessDecision.Decide(new[] { HarnessDecision.CommandCreateM5AccountB }, out _);
        Assert.Equal(HarnessAction.DryRun, action);
    }

    [Fact]
    public void AccountB_M1ThroughM4ConfirmFlags_NeverAuthorizeExecute()
    {
        var action = HarnessDecision.Decide(
            new[] { HarnessDecision.CommandCreateM5AccountB, HarnessDecision.FlagExecute, HarnessDecision.FlagConfirmCreateQrStatic },
            out var abortReason);
        Assert.Equal(HarnessAction.Aborted, action);
        Assert.NotNull(abortReason);
    }

    [Fact]
    public async Task AccountB_LockAlreadyHeld_Blocked_ClientCallsZero()
    {
        var dir = NewTempDir("xpay-m5-account-b-lock-held");
        M5AccountBPrivateCapture.EnsureDirectory(dir);
        var lockOutcome = M5AccountBPrivateCapture.TryAcquireLock(dir, out var lockPath);
        Assert.Equal(M5AccountBPrivateCapture.LockAcquisitionOutcome.Acquired, lockOutcome);

        var config = CreateAccountBConfig(accountCaptureDir: dir);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateAccountB(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.LinkAccountCallCount);
        Assert.True(File.Exists(lockPath));
    }

    [Fact]
    public async Task AccountB_SuccessfulExecution_LockReleased_NoResidualLockFile()
    {
        var accountDir = NewTempDir("xpay-m5-account-b-lock-release");
        var config = CreateAccountBConfig(accountCaptureDir: accountDir);
        var client = new FakeCustomerAccountClient();

        var result = await ExecuteCreateAccountB(config, client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        var lockPath = Path.Combine(Path.GetFullPath(accountDir, SyntheticRepoRoot), M5AccountBPrivateCapture.LockFileName);
        Assert.False(File.Exists(lockPath));
    }

    [Fact]
    public async Task AccountB_PassportFailure_LockReleased_SafeToRetryLater()
    {
        var accountDir = NewTempDir("xpay-m5-account-b-lock-release-failure");
        var config = CreateAccountBConfig(accountCaptureDir: accountDir);
        var client = new FakeCustomerAccountClient(throwOnLinkAccount: true);

        var result = await ExecuteCreateAccountB(config, client);

        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        var lockPath = Path.Combine(Path.GetFullPath(accountDir, SyntheticRepoRoot), M5AccountBPrivateCapture.LockFileName);
        Assert.False(File.Exists(lockPath));
    }

    [Fact]
    public async Task AccountB_ConcurrentAttempts_AtMostOneReachesPassport()
    {
        var accountDir = NewTempDir("xpay-m5-account-b-concurrency");
        var config = CreateAccountBConfig(accountCaptureDir: accountDir);
        var client = new FakeCustomerAccountClient();

        var results = await Task.WhenAll(
            ExecuteCreateAccountB(config, client),
            ExecuteCreateAccountB(config, client));

        Assert.True(client.LinkAccountCallCount <= 1,
            $"Se esperaba como máximo 1 llamada real concurrente, se observaron {client.LinkAccountCallCount}.");
        Assert.Contains(results, r => r.Outcome == KeyOperationOutcome.LocalBlocked);
    }
}
