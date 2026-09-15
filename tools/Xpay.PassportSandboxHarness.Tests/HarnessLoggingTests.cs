using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-330 — regresión OFFLINE del incidente de logging sensible (ejecución
// real de M3-T3, Suspend Key): los logging handlers AUTOMÁTICOS que
// Microsoft.Extensions.Http (AddHttpClient()) adjunta a cada HttpClient
// registraron a nivel Information la URI completa de cada request —
// incluyendo el key_id remoto real embebido en el path. Estos tests
// ejercitan esa MISMA capa (IHttpClientFactory real + HarnessLogging.
// Configure, no un mock de PassportHttpClient) contra un HttpMessageHandler
// 100% fake — nunca hay red real, y ningún dato es real: todos los
// "canarios" son literales sintéticos que jamás deben aparecer en ningún
// log capturado.
public class HarnessLoggingTests
{
    private const string SensitiveKeyIdCanary   = "synthetic-sensitive-key-id-never-log";
    private const string SensitiveAccountCanary = "synthetic-sensitive-account";
    private const string SensitiveValueCanary   = "synthetic-sensitive-value";
    private const string BearerCanary           = "synthetic-sensitive-bearer-token-never-log";

    // Handler fake — responde siempre 200 OK, sin cuerpo, sin tocar red.
    private sealed class FakeHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    // ILoggerProvider real (no un ILogger<T> inyectado directamente): pasa
    // por el pipeline COMPLETO de filtrado de Microsoft.Extensions.Logging
    // (LoggerFilterOptions.MinLevel, fijado por HarnessLogging.Configure),
    // exactamente como el ConsoleLoggerProvider real del harness — a
    // diferencia de un ILogger<T> fake inyectado a mano (que ignora ese
    // filtro), esto SÍ demuestra que el nivel mínimo global suprime lo que
    // habría llegado a consola.
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _messages = new();
        public IReadOnlyList<string> Messages => _messages;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);

        public void Dispose() { }

        private sealed class CapturingLogger : ILogger
        {
            private readonly List<string> _sink;
            public CapturingLogger(List<string> sink) => _sink = sink;

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

            // Deliberadamente siempre habilitado: el filtrado real ya
            // ocurrió ANTES de llegar aquí, a nivel de LoggerFilterOptions
            // (HarnessLogging.Configure). Si algo llega hasta este punto,
            // es exactamente lo que un sink real (consola) también vería.
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (_sink) { _sink.Add(formatter(state, exception)); }
            }

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();
                public void Dispose() { }
            }
        }
    }

    private static (IHttpClientFactory Factory, CapturingLoggerProvider Capture, FakeHandler Handler) BuildRealHttpPipeline()
    {
        var capture = new CapturingLoggerProvider();
        var handler = new FakeHandler();

        var services = new ServiceCollection();
        // Program.cs llama services.AddHttpClient() (sin nombre) y luego
        // _httpClientFactory.CreateClient() (sin nombre) — ambos resuelven
        // al cliente "default" (Options.DefaultName = ""). Para poder
        // reemplazar su handler de transporte por el fake hace falta el
        // IHttpClientBuilder de ESE mismo cliente por nombre explícito —
        // AddHttpClient() sin argumentos sólo registra la infraestructura
        // core y devuelve IServiceCollection, no un builder. Los logging
        // handlers automáticos (los que causaron el incidente) se adjuntan
        // igual, sea por el registro implícito o por nombre explícito.
        services.AddHttpClient(string.Empty).ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddLogging(builder =>
        {
            HarnessLogging.Configure(builder);
            builder.AddProvider(capture);
        });

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IHttpClientFactory>(), capture, handler);
    }

    // ── Regresión path-sensible (Suspend/Activate/Delete Key) ──────────

    [Fact]
    public async Task HttpClientPipeline_PathWithSensitiveKeyId_NeverLogsCanaryOrBearer()
    {
        var (factory, capture, handler) = BuildRealHttpPipeline();
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(
            HttpMethod.Patch, $"https://passport.test/v1/keys/{SensitiveKeyIdCanary}/suspend");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BearerCanary);

        var response = await client.SendAsync(request);

        // HTTP fake efectivamente ejecutado — no es un test que pase por
        // no haber corrido nada.
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        AssertNoCanaryLeaked(capture.Messages, SensitiveKeyIdCanary, BearerCanary);
    }

    // ── Regresión query-sensible (List Keys) ────────────────────────────

    [Fact]
    public async Task HttpClientPipeline_QueryWithSensitiveAccountAndValue_NeverLogsCanaries()
    {
        var (factory, capture, handler) = BuildRealHttpPipeline();
        var client = factory.CreateClient();

        var uri = $"https://passport.test/v1/keys?account_id={Uri.EscapeDataString(SensitiveAccountCanary)}" +
                  $"&key_type=BCODE&key_value={Uri.EscapeDataString(SensitiveValueCanary)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BearerCanary);

        var response = await client.SendAsync(request);

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        AssertNoCanaryLeaked(capture.Messages, SensitiveAccountCanary, SensitiveValueCanary, BearerCanary);
    }

    // ── Confirma que NO es un apagado ciego: los LogWarning explícitos ya
    // saneados del stack Passport SIGUEN apareciendo (mismo nivel mínimo,
    // Warning, los deja pasar) ─────────────────────────────────────────

    [Fact]
    public void HarnessLogging_PreservesExplicitWarningsAndErrors()
    {
        var capture = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            HarnessLogging.Configure(builder);
            builder.AddProvider(capture);
        });

        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("passport.http");

        logger.LogWarning("passport.http: respuesta de error HTTP {Status}.", 500);
        logger.LogInformation("esto es informativo y NO sensible, pero debe suprimirse igual (nivel Warning).");

        Assert.Contains(capture.Messages, m => m.Contains("respuesta de error HTTP 500"));
        Assert.DoesNotContain(capture.Messages, m => m.Contains("esto es informativo"));
    }

    // ── Confirma explícitamente que las líneas típicas de los logging
    // handlers automáticos ("Start processing HTTP request", "Sending
    // HTTP request" — las que produjeron el incidente real) no aparecen ──

    [Fact]
    public async Task HttpClientPipeline_NeverEmitsDefaultRequestProcessingLines()
    {
        var (factory, capture, _) = BuildRealHttpPipeline();
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://passport.test/v1/keys/whatever/suspend");
        await client.SendAsync(request);

        Assert.DoesNotContain(capture.Messages, m => m.Contains("Start processing HTTP request"));
        Assert.DoesNotContain(capture.Messages, m => m.Contains("Sending HTTP request"));
        Assert.DoesNotContain(capture.Messages, m => m.Contains("Received HTTP response"));
        Assert.DoesNotContain(capture.Messages, m => m.Contains("End processing HTTP request"));
    }

    private static void AssertNoCanaryLeaked(IReadOnlyList<string> messages, params string[] canaries)
    {
        foreach (var canary in canaries)
            Assert.DoesNotContain(messages, m => m.Contains(canary, StringComparison.Ordinal));
    }
}
