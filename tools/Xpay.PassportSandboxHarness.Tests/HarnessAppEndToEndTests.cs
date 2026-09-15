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
            EvidenceBaseDirectory: evidenceDir,
            QrClient: new PassportQrClient(httpClient));
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
            EvidenceBaseDirectory: evidenceDir,
            QrClient: new PassportQrClient(httpClient));
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

    // ══════════════════════════════════════════════════════════════════════
    // XPAY-332 — activate-key (M3-T4), 100% offline, mirror exacto de
    // suspend-key (M3-T3). key_id SIEMPRE sintético.
    // ══════════════════════════════════════════════════════════════════════

    private const string SyntheticActivateRemoteKeyId = "SYNTH-E2E-ACTIVATE-REMOTE-KEY-ID-must-never-appear-in-evidence-or-console";

    private sealed class LocalFakeActivateHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string? _errorBody;

        public LocalFakeActivateHandler(HttpStatusCode status = HttpStatusCode.OK, string? errorBody = null)
        {
            _status = status;
            _errorBody = errorBody;
        }

        public int CallCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            var response = new HttpResponseMessage(_status)
            {
                Content = new StringContent(
                    _errorBody ?? $$"""
                    { "id": "{{SyntheticActivateRemoteKeyId}}", "status": "ACTIVE" }
                    """,
                    System.Text.Encoding.UTF8, "application/json"),
            };
            return response;
        }
    }

    private static IConfiguration ActivateKeyConfig(string? newKeyId = SyntheticActivateRemoteKeyId) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            [PassportOptions.EnvBaseUrl] = BaseUrl,
            [PassportOptions.EnvClientId] = "synthetic-key",
            [PassportOptions.EnvClientSecret] = "synthetic-secret",
            [HarnessTargetConfig.EnvNewKeyId] = newKeyId,
        })
        .Build();

    private static HarnessApp.Dependencies BuildActivateDependencies(
        HttpMessageHandler handler, string evidenceDir, IConfiguration config)
    {
        IPassportHttpClient httpClient = new PassportHttpClient(
            new LocalFakeHttpClientFactory(handler), new LocalFakeTokenProvider(), config,
            NullLogger<PassportHttpClient>.Instance);

        return new HarnessApp.Dependencies(
            CustomerAccountClient: new PassportCustomerAccountClient(httpClient),
            KeyClient: new PassportKeyClient(httpClient),
            CommitShaProvider: new FixedCommitShaProvider("synthetic-e2e-commit-sha-0000000000000000000000000000000000000000"),
            EvidenceBaseDirectory: evidenceDir,
            QrClient: new PassportQrClient(httpClient));
    }

    // Test end-to-end obligatorio (XPAY-332 §11.H/I/J): exactamente 1
    // llamada HTTP de negocio, PATCH /v1/keys/{syntheticKeyId}/activate, sin
    // body, evidencia M3-T4 con result=PASS, commit SHA sintético, el
    // key_id real ABSENTE de evidence.json y de la salida por consola,
    // key_id_fingerprint presente, cero secretos.
    [Fact]
    public async Task ActivateKeyExecute_FullPath_ProducesExactlyOneHttpCall_AndPassEvidence()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeActivateHandler();
            var config = ActivateKeyConfig();
            var dependencies = BuildActivateDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "activate-key", "--execute", "--confirm-activate-key" }, config, dependencies, output);

            // Exactamente 1 llamada HTTP fake, PATCH /v1/keys/{key_id}/activate, sin body, sin segundo HTTP.
            Assert.Equal(1, handler.CallCount);
            Assert.Equal(HttpMethod.Patch, handler.LastRequest!.Method);
            Assert.Equal($"/v1/keys/{SyntheticActivateRemoteKeyId}/activate", handler.LastRequest.RequestUri!.AbsolutePath);
            Assert.Equal(BaseUrl, handler.LastRequest.RequestUri.GetLeftPart(UriPartial.Authority));
            Assert.Null(handler.LastRequestBody);

            var caseDir = Path.Combine(dir, "M3-T4");
            var files = Directory.GetFiles(caseDir, "evidence-*.json");
            Assert.Single(files);

            var json = File.ReadAllText(files[0]);
            using var evDoc = JsonDocument.Parse(json);
            var root = evDoc.RootElement;
            Assert.Equal("M3-T4", root.GetProperty("case_id").GetString());
            Assert.Equal("sandbox", root.GetProperty("environment").GetString());
            Assert.Equal("synthetic-e2e-commit-sha-0000000000000000000000000000000000000000",
                root.GetProperty("backend_commit_sha").GetString());
            Assert.Equal("PATCH /v1/keys/{key_id}/activate", root.GetProperty("operation").GetString());
            Assert.Equal("PASS", root.GetProperty("result").GetString());
            Assert.Equal("PENDING_PASSPORT_REVIEW", root.GetProperty("review_status").GetString());

            // key_id real AUSENTE de evidence.json; sólo su fingerprint presente.
            Assert.DoesNotContain(SyntheticActivateRemoteKeyId, json);
            Assert.True(root.GetProperty("request_sanitized").TryGetProperty("key_id_fingerprint", out _));
            Assert.True(root.GetProperty("response_sanitized").TryGetProperty("id_fingerprint", out _));
            Assert.DoesNotContain("synthetic-e2e-bearer-token", json);
            Assert.DoesNotContain("synthetic-secret", json);

            // key_id real AUSENTE también de la salida por consola.
            var consoleOutput = output.ToString();
            Assert.DoesNotContain(SyntheticActivateRemoteKeyId, consoleOutput);
            Assert.DoesNotContain("synthetic-e2e-bearer-token", consoleOutput);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // PASSPORT_TEST_NEW_KEY_ID ausente => bloqueado ANTES de HTTP
    // (AbortedTargetMissing a nivel HarnessOrchestrator.Prepare), cero HTTP,
    // cero evidencia.
    [Fact]
    public async Task ActivateKeyExecute_MissingKeyId_IsAborted_NoEvidenceNoHttp()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeActivateHandler();
            var config = ActivateKeyConfig(newKeyId: null);
            var dependencies = BuildActivateDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "activate-key", "--execute", "--confirm-activate-key" }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T4")));
            Assert.Contains("result=ABORTED", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Fallo remoto simulado (HTTP no-2xx) => exactamente 1 llamada HTTP, y
    // evidencia FAIL saneada.
    [Fact]
    public async Task ActivateKeyExecute_PassportHttpFailure_ProducesExactlyOneHttpCall_AndFailEvidence()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeActivateHandler(
                HttpStatusCode.BadRequest, errorBody: """{ "message": "invalid state" }""");
            var config = ActivateKeyConfig();
            var dependencies = BuildActivateDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "activate-key", "--execute", "--confirm-activate-key" }, config, dependencies, output);

            Assert.Equal(1, handler.CallCount); // sin reintentos, un solo intento real.

            var caseDir = Path.Combine(dir, "M3-T4");
            var files = Directory.GetFiles(caseDir, "evidence-*.json");
            Assert.Single(files);

            var json = File.ReadAllText(files[0]);
            using var evDoc = JsonDocument.Parse(json);
            var root = evDoc.RootElement;
            Assert.Equal("M3-T4", root.GetProperty("case_id").GetString());
            Assert.Equal("FAIL", root.GetProperty("result").GetString());
            Assert.StartsWith("PASSPORT_HTTP_FAILURE:", root.GetProperty("notes").GetString());
            Assert.DoesNotContain(SyntheticActivateRemoteKeyId, json);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Regresión — dry-run: cero HTTP, cero evidencia.
    [Fact]
    public async Task ActivateKeyDryRun_NoFlags_ProducesZeroHttpZeroEvidence()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeActivateHandler();
            var config = ActivateKeyConfig();
            var dependencies = BuildActivateDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(new[] { "activate-key" }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T4")));
            Assert.Contains("result=DRY_RUN", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Regresión — --execute solo, sin confirmación: cero HTTP.
    [Fact]
    public async Task ActivateKeyExecute_WithoutConfirm_StaysBlocked_ZeroHttp()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeActivateHandler();
            var config = ActivateKeyConfig();
            var dependencies = BuildActivateDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(new[] { "activate-key", "--execute" }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T4")));
            Assert.Contains("result=ABORTED", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Regresión — --confirm-suspend-key NUNCA autoriza activate-key: cero HTTP.
    [Fact]
    public async Task ActivateKeyExecute_WithConfirmSuspendKey_DoesNotAuthorize_ZeroHttp()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeActivateHandler();
            var config = ActivateKeyConfig();
            var dependencies = BuildActivateDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "activate-key", "--execute", "--confirm-suspend-key" }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T4")));
            Assert.Contains("result=ABORTED", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // XPAY-334 — delete-key (M3-T5), 100% offline, mirror de suspend-key/
    // activate-key. Éxito = 204 No Content SIN body (contrato XPAY-292/293)
    // — a diferencia de Suspend/Activate, no hay PassportKeyResponse.
    // key_id SIEMPRE sintético.
    // ══════════════════════════════════════════════════════════════════════

    private const string SyntheticDeleteRemoteKeyId = "SYNTH-E2E-DELETE-REMOTE-KEY-ID-must-never-appear-in-evidence-or-console";

    private sealed class LocalFakeDeleteHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string? _errorBody;

        public LocalFakeDeleteHandler(HttpStatusCode status = HttpStatusCode.NoContent, string? errorBody = null)
        {
            _status = status;
            _errorBody = errorBody;
        }

        public int CallCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            // Éxito real = 204 No Content SIN cuerpo — nunca se fabrica un
            // body para el caso exitoso, exactamente como respondería
            // Passport. _errorBody sólo se usa para simular un fallo real.
            if (_errorBody is not null)
            {
                return new HttpResponseMessage(_status)
                {
                    Content = new StringContent(_errorBody, System.Text.Encoding.UTF8, "application/json"),
                };
            }
            return new HttpResponseMessage(_status);
        }
    }

    private static IConfiguration DeleteKeyConfig(string? newKeyId = SyntheticDeleteRemoteKeyId) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            [PassportOptions.EnvBaseUrl] = BaseUrl,
            [PassportOptions.EnvClientId] = "synthetic-key",
            [PassportOptions.EnvClientSecret] = "synthetic-secret",
            [HarnessTargetConfig.EnvNewKeyId] = newKeyId,
        })
        .Build();

    private static HarnessApp.Dependencies BuildDeleteDependencies(
        HttpMessageHandler handler, string evidenceDir, IConfiguration config)
    {
        IPassportHttpClient httpClient = new PassportHttpClient(
            new LocalFakeHttpClientFactory(handler), new LocalFakeTokenProvider(), config,
            NullLogger<PassportHttpClient>.Instance);

        return new HarnessApp.Dependencies(
            CustomerAccountClient: new PassportCustomerAccountClient(httpClient),
            KeyClient: new PassportKeyClient(httpClient),
            CommitShaProvider: new FixedCommitShaProvider("synthetic-e2e-commit-sha-0000000000000000000000000000000000000000"),
            EvidenceBaseDirectory: evidenceDir,
            QrClient: new PassportQrClient(httpClient));
    }

    // Test end-to-end obligatorio (XPAY-334 §11.H/I/J/L/M/N): exactamente 1
    // llamada HTTP de negocio, DELETE /v1/keys/{syntheticKeyId}, SIN body de
    // request, HTTP 204 No Content en la respuesta fake, evidencia M3-T5
    // con result=PASS, sin campos de respuesta inventados, commit SHA
    // sintético, key_id real ABSENTE de evidence.json y de la consola,
    // key_id_fingerprint presente, cero secretos.
    [Fact]
    public async Task DeleteKeyExecute_FullPath_ProducesExactlyOneHttpCall_AndPassEvidence()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeDeleteHandler(); // 204 No Content por defecto.
            var config = DeleteKeyConfig();
            var dependencies = BuildDeleteDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "delete-key", "--execute", "--confirm-delete-key" }, config, dependencies, output);

            // Exactamente 1 llamada HTTP fake, DELETE /v1/keys/{key_id}, sin body, sin segundo HTTP.
            Assert.Equal(1, handler.CallCount);
            Assert.Equal(HttpMethod.Delete, handler.LastRequest!.Method);
            Assert.Equal($"/v1/keys/{SyntheticDeleteRemoteKeyId}", handler.LastRequest.RequestUri!.AbsolutePath);
            Assert.Equal(BaseUrl, handler.LastRequest.RequestUri.GetLeftPart(UriPartial.Authority));
            Assert.Null(handler.LastRequestBody);

            var caseDir = Path.Combine(dir, "M3-T5");
            var files = Directory.GetFiles(caseDir, "evidence-*.json");
            Assert.Single(files);

            var json = File.ReadAllText(files[0]);
            using var evDoc = JsonDocument.Parse(json);
            var root = evDoc.RootElement;
            Assert.Equal("M3-T5", root.GetProperty("case_id").GetString());
            Assert.Equal("sandbox", root.GetProperty("environment").GetString());
            Assert.Equal("synthetic-e2e-commit-sha-0000000000000000000000000000000000000000",
                root.GetProperty("backend_commit_sha").GetString());
            Assert.Equal("DELETE /v1/keys/{key_id}", root.GetProperty("operation").GetString());
            Assert.Equal("PASS", root.GetProperty("result").GetString());
            Assert.Equal("PENDING_PASSPORT_REVIEW", root.GetProperty("review_status").GetString());

            // key_id real AUSENTE de evidence.json; sólo su fingerprint presente.
            Assert.DoesNotContain(SyntheticDeleteRemoteKeyId, json);
            Assert.True(root.GetProperty("request_sanitized").TryGetProperty("key_id_fingerprint", out _));

            // response_sanitized queda vacío — NUNCA se inventan status/id/deleted_at
            // que Passport no devolvió (204 No Content sin body).
            Assert.Empty(root.GetProperty("response_sanitized").EnumerateObject());

            Assert.DoesNotContain("synthetic-e2e-bearer-token", json);
            Assert.DoesNotContain("synthetic-secret", json);

            // key_id real AUSENTE también de la salida por consola.
            var consoleOutput = output.ToString();
            Assert.DoesNotContain(SyntheticDeleteRemoteKeyId, consoleOutput);
            Assert.DoesNotContain("synthetic-e2e-bearer-token", consoleOutput);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // PASSPORT_TEST_NEW_KEY_ID ausente => bloqueado ANTES de HTTP
    // (AbortedTargetMissing a nivel HarnessOrchestrator.Prepare), cero HTTP,
    // cero evidencia.
    [Fact]
    public async Task DeleteKeyExecute_MissingKeyId_IsAborted_NoEvidenceNoHttp()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeDeleteHandler();
            var config = DeleteKeyConfig(newKeyId: null);
            var dependencies = BuildDeleteDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "delete-key", "--execute", "--confirm-delete-key" }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T5")));
            Assert.Contains("result=ABORTED", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Fallo remoto simulado (HTTP no-2xx) => exactamente 1 llamada HTTP, y
    // evidencia FAIL saneada — sin reintento.
    [Fact]
    public async Task DeleteKeyExecute_PassportHttpFailure_ProducesExactlyOneHttpCall_AndFailEvidence()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeDeleteHandler(
                HttpStatusCode.BadRequest, errorBody: """{ "message": "invalid state" }""");
            var config = DeleteKeyConfig();
            var dependencies = BuildDeleteDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "delete-key", "--execute", "--confirm-delete-key" }, config, dependencies, output);

            Assert.Equal(1, handler.CallCount); // sin reintentos, un solo intento real.

            var caseDir = Path.Combine(dir, "M3-T5");
            var files = Directory.GetFiles(caseDir, "evidence-*.json");
            Assert.Single(files);

            var json = File.ReadAllText(files[0]);
            using var evDoc = JsonDocument.Parse(json);
            var root = evDoc.RootElement;
            Assert.Equal("M3-T5", root.GetProperty("case_id").GetString());
            Assert.Equal("FAIL", root.GetProperty("result").GetString());
            Assert.StartsWith("PASSPORT_HTTP_FAILURE:", root.GetProperty("notes").GetString());
            Assert.DoesNotContain(SyntheticDeleteRemoteKeyId, json);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Regresión — dry-run: cero HTTP, cero evidencia.
    [Fact]
    public async Task DeleteKeyDryRun_NoFlags_ProducesZeroHttpZeroEvidence()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeDeleteHandler();
            var config = DeleteKeyConfig();
            var dependencies = BuildDeleteDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(new[] { "delete-key" }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T5")));
            Assert.Contains("result=DRY_RUN", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Regresión — --execute solo, sin confirmación: cero HTTP.
    [Fact]
    public async Task DeleteKeyExecute_WithoutConfirm_StaysBlocked_ZeroHttp()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeDeleteHandler();
            var config = DeleteKeyConfig();
            var dependencies = BuildDeleteDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(new[] { "delete-key", "--execute" }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T5")));
            Assert.Contains("result=ABORTED", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Regresión — --confirm-suspend-key/--confirm-activate-key NUNCA
    // autorizan delete-key: cero HTTP.
    [Theory]
    [InlineData("--confirm-suspend-key")]
    [InlineData("--confirm-activate-key")]
    [InlineData("--confirm-create-key")]
    public async Task DeleteKeyExecute_WithWrongConfirmFlag_DoesNotAuthorize_ZeroHttp(string wrongConfirmFlag)
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeDeleteHandler();
            var config = DeleteKeyConfig();
            var dependencies = BuildDeleteDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "delete-key", "--execute", wrongConfirmFlag }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T5")));
            Assert.Contains("result=ABORTED", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // XPAY-336 — delete-already-deleted-key (M3-T7), 100% offline.
    // Reutiliza el MISMO IPassportKeyClient.DeleteKeyAsync que delete-key
    // (M3-T5), pero genera evidencia con case_id COMPLETAMENTE distinto —
    // nunca se confunden. key_id SIEMPRE sintético.
    // ══════════════════════════════════════════════════════════════════════

    private const string SyntheticAlreadyDeletedRemoteKeyId = "SYNTH-E2E-ALREADY-DELETED-REMOTE-KEY-ID-must-never-appear-in-evidence-or-console";

    private static IConfiguration DeleteAlreadyDeletedKeyConfig(string? newKeyId = SyntheticAlreadyDeletedRemoteKeyId) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            [PassportOptions.EnvBaseUrl] = BaseUrl,
            [PassportOptions.EnvClientId] = "synthetic-key",
            [PassportOptions.EnvClientSecret] = "synthetic-secret",
            [HarnessTargetConfig.EnvNewKeyId] = newKeyId,
        })
        .Build();

    // Test end-to-end obligatorio (XPAY-336 §11.F/G/H/I/J/K/L): exactamente
    // 1 llamada HTTP de negocio, DELETE /v1/keys/{syntheticKeyId}, sin
    // body, evidencia M3-T7 (NO M3-T5) con result=PASS a nivel transporte,
    // key_id real ABSENTE de evidence.json y de la consola, sin otro
    // método Passport invocado (el mismo LocalFakeDeleteHandler/
    // PassportKeyClient real ya garantiza que sólo pudo pasar por
    // DeleteKeyAsync — DeleteKeyClient no tiene otra ruta hacia DELETE
    // /v1/keys/{id}).
    [Fact]
    public async Task DeleteAlreadyDeletedKeyExecute_FullPath_ProducesExactlyOneHttpCall_AndTransportPassEvidence()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeDeleteHandler(); // 204 No Content por defecto.
            var config = DeleteAlreadyDeletedKeyConfig();
            var dependencies = BuildDeleteDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "delete-already-deleted-key", "--execute", "--confirm-delete-already-deleted-key" },
                config, dependencies, output);

            Assert.Equal(1, handler.CallCount);
            Assert.Equal(HttpMethod.Delete, handler.LastRequest!.Method);
            Assert.Equal($"/v1/keys/{SyntheticAlreadyDeletedRemoteKeyId}", handler.LastRequest.RequestUri!.AbsolutePath);
            Assert.Null(handler.LastRequestBody);

            // Evidencia bajo M3-T7 — NUNCA bajo M3-T5.
            var caseDir = Path.Combine(dir, "M3-T7");
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T5")));
            var files = Directory.GetFiles(caseDir, "evidence-*.json");
            Assert.Single(files);

            var json = File.ReadAllText(files[0]);
            using var evDoc = JsonDocument.Parse(json);
            var root = evDoc.RootElement;
            Assert.Equal("M3-T7", root.GetProperty("case_id").GetString());
            Assert.Equal("DELETE /v1/keys/{key_id}", root.GetProperty("operation").GetString());
            Assert.Equal("PASS", root.GetProperty("result").GetString());
            Assert.Equal("PENDING_PASSPORT_REVIEW", root.GetProperty("review_status").GetString());

            // La nota explícita de revisión contractual está presente —
            // nunca se afirma que "PASS" aquí sea un juicio de certificación.
            Assert.Contains("revisión contractual", root.GetProperty("notes").GetString()!);

            Assert.DoesNotContain(SyntheticAlreadyDeletedRemoteKeyId, json);
            Assert.True(root.GetProperty("request_sanitized").TryGetProperty("key_id_fingerprint", out _));
            Assert.Empty(root.GetProperty("response_sanitized").EnumerateObject());

            var consoleOutput = output.ToString();
            Assert.DoesNotContain(SyntheticAlreadyDeletedRemoteKeyId, consoleOutput);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // PASSPORT_TEST_NEW_KEY_ID ausente => bloqueado ANTES de HTTP, cero
    // HTTP, cero evidencia.
    [Fact]
    public async Task DeleteAlreadyDeletedKeyExecute_MissingKeyId_IsAborted_NoEvidenceNoHttp()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeDeleteHandler();
            var config = DeleteAlreadyDeletedKeyConfig(newKeyId: null);
            var dependencies = BuildDeleteDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "delete-already-deleted-key", "--execute", "--confirm-delete-already-deleted-key" },
                config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T7")));
            Assert.Contains("result=ABORTED", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Fallo remoto simulado (HTTP no-2xx) — test M: la evidencia conserva
    // la semántica M3-T7 sin filtrar información Y sin afirmar que este
    // FAIL a nivel transporte es un veredicto de certificación (podría ser
    // precisamente el rechazo correctamente esperado).
    [Fact]
    public async Task DeleteAlreadyDeletedKeyExecute_PassportHttpFailure_ProducesExactlyOneHttpCall_AndTransportFailEvidenceWithoutCertificationJudgment()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeDeleteHandler(
                HttpStatusCode.BadRequest, errorBody: """{ "message": "key already deleted" }""");
            var config = DeleteAlreadyDeletedKeyConfig();
            var dependencies = BuildDeleteDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "delete-already-deleted-key", "--execute", "--confirm-delete-already-deleted-key" },
                config, dependencies, output);

            Assert.Equal(1, handler.CallCount); // sin reintentos.

            var caseDir = Path.Combine(dir, "M3-T7");
            var files = Directory.GetFiles(caseDir, "evidence-*.json");
            Assert.Single(files);

            var json = File.ReadAllText(files[0]);
            using var evDoc = JsonDocument.Parse(json);
            var root = evDoc.RootElement;
            Assert.Equal("M3-T7", root.GetProperty("case_id").GetString());
            Assert.Equal("FAIL", root.GetProperty("result").GetString());

            var notes = root.GetProperty("notes").GetString()!;
            Assert.StartsWith("PASSPORT_HTTP_FAILURE:", notes);
            // La nota debe dejar explícito que este FAIL no es, por sí
            // solo, un veredicto de certificación.
            Assert.Contains("revisión contractual", notes);
            Assert.DoesNotContain(SyntheticAlreadyDeletedRemoteKeyId, json);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Regresión — dry-run: cero HTTP, cero evidencia.
    [Fact]
    public async Task DeleteAlreadyDeletedKeyDryRun_NoFlags_ProducesZeroHttpZeroEvidence()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeDeleteHandler();
            var config = DeleteAlreadyDeletedKeyConfig();
            var dependencies = BuildDeleteDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(new[] { "delete-already-deleted-key" }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T7")));
            Assert.Contains("result=DRY_RUN", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Regresión — --execute solo, sin confirmación: cero HTTP.
    [Fact]
    public async Task DeleteAlreadyDeletedKeyExecute_WithoutConfirm_StaysBlocked_ZeroHttp()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeDeleteHandler();
            var config = DeleteAlreadyDeletedKeyConfig();
            var dependencies = BuildDeleteDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "delete-already-deleted-key", "--execute" }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T7")));
            Assert.Contains("result=ABORTED", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Regresión — ninguna otra confirmación (incluida --confirm-delete-key
    // de M3-T5) autoriza M3-T7: cero HTTP.
    [Theory]
    [InlineData("--confirm-delete-key")]
    [InlineData("--confirm-suspend-key")]
    [InlineData("--confirm-activate-key")]
    [InlineData("--confirm-create-key")]
    public async Task DeleteAlreadyDeletedKeyExecute_WithWrongConfirmFlag_DoesNotAuthorize_ZeroHttp(string wrongConfirmFlag)
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeDeleteHandler();
            var config = DeleteAlreadyDeletedKeyConfig();
            var dependencies = BuildDeleteDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "delete-already-deleted-key", "--execute", wrongConfirmFlag }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T7")));
            Assert.Contains("result=ABORTED", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Test N — M3-T5 y M3-T7 generan case_id (y evidencia) DIFERENTES aunque
    // ambos reutilicen exactamente el mismo IPassportKeyClient.DeleteKeyAsync
    // y el mismo PassportKeyClient/handler subyacente.
    [Fact]
    public async Task DeleteKeyAndDeleteAlreadyDeletedKey_ShareUnderlyingCall_ButProduceSeparateCaseIds()
    {
        var dir = NewTempDir();
        try
        {
            var handlerM3T5 = new LocalFakeDeleteHandler();
            var configM3T5 = DeleteKeyConfig();
            var depsM3T5 = BuildDeleteDependencies(handlerM3T5, dir, configM3T5);
            await HarnessApp.RunAsync(
                new[] { "delete-key", "--execute", "--confirm-delete-key" }, configM3T5, depsM3T5, TextWriter.Null);

            var handlerM3T7 = new LocalFakeDeleteHandler();
            var configM3T7 = DeleteAlreadyDeletedKeyConfig();
            var depsM3T7 = BuildDeleteDependencies(handlerM3T7, dir, configM3T7);
            await HarnessApp.RunAsync(
                new[] { "delete-already-deleted-key", "--execute", "--confirm-delete-already-deleted-key" },
                configM3T7, depsM3T7, TextWriter.Null);

            // Ambos ejecutaron exactamente 1 DELETE — mismo tipo de
            // operación productiva subyacente.
            Assert.Equal(1, handlerM3T5.CallCount);
            Assert.Equal(1, handlerM3T7.CallCount);

            // Pero produjeron carpetas/case_id de evidencia completamente
            // separados — nunca se conflatan.
            var m3t5Files = Directory.GetFiles(Path.Combine(dir, "M3-T5"), "evidence-*.json");
            var m3t7Files = Directory.GetFiles(Path.Combine(dir, "M3-T7"), "evidence-*.json");
            Assert.Single(m3t5Files);
            Assert.Single(m3t7Files);

            using var docM3T5 = JsonDocument.Parse(File.ReadAllText(m3t5Files[0]));
            using var docM3T7 = JsonDocument.Parse(File.ReadAllText(m3t7Files[0]));
            Assert.Equal("M3-T5", docM3T5.RootElement.GetProperty("case_id").GetString());
            Assert.Equal("M3-T7", docM3T7.RootElement.GetProperty("case_id").GetString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // XPAY-340 — resolve-key (M3-T2), 100% offline. Target COMPLETAMENTE
    // DISTINTO de Suspend/Activate/Delete: customer_id + key_type/key_value
    // del recurso Bre-B YA provisto por Passport — nunca
    // PASSPORT_TEST_NEW_KEY_ID. customer_id/key_value SIEMPRE sintéticos.
    // ══════════════════════════════════════════════════════════════════════

    private const string SyntheticCustomerId  = "SYNTH-E2E-CUSTOMER-ID-must-never-appear-in-evidence-or-console";
    private const string SyntheticBrebKeyValue = "SYNTH-E2E-BREB-KEY-VALUE-must-never-appear-in-evidence-or-console";
    private const string SyntheticResolutionId = "synthetic-resolution-id-e2e";

    private sealed class LocalFakeResolveHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string? _errorBody;

        public LocalFakeResolveHandler(HttpStatusCode status = HttpStatusCode.OK, string? errorBody = null)
        {
            _status = status;
            _errorBody = errorBody;
        }

        public int CallCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            var response = new HttpResponseMessage(_status)
            {
                Content = new StringContent(
                    _errorBody ?? $$"""
                    {
                      "id": "{{SyntheticResolutionId}}",
                      "receptor_node": "SYNTH-NODE",
                      "resolved_at": "2026-01-01T00:00:00.000000Z",
                      "expires_at": "2026-01-01T00:30:00.000000Z",
                      "customer_id": "{{SyntheticCustomerId}}",
                      "owner": { "first_name": "Synthetic", "identification_type": "CC", "identification_number": "0000000000", "type": "PERSON" },
                      "key": { "key_type": "BCODE", "key_value": "{{SyntheticBrebKeyValue}}" },
                      "participant": { "name": "Synthetic Participant", "identification_number": "1111111111" },
                      "account": { "account_number": "2222222222", "account_type": "SAVINGS" }
                    }
                    """,
                    System.Text.Encoding.UTF8, "application/json"),
            };
            return response;
        }
    }

    private static IConfiguration ResolveKeyConfig(
        string? customerId = SyntheticCustomerId, string? brebKeyType = "BCODE", string? brebKeyValue = SyntheticBrebKeyValue) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            [PassportOptions.EnvBaseUrl] = BaseUrl,
            [PassportOptions.EnvClientId] = "synthetic-key",
            [PassportOptions.EnvClientSecret] = "synthetic-secret",
            [HarnessTargetConfig.EnvCustomerId] = customerId,
            [HarnessTargetConfig.EnvBrebKeyType] = brebKeyType,
            [HarnessTargetConfig.EnvBrebKeyValue] = brebKeyValue,
        })
        .Build();

    private static HarnessApp.Dependencies BuildResolveDependencies(
        HttpMessageHandler handler, string evidenceDir, IConfiguration config)
    {
        IPassportHttpClient httpClient = new PassportHttpClient(
            new LocalFakeHttpClientFactory(handler), new LocalFakeTokenProvider(), config,
            NullLogger<PassportHttpClient>.Instance);

        return new HarnessApp.Dependencies(
            CustomerAccountClient: new PassportCustomerAccountClient(httpClient),
            KeyClient: new PassportKeyClient(httpClient),
            CommitShaProvider: new FixedCommitShaProvider("synthetic-e2e-commit-sha-0000000000000000000000000000000000000000"),
            EvidenceBaseDirectory: evidenceDir,
            QrClient: new PassportQrClient(httpClient));
    }

    // Tests K/L/N/O/P/Q/R/S — end-to-end obligatorio: exactamente 1 POST
    // /v1/resolve-key con el body exacto del contrato productivo,
    // evidencia M3-T2 con result=PASS, resolution_id tratado como
    // resolution_id (NUNCA key_id), customer_id/key_value real AUSENTES de
    // evidencia y consola, cero secretos.
    [Fact]
    public async Task ResolveKeyExecute_FullPath_ProducesExactlyOnePostCall_AndPassEvidence()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeResolveHandler();
            var config = ResolveKeyConfig();
            var dependencies = BuildResolveDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "resolve-key", "--execute", "--confirm-resolve-key" }, config, dependencies, output);

            Assert.Equal(1, handler.CallCount);
            Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
            Assert.Equal("/v1/resolve-key", handler.LastRequest.RequestUri!.AbsolutePath);
            Assert.Equal(BaseUrl, handler.LastRequest.RequestUri.GetLeftPart(UriPartial.Authority));

            // El request coincide con el contrato productivo: customer_id +
            // key{key_type,key_value}.
            using var reqDoc = JsonDocument.Parse(handler.LastRequestBody!);
            Assert.Equal(SyntheticCustomerId, reqDoc.RootElement.GetProperty("customer_id").GetString());
            Assert.Equal("BCODE", reqDoc.RootElement.GetProperty("key").GetProperty("key_type").GetString());
            Assert.Equal(SyntheticBrebKeyValue, reqDoc.RootElement.GetProperty("key").GetProperty("key_value").GetString());

            var caseDir = Path.Combine(dir, "M3-T2");
            var files = Directory.GetFiles(caseDir, "evidence-*.json");
            Assert.Single(files);

            var json = File.ReadAllText(files[0]);
            using var evDoc = JsonDocument.Parse(json);
            var root = evDoc.RootElement;
            Assert.Equal("M3-T2", root.GetProperty("case_id").GetString());
            Assert.Equal("sandbox", root.GetProperty("environment").GetString());
            Assert.Equal("synthetic-e2e-commit-sha-0000000000000000000000000000000000000000",
                root.GetProperty("backend_commit_sha").GetString());
            Assert.Equal("POST /v1/resolve-key", root.GetProperty("operation").GetString());
            Assert.Equal("PASS", root.GetProperty("result").GetString());
            Assert.Equal("PENDING_PASSPORT_REVIEW", root.GetProperty("review_status").GetString());

            // resolution_id tratado como resolution_id — NUNCA key_id.
            var responseSanitized = root.GetProperty("response_sanitized");
            Assert.True(responseSanitized.TryGetProperty("resolution_id_fingerprint", out _));
            Assert.False(responseSanitized.TryGetProperty("key_id_fingerprint", out _));
            Assert.False(responseSanitized.TryGetProperty("id_fingerprint", out _));

            var requestSanitized = root.GetProperty("request_sanitized");
            Assert.True(requestSanitized.TryGetProperty("customer_id_fingerprint", out _));
            Assert.True(requestSanitized.TryGetProperty("key_value_fingerprint", out _));
            Assert.Equal("BCODE", requestSanitized.GetProperty("key_type").GetString());

            // customer_id/key_value reales AUSENTES de evidence.json y consola.
            Assert.DoesNotContain(SyntheticCustomerId, json);
            Assert.DoesNotContain(SyntheticBrebKeyValue, json);
            Assert.DoesNotContain("synthetic-e2e-bearer-token", json);
            Assert.DoesNotContain("synthetic-secret", json);

            var consoleOutput = output.ToString();
            Assert.DoesNotContain(SyntheticCustomerId, consoleOutput);
            Assert.DoesNotContain(SyntheticBrebKeyValue, consoleOutput);
            Assert.DoesNotContain("synthetic-e2e-bearer-token", consoleOutput);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // G/H/I — cualquier target ausente => bloqueado ANTES de HTTP, cero
    // HTTP, cero evidencia.
    [Theory]
    [InlineData(null, "BCODE", SyntheticBrebKeyValue)]
    [InlineData(SyntheticCustomerId, null, SyntheticBrebKeyValue)]
    [InlineData(SyntheticCustomerId, "BCODE", null)]
    public async Task ResolveKeyExecute_MissingTarget_IsAborted_NoEvidenceNoHttp(
        string? customerId, string? brebKeyType, string? brebKeyValue)
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeResolveHandler();
            var config = ResolveKeyConfig(customerId, brebKeyType, brebKeyValue);
            var dependencies = BuildResolveDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "resolve-key", "--execute", "--confirm-resolve-key" }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T2")));
            Assert.Contains("result=ABORTED", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // J — key_type inválido: bloqueado a nivel LOCAL (dentro del executor,
    // no en Prepare), cero HTTP, cero evidencia.
    [Fact]
    public async Task ResolveKeyExecute_InvalidKeyType_IsLocalBlocked_NoEvidenceNoHttp()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeResolveHandler();
            var config = ResolveKeyConfig(brebKeyType: "MOBILE"); // inválido — no se normaliza a PHONE
            var dependencies = BuildResolveDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "resolve-key", "--execute", "--confirm-resolve-key" }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T2")));
            Assert.Contains("LOCAL_BLOCKED", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // T — fallo remoto simulado: exactamente 1 llamada, sin retry, evidencia FAIL saneada.
    [Fact]
    public async Task ResolveKeyExecute_PassportHttpFailure_ProducesExactlyOneHttpCall_AndFailEvidence()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeResolveHandler(
                HttpStatusCode.BadRequest, errorBody: """{ "message": "not found" }""");
            var config = ResolveKeyConfig();
            var dependencies = BuildResolveDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "resolve-key", "--execute", "--confirm-resolve-key" }, config, dependencies, output);

            Assert.Equal(1, handler.CallCount);

            var caseDir = Path.Combine(dir, "M3-T2");
            var files = Directory.GetFiles(caseDir, "evidence-*.json");
            Assert.Single(files);

            var json = File.ReadAllText(files[0]);
            using var evDoc = JsonDocument.Parse(json);
            var root = evDoc.RootElement;
            Assert.Equal("M3-T2", root.GetProperty("case_id").GetString());
            Assert.Equal("FAIL", root.GetProperty("result").GetString());
            Assert.StartsWith("PASSPORT_HTTP_FAILURE:", root.GetProperty("notes").GetString());
            Assert.DoesNotContain(SyntheticCustomerId, json);
            Assert.DoesNotContain(SyntheticBrebKeyValue, json);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // B/U — dry-run: cero HTTP, cero evidencia, no lee/imprime secretos.
    [Fact]
    public async Task ResolveKeyDryRun_NoFlags_ProducesZeroHttpZeroEvidence()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeResolveHandler();
            var config = ResolveKeyConfig();
            var dependencies = BuildResolveDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(new[] { "resolve-key" }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T2")));
            Assert.Contains("result=DRY_RUN", output.ToString());
            Assert.DoesNotContain(SyntheticCustomerId, output.ToString());
            Assert.DoesNotContain(SyntheticBrebKeyValue, output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // C — --execute solo, sin confirmación: cero HTTP.
    [Fact]
    public async Task ResolveKeyExecute_WithoutConfirm_StaysBlocked_ZeroHttp()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeResolveHandler();
            var config = ResolveKeyConfig();
            var dependencies = BuildResolveDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(new[] { "resolve-key", "--execute" }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T2")));
            Assert.Contains("result=ABORTED", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // D/F — ninguna otra confirmación autoriza resolve-key: cero HTTP.
    [Theory]
    [InlineData("--confirm-create-key")]
    [InlineData("--confirm-suspend-key")]
    [InlineData("--confirm-activate-key")]
    [InlineData("--confirm-delete-key")]
    [InlineData("--confirm-delete-already-deleted-key")]
    public async Task ResolveKeyExecute_WithWrongConfirmFlag_DoesNotAuthorize_ZeroHttp(string wrongConfirmFlag)
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeResolveHandler();
            var config = ResolveKeyConfig();
            var dependencies = BuildResolveDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "resolve-key", "--execute", wrongConfirmFlag }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T2")));
            Assert.Contains("result=ABORTED", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // XPAY-344 — create-key-missing / create-key-invalid (M3-T6), 100%
    // offline. account_id/key_type SIEMPRE sintéticos.
    // ══════════════════════════════════════════════════════════════════════

    private const string SyntheticMissingInvalidAccountId = "SYNTH-E2E-M3T6-ACCOUNT-ID";

    private static IConfiguration MissingInvalidConfig(
        string? accountId = SyntheticMissingInvalidAccountId, string? newKeyType = "BCODE") => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            [PassportOptions.EnvBaseUrl] = BaseUrl,
            [PassportOptions.EnvClientId] = "synthetic-key",
            [PassportOptions.EnvClientSecret] = "synthetic-secret",
            [HarnessTargetConfig.EnvAccountId] = accountId,
            [HarnessTargetConfig.EnvNewKeyType] = newKeyType,
        })
        .Build();

    private static HarnessApp.Dependencies BuildMissingInvalidDependencies(
        HttpMessageHandler handler, string evidenceDir, IConfiguration config)
    {
        IPassportHttpClient httpClient = new PassportHttpClient(
            new LocalFakeHttpClientFactory(handler), new LocalFakeTokenProvider(), config,
            NullLogger<PassportHttpClient>.Instance);

        return new HarnessApp.Dependencies(
            CustomerAccountClient: new PassportCustomerAccountClient(httpClient),
            KeyClient: new PassportKeyClient(httpClient),
            CommitShaProvider: new FixedCommitShaProvider("synthetic-e2e-commit-sha-0000000000000000000000000000000000000000"),
            EvidenceBaseDirectory: evidenceDir,
            QrClient: new PassportQrClient(httpClient));
    }

    // ── create-key-missing ──────────────────────────────────────────────

    // Test obligatorio: ejecución fake demuestra bloqueo LOCAL — cero
    // llamadas HTTP (incluso con un IPassportHttpClient/handler REAL
    // conectado), evidencia M3-T6-MISSING sanitizada, sin raw key_value ni
    // credenciales.
    [Fact]
    public async Task CreateKeyMissingExecute_FullPath_ProducesZeroHttpCalls_AndPassEvidence()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeMissingInvalidHandler(); // nunca debería ser invocado.
            var config = MissingInvalidConfig();
            var dependencies = BuildMissingInvalidDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "create-key-missing", "--execute", "--confirm-create-key-missing" }, config, dependencies, output);

            // CreateKey HTTP count=0 — el guard productivo de
            // CreateKeyAsync rechaza key_value vacío ANTES de cualquier
            // HTTP, incluso usando el stack productivo real.
            Assert.Equal(0, handler.CallCount);

            var caseDir = Path.Combine(dir, "M3-T6-MISSING");
            var files = Directory.GetFiles(caseDir, "evidence-*.json");
            Assert.Single(files);

            var json = File.ReadAllText(files[0]);
            using var evDoc = JsonDocument.Parse(json);
            var root = evDoc.RootElement;
            Assert.Equal("M3-T6-MISSING", root.GetProperty("case_id").GetString());
            Assert.Equal("sandbox", root.GetProperty("environment").GetString());
            Assert.Equal("synthetic-e2e-commit-sha-0000000000000000000000000000000000000000",
                root.GetProperty("backend_commit_sha").GetString());
            Assert.Equal("POST /v1/keys — blocked before transport", root.GetProperty("operation").GetString());
            // Result=PASS: el bloqueo local ES el resultado deseado.
            Assert.Equal("PASS", root.GetProperty("result").GetString());
            Assert.Equal("PENDING_PASSPORT_REVIEW", root.GetProperty("review_status").GetString());

            var requestSanitized = root.GetProperty("request_sanitized");
            Assert.Equal("key_value", requestSanitized.GetProperty("missing_field").GetString());
            Assert.Equal("LOCAL", requestSanitized.GetProperty("validation_layer").GetString());
            Assert.Equal("BCODE", requestSanitized.GetProperty("key_type").GetString());

            var responseSanitized = root.GetProperty("response_sanitized");
            Assert.False(responseSanitized.GetProperty("passport_http_attempted").GetBoolean());
            Assert.Equal("NOT_ATTEMPTED", responseSanitized.GetProperty("transport_result").GetString());
            Assert.Equal("PASS", responseSanitized.GetProperty("certification_case_result").GetString());

            // Sin raw account_id, sin credenciales, sin Bearer.
            Assert.DoesNotContain(SyntheticMissingInvalidAccountId, json);
            Assert.DoesNotContain("synthetic-e2e-bearer-token", json);
            Assert.DoesNotContain("synthetic-secret", json);

            var consoleOutput = output.ToString();
            Assert.DoesNotContain(SyntheticMissingInvalidAccountId, consoleOutput);
            Assert.DoesNotContain("synthetic-e2e-bearer-token", consoleOutput);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task CreateKeyMissingDryRun_NoFlags_ProducesZeroHttpZeroEvidence()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeMissingInvalidHandler();
            var config = MissingInvalidConfig();
            var dependencies = BuildMissingInvalidDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(new[] { "create-key-missing" }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T6-MISSING")));
            Assert.Contains("result=DRY_RUN", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task CreateKeyMissingExecute_WithoutConfirm_StaysBlocked_ZeroHttp()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeMissingInvalidHandler();
            var config = MissingInvalidConfig();
            var dependencies = BuildMissingInvalidDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(new[] { "create-key-missing", "--execute" }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T6-MISSING")));
            Assert.Contains("result=ABORTED", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task CreateKeyMissingExecute_MissingTarget_IsAborted_NoEvidenceNoHttp()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeMissingInvalidHandler();
            var config = MissingInvalidConfig(accountId: null);
            var dependencies = BuildMissingInvalidDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "create-key-missing", "--execute", "--confirm-create-key-missing" }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T6-MISSING")));
            Assert.Contains("result=ABORTED", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ── create-key-invalid ──────────────────────────────────────────────

    [Fact]
    public async Task CreateKeyInvalidDryRun_NoFlags_ProducesZeroHttpZeroEvidence()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeMissingInvalidHandler();
            var config = MissingInvalidConfig();
            var dependencies = BuildMissingInvalidDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(new[] { "create-key-invalid" }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T6-INVALID")));
            Assert.Contains("result=DRY_RUN", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task CreateKeyInvalidExecute_WithoutConfirm_StaysBlocked_ZeroHttp()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeMissingInvalidHandler();
            var config = MissingInvalidConfig();
            var dependencies = BuildMissingInvalidDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(new[] { "create-key-invalid", "--execute" }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T6-INVALID")));
            Assert.Contains("result=ABORTED", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task CreateKeyInvalidExecute_UnsupportedKeyType_IsLocalBlocked_NoEvidenceNoHttp()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeMissingInvalidHandler();
            var config = MissingInvalidConfig(newKeyType: "PHONE"); // sin contrato de formato confirmado.
            var dependencies = BuildMissingInvalidDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "create-key-invalid", "--execute", "--confirm-create-key-invalid" }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T6-INVALID")));
            Assert.Contains("LOCAL_BLOCKED", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Test obligatorio §12: "fake de Passport recibe exactamente UNA
    // intención CreateKey cuando ejecución fake autorizada" — 100% offline,
    // handler completamente fake, NUNCA red real ni Sandbox real. Simula
    // HTTP 400 (el rechazo documentalmente esperado de Passport).
    [Fact]
    public async Task CreateKeyInvalidExecute_FakePassportRejects400_ProducesExactlyOneCall_AndFailEvidence()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeMissingInvalidHandler(
                () => LocalFakeMissingInvalidHandler.Json(HttpStatusCode.BadRequest, """{ "message": "invalid key_value format" }"""));
            var config = MissingInvalidConfig();
            var dependencies = BuildMissingInvalidDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "create-key-invalid", "--execute", "--confirm-create-key-invalid" }, config, dependencies, output);

            // Exactamente 1 intento — sin retry.
            Assert.Equal(1, handler.CallCount);
            Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
            Assert.Equal("/v1/keys", handler.LastRequest.RequestUri!.AbsolutePath);

            var caseDir = Path.Combine(dir, "M3-T6-INVALID");
            var files = Directory.GetFiles(caseDir, "evidence-*.json");
            Assert.Single(files);

            var json = File.ReadAllText(files[0]);
            using var evDoc = JsonDocument.Parse(json);
            var root = evDoc.RootElement;
            Assert.Equal("M3-T6-INVALID", root.GetProperty("case_id").GetString());
            Assert.Equal("POST /v1/keys", root.GetProperty("operation").GetString());
            Assert.Equal("FAIL", root.GetProperty("result").GetString());

            var requestSanitized = root.GetProperty("request_sanitized");
            Assert.Equal("BCODE", requestSanitized.GetProperty("key_type").GetString());
            Assert.Equal("key_value_format", requestSanitized.GetProperty("invalid_dimension").GetString());
            Assert.True(requestSanitized.GetProperty("invalid_value_present").GetBoolean());
            Assert.Equal(400, requestSanitized.GetProperty("expected_http_status").GetInt32());
            Assert.Equal("PASSPORT_OFFICIAL_DOCS", requestSanitized.GetProperty("expected_http_status_provenance").GetString());

            var responseSanitized = root.GetProperty("response_sanitized");
            Assert.True(responseSanitized.GetProperty("passport_http_attempted").GetBoolean());
            Assert.Equal("FAILURE", responseSanitized.GetProperty("transport_result").GetString());
            Assert.Equal("PENDING_DIRECTOR_REVIEW", responseSanitized.GetProperty("certification_interpretation").GetString());

            // error body sensible ("invalid key_value format" es genérico,
            // pero el body crudo de Passport NUNCA se persiste íntegro).
            Assert.DoesNotContain("message", json);

            // Nunca raw account_id/credenciales/Bearer.
            Assert.DoesNotContain(SyntheticMissingInvalidAccountId, json);
            Assert.DoesNotContain("synthetic-e2e-bearer-token", json);
            Assert.DoesNotContain("synthetic-secret", json);

            var consoleOutput = output.ToString();
            Assert.DoesNotContain(SyntheticMissingInvalidAccountId, consoleOutput);
            Assert.DoesNotContain("synthetic-e2e-bearer-token", consoleOutput);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // XPAY-343/344 — regresión: todos los casos M3 previos permanecen
    // intactos y publicables (sanity check estructural, sin llamada real).
    // ══════════════════════════════════════════════════════════════════════

    private sealed class LocalFakeMissingInvalidHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _responseFactory;

        public LocalFakeMissingInvalidHandler(Func<HttpResponseMessage>? responseFactory = null) =>
            _responseFactory = responseFactory ?? (() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "id": "synthetic-id", "status": "ACTIVE" }""",
                    System.Text.Encoding.UTF8, "application/json"),
            });

        public int CallCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_responseFactory());
        }

        public static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
    }

    // ══════════════════════════════════════════════════════════════════════
    // XPAY-351 — create-qr-static (M4-T1) — camino COMPLETO end-to-end,
    // dependencias 100% fake (nunca red real). Confirma: exactamente 1
    // llamada HTTP de negocio POST /v1/qrcodes con type=STATIC y SIN
    // "amount" en el body; evidencia M4-T1 con result=PASS,
    // backend_commit_sha sintético, key_id/customer_id/qr_code_data/
    // qr_code_image reales AUSENTES de evidence.json y de la consola;
    // dry-run/aborted nunca generan HTTP ni evidencia; ninguna confirmación
    // de M3 autoriza este comando a nivel de flujo completo.
    // ══════════════════════════════════════════════════════════════════════

    private const string RealQrKeyId      = "SYNTH-E2E-QR-KEY-ID-should-be-fingerprinted-only";
    private const string RealQrCustomerId = "SYNTH-E2E-QR-CUSTOMER-ID-should-be-fingerprinted-only";
    private const string RealQrCodeData   = "00020101SYNTH-E2E-QR-DATA-must-never-appear-in-evidence6304WXYZ";
    private const string RealQrCodeImage  = "data:image/png;base64,SYNTH-E2E-QR-IMAGE-must-never-appear-in-evidence";

    private sealed class LocalFakeQrHandler : HttpMessageHandler
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
                    { "id": "synthetic-qr-id-e2e", "status": "ACTIVE", "type": "STATIC",
                      "qr_code_data": "{{RealQrCodeData}}",
                      "qr_code_image": "{{RealQrCodeImage}}",
                      "key_id": "{{RealQrKeyId}}",
                      "customer_id": "{{RealQrCustomerId}}" }
                    """,
                    System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    private static IConfiguration FullQrConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            [PassportOptions.EnvBaseUrl] = BaseUrl,
            [PassportOptions.EnvClientId] = "synthetic-key",
            [PassportOptions.EnvClientSecret] = "synthetic-secret",
            [HarnessTargetConfig.EnvNewKeyId] = RealQrKeyId,
            [HarnessTargetConfig.EnvCustomerId] = RealQrCustomerId,
        })
        .Build();

    private static HarnessApp.Dependencies BuildQrDependencies(
        LocalFakeQrHandler handler, string evidenceDir, IConfiguration config)
    {
        IPassportHttpClient httpClient = new PassportHttpClient(
            new LocalFakeHttpClientFactory(handler), new LocalFakeTokenProvider(), config,
            NullLogger<PassportHttpClient>.Instance);

        return new HarnessApp.Dependencies(
            CustomerAccountClient: new PassportCustomerAccountClient(httpClient),
            KeyClient: new PassportKeyClient(httpClient),
            CommitShaProvider: new FixedCommitShaProvider("synthetic-e2e-commit-sha-0000000000000000000000000000000000000000"),
            EvidenceBaseDirectory: evidenceDir,
            QrClient: new PassportQrClient(httpClient));
    }

    [Fact]
    public async Task CreateQrStaticExecute_FullPath_ProducesExactlyOneHttpCall_AndPassEvidence()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeQrHandler();
            var config = FullQrConfig();
            var dependencies = BuildQrDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "create-qr-static", "--execute", "--confirm-create-qr-static" }, config, dependencies, output);

            // F/G/H — exactamente 1 llamada HTTP, POST /v1/qrcodes,
            // type=STATIC, SIN "amount" ni "vat" ni "inc" ni
            // "qr_code_reference" en el body (XPAY-357).
            Assert.Equal(1, handler.CallCount);
            Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
            Assert.Equal("/v1/qrcodes", handler.LastRequest.RequestUri!.AbsolutePath);
            Assert.Contains("\"STATIC\"", handler.LastRequestBody);
            Assert.DoesNotContain("\"amount\"", handler.LastRequestBody);
            Assert.DoesNotContain("\"vat\"", handler.LastRequestBody);
            Assert.DoesNotContain("\"inc\"", handler.LastRequestBody);
            Assert.DoesNotContain("\"qr_code_reference\"", handler.LastRequestBody);

            // XPAY-356 — regresión channel: el body real de negocio debe
            // llevar "channel":"POS" (corregido desde APP tras el HTTP 400
            // observado en XPAY-354 / RCA de XPAY-355) y NUNCA "APP".
            Assert.Contains("\"channel\":\"POS\"", handler.LastRequestBody);
            Assert.DoesNotContain("\"channel\":\"APP\"", handler.LastRequestBody);

            var evidencePath = Path.Combine(dir, "M4-T1");
            var files = Directory.GetFiles(evidencePath, "evidence-*.json");
            Assert.Single(files);

            var json = File.ReadAllText(files[0]);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("M4-T1", root.GetProperty("case_id").GetString());
            Assert.Equal("POST /v1/qrcodes", root.GetProperty("operation").GetString());
            Assert.Equal("PASS", root.GetProperty("result").GetString());
            Assert.Equal(
                "synthetic-e2e-commit-sha-0000000000000000000000000000000000000000",
                root.GetProperty("backend_commit_sha").GetString());
            Assert.Equal("PENDING_PASSPORT_REVIEW", root.GetProperty("review_status").GetString());

            var requestSanitized = root.GetProperty("request_sanitized");
            Assert.Equal("STATIC", requestSanitized.GetProperty("type").GetString());
            // XPAY-356 — la evidencia futura (result=PASS) debe reflejar POS.
            Assert.Equal("POS", requestSanitized.GetProperty("channel").GetString());
            Assert.False(requestSanitized.GetProperty("amount_present").GetBoolean());
            // XPAY-357 — vat_present=false, sin inventar vat_type/vat_value/vat_base_value.
            Assert.False(requestSanitized.GetProperty("vat_present").GetBoolean());
            Assert.False(requestSanitized.GetProperty("qr_code_reference_present").GetBoolean());

            var responseSanitized = root.GetProperty("response_sanitized");
            Assert.True(responseSanitized.GetProperty("qr_code_data_present").GetBoolean());
            Assert.True(responseSanitized.GetProperty("qr_code_image_present").GetBoolean());

            // L/M/N/O — nunca key_id/customer_id/qr_code_data/qr_code_image
            // reales, ni en evidence.json ni en la salida por consola.
            Assert.DoesNotContain(RealQrKeyId, json);
            Assert.DoesNotContain(RealQrCustomerId, json);
            Assert.DoesNotContain(RealQrCodeData, json);
            Assert.DoesNotContain(RealQrCodeImage, json);
            Assert.DoesNotContain("synthetic-e2e-bearer-token", json);

            var consoleOutput = output.ToString();
            Assert.DoesNotContain(RealQrKeyId, consoleOutput);
            Assert.DoesNotContain(RealQrCustomerId, consoleOutput);
            Assert.DoesNotContain(RealQrCodeData, consoleOutput);
            Assert.DoesNotContain(RealQrCodeImage, consoleOutput);
            Assert.DoesNotContain("synthetic-e2e-bearer-token", consoleOutput);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // B. dry-run: HTTP=0, OAuth (implícito: sin token pedido, ningún
    // handler invocado), evidence=0.
    [Fact]
    public async Task CreateQrStaticDryRun_NoHttpCall_NoEvidenceFile()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeQrHandler();
            var config = FullQrConfig();
            var dependencies = BuildQrDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(new[] { "create-qr-static" }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M4-T1")));
            Assert.Contains("result=DRY_RUN", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // C. --execute sin confirmación => Aborted, cero HTTP, cero evidencia.
    [Fact]
    public async Task CreateQrStaticExecuteWithoutConfirm_Aborted_NoHttpCall_NoEvidenceFile()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeQrHandler();
            var config = FullQrConfig();
            var dependencies = BuildQrDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(new[] { "create-qr-static", "--execute" }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M4-T1")));
            Assert.Contains("result=ABORTED", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // D/S. confirmación de un comando M3 NO autoriza create-qr-static a
    // nivel de flujo completo => Aborted, cero HTTP, cero evidencia.
    [Theory]
    [InlineData("--confirm-create-key")]
    [InlineData("--confirm-resolve-key")]
    public async Task CreateQrStaticExecute_WithM3Confirmation_Aborted_NoHttpCall(string wrongConfirmFlag)
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeQrHandler();
            var config = FullQrConfig();
            var dependencies = BuildQrDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "create-qr-static", "--execute", wrongConfirmFlag }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.False(Directory.Exists(Path.Combine(dir, "M4-T1")));
            Assert.Contains("result=ABORTED", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // R. --confirm-create-qr-static NUNCA autoriza create-key, a nivel de
    // flujo completo (reutiliza el stack fake ya existente de create-key).
    [Fact]
    public async Task CreateKeyExecute_WithConfirmCreateQrStatic_Aborted_NoHttpCall()
    {
        var dir = NewTempDir();
        try
        {
            var handler = new LocalFakeHandler();
            var config = FullConfig();
            var dependencies = BuildDependencies(handler, dir, config);
            var output = new StringWriter();

            await HarnessApp.RunAsync(
                new[] { "create-key", "--execute", "--confirm-create-qr-static" }, config, dependencies, output);

            Assert.Equal(0, handler.CallCount);
            Assert.Contains("result=ABORTED", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
