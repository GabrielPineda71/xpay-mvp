using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// Verificación de contactos — canales que entregan el código de un solo uso.
//   EMAIL:    Azure Communication Services Email (REST), autenticado con la
//             identidad administrada del App Service (sin secretos en la app).
//   WHATSAPP: WhatsApp Business Platform (Cloud API) con una plantilla de
//             autenticación con botón «copiar código».
// La entrega NO verifica nada: el contacto se verifica solo cuando el usuario
// escribe el código correcto (ContactoVerificacionService).
//
// Reglas comunes: ninguna implementación registra el código, el destino ni
// credenciales; ante cualquier fallo lanzan CanalContactoException con un
// motivo técnico corto (estado HTTP / código de error del proveedor).
public interface ICanalCodigoContacto
{
    string Canal { get; }
    // true solo si la configuración completa está presente. No prueba la red.
    bool Disponible { get; }
    Task EnviarCodigoAsync(string destino, string codigo, int minutosVigencia, CancellationToken ct = default);
}

public sealed class CanalContactoException(string motivo) : Exception(motivo);

// Sin configuración: nunca simula éxito.
public sealed class CanalCodigoNoConfigurado(string canal) : ICanalCodigoContacto
{
    public string Canal => canal;
    public bool Disponible => false;
    public Task EnviarCodigoAsync(string destino, string codigo, int minutosVigencia, CancellationToken ct = default) =>
        Task.FromException(new CanalContactoException("CANAL_NO_CONFIGURADO"));
}

// Claves de App Settings. Solo XPAY_CONTACTOS_WHATSAPP_TOKEN es secreto
// (referencia a Key Vault); las demás son identificadores públicos.
public static class ConfiguracionCanalesContacto
{
    public const string ClaveAcsEndpoint        = "XPAY_CONTACTOS_EMAIL_ACS_ENDPOINT";
    public const string ClaveEmailRemitente     = "XPAY_CONTACTOS_EMAIL_REMITENTE";
    public const string ClaveWhatsAppNumeroId   = "XPAY_CONTACTOS_WHATSAPP_PHONE_NUMBER_ID";
    public const string ClaveWhatsAppPlantilla  = "XPAY_CONTACTOS_WHATSAPP_PLANTILLA";
    public const string ClaveWhatsAppIdioma     = "XPAY_CONTACTOS_WHATSAPP_IDIOMA";
    public const string ClaveWhatsAppGraphVer   = "XPAY_CONTACTOS_WHATSAPP_GRAPH_VERSION";
    public const string ClaveWhatsAppToken      = "XPAY_CONTACTOS_WHATSAPP_TOKEN";

    public const string DominioRemitente = "@xpaycol.com";

    internal const string ClienteAcs      = "contactos-acs-email";
    internal const string ClienteWhatsApp = "contactos-whatsapp";
    internal const string ClienteIdentidad = "contactos-identidad-administrada";

    // Configuración parcial o inválida: el canal queda NoConfigurado (visible
    // como no disponible en GET /api/contactos/estado). No aborta el
    // arranque: un canal ausente no debe tumbar la API.
    public static IServiceCollection AddCanalesCodigoContacto(this IServiceCollection services, IConfiguration config)
    {
        services.AddHttpClient(ClienteAcs, c => c.Timeout = TimeSpan.FromSeconds(15));
        services.AddHttpClient(ClienteWhatsApp, c => c.Timeout = TimeSpan.FromSeconds(15));
        services.AddHttpClient(ClienteIdentidad, c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<ProveedorTokenIdentidadAdministrada>();

        var email = OpcionesEmailAcs.Desde(config);
        if (email is not null)
            services.AddSingleton<ICanalCodigoContacto>(sp => new CanalEmailAcs(
                email, sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<ProveedorTokenIdentidadAdministrada>(),
                sp.GetRequiredService<ILogger<CanalEmailAcs>>()));
        else
            services.AddSingleton<ICanalCodigoContacto>(_ => new CanalCodigoNoConfigurado(CanalContacto.Email));

        var whatsapp = OpcionesWhatsAppCloud.Desde(config);
        if (whatsapp is not null)
            services.AddSingleton<ICanalCodigoContacto>(sp => new CanalWhatsAppCloudApi(
                whatsapp, sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<ILogger<CanalWhatsAppCloudApi>>()));
        else
            services.AddSingleton<ICanalCodigoContacto>(_ => new CanalCodigoNoConfigurado(CanalContacto.WhatsApp));

        return services;
    }
}

public sealed record OpcionesEmailAcs(Uri Endpoint, string Remitente)
{
    // null si falta alguna clave o si los valores no son válidos.
    public static OpcionesEmailAcs? Desde(IConfiguration config)
    {
        var endpoint  = config[ConfiguracionCanalesContacto.ClaveAcsEndpoint]?.Trim();
        var remitente = config[ConfiguracionCanalesContacto.ClaveEmailRemitente]?.Trim();
        if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(remitente)) return null;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !uri.Host.EndsWith(".communication.azure.com", StringComparison.OrdinalIgnoreCase))
            return null;
        if (!remitente.EndsWith(ConfiguracionCanalesContacto.DominioRemitente, StringComparison.OrdinalIgnoreCase)
            || remitente.Length <= ConfiguracionCanalesContacto.DominioRemitente.Length)
            return null;
        return new OpcionesEmailAcs(uri, remitente);
    }
}

public sealed record OpcionesWhatsAppCloud(string NumeroId, string Plantilla, string Idioma, string GraphVersion, string Token)
{
    public static OpcionesWhatsAppCloud? Desde(IConfiguration config)
    {
        string? Leer(string k) => config[k]?.Trim();
        var numeroId  = Leer(ConfiguracionCanalesContacto.ClaveWhatsAppNumeroId);
        var plantilla = Leer(ConfiguracionCanalesContacto.ClaveWhatsAppPlantilla);
        var idioma    = Leer(ConfiguracionCanalesContacto.ClaveWhatsAppIdioma);
        var version   = Leer(ConfiguracionCanalesContacto.ClaveWhatsAppGraphVer);
        var token     = Leer(ConfiguracionCanalesContacto.ClaveWhatsAppToken);
        if (string.IsNullOrEmpty(numeroId) || string.IsNullOrEmpty(plantilla) || string.IsNullOrEmpty(idioma)
            || string.IsNullOrEmpty(version) || string.IsNullOrEmpty(token))
            return null;
        // Identificadores que viajan en la URL: solo formatos esperados.
        if (!numeroId.All(char.IsAsciiDigit)) return null;
        if (version.Length < 2 || version[0] != 'v' || !version[1..].All(c => char.IsAsciiDigit(c) || c == '.')) return null;
        if (!plantilla.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_')) return null;
        return new OpcionesWhatsAppCloud(numeroId, plantilla, idioma, version, token);
    }

    // Un record imprime todos sus miembros: el token nunca debe salir en un log.
    public override string ToString() =>
        $"OpcionesWhatsAppCloud {{ NumeroId = {NumeroId}, Plantilla = {Plantilla}, Idioma = {Idioma}, GraphVersion = {GraphVersion}, Token = *** }}";
}

// Token de Microsoft Entra para ACS mediante el endpoint local de identidad
// administrada del App Service (IDENTITY_ENDPOINT / IDENTITY_HEADER). Se
// guarda en memoria hasta 5 minutos antes de vencer.
public sealed class ProveedorTokenIdentidadAdministrada(IHttpClientFactory httpFactory, TimeProvider tiempo)
{
    public const string RecursoAcs = "https://communication.azure.com";

    private readonly SemaphoreSlim _mutex = new(1, 1);
    private string? _token;
    private DateTimeOffset _vence;

    public async Task<string> ObtenerAsync(CancellationToken ct)
    {
        if (_token is not null && tiempo.GetUtcNow() < _vence) return _token;
        await _mutex.WaitAsync(ct);
        try
        {
            if (_token is not null && tiempo.GetUtcNow() < _vence) return _token;

            var endpoint = Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT");
            var header   = Environment.GetEnvironmentVariable("IDENTITY_HEADER");
            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(header))
                throw new CanalContactoException("IDENTIDAD_ADMINISTRADA_NO_DISPONIBLE");

            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"{endpoint}?resource={Uri.EscapeDataString(RecursoAcs)}&api-version=2019-08-01");
            req.Headers.Add("X-IDENTITY-HEADER", header);
            using var resp = await httpFactory.CreateClient(ConfiguracionCanalesContacto.ClienteIdentidad).SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                throw new CanalContactoException($"IDENTIDAD_HTTP_{(int)resp.StatusCode}");

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var token = doc.RootElement.GetProperty("access_token").GetString();
            var expiraTexto = doc.RootElement.GetProperty("expires_on").ToString();
            if (string.IsNullOrEmpty(token) || !long.TryParse(expiraTexto, out var expiraUnix))
                throw new CanalContactoException("IDENTIDAD_RESPUESTA_INVALIDA");

            _token = token;
            _vence = DateTimeOffset.FromUnixTimeSeconds(expiraUnix).AddMinutes(-5);
            return token;
        }
        finally { _mutex.Release(); }
    }
}

public sealed class CanalEmailAcs(
    OpcionesEmailAcs opciones,
    IHttpClientFactory httpFactory,
    ProveedorTokenIdentidadAdministrada tokens,
    ILogger<CanalEmailAcs> logger) : ICanalCodigoContacto
{
    public const string ApiVersion = "2025-09-01";

    public string Canal => CanalContacto.Email;
    public bool Disponible => true;

    public async Task EnviarCodigoAsync(string destino, string codigo, int minutosVigencia, CancellationToken ct = default)
    {
        var cuerpo = CuerpoSolicitud(opciones.Remitente, destino, codigo, minutosVigencia);
        using var req = new HttpRequestMessage(HttpMethod.Post,
            new Uri(opciones.Endpoint, $"/emails:send?api-version={ApiVersion}"))
        {
            Content = JsonContent.Create(cuerpo),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.ObtenerAsync(ct));
        req.Headers.Add("Operation-Id", Guid.NewGuid().ToString());

        using var resp = await httpFactory.CreateClient(ConfiguracionCanalesContacto.ClienteAcs).SendAsync(req, ct);
        if ((int)resp.StatusCode != StatusCodes.Status202Accepted)
        {
            var codigoError = resp.Headers.TryGetValues("x-ms-error-code", out var v) ? v.FirstOrDefault() : null;
            logger.LogWarning("Envío de código por correo rechazado por ACS: http={Status} error={Error}",
                (int)resp.StatusCode, codigoError ?? "-");
            throw new CanalContactoException($"ACS_HTTP_{(int)resp.StatusCode}");
        }
    }

    internal static object CuerpoSolicitud(string remitente, string destino, string codigo, int minutos)
    {
        var texto =
            $"Tu código para verificar este correo en XPAY es: {codigo}\n\n" +
            $"Vence en {minutos} minutos y solo se puede usar una vez.\n" +
            "Si no lo solicitaste, ignora este mensaje. XPAY nunca te pedirá este código por llamada, chat ni correo.";
        var html =
            "<p>Tu código para verificar este correo en XPAY es:</p>" +
            $"<p style=\"font-size:24px;font-weight:bold;letter-spacing:4px\">{codigo}</p>" +
            $"<p>Vence en {minutos} minutos y solo se puede usar una vez.</p>" +
            "<p>Si no lo solicitaste, ignora este mensaje. XPAY nunca te pedirá este código por llamada, chat ni correo.</p>";
        return new
        {
            senderAddress = remitente,
            recipients = new { to = new[] { new { address = destino } } },
            content = new { subject = "Tu código de verificación XPAY", plainText = texto, html },
            userEngagementTrackingDisabled = true,
        };
    }
}

public sealed class CanalWhatsAppCloudApi(
    OpcionesWhatsAppCloud opciones,
    IHttpClientFactory httpFactory,
    ILogger<CanalWhatsAppCloudApi> logger) : ICanalCodigoContacto
{
    public string Canal => CanalContacto.WhatsApp;
    public bool Disponible => true;

    // destino: número E.164 sin '+' (p. ej. 573001234567). minutosVigencia no
    // viaja: la plantilla aprobada fija su propio aviso de caducidad.
    public async Task EnviarCodigoAsync(string destino, string codigo, int minutosVigencia, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"https://graph.facebook.com/{opciones.GraphVersion}/{opciones.NumeroId}/messages")
        {
            Content = JsonContent.Create(CuerpoSolicitud(opciones.Plantilla, opciones.Idioma, destino, codigo)),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opciones.Token);

        using var resp = await httpFactory.CreateClient(ConfiguracionCanalesContacto.ClienteWhatsApp).SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var codigoError = await CodigoErrorMetaAsync(resp, ct);
            logger.LogWarning("Envío de código por WhatsApp rechazado por Meta: http={Status} error={Error}",
                (int)resp.StatusCode, codigoError ?? "-");
            throw new CanalContactoException($"WHATSAPP_HTTP_{(int)resp.StatusCode}");
        }
    }

    // Contrato de Meta para plantillas de autenticación con botón de copiar
    // código: el código va en el cuerpo y en el parámetro del botón (índice 0).
    internal static object CuerpoSolicitud(string plantilla, string idioma, string destino, string codigo) => new
    {
        messaging_product = "whatsapp",
        recipient_type = "individual",
        to = destino,
        type = "template",
        template = new
        {
            name = plantilla,
            language = new { code = idioma },
            components = new object[]
            {
                new { type = "body", parameters = new[] { new { type = "text", text = codigo } } },
                new { type = "button", sub_type = "url", index = "0", parameters = new[] { new { type = "text", text = codigo } } },
            },
        },
    };

    // Solo el código numérico de error (error.code); el cuerpo puede repetir el destino.
    private static async Task<string?> CodigoErrorMetaAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            return doc.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("code", out var c)
                ? c.ToString()
                : null;
        }
        catch (JsonException) { return null; }
    }
}
