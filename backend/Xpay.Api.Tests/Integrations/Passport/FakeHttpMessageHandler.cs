using System.Net;
using System.Net.Http;
using System.Text;

namespace Xpay.Api.Tests.Integrations.Passport;

// Ayudantes de test SIN dependencias externas (nada de Moq / WireMock /
// MockHttp), SIN red real. Copia independiente y propia del patrón usado en
// Xpay.Api.Tests.Integrations.MiDecisor — NO se reutiliza esa clase para
// mantener los dos conjuntos de tests desacoplados entre sí.

// HttpMessageHandler falso: cuenta llamadas, guarda la última petición y su
// body ya materializado, y delega la respuesta en un responder inyectado.
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

    private int _callCount;
    public int CallCount => Volatile.Read(ref _callCount);

    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    public FakeHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        => _responder = responder;

    // Atajo para responders que no dependen del contenido de la petición.
    public FakeHttpMessageHandler(Func<HttpResponseMessage> responder)
        : this((_, _) => Task.FromResult(responder()))
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);
        LastRequest = request;
        if (request.Content is not null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

        return await _responder(request, cancellationToken);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

// IHttpClientFactory falso: devuelve siempre un HttpClient sobre el handler
// dado, sin disponer el handler (lo controla el test).
internal sealed class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;

    public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}

// TimeProvider determinista: reloj manual, avanzable por el test.
internal sealed class TestTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public TestTimeProvider(DateTimeOffset start) => _utcNow = start;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan by) => _utcNow = _utcNow.Add(by);
}

// IConfiguration mínimo para tests: sólo el indexador plano (el patrón que
// usa PassportOptions.FromConfiguration).
internal sealed class FakeConfiguration : Microsoft.Extensions.Configuration.IConfiguration
{
    private readonly Dictionary<string, string?> _values;

    public FakeConfiguration(Dictionary<string, string?> values) => _values = values;

    public string? this[string key]
    {
        get => _values.TryGetValue(key, out var v) ? v : null;
        set => _values[key] = value;
    }

    public Microsoft.Extensions.Configuration.IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    public IEnumerable<Microsoft.Extensions.Configuration.IConfigurationSection> GetChildren() => throw new NotSupportedException();
    public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => throw new NotSupportedException();
}
