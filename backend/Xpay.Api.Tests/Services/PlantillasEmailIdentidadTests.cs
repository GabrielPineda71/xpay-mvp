using Microsoft.Extensions.Configuration;
using Xpay.Api.Common;
using Xpay.Api.Services;
using Xpay.Api.Tests.Common;
using Xunit;

namespace Xpay.Api.Tests.Services;

// P0-B1 — PlantillasEmailIdentidad: el token solo va en el fragmento del
// enlace; en productivo la configuración ausente o inválida falla cerrado.
public class PlantillasEmailIdentidadTests
{
    private const string Token = "TOKEN-SINTETICO_0123456789-abcdefghijklmnopq";
    private static readonly TokenEmitido Emitido = new(Token, new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc));

    private static PlantillasEmailIdentidad Plantillas(string ambiente, string? url, string? ruta)
    {
        var valores = new Dictionary<string, string?>();
        if (url is not null) valores[PlantillasEmailIdentidad.ClaveUrlFrontend] = url;
        if (ruta is not null) valores[PlantillasEmailIdentidad.ClaveRutaVerificacion] = ruta;
        return new(new ConfigurationBuilder().AddInMemoryCollection(valores).Build(), AmbienteXpayTests.Ambiente(ambiente));
    }

    private static string Enlace(MensajeEmail mensaje)
        => mensaje.CuerpoTexto.Split('\n').Single(l => l.Contains("#token=", StringComparison.Ordinal)).Trim();

    [Fact]
    public void Productivo_ConfiguracionValida_TokenSoloEnElFragmento()
    {
        var mensaje = Plantillas("Production", "https://app.xpay.test/", "/cuenta/verificar").VerificacionEmail("a@b.co", Emitido);
        var enlace  = Enlace(mensaje);

        Assert.Equal("https://app.xpay.test/cuenta/verificar#token=" + Token, enlace);
        Assert.Equal(string.Empty, new Uri(enlace).Query);
        Assert.Equal("a@b.co", mensaje.Destinatario);
        Assert.True(mensaje.ToString().IndexOf(Token, StringComparison.Ordinal) < 0);
    }

    [Theory]
    [InlineData(null, "/v")]
    [InlineData("https://app.xpay.test", null)]
    [InlineData("http://app.xpay.test", "/v")]
    [InlineData("https://app.xpay.test", "v")]
    [InlineData("https://app.xpay.test", "/v?x=1")]
    [InlineData("https://app.xpay.test?x=1", "/v")]
    [InlineData("no-es-url", "/v")]
    public void Productivo_ConfiguracionAusenteOInvalida_FallaCerrado(string? url, string? ruta)
    {
        var plantillas = Plantillas("Production", url, ruta);
        Assert.Throws<IdentidadConfiguracionException>(() => plantillas.ValidarConfiguracion());
        Assert.Throws<IdentidadConfiguracionException>(() => plantillas.VerificacionEmail("a@b.co", Emitido));
    }

    [Fact]
    public void NoProductivo_SinConfiguracion_UsaDestinoNoReal()
    {
        var plantillas = Plantillas("QA", null, null);
        plantillas.ValidarConfiguracion();
        Assert.StartsWith(PlantillasEmailIdentidad.DestinoNoReal + "#token=", Enlace(plantillas.VerificacionEmail("a@b.co", Emitido)));
    }

    [Fact]
    public void NoProductivo_PermiteHttpLocal()
        => Assert.StartsWith("http://localhost:5173/verificar#token=",
            Enlace(Plantillas("Development", "http://localhost:5173", "/verificar").VerificacionEmail("a@b.co", Emitido)));
}
