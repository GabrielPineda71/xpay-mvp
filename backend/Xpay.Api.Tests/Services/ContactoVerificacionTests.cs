using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Common;
using Xpay.Api.Controllers;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// Verificación de contactos — piezas puras (sin BD): normalización de
// destinos, códigos, canales (contrato HTTP de ACS y de Meta), requisito
// desactivado por defecto y su alcance en los controllers.
public sealed class ContactoVerificacionTests
{
    // ── Destinos ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("3001234567", "573001234567")]
    [InlineData("300 123 4567", "573001234567")]
    [InlineData("+57 300 123 4567", "573001234567")]
    [InlineData("573001234567", "573001234567")]
    [InlineData("(300) 123-4567", "573001234567")]
    public void Celular_ColombianoValido_SeNormalizaAE164SinMas(string crudo, string esperado)
    {
        Assert.True(ContactoVerificacionService.TryNormalizarDestino(CanalContacto.WhatsApp, crudo, out var d));
        Assert.Equal(esperado, d);
    }

    [Theory]
    [InlineData("6011234567")]     // fijo
    [InlineData("300123456")]      // 9 dígitos
    [InlineData("+13001234567")]   // otro país
    [InlineData("+3001234567")]    // '+' sin indicativo
    [InlineData("30012345ab")]
    [InlineData("")]
    [InlineData(null)]
    public void Celular_NoMovilColombiano_SeRechaza(string? crudo)
    {
        Assert.False(ContactoVerificacionService.TryNormalizarDestino(CanalContacto.WhatsApp, crudo, out _));
    }

    [Fact]
    public void Email_SeNormalizaEnMinusculas()
    {
        Assert.True(ContactoVerificacionService.TryNormalizarDestino(CanalContacto.Email, " Juana@Correo.COM ", out var d));
        Assert.Equal("juana@correo.com", d);
        Assert.False(ContactoVerificacionService.TryNormalizarDestino(CanalContacto.Email, "no-es-correo", out _));
    }

    [Fact]
    public void Enmascarar_NoMuestraElContactoCompleto()
    {
        Assert.Equal("+57 ******4567", ContactoVerificacionService.Enmascarar(CanalContacto.WhatsApp, "573001234567"));
        Assert.Equal("j***@correo.com", ContactoVerificacionService.Enmascarar(CanalContacto.Email, "juana@correo.com"));
    }

    // ── Códigos ──────────────────────────────────────────────────────────

    [Fact]
    public void Codigo_SeisDigitos_YHashDependeDeLaSal()
    {
        for (var i = 0; i < 200; i++)
        {
            var c = ContactoVerificacionService.GenerarCodigo();
            Assert.Equal(6, c.Length);
            Assert.True(c.All(char.IsAsciiDigit));
        }
        var sal1 = new byte[16]; var sal2 = new byte[16]; sal2[0] = 1;
        Assert.Equal(ContactoVerificacionService.Hash(sal1, "123456"), ContactoVerificacionService.Hash(sal1, "123456"));
        Assert.NotEqual(ContactoVerificacionService.Hash(sal1, "123456"), ContactoVerificacionService.Hash(sal2, "123456"));
        Assert.NotEqual(ContactoVerificacionService.Hash(sal1, "123456"), ContactoVerificacionService.Hash(sal1, "123457"));
    }

    // ── Configuración de canales ─────────────────────────────────────────

    private static IConfiguration Config(Dictionary<string, string?> valores) =>
        new ConfigurationBuilder().AddInMemoryCollection(valores).Build();

    private static Dictionary<string, string?> EmailOk() => new()
    {
        [ConfiguracionCanalesContacto.ClaveAcsEndpoint]    = "https://acs-xpay-prod.unitedstates.communication.azure.com",
        [ConfiguracionCanalesContacto.ClaveEmailRemitente] = "no-reply@xpaycol.com",
    };

    private static Dictionary<string, string?> WhatsAppOk() => new()
    {
        [ConfiguracionCanalesContacto.ClaveWhatsAppNumeroId]  = "123456789012345",
        [ConfiguracionCanalesContacto.ClaveWhatsAppPlantilla] = "xpay_codigo_verificacion",
        [ConfiguracionCanalesContacto.ClaveWhatsAppIdioma]    = "es",
        [ConfiguracionCanalesContacto.ClaveWhatsAppGraphVer]  = "v23.0",
        [ConfiguracionCanalesContacto.ClaveWhatsAppToken]     = "token-de-prueba",
    };

    [Fact]
    public void Email_ConfiguracionCompleta_EsValida()
    {
        Assert.NotNull(OpcionesEmailAcs.Desde(Config(EmailOk())));
    }

    [Theory]
    [InlineData(ConfiguracionCanalesContacto.ClaveAcsEndpoint, null)]
    [InlineData(ConfiguracionCanalesContacto.ClaveAcsEndpoint, "http://acs.communication.azure.com")]
    [InlineData(ConfiguracionCanalesContacto.ClaveAcsEndpoint, "https://otro-host.example.com")]
    [InlineData(ConfiguracionCanalesContacto.ClaveEmailRemitente, null)]
    [InlineData(ConfiguracionCanalesContacto.ClaveEmailRemitente, "no-reply@otrodominio.com")]
    [InlineData(ConfiguracionCanalesContacto.ClaveEmailRemitente, "@xpaycol.com")]
    public void Email_ConfiguracionIncompletaOInvalida_QuedaNoConfigurado(string clave, string? valor)
    {
        var v = EmailOk(); v[clave] = valor;
        Assert.Null(OpcionesEmailAcs.Desde(Config(v)));
    }

    [Theory]
    [InlineData(ConfiguracionCanalesContacto.ClaveWhatsAppToken, null)]
    [InlineData(ConfiguracionCanalesContacto.ClaveWhatsAppNumeroId, "12345/../x")]
    [InlineData(ConfiguracionCanalesContacto.ClaveWhatsAppGraphVer, "23")]
    [InlineData(ConfiguracionCanalesContacto.ClaveWhatsAppPlantilla, "Plantilla Con Espacios")]
    [InlineData(ConfiguracionCanalesContacto.ClaveWhatsAppIdioma, "")]
    public void WhatsApp_ConfiguracionIncompletaOInvalida_QuedaNoConfigurado(string clave, string? valor)
    {
        var v = WhatsAppOk(); v[clave] = valor;
        Assert.Null(OpcionesWhatsAppCloud.Desde(Config(v)));
    }

    [Fact]
    public void WhatsApp_ToString_NoRevelaElToken()
    {
        var o = OpcionesWhatsAppCloud.Desde(Config(WhatsAppOk()))!;
        Assert.DoesNotContain("token-de-prueba", o.ToString());
    }

    // ── Contrato HTTP ────────────────────────────────────────────────────

    [Fact]
    public void WhatsApp_Cuerpo_CodigoEnCuerpoYBoton()
    {
        var json = JsonSerializer.Serialize(CanalWhatsAppCloudApi.CuerpoSolicitud("xpay_codigo", "es", "573001234567", "482913"));
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        Assert.Equal("whatsapp", r.GetProperty("messaging_product").GetString());
        Assert.Equal("573001234567", r.GetProperty("to").GetString());
        Assert.Equal("template", r.GetProperty("type").GetString());
        var t = r.GetProperty("template");
        Assert.Equal("xpay_codigo", t.GetProperty("name").GetString());
        Assert.Equal("es", t.GetProperty("language").GetProperty("code").GetString());
        var comps = t.GetProperty("components");
        Assert.Equal("body", comps[0].GetProperty("type").GetString());
        Assert.Equal("482913", comps[0].GetProperty("parameters")[0].GetProperty("text").GetString());
        Assert.Equal("button", comps[1].GetProperty("type").GetString());
        Assert.Equal("url", comps[1].GetProperty("sub_type").GetString());
        Assert.Equal("0", comps[1].GetProperty("index").GetString());
        Assert.Equal("482913", comps[1].GetProperty("parameters")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task WhatsApp_Envio_UsaGraphConBearer_YErrorNoExponeCuerpo()
    {
        var handler = new HandlerCaptura(HttpStatusCode.OK, "{\"messages\":[{\"id\":\"wamid.x\"}]}");
        var canal = new CanalWhatsAppCloudApi(OpcionesWhatsAppCloud.Desde(Config(WhatsAppOk()))!,
            new FabricaHttp(handler), NullLogger<CanalWhatsAppCloudApi>.Instance);

        await canal.EnviarCodigoAsync("573001234567", "482913", 10);
        Assert.Equal("https://graph.facebook.com/v23.0/123456789012345/messages", handler.Ultima!.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Ultima.Headers.Authorization!.Scheme);

        handler.Estado = HttpStatusCode.BadRequest;
        handler.Respuesta = "{\"error\":{\"code\":131026,\"message\":\"no entregable a 573001234567\"}}";
        var ex = await Assert.ThrowsAsync<CanalContactoException>(() => canal.EnviarCodigoAsync("573001234567", "482913", 10));
        Assert.Equal("WHATSAPP_HTTP_400", ex.Message);
    }

    [Fact]
    public void Acs_Cuerpo_RemitenteDestinoYSinSeguimiento()
    {
        var json = JsonSerializer.Serialize(CanalEmailAcs.CuerpoSolicitud("no-reply@xpaycol.com", "juana@correo.com", "482913", 10));
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        Assert.Equal("no-reply@xpaycol.com", r.GetProperty("senderAddress").GetString());
        Assert.Equal("juana@correo.com", r.GetProperty("recipients").GetProperty("to")[0].GetProperty("address").GetString());
        Assert.Contains("482913", r.GetProperty("content").GetProperty("plainText").GetString());
        Assert.True(r.GetProperty("userEngagementTrackingDisabled").GetBoolean());
    }

    [Fact]
    public async Task CanalNoConfigurado_NuncaSimulaExito()
    {
        var c = new CanalCodigoNoConfigurado(CanalContacto.Email);
        Assert.False(c.Disponible);
        await Assert.ThrowsAsync<CanalContactoException>(() => c.EnviarCodigoAsync("a@b.co", "123456", 10));
    }

    // ── Requisito de contactos verificados ───────────────────────────────

    private sealed class CanalFijo(string canal, bool disponible) : ICanalCodigoContacto
    {
        public string Canal => canal;
        public bool Disponible => disponible;
        public Task EnviarCodigoAsync(string destino, string codigo, int minutosVigencia, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Theory]
    [InlineData(null,   true,  true,  false)] // por defecto: desactivado
    [InlineData("TRUE", true,  true,  false)] // solo "true" exacto
    [InlineData("true", true,  true,  true)]
    [InlineData("true", false, true,  false)] // sin correo: no se aplica
    [InlineData("true", true,  false, false)] // sin WhatsApp: no se aplica
    public void Requisito_SoloEfectivoConInterruptorYAmbosCanales(string? valor, bool email, bool whatsapp, bool esperado)
    {
        var req = new RequisitoContactosVerificados(
            Config(new() { [RequisitoContactosVerificados.ClaveInterruptor] = valor }),
            new ICanalCodigoContacto[] { new CanalFijo(CanalContacto.Email, email), new CanalFijo(CanalContacto.WhatsApp, whatsapp) });
        Assert.Equal(esperado, req.Efectivo);
    }

    // ── Alcance del requisito en los controllers ─────────────────────────

    private static IEnumerable<MethodInfo> Acciones() =>
        typeof(ContactosController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

    [Fact]
    public void Requisito_SoloEnLasCuatroOperacionesIniciadasPorElUsuario()
    {
        var marcadas = Acciones()
            .Where(m => m.GetCustomAttribute<RequiereContactosVerificadosAttribute>() is not null)
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[]
        {
            "BrebController.SolicitarRetiroReal",
            "LibranzaClienteController.SolicitarAnticipo",
            "QrController.Pagar",
            "WalletsController.Transferir",
        }, marcadas);
    }

    [Fact]
    public void Requisito_CoincideConLosEndpointsConPin()
    {
        foreach (var m in Acciones())
        {
            var conPin = m.GetCustomAttribute<RequierePinTransaccionalAttribute>() is not null;
            var conContactos = m.GetCustomAttribute<RequiereContactosVerificadosAttribute>() is not null;
            Assert.True(conPin == conContactos, $"{m.DeclaringType!.Name}.{m.Name}: PIN={conPin} contactos={conContactos}");
        }
    }

    [Fact]
    public void Requisito_CorreAntesQueElPin()
    {
        Assert.True(new RequiereContactosVerificadosAttribute().Order < 0);
    }

    [Fact]
    public void ContactosController_NoDependeDelInterruptorFinancieroNiDeKyc()
    {
        foreach (var m in typeof(ContactosController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            Assert.Null(m.GetCustomAttribute<OperacionFinancieraAttribute>());
            Assert.Null(m.GetCustomAttribute<RequiereContactosVerificadosAttribute>());
            Assert.DoesNotContain(m.GetCustomAttributes<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>(), a => a.Policy == "KycAprobado");
        }
    }

    [Theory]
    [InlineData("email", CanalContacto.Email)]
    [InlineData("celular", CanalContacto.WhatsApp)]
    [InlineData("EMAIL", null)]
    [InlineData("whatsapp", null)]
    public void Ruta_SoloEmailYCelular(string ruta, string? esperado)
    {
        Assert.Equal(esperado, ContactosController.CanalDesdeRuta(ruta));
    }

    // ── Infraestructura HTTP de prueba ───────────────────────────────────

    private sealed class HandlerCaptura(HttpStatusCode estado, string respuesta) : HttpMessageHandler
    {
        public HttpStatusCode Estado { get; set; } = estado;
        public string Respuesta { get; set; } = respuesta;
        public HttpRequestMessage? Ultima { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Ultima = request;
            return Task.FromResult(new HttpResponseMessage(Estado) { Content = new StringContent(Respuesta) });
        }
    }

    private sealed class FabricaHttp(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
