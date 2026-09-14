using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Xpay.Api.Integrations.Passport;

// XPAY-272 — proveedor de access token OAuth2 client_credentials para
// Passport. Arquitectura DE REFERENCIA (Options + HttpClient + cache en
// memoria + SemaphoreSlim con doble verificación) tomada del PATRÓN de
// Integrations/MiDecisor/MiDecisorTokenProvider, pero con lógica de
// protocolo PROPIA e independiente:
//
//   - Passport envía client_id/client_secret/grant_type en el BODY JSON
//     (MiDecisor los envía como headers Client_id/Client_secret).
//   - Passport NO usa username/password.
//   - expires_in llega como NÚMERO (MiDecisor lo documenta como string).
//   - Sin refresh_token en ningún caso (igual que MiDecisor: al expirar se
//     vuelve a autenticar).
//
// - Cache EN MEMORIA por proceso: un único par (access token, expiración
//   efectiva UTC). Sin DB, sin archivo, sin cache distribuida.
// - Concurrencia: SemaphoreSlim(1,1) con doble verificación — una ráfaga
//   sobre cache frío produce UNA sola llamada de auth.
// - CancellationToken: se propaga a WaitAsync, SendAsync y a la lectura de
//   la respuesta. La cancelación del caller se re-lanza tal cual, nunca se
//   reclasifica como error de Passport.
// - Sin reintentos: cada refresh hace como máximo 1 llamada HTTP.
// - Logging saneado: nunca ClientSecret ni AccessToken, ni el body de la
//   petición/respuesta.
// - Margen de expiración (TokenSafetyMarginSeconds) es un detalle técnico
//   interno para no usar un token al borde de expiración — NO tiene
//   relación alguna con X-Passport-Timestamp (tolerancia de webhooks),
//   que es un asunto completamente distinto y no se toca en esta clase.
//
// Lifetime en DI: Singleton (el cache y el semáforo deben vivir todo el
// proceso). Dependencias inyectadas — todas singleton-safe.
public sealed class PassportTokenProvider : IPassportTokenProvider
{
    private readonly IHttpClientFactory              _httpClientFactory;
    private readonly IConfiguration                  _configuration;
    private readonly ILogger<PassportTokenProvider>  _logger;
    private readonly TimeProvider                    _timeProvider;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile CachedToken?  _cache;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public PassportTokenProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<PassportTokenProvider> logger,
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
        var options = PassportOptions.FromConfiguration(_configuration, out var numericWarnings);
        foreach (var key in numericWarnings)
            _logger.LogWarning("passport.token: config {Key} inválida; se usa el default.", key);

        // Validación de configuración — fail closed ANTES de cualquier HTTP.
        if (string.IsNullOrWhiteSpace(options.BaseUrl)
            || string.IsNullOrWhiteSpace(options.ClientId)
            || string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            throw new PassportConfigurationException(
                "Configuración de Passport incompleta (base URL o credenciales ausentes).");
        }

        if (!Uri.TryCreate(CombineBaseAndPath(options.BaseUrl!, PassportOptions.OAuthTokenPath), UriKind.Absolute, out var authUri)
            || (authUri.Scheme != Uri.UriSchemeHttp && authUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new PassportConfigurationException("PASSPORT_BASE_URL no es una URL http/https absoluta válida.");
        }

        var client = _httpClientFactory.CreateClient();

        // Timeout aplicado vía CTS enlazado — no se toca client.Timeout (el
        // client viene del factory y puede compartirse con otros consumidores).
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
        using var linkedCts  = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        using var request = new HttpRequestMessage(HttpMethod.Post, authUri)
        {
            Content = JsonContent.Create(new PassportOAuthTokenRequest(options.ClientId!, options.ClientSecret!)),
        };

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
            _logger.LogWarning("passport.token: timeout de transporte tras {Timeout}s.", options.TimeoutSeconds);
            throw new PassportTransportException("Timeout de conexión con Passport.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("passport.token: fallo de conexión. ExceptionType={Type}", ex.GetType().Name);
            throw new PassportTransportException("Error de conexión con Passport.");
        }

        using (response)
        {
            var elapsedMs = _timeProvider.GetElapsedTime(startedAt).TotalMilliseconds;
            var status    = (int)response.StatusCode;

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("passport.token: auth rechazada HTTP {Status} ({Elapsed:F0} ms).", status, elapsedMs);
                throw new PassportAuthenticationException(
                    $"Passport rechazó las credenciales (HTTP {status}).");
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("passport.token: auth respondió HTTP {Status} ({Elapsed:F0} ms).", status, elapsedMs);
                throw new PassportTransportException(
                    $"Passport respondió con error HTTP {status}.");
            }

            PassportOAuthTokenResponse? payload;
            try
            {
                payload = await response.Content
                    .ReadFromJsonAsync<PassportOAuthTokenResponse>(JsonOpts, linkedCts.Token)
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
                    "passport.token: timeout de transporte durante la lectura de la respuesta tras {Timeout}s.",
                    options.TimeoutSeconds);
                throw new PassportTransportException("Timeout de conexión con Passport.");
            }
            catch (Exception ex) when (ex is JsonException or HttpRequestException or InvalidOperationException or NotSupportedException)
            {
                _logger.LogWarning("passport.token: respuesta de auth ilegible.");
                throw new PassportProtocolException("Respuesta de Passport no interpretable.");
            }

            if (payload is null || string.IsNullOrWhiteSpace(payload.AccessToken))
                throw new PassportProtocolException("La respuesta de auth no contiene access_token.");

            if (payload.ExpiresIn is null || payload.ExpiresIn <= 0)
                throw new PassportProtocolException("La respuesta de auth trae expires_in ausente o no válido.");

            var margin = Math.Max(0, options.TokenSafetyMarginSeconds);

            // Mínimo de 1 segundo: si el margen de seguridad es >= expires_in
            // NO reautenticamos en bucle — cacheamos el token recién obtenido
            // por una ventana mínima positiva y lo devolvemos. Cada invocación
            // de GetAccessTokenAsync hace como máximo 1 llamada de auth.
            var expiresInSeconds  = payload.ExpiresIn.Value;
            var effectiveLifetime = Math.Max(1, expiresInSeconds - margin);
            if (expiresInSeconds - margin <= 0)
                _logger.LogWarning("passport.token: safety margin >= expires_in; token cacheado por ventana mínima.");

            var expiresAtUtc = _timeProvider.GetUtcNow().AddSeconds(effectiveLifetime);
            _logger.LogInformation(
                "passport.token: refresh OK HTTP {Status} ({Elapsed:F0} ms), expires_in={ExpiresIn}s, token_type={TokenType}.",
                status, elapsedMs, expiresInSeconds, payload.TokenType);

            return new CachedToken(payload.AccessToken!, expiresAtUtc);
        }
    }

    private static string CombineBaseAndPath(string baseUrl, string path)
        => $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

    // Snapshot inmutable: se publica por asignación de referencia a _cache.
    private sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresAtUtc);
}
