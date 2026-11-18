using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xpay.Api.Common;

namespace Xpay.Api.Services;

// Enlaces de identidad por ACS; el cuerpo con token nunca se registra.
public sealed class EmailSenderAcs : IEmailSender
{
    internal const string Modo = "Acs";
    internal const string Cliente = "registro-acs-email";
    private readonly OpcionesEmailAcs _opciones;
    private readonly IHttpClientFactory _http;
    private readonly Func<CancellationToken, Task<string>> _obtenerToken;

    public EmailSenderAcs(OpcionesEmailAcs opciones, IHttpClientFactory http,
        ProveedorTokenIdentidadAdministrada tokens) : this(opciones, http, tokens.ObtenerAsync) { }

    internal EmailSenderAcs(OpcionesEmailAcs opciones, IHttpClientFactory http,
        Func<CancellationToken, Task<string>> obtenerToken)
    {
        _opciones = opciones;
        _http = http;
        _obtenerToken = obtenerToken;
    }

    public async Task EnviarAsync(MensajeEmail mensaje, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mensaje);
        if (!EmailNormalizador.TryNormalizar(mensaje.Destinatario, out var destino))
            throw new InvalidOperationException("Destinatario de identidad inválido.");

        using var solicitud = new HttpRequestMessage(HttpMethod.Post,
            new Uri(_opciones.Endpoint, $"/emails:send?api-version={CanalEmailAcs.ApiVersion}"))
        {
            Content = JsonContent.Create(new
            {
                senderAddress = _opciones.Remitente,
                recipients = new { to = new[] { new { address = destino } } },
                content = new { subject = mensaje.Asunto, plainText = mensaje.CuerpoTexto },
                userEngagementTrackingDisabled = true,
            }),
        };
        solicitud.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _obtenerToken(ct));
        solicitud.Headers.Add("Operation-Id", Guid.NewGuid().ToString());
        using var respuesta = await _http.CreateClient(Cliente).SendAsync(solicitud, ct);
        if (respuesta.StatusCode != HttpStatusCode.Accepted)
            throw new InvalidOperationException($"Envío de identidad rechazado por ACS: HTTP {(int)respuesta.StatusCode}.");
    }
}
