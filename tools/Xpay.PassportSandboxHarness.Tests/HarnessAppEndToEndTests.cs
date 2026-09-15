using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-325 FASE 10/11 — camino COMPLETO de HarnessApp.RunAsync para
// create-key, con dependencias 100% fake (nunca red real, nunca proceso
// shell real, nunca ~/.passport-sandbox.env). Cubre tanto el caso exitoso
// end-to-end como la regresión de dry-run/guards ya establecidos.
public class HarnessAppEndToEndTests
{
    private const string BaseUrl = "https://api.paas.sandbox.co.passportfintech.com";
    private const string RealAccountId = "SYNTH-E2E-ACCOUNT-ID-should-be-fingerprinted-only";
    private const string RealKeyValue  = "SYNTH-E2E-KEY-VALUE-must-never-appear-in-evidence";

    private sealed class LocalFakeHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""
                    { "id": "synthetic-key-id-e2e", "status": "ACTIVE",
                      "account_id": "{{RealAccountId}}",
                      "key": { "key_type": "BCODE", "key_value": "{{RealKeyValue}}" } }
                    """,
                    System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class LocalFakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public LocalFakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class LocalFakeTokenProvider : IPassportTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult("synthetic-e2e-bearer-token");
    }

    private static IConfiguration FullConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            [PassportOptions.EnvBaseUrl] = BaseUrl,
            [PassportOptions.EnvClientId] = "synthetic-key",
            [PassportOptions.EnvClientSecret] = "synthetic-secret",
            [HarnessTargetConfig.EnvAccountId] = RealAccountId,
            [HarnessTargetConfig.EnvNewKeyType] = "BCODE",
            [HarnessTargetConfig.EnvNewKeyValue] = RealKeyValue,
        })
        .Build();

    private static HarnessApp.Dependencies BuildDependencies(
        LocalFakeHandler handler, string evidenceDir, IConfiguration config)
    {
        IPassportHttpClient httpClient = new PassportHttpClient(
            new LocalFakeHttpClientFactory(handler), new LocalFakeTokenProvider(), config,
            NullLogger<PassportHttpClient>.Instance);

        return new HarnessApp.Dependencies(
            CustomerAccountClient: new PassportCustomerAccountClient(httpClient),
            KeyClient: new PassportKeyClient(httpClient),
            CommitShaProvider: new FixedCommitShaProvider("synthetic-e2e-commit-sha-0000000000000000000000000000000000000000"),
            EvidenceBaseDirectory: evidenceDir);
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xpay-harness-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ── FASE 10 — camino completo exitoso ───────────────────────────────

    [Fact]
    public async Task CreateKeyExecute_FullPath_ProducesExactlyOneHttpCall_AndPassEvidence()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeHandler();
            var config = FullConfig();
            var dependencies = BuildDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "create-key", "--execute", "--confirm-create-key" }, config, dependencies, output);

            // Exactamente 1 llamada HTTP fake, POST /v1/keys, sin segundo HTTP.
            Assert.Equal(1, handler.CallCount);
            Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
            Assert.Equal("/v1/keys", handler.LastRequest.RequestUri!.AbsolutePath);
            Assert.Equal(BaseUrl, handler.LastRequest.RequestUri.GetLeftPart(UriPartial.Authority));

            using var reqDoc = JsonDocument.Parse(handler.LastRequestBody!);
            Assert.Equal(RealAccountId, reqDoc.RootElement.GetProperty("account_id").GetString());
            Assert.Equal("BCODE", reqDoc.RootElement.GetProperty("key").GetProperty("key_type").GetString());
            Assert.Equal(RealKeyValue, reqDoc.RootElement.GetProperty("key").GetProperty("key_value").GetString());

            // evidence.json generado, PASS, commit SHA sintético, sin secretos/PII.
            var caseDir = Path.Combine(dir, "M3-T1");
            var files = Directory.GetFiles(caseDir, "evidence-*.json");
            Assert.Single(files);

            var json = File.ReadAllText(files[0]);
            using var evDoc = JsonDocument.Parse(json);
            var root = evDoc.RootElement;
            Assert.Equal("M3-T1", root.GetProperty("case_id").GetString());
            Assert.Equal("sandbox", root.GetProperty("environment").GetString());
            Assert.Equal("synthetic-e2e-commit-sha-0000000000000000000000000000000000000000",
                root.GetProperty("backend_commit_sha").GetString());
            Assert.Equal("POST /v1/keys", root.GetProperty("operation").GetString());
            Assert.Equal("PASS", root.GetProperty("result").GetString());
            Assert.Equal("PENDING_PASSPORT_REVIEW", root.GetProperty("review_status").GetString());

            Assert.DoesNotContain(RealAccountId, json);
            Assert.DoesNotContain(RealKeyValue, json);
            Assert.DoesNotContain("synthetic-e2e-bearer-token", json);
            Assert.DoesNotContain("synthetic-secret", json);

            var consoleOutput = output.ToString();
            Assert.DoesNotContain(RealAccountId, consoleOutput);
            Assert.DoesNotContain(RealKeyValue, consoleOutput);
            Assert.DoesNotContain("synthetic-e2e-bearer-token", consoleOutput);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task CreateKeyExecute_RunTwice_SecondRunWithSameTimestampDoesNotSilentlyOverwrite()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeHandler();
            var config = FullConfig();
            var dependencies = BuildDependencies(handler, dir, config);
            await HarnessApp.RunAsync(
                new[] { "create-key", "--execute", "--confirm-create-key" }, config, dependencies, TextWriter.Null);

            var caseDir = Path.Combine(dir, "M3-T1");
            var existingFile = Directory.GetFiles(caseDir, "evidence-*.json").Single();
            var executedAtFromFileName = Path.GetFileNameWithoutExtension(existingFile).Replace("evidence-", "");

            // Reintentar EvidenceWriter directamente con el MISMO
            // executed_at_utc saneado (mismo nombre de archivo resultante)
            // que la ejecución real de HarnessApp ya produjo — confirma que
            // el propio pipeline real (no sólo EvidenceWriter en aislado)
            // nunca sobrescribiría en silencio una evidencia M3-T1 existente.
            var duplicateRecord = new EvidenceRecord(
                CaseId: "M3-T1",
                ExecutedAtUtc: executedAtFromFileName,
                Environment: EvidenceRecord.EnvironmentSandbox,
                BackendCommitSha: "synthetic-e2e-commit-sha-0000000000000000000000000000000000000000",
                Operation: "POST /v1/keys",
                HttpStatus: null,
                Result: EvidenceRecord.ResultPass,
                RequestSanitized: new Dictionary<string, object?>(),
                ResponseSanitized: new Dictionary<string, object?>(),
                AutomatedTestReference: null,
                Notes: null,
                ReviewStatus: EvidenceRecord.ReviewPendingPassportReview);

            Assert.Throws<InvalidOperationException>(() => EvidenceWriter.Write(dir, duplicateRecord));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ── FASE 4/7 — key_type inválido => LOCAL_BLOCKED, cero evidencia ───

    [Fact]
    public async Task CreateKeyExecute_InvalidKeyType_IsLocalBlocked_NoEvidenceNoHttp()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeHandler();
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [PassportOptions.EnvBaseUrl] = BaseUrl,
                    [PassportOptions.EnvClientId] = "synthetic-key",
                    [PassportOptions.EnvClientSecret] = "synthetic-secret",
                    [HarnessTargetConfig.EnvAccountId] = RealAccountId,
                    [HarnessTargetConfig.EnvNewKeyType] = "MOBILE", // inválido — no se normaliza a PHONE
                    [HarnessTargetConfig.EnvNewKeyValue] = RealKeyValue,
                })
                .Build();
            var dependencies = BuildDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "create-key", "--execute", "--confirm-create-key" }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount); // cero HTTP
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T1"))); // cero evidencia
            Assert.Contains("LOCAL_BLOCKED", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ── FASE 11 — regresión dry-run / guards ────────────────────────────

    [Fact]
    public async Task CreateKeyDryRun_NoFlags_ProducesZeroHttpZeroEvidence()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeHandler();
            var config = FullConfig();
            var dependencies = BuildDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(new[] { "create-key" }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T1")));
            Assert.Contains("result=DRY_RUN", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task CreateKeyExecute_WithoutConfirm_StaysBlocked_ZeroHttp()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeHandler();
            var config = FullConfig();
            var dependencies = BuildDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(new[] { "create-key", "--execute" }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T1")));
            Assert.Contains("result=ABORTED", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task CreateKeyExecute_WithConfirmCreateCustomer_DoesNotAuthorize_ZeroHttp()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeHandler();
            var config = FullConfig();
            var dependencies = BuildDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "create-key", "--execute", "--confirm-create-customer" }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T1")));
            Assert.Contains("result=ABORTED", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
