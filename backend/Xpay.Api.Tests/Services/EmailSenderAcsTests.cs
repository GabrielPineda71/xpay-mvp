using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xpay.Api.Common;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

public sealed class EmailSenderAcsTests
{
    [Fact]
    public async Task EnviaEnlaceUnaVezConIdentidadAdministradaYSinSeguimiento()
    {
        var transporte = new Transporte(HttpStatusCode.Accepted);
        var mensaje = new MensajeEmail("persona@example.test", "Confirma tu correo en XPAY",
            "Confirma en https://app.xpaycol.com/verificar-email#token=TOKEN_SOLO_EN_CUERPO");
        var sender = Sender(transporte);

        await sender.EnviarAsync(mensaje);

        Assert.Equal(1, transporte.Envios);
        Assert.Equal(HttpMethod.Post, transporte.Metodo);
        Assert.Equal("https://acs-prueba.communication.azure.com/emails:send?api-version=" + CanalEmailAcs.ApiVersion,
            transporte.Url);
        Assert.DoesNotContain("TOKEN_SOLO_EN_CUERPO", transporte.Url);
        Assert.Equal("Bearer TOKEN_IDENTIDAD_PRUEBA", transporte.Autorizacion);
        Assert.True(Guid.TryParse(transporte.Operacion, out _));
        using var json = JsonDocument.Parse(transporte.Cuerpo!);
        Assert.Equal("DoNotReply@xpaycol.com", json.RootElement.GetProperty("senderAddress").GetString());
        Assert.Equal("persona@example.test", json.RootElement.GetProperty("recipients").GetProperty("to")[0].GetProperty("address").GetString());
        Assert.Equal(mensaje.CuerpoTexto, json.RootElement.GetProperty("content").GetProperty("plainText").GetString());
        Assert.True(json.RootElement.GetProperty("userEngagementTrackingDisabled").GetBoolean());
    }

    [Fact]
    public async Task RechazoProveedorNoExponeDestinatarioTokenNiCuerpo()
    {
        var transporte = new Transporte(HttpStatusCode.BadRequest);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Sender(transporte)
            .EnviarAsync(new("persona@example.test", "Activación", "TOKEN_PRIVADO")));
        Assert.Contains("HTTP 400", ex.Message);
        Assert.DoesNotContain("persona", ex.ToString());
        Assert.DoesNotContain("TOKEN", ex.ToString());
    }

    [Fact]
    public async Task DireccionInvalidaNoSolicitaTokenNiEnvia()
    {
        var transporte = new Transporte(HttpStatusCode.Accepted);
        var llamadas = 0;
        var sender = new EmailSenderAcs(new(new("https://acs-prueba.communication.azure.com"), "DoNotReply@xpaycol.com"),
            transporte, _ => { llamadas++; return Task.FromResult("token"); });
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.EnviarAsync(new("no-es-correo", "Asunto", "Cuerpo")));
        Assert.Equal(0, llamadas);
        Assert.Equal(0, transporte.Envios);
    }

    [Fact]
    public void AcsSoloSeSeleccionaExplicitamenteEnProduction()
    {
        var valores = ConfiguracionAcs();
        var servicios = new ServiceCollection();
        servicios.AddLogging();
        servicios.AddEmailSenderXpay("Production", new ConfigurationBuilder().AddInMemoryCollection(valores).Build());
        using var proveedor = servicios.BuildServiceProvider();
        Assert.IsType<EmailSenderAcs>(proveedor.GetRequiredService<IEmailSender>());
    }

    [Theory]
    [InlineData("QA")]
    [InlineData("Development")]
    [InlineData("Test")]
    [InlineData("Staging")]
    [InlineData("production")]
    public void ModoAcsFueraDeProductionNoActivaEnvio(string ambiente)
    {
        var servicios = new ServiceCollection();
        servicios.AddLogging();
        Assert.Throws<EmailConfiguracionException>(() => servicios.AddEmailSenderXpay(ambiente,
            new ConfigurationBuilder().AddInMemoryCollection(ConfiguracionAcs()).Build()));
    }

    [Fact]
    public void AcsNoAceptaConfiguracionSmtpDeQaEnProduction()
    {
        var valores = ConfiguracionAcs();
        valores[OpcionesEmailQaSmtp.ClaveHost] = "smtp.example.test";
        Assert.Throws<EmailConfiguracionException>(() => new ServiceCollection().AddEmailSenderXpay("Production",
            new ConfigurationBuilder().AddInMemoryCollection(valores).Build()));
    }

    internal static Dictionary<string, string?> ConfiguracionAcs() => new()
    {
        [OpcionesEmailQaSmtp.ClaveModo] = EmailSenderAcs.Modo,
        [ConfiguracionCanalesContacto.ClaveAcsEndpoint] = "https://acs-prueba.communication.azure.com",
        [ConfiguracionCanalesContacto.ClaveEmailRemitente] = "DoNotReply@xpaycol.com",
        [PlantillasEmailIdentidad.ClaveUrlFrontend] = "https://app.xpaycol.com",
        [PlantillasEmailIdentidad.ClaveRutaVerificacion] = "/verificar-email",
    };

    internal static EmailSenderAcs Sender(IHttpClientFactory transporte) =>
        new(new(new("https://acs-prueba.communication.azure.com"), "DoNotReply@xpaycol.com"),
            transporte, _ => Task.FromResult("TOKEN_IDENTIDAD_PRUEBA"));

    internal sealed class Transporte(HttpStatusCode status) : HttpMessageHandler, IHttpClientFactory
    {
        public int Envios { get; private set; }
        public string? Url { get; private set; }
        public HttpMethod? Metodo { get; private set; }
        public string? Cuerpo { get; private set; }
        public string? Autorizacion { get; private set; }
        public string? Operacion { get; private set; }
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Envios++;
            Url = request.RequestUri!.AbsoluteUri;
            Metodo = request.Method;
            Cuerpo = await request.Content!.ReadAsStringAsync(ct);
            Autorizacion = request.Headers.Authorization?.ToString();
            Operacion = request.Headers.GetValues("Operation-Id").Single();
            return new(status);
        }
    }
}
