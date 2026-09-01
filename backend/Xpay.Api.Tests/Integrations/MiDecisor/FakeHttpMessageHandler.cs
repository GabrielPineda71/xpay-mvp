using System.Net;
using System.Net.Http;
using System.Text;

namespace Xpay.Api.Tests.Integrations.MiDecisor;

// Ayudantes de test SIN dependencias externas (nada de Moq / WireMock /
// MockHttp). Sólo se usan en memoria dentro de los tests.

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

// HttpContent cuyos headers ya "llegaron" pero cuya lectura del body se cuelga
// hasta que se cancela el token — para probar el timeout interno DURANTE la
// lectura del body (no durante SendAsync). El stream de lectura honra el
// CancellationToken que le pasa el deserializador, de modo que la lectura
// falla con OperationCanceledException cuando vence el CTS del provider.
internal sealed class StallingHttpContent : HttpContent
{
    public StallingHttpContent()
        => Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

    protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
        => Task.FromResult<Stream>(new BlockingStream());

    protected override Task<Stream> CreateContentReadStreamAsync()
        => Task.FromResult<Stream>(new BlockingStream());

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        => throw new NotSupportedException();

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }

    private sealed class BlockingStream : Stream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
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
