using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Xpay.Api.Integrations.MiDecisor;

// M2.1 — proveedor de access token OAuth2 para MiDecisor.
//
// - Cache EN MEMORIA por proceso: un único par (access token, expiración
//   efectiva UTC). Sin DB, sin archivo, sin cache distribuida.
// - Expiración defensiva: expires_in llega como STRING; se parsea a entero
//   positivo o se lanza MiDecisorProtocolException (nada se cachea).
// - Concurrencia: SemaphoreSlim(1,1) con doble verificación — una ráfaga
//   sobre cache frío produce UNA sola llamada de auth.
// - CancellationToken: se propaga a WaitAsync, SendAsync y a la lectura de
//   la respuesta. La cancelación del caller se re-lanza tal cual, nunca se
//   reclasifica como error del proveedor.
// - Sin reintentos: cada refresh hace como máximo 1 llamada HTTP.
// - Logging saneado: nunca credenciales, token, headers sensibles ni bodies.
//
// Lifetime en DI: Singleton (el cache y el semáforo deben vivir todo el
// proceso). Dependencias inyectadas — todas singleton-safe.
public sealed class MiDecisorTokenProvider : IMiDecisorTokenProvider
{
    private readonly IHttpClientFactory              _httpClientFactory;
    private readonly IConfiguration                  _configuration;
    private readonly ILogger<MiDecisorTokenProvider> _logger;
    private readonly TimeProvider                    _timeProvider;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile CachedToken?  _cache;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public MiDecisorTokenProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<MiDecisorTokenProvider> logger,
        TimeProvider timeProvider)
    {
        _httpClientFactory = httpClientFactory;
        _configuration     = configuration;
        _logger            = logger;
        _timeProvider      = timeProvider;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        // Fast path — lectura lock-free del snapshot inmutable en cache.
        var cached = _cache;
        if (cached is not null && _timeProvider.GetUtcNow() < cached.ExpiresAtUtc)
            return cached.AccessToken;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Doble verificación: otro caller pudo refrescar mientras esperábamos.
            cached = _cache;
            if (cached is not null && _timeProvider.GetUtcNow() < cached.ExpiresAtUtc)
                return cached.AccessToken;

            var fresh = await AuthenticateOnceAsync(cancellationToken).ConfigureAwait(false);
            _cache = fresh;
            return fresh.AccessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<CachedToken> AuthenticateOnceAsync(CancellationToken cancellationToken)
    {
        var options = MiDecisorOptions.FromConfiguration(_configuration, out var numericWarnings);
        foreach (var key in numericWarnings)
            _logger.LogWarning("midecisor.token: config {Key} inválida; se usa el default.", key);

        // Validación de configuración — fail closed ANTES de cualquier HTTP.
        if (string.IsNullOrWhiteSpace(options.BaseUrl)
            || string.IsNullOrWhiteSpace(options.ClientId)
            || string.IsNullOrWhiteSpace(options.ClientSecret)
            || string.IsNullOrWhiteSpace(options.Username)
            || string.IsNullOrWhiteSpace(options.Password))
        {
            throw new MiDecisorConfigurationException(
                "Configuración de MiDecisor incompleta (base URL o credenciales ausentes).");
        }

        if (!Uri.TryCreate(CombineBaseAndPath(options.BaseUrl!, options.AuthPath), UriKind.Absolute, out var authUri)
            || (authUri.Scheme != Uri.UriSchemeHttp && authUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new MiDecisorConfigurationException("MIDECISOR_BASE_URL no es una URL http/https absoluta válida.");
        }

        var client = _httpClientFactory.CreateClient();

        // Timeout aplicado vía CTS enlazado — no se toca client.Timeout (el
        // client viene del factory y puede compartirse con otros consumidores).
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
        using var linkedCts  = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        using var request = new HttpRequestMessage(HttpMethod.Post, authUri);
        request.Headers.TryAddWithoutValidation("Client_id",     options.ClientId);
        request.Headers.TryAddWithoutValidation("Client_secret", options.ClientSecret);
        request.Content = JsonContent.Create(new MiDecisorAuthRequest(options.Username!, options.Password!));

        var startedAt = _timeProvider.GetTimestamp();
        HttpResponseMessage response;
        try
        {
            response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // cancelación del caller — se propaga sin reclasificar
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("midecisor.token: timeout de transporte tras {Timeout}s.", options.TimeoutSeconds);
            throw new MiDecisorTransportException("Timeout de conexión con el proveedor de identidad.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("midecisor.token: fallo de conexión. ExceptionType={Type}", ex.GetType().Name);
            throw new MiDecisorTransportException("Error de conexión con el proveedor de identidad.");
        }

        using (response)
        {
            var elapsedMs = _timeProvider.GetElapsedTime(startedAt).TotalMilliseconds;
            var status    = (int)response.StatusCode;

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("midecisor.token: auth rechazada HTTP {Status} ({Elapsed:F0} ms).", status, elapsedMs);
                throw new MiDecisorAuthenticationException(
                    $"El proveedor de identidad rechazó las credenciales (HTTP {status}).");
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("midecisor.token: auth respondió HTTP {Status} ({Elapsed:F0} ms).", status, elapsedMs);
                throw new MiDecisorTransportException(
                    $"El proveedor de identidad respondió con error HTTP {status}.");
            }

            MiDecisorTokenResponse? payload;
            try
            {
                payload = await response.Content
                    .ReadFromJsonAsync<MiDecisorTokenResponse>(JsonOpts, linkedCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // cancelación del caller — se propaga sin reclasificar
            }
            catch (OperationCanceledException)
            {
                // Timeout interno del provider durante la lectura del body
                // (headers ya recibidos). Mismo trato que un timeout en SendAsync.
                _logger.LogWarning(
                    "midecisor.token: timeout de transporte durante la lectura de la respuesta tras {Timeout}s.",
                    options.TimeoutSeconds);
                throw new MiDecisorTransportException("Timeout de conexión con el proveedor de identidad.");
            }
            catch (Exception ex) when (ex is JsonException or HttpRequestException or InvalidOperationException or NotSupportedException)
            {
                _logger.LogWarning("midecisor.token: respuesta de auth ilegible.");
                throw new MiDecisorProtocolException("Respuesta del proveedor de identidad no interpretable.");
            }

            if (payload is null || string.IsNullOrWhiteSpace(payload.AccessToken))
                throw new MiDecisorProtocolException("La respuesta de auth no contiene access_token.");

            if (!TryParseExpiresIn(payload.ExpiresIn, out var expiresInSeconds))
                throw new MiDecisorProtocolException("La respuesta de auth trae expires_in ausente o no válido.");

            var margin = Math.Max(0, options.TokenSafetyMarginSeconds);

            // Mínimo de 1 segundo: si el margen de seguridad es >= expires_in
            // NO reautenticamos en bucle — cacheamos el token recién obtenido
            // por una ventana mínima positiva y lo devolvemos. Cada invocación
            // de GetAccessTokenAsync hace como máximo 1 llamada de auth.
            var effectiveLifetime = Math.Max(1, expiresInSeconds - margin);
            if (expiresInSeconds - margin <= 0)
                _logger.LogWarning("midecisor.token: safety margin >= expires_in; token cacheado por ventana mínima.");

            var expiresAtUtc = _timeProvider.GetUtcNow().AddSeconds(effectiveLifetime);
            _logger.LogInformation(
                "midecisor.token: refresh OK HTTP {Status} ({Elapsed:F0} ms), expires_in={ExpiresIn}s.",
                status, elapsedMs, expiresInSeconds);

            return new CachedToken(payload.AccessToken!, expiresAtUtc);
        }
    }

    private static string CombineBaseAndPath(string baseUrl, string path)
        => $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

    private static bool TryParseExpiresIn(string? raw, out int seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        if (!int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return false;
        if (parsed <= 0)
            return false;
        seconds = parsed;
        return true;
    }

    // Snapshot inmutable: se publica por asignación de referencia a _cache.
    private sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresAtUtc);
}
