using Microsoft.Extensions.Configuration;
using Xpay.Api.Common;
using Xpay.Api.Services;
using Xpay.Api.Tests.Common;
using Xunit;

namespace Xpay.Api.Tests.Services;

// P0-B2 — plantilla de recuperación: token solo en el fragmento, ruta propia
// (Identidad:RutaRecuperacionClave) y fallo cerrado en productivo, sin
// alterar la plantilla de verificación de P0-B1.
public class PlantillasRecuperacionClaveTests
{
    private const string Token = "TOKEN-SINTETICO_0123456789-abcdefghijklmnopq";
    private static readonly TokenEmitido Emitido = new(Token, new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc));

    private static PlantillasEmailIdentidad Plantillas(string ambiente, string? url, string? rutaRecuperacion, string? rutaVerificacion = null)
    {
        var valores = new Dictionary<string, string?>();
        if (url is not null) valores[PlantillasEmailIdentidad.ClaveUrlFrontend] = url;
        if (rutaRecuperacion is not null) valores[PlantillasEmailIdentidad.ClaveRutaRecuperacion] = rutaRecuperacion;
        if (rutaVerificacion is not null) valores[PlantillasEmailIdentidad.ClaveRutaVerificacion] = rutaVerificacion;
        return new(new ConfigurationBuilder().AddInMemoryCollection(valores).Build(), AmbienteXpayTests.Ambiente(ambiente));
    }

    private static string Enlace(MensajeEmail mensaje)
        => mensaje.CuerpoTexto.Split('\n').Single(l => l.Contains("#token=", StringComparison.Ordinal)).Trim();

    [Fact]
    public void Productivo_ConfiguracionValida_TokenSoloEnElFragmento()
    {
        var mensaje = Plantillas("Production", "https://app.xpay.test/", "/cuenta/recuperar").RecuperacionClave("a@b.co", Emitido);
        var enlace  = Enlace(mensaje);

        Assert.Equal("https://app.xpay.test/cuenta/recuperar#token=" + Token, enlace);
        Assert.Equal(string.Empty, new Uri(enlace).Query);
        Assert.Equal("a@b.co", mensaje.Destinatario);
        Assert.Equal("Restablece tu contraseña de XPAY", mensaje.Asunto);
        Assert.Contains("2026-03-10 12:00", mensaje.CuerpoTexto);
        Assert.True(mensaje.ToString().IndexOf(Token, StringComparison.Ordinal) < 0);
    }

    [Theory]
    [InlineData(null, "/r")]
    [InlineData("https://app.xpay.test", null)]
    [InlineData("http://app.xpay.test", "/r")]
    [InlineData("https://app.xpay.test", "r")]
    [InlineData("https://app.xpay.test", "/r#x")]
    [InlineData("https://app.xpay.test#x", "/r")]
    public void Productivo_ConfiguracionAusenteOInvalida_FallaCerrado(string? url, string? ruta)
    {
        var plantillas = Plantillas("Production", url, ruta);
        Assert.Throws<IdentidadConfiguracionException>(() => plantillas.ValidarConfiguracionRecuperacion());
        Assert.Throws<IdentidadConfiguracionException>(() => plantillas.RecuperacionClave("a@b.co", Emitido));
    }

    [Fact]
    public void Productivo_RutasIndependientes_VerificacionNoHabilitaRecuperacion()
    {
        var plantillas = Plantillas("Production", "https://app.xpay.test", rutaRecuperacion: null, rutaVerificacion: "/verificar");
        plantillas.ValidarConfiguracion();
        Assert.Throws<IdentidadConfiguracionException>(() => plantillas.ValidarConfiguracionRecuperacion());
        Assert.Equal("https://app.xpay.test/verificar#token=" + Token, Enlace(plantillas.VerificacionEmail("a@b.co", Emitido)));
    }

    [Fact]
    public void NoProductivo_SinConfiguracion_UsaDestinoNoRealDeRecuperacion()
    {
        var plantillas = Plantillas("QA", null, null);
        plantillas.ValidarConfiguracionRecuperacion();
        Assert.StartsWith(PlantillasEmailIdentidad.DestinoNoRealRecuperacion + "#token=",
            Enlace(plantillas.RecuperacionClave("a@b.co", Emitido)));
    }
}
