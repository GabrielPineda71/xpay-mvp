using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Xpay.Api.Integrations.MiDecisor;

// M2.2 — cliente de consulta de riesgo Persona Natural de MiDecisor.
//
// Flujo por invocación:
//   1. valida el request LOCALMENTE (sin token, sin HTTP);
//   2. valida configuración y arma la URI de consulta (sin token, sin HTTP);
//   3. obtiene el access token del IMiDecisorTokenProvider (1 llamada);
//   4. envía EXACTAMENTE UNA petición HTTP POST con Bearer;
//   5. clasifica por HTTP status ANTES de intentar interpretar el body;
//   6. en 200, deserializa el envelope y lo mapea a MiDecisorResultado.
//
// SIN reintentos (401/403/404/429/5xx → error controlado, 1 sola llamada).
// SIN invalidar el token. SIN refresh_token. SIN decisión de crédito, edad,
// ni evaluación de alertas. `score` y `montoSugerido` se devuelven como
// string crudo. Logging saneado: nunca documento, apellido, token, headers,
// bodies ni datos de riesgo.
//
// Lifetime en DI: Singleton (sin estado; dependencias singleton-safe).
public sealed class MiDecisorClient : IMiDecisorClient
{
    private readonly IHttpClientFactory        _httpClientFactory;
    private readonly IMiDecisorTokenProvider   _tokenProvider;
    private readonly IConfiguration            _configuration;
    private readonly ILogger<MiDecisorClient>  _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public MiDecisorClient(
        IHttpClientFactory httpClientFactory,
        IMiDecisorTokenProvider tokenProvider,
        IConfiguration configuration,
        ILogger<MiDecisorClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider     = tokenProvider;
        _configuration     = configuration;
        _logger            = logger;
    }

    public async Task<MiDecisorResultado> ConsultarPersonaNaturalAsync(
        MiDecisorConsultaRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ── 1. Validación LOCAL — sin token, sin HTTP.
        var tipoXpay = (request.TipoIdentificacion ?? string.Empty).Trim();
        var numero   = (request.NumeroIdentificacion ?? string.Empty).Trim();
        var apellido = (request.ApellidoRazonSocial ?? string.Empty).Trim();

        if (!TipoDocumentoMiDecisorMapper.TryMapPersonaNatural(tipoXpay, out var tipoCodigo))
            throw new MiDecisorRequestValidationException(
                "Tipo de identificación no soportado para Persona Natural.");

        if (numero.Length is < 3 or > 13 || !EsSoloDigitosAscii(numero))
            throw new MiDecisorRequestValidationException(
                "El número de identificación debe tener entre 3 y 13 dígitos.");

        if (apellido.Length == 0)
            throw new MiDecisorRequestValidationException(
                "El apellido / razón social es obligatorio.");

        // ── 2. Configuración + URI — sin token, sin HTTP.
        var options = MiDecisorOptions.FromConfiguration(_configuration, out var numericWarnings);
        foreach (var key in numericWarnings)
            _logger.LogWarning("midecisor.query: config {Key} inválida; se usa el default.", key);

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
            throw new MiDecisorConfigurationException(
                "Configuración de MiDecisor incompleta (base URL ausente).");

        if (!Uri.TryCreate(CombineBaseAndPath(options.BaseUrl!, options.QueryPath), UriKind.Absolute, out var queryUri)
            || (queryUri.Scheme != Uri.UriSchemeHttp && queryUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new MiDecisorConfigurationException(
                "MIDECISOR_BASE_URL / MIDECISOR_QUERY_PATH no forman una URL http/https absoluta válida.");
        }

        // ── 3. Token — una sola llamada, sólo tras validar todo lo anterior.
        //    Una MiDecisorException del provider se propaga sin envolver;
        //    la cancelación del caller también.
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        // ── 4. Una única petición HTTP.
        var client = _httpClientFactory.CreateClient();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
        using var linkedCts  = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, queryUri);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpRequest.Content = JsonContent.Create(
            new MiDecisorConsultaRequest(tipoCodigo, numero, apellido));

        HttpResponseMessage response;
        try
        {
            response = await client
                .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // cancelación del caller — se propaga sin reclasificar
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("midecisor.query: timeout de transporte tras {Timeout}s.", options.TimeoutSeconds);
            throw new MiDecisorTransportException("Timeout de conexión con el proveedor de riesgo.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("midecisor.query: fallo de conexión. ExceptionType={Type}", ex.GetType().Name);
            throw new MiDecisorTransportException("Error de conexión con el proveedor de riesgo.");
        }

        using (response)
        {
            var status = (int)response.StatusCode;

            // ── 5. Clasificación por HTTP status ANTES de interpretar el body.
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    break;

                case HttpStatusCode.Unauthorized:
                case HttpStatusCode.Forbidden:
                    // NO retry, NO invalidación de token.
                    _logger.LogWarning("midecisor.query: acceso rechazado HTTP {Status}.", status);
                    throw new MiDecisorAuthenticationException(
                        $"El proveedor de riesgo rechazó el acceso (HTTP {status}).");

                default:
                    // 400 / 404 / 429 / 5xx / cualquier otro non-2xx. NO retry.
                    _logger.LogWarning("midecisor.query: respuesta HTTP {Status}.", status);
                    throw new MiDecisorTransportException(
                        $"El proveedor de riesgo respondió con error HTTP {status}.");
            }

            MiDecisorRespuestaEnvelope? envelope;
            try
            {
                envelope = await response.Content
                    .ReadFromJsonAsync<MiDecisorRespuestaEnvelope>(JsonOpts, linkedCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("midecisor.query: timeout leyendo la respuesta tras {Timeout}s.", options.TimeoutSeconds);
                throw new MiDecisorTransportException("Timeout de conexión con el proveedor de riesgo.");
            }
            catch (Exception ex) when (ex is JsonException or HttpRequestException or InvalidOperationException or NotSupportedException)
            {
                _logger.LogWarning("midecisor.query: respuesta no interpretable.");
                throw new MiDecisorProtocolException("Respuesta del proveedor de riesgo no interpretable.");
            }

            return MapearEnvelope(envelope);
        }
    }

    // ── 6. Mapeo del envelope 200 a MiDecisorResultado. Sin interpretar valores.
    private MiDecisorResultado MapearEnvelope(MiDecisorRespuestaEnvelope? envelope)
    {
        var estado = envelope?.Status?.Trim();

        if (string.Equals(estado, "PRECONDITION_FAILED", StringComparison.Ordinal))
        {
            _logger.LogWarning("midecisor.query: el proveedor rechazó la consulta (envelope PRECONDITION_FAILED).");
            throw new MiDecisorQueryRejectedException("El proveedor de riesgo rechazó la consulta.");
        }

        if (!string.Equals(estado, "ACCEPTED", StringComparison.Ordinal))
        {
            _logger.LogWarning("midecisor.query: envelope status no reconocido.");
            throw new MiDecisorProtocolException(
                "El proveedor de riesgo devolvió un envelope con estado no reconocido.");
        }

        var content = envelope!.Content;
        if (content is null || content.Respuesta is null)
            throw new MiDecisorProtocolException(
                "El proveedor de riesgo devolvió una respuesta ACCEPTED incompleta.");

        // informacionRiesgo PUEDE ser null en una respuesta ACCEPTED válida
        // (persona sin información de riesgo) — no es un error de protocolo.
        var riesgo       = content.Respuesta.InformacionRiesgo;
        var alertasCount = riesgo?.Alertas?.Count ?? 0;

        _logger.LogInformation(
            "midecisor.query: OK envelope={Estado} content={ContentStatus} conInformacion={ConInfo} alertas={Alertas}.",
            estado, content.Status, riesgo?.ConInformacion, alertasCount);

        return new MiDecisorResultado(
            EstadoEnvelope:   estado,
            ContentStatus:    content.Status,
            ConInformacion:   riesgo?.ConInformacion,
            ScoreRaw:         riesgo?.Score,
            Viabilidad:       riesgo?.Viabilidad,
            RatingRecaudos:   riesgo?.RatingRecaudos,
            MontoSugeridoRaw: riesgo?.MontoSugerido,
            AlertasCount:     alertasCount);
    }

    private static string CombineBaseAndPath(string baseUrl, string path)
        => $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

    private static bool EsSoloDigitosAscii(string value)
    {
        foreach (var c in value)
            if (!char.IsAsciiDigit(c))
                return false;
        return value.Length > 0;
    }
}
