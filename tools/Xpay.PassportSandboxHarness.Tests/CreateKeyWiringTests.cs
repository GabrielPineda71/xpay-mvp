using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-325 — confirma que el pipeline de evidencia de create-key (M3-T1) se
// construye sobre el stack REAL (PassportHttpClient/PassportKeyClient), no
// un stack duplicado, y que el EvidenceRecord resultante nunca contiene el
// key_value/account_id reales — sólo saneados. Ningún test de este archivo
// abre un socket: el HttpMessageHandler es un fake local en memoria.
public class CreateKeyWiringTests
{
    private const string RealAccountId = "SYNTH-REAL-ACCOUNT-ID-should-be-fingerprinted-only";
    private const string RealKeyValue  = "SYNTH-REAL-KEY-VALUE-must-never-appear-in-evidence";

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

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""
                    { "id": "synthetic-key-id-harness-test", "status": "ACTIVE",
                      "account_id": "{{RealAccountId}}",
                      "key": { "key_type": "BCODE", "key_value": "{{RealKeyValue}}" } }
                    """,
                    System.Text.Encoding.UTF8, "application/json"),
            };
            return response;
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
            => Task.FromResult("synthetic-harness-test-token");
    }

    [Fact]
    public async Task RealPassportKeyClient_CreateKey_ProducesRedactedEvidence_NoDuplicateStack()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [PassportOptions.EnvBaseUrl] = "https://api.paas.sandbox.co.passportfintech.com",
            })
            .Build();

        var handler = new LocalFakeHandler();
        var httpClientFactory = new LocalFakeHttpClientFactory(handler);

        // Tipos EXACTOS de backend/Xpay.Api.Integrations.Passport.
        IPassportHttpClient httpClient = new PassportHttpClient(
            httpClientFactory, new LocalFakeTokenProvider(), config, NullLogger<PassportHttpClient>.Instance);
        IPassportKeyClient keyClient = new PassportKeyClient(httpClient);

        Assert.IsType<PassportKeyClient>(keyClient);
        Assert.IsType<PassportHttpClient>(httpClient);

        var request = new PassportCreateKeyRequest(
            AccountId: RealAccountId,
            Key: new PassportKeyRequest(PassportKeyType.BCODE, RealKeyValue))
        {
            DisplayName = "XPay Certification Test Key",
        };

        var response = await keyClient.CreateKeyAsync(request);
        Assert.Equal(1, handler.CallCount); // exactamente una llamada, nunca red real.

        var commitSha = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000")
            .GetCommitSha();
        var executedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var evidence = CreateKeyEvidenceBuilder.BuildSuccess(
            request, response, httpStatus: 200, commitSha, executedAt,
            automatedTestReference: "CreateKeyWiringTests.RealPassportKeyClient_CreateKey_ProducesRedactedEvidence_NoDuplicateStack");

        Assert.Equal("M3-T1", evidence.CaseId);
        Assert.Equal("sandbox", evidence.Environment);
        Assert.Equal(commitSha, evidence.BackendCommitSha);
        Assert.Equal("POST /v1/keys", evidence.Operation);
        Assert.Equal(EvidenceRecord.ResultPass, evidence.Result);

        // Nunca el valor real — sólo fingerprint / REDACTED / categoría.
        Assert.Equal("REDACTED", evidence.RequestSanitized["key_value"]);
        Assert.Equal("BCODE", evidence.RequestSanitized["key_type"]!.ToString());
        Assert.DoesNotContain(RealAccountId, evidence.RequestSanitized.Values.Select(v => v?.ToString() ?? ""));
        Assert.DoesNotContain(RealKeyValue, evidence.RequestSanitized.Values.Select(v => v?.ToString() ?? ""));
        Assert.DoesNotContain(RealAccountId, evidence.ResponseSanitized.Values.Select(v => v?.ToString() ?? ""));
        Assert.DoesNotContain(RealKeyValue, evidence.ResponseSanitized.Values.Select(v => v?.ToString() ?? ""));

        // Persistir y confirmar que el archivo tampoco contiene los valores reales.
        var dir = Path.Combine(Path.GetTempPath(), "xpay-evidence-wiring-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = EvidenceWriter.Write(dir, evidence);
            var json = File.ReadAllText(path);
            Assert.DoesNotContain(RealAccountId, json);
            Assert.DoesNotContain(RealKeyValue, json);
            Assert.DoesNotContain("synthetic-harness-test-token", json); // nunca el Bearer
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
