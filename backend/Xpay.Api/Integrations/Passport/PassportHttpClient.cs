using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Xpay.Api.Integrations.Passport;

// XPAY-272 — implementación de IPassportHttpClient. NO conoce ningún
// endpoint de negocio (resolve-key, payments/breb, customers, accounts,
// keys, QR, webhooks) — sólo el mecanismo de envío autenticado + BaseUrl +
// JSON + cancellation token. Ningún consumidor real está conectado todavía;
// esta fase no implementa ninguna operación concreta sobre esta base.
//
// Cada llamada obtiene el token vigente del IPassportTokenProvider (que
// tiene su propio cache); esta clase no cachea nada por sí misma y no
// realiza ninguna llamada durante el arranque — sólo cuando un futuro
// consumidor invoque PostAsync/GetAsync.
//
// Logging saneado: nunca AccessToken ni el body de la petición/respuesta.
//
// Lifetime en DI: Singleton (sin estado mutable propio — el estado vive en
// el token provider inyectado).
public sealed class PassportHttpClient : IPassportHttpClient
{
    private readonly IHttpClientFactory           _httpClientFactory;
    private readonly IPassportTokenProvider       _tokenProvider;
    private readonly IConfiguration               _configuration;
    private readonly ILogger<PassportHttpClient>  _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public PassportHttpClient(
        IHttpClientFactory httpClientFactory,
        IPassportTokenProvider tokenProvider,
        IConfiguration configuration,
        ILogger<PassportHttpClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider     = tokenProvider;
        _configuration     = configuration;
        _logger            = logger;
    }

    public Task<TResponse?> PostAsync<TRequest, TResponse>(
        string relativePath, TRequest body, CancellationToken cancellationToken = default)
        => SendAsync<TRequest, TResponse>(HttpMethod.Post, relativePath, body, cancellationToken);

    public Task<TResponse?> GetAsync<TResponse>(
        string relativePath, CancellationToken cancellationToken = default)
        => SendAsync<object?, TResponse>(HttpMethod.Get, relativePath, null, cancellationToken);

    private async Task<TResponse?> SendAsync<TRequest, TResponse>(
        HttpMethod method, string relativePath, TRequest? body, CancellationToken cancellationToken)
    {
        var options = PassportOptions.FromConfiguration(_configuration, out _);

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
            throw new PassportConfigurationException(
                "PASSPORT_BASE_URL ausente — no se puede construir la URL de destino.");

        if (!Uri.TryCreate(CombineBaseAndPath(options.BaseUrl!, relativePath), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new PassportConfigurationException("PASSPORT_BASE_URL no es una URL http/https absoluta válida.");
        }

        // El token provider hace su propio fail-closed sobre ClientId/ClientSecret.
        var accessToken = await _tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        var client = _httpClientFactory.CreateClient();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
        using var linkedCts  = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
            request.Content = JsonContent.Create(body);

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
            _logger.LogWarning("passport.http: timeout de transporte tras {Timeout}s.", options.TimeoutSeconds);
            throw new PassportTransportException("Timeout de conexión con Passport.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("passport.http: fallo de conexión. ExceptionType={Type}", ex.GetType().Name);
            throw new PassportTransportException("Error de conexión con Passport.");
        }

        using (response)
        {
            var status = (int)response.StatusCode;

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("passport.http: rechazo de autorización HTTP {Status}.", status);
                throw new PassportAuthenticationException($"Passport rechazó la solicitud (HTTP {status}).");
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("passport.http: respuesta de error HTTP {Status}.", status);
                throw new PassportTransportException($"Passport respondió con error HTTP {status}.");
            }

            if (response.Content.Headers.ContentLength is 0)
                return default;

            try
            {
                return await response.Content
                    .ReadFromJsonAsync<TResponse>(JsonOpts, linkedCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // cancelación del caller — se propaga sin reclasificar
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning(
                    "passport.http: timeout durante la lectura de la respuesta tras {Timeout}s.",
                    options.TimeoutSeconds);
                throw new PassportTransportException("Timeout de conexión con Passport.");
            }
            catch (Exception ex) when (ex is JsonException or HttpRequestException or InvalidOperationException or NotSupportedException)
            {
                _logger.LogWarning("passport.http: respuesta ilegible.");
                throw new PassportProtocolException("Respuesta de Passport no interpretable.");
            }
        }
    }

    private static string CombineBaseAndPath(string baseUrl, string path)
        => $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
}
