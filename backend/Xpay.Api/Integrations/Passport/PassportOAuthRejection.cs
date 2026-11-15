using System.Text.Json;
using System.Text.RegularExpressions;

namespace Xpay.Api.Integrations.Passport;

internal static partial class PassportOAuthRejection
{
    private static readonly string[] ReferenceHeaders =
        { "x-request-id", "apim-request-id", "request-id", "x-correlation-id", "cf-ray" };

    public static string? ReadReference(HttpResponseMessage response, string clientId, string clientSecret)
    {
        foreach (var header in ReferenceHeaders)
        {
            if (!response.Headers.TryGetValues(header, out var values))
                continue;

            using var entries = values.GetEnumerator();
            if (!entries.MoveNext())
                continue;

            var value = entries.Current;
            if (entries.MoveNext() || value.Length > 36)
                continue;

            var valid = header == "cf-ray"
                ? CloudflareRay().IsMatch(value)
                : (value.Length == 36 && Guid.TryParseExact(value, "D", out _))
                  || (value.Length == 32 && Guid.TryParseExact(value, "N", out _));
            if (valid && !ReflectsCredential(value, clientId) && !ReflectsCredential(value, clientSecret))
                return value;
        }

        return null;
    }

    private static bool ReflectsCredential(string reference, string credential) =>
        reference.Contains(credential, StringComparison.OrdinalIgnoreCase)
        || credential.Contains(reference, StringComparison.OrdinalIgnoreCase)
        || (Guid.TryParse(reference, out var referenceId) && Guid.TryParse(credential, out var credentialId)
            && referenceId == credentialId);

    public static async Task<string?> ReadCodeAsync(
        HttpContent content, CancellationToken requestToken, CancellationToken callerToken)
    {
        var mediaType = content.Headers.ContentType?.MediaType;
        var maxBytes = PassportErrorBodySanitizer.MaxBodyLengthForDiagnosticsBytes;
        if (!string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase)
            && mediaType?.EndsWith("+json", StringComparison.OrdinalIgnoreCase) != true)
            return null;
        if (content.Headers.ContentLength > maxBytes)
            return null;

        using var diagnosticTimeout = CancellationTokenSource.CreateLinkedTokenSource(requestToken);
        diagnosticTimeout.CancelAfter(TimeSpan.FromSeconds(1));
        var token = diagnosticTimeout.Token;

        try
        {
            var bytes = new byte[maxBytes + 1];
            await using var stream = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
            var count = await stream.ReadAtLeastAsync(bytes, bytes.Length, false, token).ConfigureAwait(false);
            if (count == 0 || count > maxBytes)
                return null;

            using var document = JsonDocument.Parse(bytes.AsMemory(0, count));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            if (root.TryGetProperty("error", out var error))
            {
                var code = error.ValueKind == JsonValueKind.Object ? ReadField(error, "code") : KnownCode(error);
                if (code is not null)
                    return code;
            }

            return ReadField(root, "error_code") ?? ReadField(root, "code");
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception error) when (error is JsonException or HttpRequestException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? ReadField(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) ? KnownCode(property) : null;

    private static string? KnownCode(JsonElement property)
    {
        var code = property.ValueKind == JsonValueKind.String ? property.GetString() : null;
        return code is "invalid_request" or "invalid_client" or "invalid_grant" or "unauthorized_client"
            or "unsupported_grant_type" or "invalid_scope" or "access_denied" ? code : null;
    }

    [GeneratedRegex(@"\A[0-9A-Fa-f]{16}-[A-Z]{3}\z")]
    private static partial Regex CloudflareRay();
}
