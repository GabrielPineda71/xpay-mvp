using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Xpay.Api.Integrations.MiDecisor;

// Compatibility established with the provider: UTF-8 JSON bytes, known length,
// application/json without charset. No request/response payload is logged.
internal static class MiDecisorJsonTransport
{
    internal const string ClientName = "MiDecisor";
    internal const int AuthRequestLimit = 65_536;
    internal const int QueryRequestLimit = 4_096;
    internal const int AuthResponseLimit = 65_536;
    internal const int QueryResponseLimit = 1_048_576;

    internal static async Task<HttpContent> CreateContentAsync<T>(T value, int limit, CancellationToken ct)
    {
        using var original = JsonContent.Create(value);
        using var buffer = new RequestBuffer(limit);
        await original.CopyToAsync(buffer, ct).ConfigureAwait(false);
        var bytes = buffer.ToArray();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Headers.ContentLength = bytes.Length;
        return content;
    }

    internal static async Task<T?> ReadJsonAsync<T>(HttpContent content, JsonSerializerOptions options, int limit, CancellationToken ct)
    {
        using var input = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var output = new MemoryStream();
        var bytes = new byte[8192];
        while (true)
        {
            var read = await input.ReadAsync(bytes.AsMemory(0, Math.Min(bytes.Length, limit + 1 - (int)output.Length)), ct).ConfigureAwait(false);
            if (read == 0) break;
            output.Write(bytes, 0, read);
            if (output.Length > limit)
                throw new MiDecisorProtocolException("La respuesta de MiDecisor excede el límite permitido.");
        }
        return JsonSerializer.Deserialize<T>(output.ToArray(), options);
    }

    private sealed class RequestBuffer(int limit) : MemoryStream
    {
        private void Check(int count)
        {
            if (count < 0 || Math.Max(Length, Position + count) > limit)
                throw new MiDecisorRequestValidationException("El request de MiDecisor excede el límite permitido.");
        }
        public override void WriteByte(byte value) { Check(1); base.WriteByte(value); }
        public override void Write(byte[] value, int offset, int count) { Check(count); base.Write(value, offset, count); }
        public override void Write(ReadOnlySpan<byte> value) { Check(value.Length); base.Write(value); }
        public override Task WriteAsync(byte[] value, int offset, int count, CancellationToken ct)
        { Check(count); return base.WriteAsync(value, offset, count, ct); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> value, CancellationToken ct = default)
        { Check(value.Length); return base.WriteAsync(value, ct); }
        public override void SetLength(long value)
        {
            if (value < 0 || value > limit) throw new MiDecisorRequestValidationException("El request de MiDecisor excede el límite permitido.");
            base.SetLength(value);
        }
    }
}
