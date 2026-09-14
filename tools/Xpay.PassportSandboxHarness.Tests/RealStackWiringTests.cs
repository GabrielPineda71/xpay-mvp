using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// 8. el stack usado es el REAL PassportCustomerAccountClient/IPassportHttpClient
// de backend/Xpay.Api (vía ProjectReference) — NO un segundo stack HTTP/OAuth
// duplicado por el harness. Se demuestra construyendo la cadena completa
// (PassportTokenProvider -> PassportHttpClient -> PassportCustomerAccountClient)
// EXACTAMENTE como lo haría Program.cs en su rama ReadyToExecute, pero sobre
// un HttpMessageHandler FALSO local a este test (nunca red real) — ningún
// test de este archivo abre un socket.
public class RealStackWiringTests
{
    // Fake mínimo, LOCAL a este proyecto de test — no se reutiliza ninguna
    // clase interna de backend/Xpay.Api.Tests (son `internal` a esa
    // assembly). Nunca contacta la red real: sólo responde en memoria.
    private sealed class LocalFakeHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"id\":\"synthetic-customer-id-harness-test\"}",
                    System.Text.Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
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
    public async Task RealPassportCustomerAccountClient_WiresThroughRealIPassportHttpClient_NoDuplicateStack()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [PassportOptions.EnvBaseUrl] = "https://api.paas.sandbox.co.passportfintech.com",
            })
            .Build();

        var handler = new LocalFakeHandler();
        var httpClientFactory = new LocalFakeHttpClientFactory(handler);

        // Tipos EXACTOS de backend/Xpay.Api.Integrations.Passport — no hay
        // ninguna clase "HarnessHttpClient"/"HarnessTokenProvider" propia.
        IPassportHttpClient httpClient = new PassportHttpClient(
            httpClientFactory,
            new LocalFakeTokenProvider(),
            config,
            NullLogger<PassportHttpClient>.Instance);

        IPassportCustomerAccountClient client = new PassportCustomerAccountClient(httpClient);

        Assert.IsType<PassportCustomerAccountClient>(client);
        Assert.IsType<PassportHttpClient>(httpClient);

        var request = SyntheticCustomerRequestFactory.BuildSynthetic();
        var response = await client.LinkMerchantAsync(request);

        Assert.Equal("synthetic-customer-id-harness-test", response.Id);
        Assert.Equal(1, handler.CallCount); // exactamente una llamada, contra el handler FALSO local, nunca red real.
    }
}
