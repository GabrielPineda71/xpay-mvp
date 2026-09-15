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

    // ══════════════════════════════════════════════════════════════════════
    // XPAY-326 — suspend-key (M3-T3), 100% offline, key_id SIEMPRE sintético.
    // ══════════════════════════════════════════════════════════════════════

    private const string SyntheticRemoteKeyId = "SYNTH-E2E-REMOTE-KEY-ID-must-never-appear-in-evidence-or-console";

    private sealed class LocalFakeSuspendHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string? _errorBody;

        public LocalFakeSuspendHandler(HttpStatusCode status = HttpStatusCode.OK, string? errorBody = null)
        {
            _status = status;
            _errorBody = errorBody;
        }

        public int CallCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;

            var response = new HttpResponseMessage(_status)
            {
                Content = new StringContent(
                    _errorBody ?? $$"""
                    { "id": "{{SyntheticRemoteKeyId}}", "status": "SUSPENDED" }
                    """,
                    System.Text.Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

    private static IConfiguration SuspendKeyConfig(string? newKeyId = SyntheticRemoteKeyId) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            [PassportOptions.EnvBaseUrl] = BaseUrl,
            [PassportOptions.EnvClientId] = "synthetic-key",
            [PassportOptions.EnvClientSecret] = "synthetic-secret",
            [HarnessTargetConfig.EnvNewKeyId] = newKeyId,
        })
        .Build();

    private static HarnessApp.Dependencies BuildSuspendDependencies(
        HttpMessageHandler handler, string evidenceDir, IConfiguration config)
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

    // Test end-to-end obligatorio (XPAY-326 §9): exactamente 1 llamada HTTP
    // de negocio, PATCH /v1/keys/{syntheticKeyId}/suspend, evidencia M3-T3
    // con result=PASS, commit SHA sintético, key_id real AUSENTE de
    // evidence.json y de la salida por consola, key_id_fingerprint presente,
    // cero secretos.
    [Fact]
    public async Task SuspendKeyExecute_FullPath_ProducesExactlyOneHttpCall_AndPassEvidence()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeSuspendHandler();
            var config = SuspendKeyConfig();
            var dependencies = BuildSuspendDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "suspend-key", "--execute", "--confirm-suspend-key" }, config, dependencies, output);

            // Exactamente 1 llamada HTTP fake, PATCH /v1/keys/{key_id}/suspend, sin segundo HTTP.
            Assert.Equal(1, handler.CallCount);
            Assert.Equal(HttpMethod.Patch, handler.LastRequest!.Method);
            Assert.Equal($"/v1/keys/{SyntheticRemoteKeyId}/suspend", handler.LastRequest.RequestUri!.AbsolutePath);
            Assert.Equal(BaseUrl, handler.LastRequest.RequestUri.GetLeftPart(UriPartial.Authority));

            var caseDir = Path.Combine(dir, "M3-T3");
            var files = Directory.GetFiles(caseDir, "evidence-*.json");
            Assert.Single(files);

            var json = File.ReadAllText(files[0]);
            using var evDoc = JsonDocument.Parse(json);
            var root = evDoc.RootElement;
            Assert.Equal("M3-T3", root.GetProperty("case_id").GetString());
            Assert.Equal("sandbox", root.GetProperty("environment").GetString());
            Assert.Equal("synthetic-e2e-commit-sha-0000000000000000000000000000000000000000",
                root.GetProperty("backend_commit_sha").GetString());
            Assert.Equal("PATCH /v1/keys/{key_id}/suspend", root.GetProperty("operation").GetString());
            Assert.Equal("PASS", root.GetProperty("result").GetString());
            Assert.Equal("PENDING_PASSPORT_REVIEW", root.GetProperty("review_status").GetString());

            // key_id real AUSENTE de evidence.json; sólo su fingerprint presente.
            Assert.DoesNotContain(SyntheticRemoteKeyId, json);
            Assert.True(root.GetProperty("request_sanitized").TryGetProperty("key_id_fingerprint", out _));
            Assert.True(root.GetProperty("response_sanitized").TryGetProperty("id_fingerprint", out _));
            Assert.DoesNotContain("synthetic-e2e-bearer-token", json);
            Assert.DoesNotContain("synthetic-secret", json);

            // key_id real AUSENTE también de la salida por consola.
            var consoleOutput = output.ToString();
            Assert.DoesNotContain(SyntheticRemoteKeyId, consoleOutput);
            Assert.DoesNotContain("synthetic-e2e-bearer-token", consoleOutput);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // PASSPORT_TEST_NEW_KEY_ID ausente => bloqueado ANTES de HTTP (a nivel
    // HarnessOrchestrator.Prepare, AbortedTargetMissing), cero HTTP, cero evidencia.
    [Fact]
    public async Task SuspendKeyExecute_MissingKeyId_IsAborted_NoEvidenceNoHttp()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeSuspendHandler();
            var config = SuspendKeyConfig(newKeyId: null);
            var dependencies = BuildSuspendDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "suspend-key", "--execute", "--confirm-suspend-key" }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T3")));
            Assert.Contains("result=ABORTED", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Fallo remoto simulado (HTTP no-2xx) => exactamente 1 llamada HTTP, y
    // evidencia FAIL saneada (nunca finge un LOCAL_BLOCKED cuando sí hubo
    // interacción remota real).
    [Fact]
    public async Task SuspendKeyExecute_PassportHttpFailure_ProducesExactlyOneHttpCall_AndFailEvidence()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeSuspendHandler(
                HttpStatusCode.BadRequest, errorBody: """{ "message": "invalid state" }""");
            var config = SuspendKeyConfig();
            var dependencies = BuildSuspendDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "suspend-key", "--execute", "--confirm-suspend-key" }, config, dependencies, output);

            Assert.Equal(1, handler.CallCount); // sin reintentos, un solo intento real.

            var caseDir = Path.Combine(dir, "M3-T3");
            var files = Directory.GetFiles(caseDir, "evidence-*.json");
            Assert.Single(files);

            var json = File.ReadAllText(files[0]);
            using var evDoc = JsonDocument.Parse(json);
            var root = evDoc.RootElement;
            Assert.Equal("M3-T3", root.GetProperty("case_id").GetString());
            Assert.Equal("FAIL", root.GetProperty("result").GetString());
            Assert.StartsWith("PASSPORT_HTTP_FAILURE:", root.GetProperty("notes").GetString());
            Assert.DoesNotContain(SyntheticRemoteKeyId, json);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Regresión — dry-run: cero HTTP, cero evidencia.
    [Fact]
    public async Task SuspendKeyDryRun_NoFlags_ProducesZeroHttpZeroEvidence()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeSuspendHandler();
            var config = SuspendKeyConfig();
            var dependencies = BuildSuspendDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(new[] { "suspend-key" }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T3")));
            Assert.Contains("result=DRY_RUN", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Regresión — --execute solo, sin confirmación: cero HTTP.
    [Fact]
    public async Task SuspendKeyExecute_WithoutConfirm_StaysBlocked_ZeroHttp()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeSuspendHandler();
            var config = SuspendKeyConfig();
            var dependencies = BuildSuspendDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(new[] { "suspend-key", "--execute" }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T3")));
            Assert.Contains("result=ABORTED", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Regresión — --confirm-create-key NUNCA autoriza suspend-key: cero HTTP.
    [Fact]
    public async Task SuspendKeyExecute_WithConfirmCreateKey_DoesNotAuthorize_ZeroHttp()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeSuspendHandler();
            var config = SuspendKeyConfig();
            var dependencies = BuildSuspendDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "suspend-key", "--execute", "--confirm-create-key" }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T3")));
            Assert.Contains("result=ABORTED", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
