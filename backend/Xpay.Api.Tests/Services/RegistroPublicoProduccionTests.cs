using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Services;
using Xpay.Api.Tests.Common;
using Xunit;

namespace Xpay.Api.Tests.Services;

public sealed class RegistroPublicoProduccionTests
{
    [Fact]
    public void ConfiguracionExplicitaExponeDocumentosProductivos()
    {
        using var db = new XpayDbContext(new DbContextOptionsBuilder<XpayDbContext>().Options);
        var servicio = Servicio(db, Configuracion());
        var datos = Assert.IsType<RegistroPublicoConfiguracionResponse>(servicio.ObtenerConfiguracion());
        Assert.Equal("1.2", datos.Terminos.Version);
        Assert.Equal("https://app.xpaycol.com/legal/v1.2/terminos.pdf", datos.Terminos.Url);
    }

    [Theory]
    [InlineData("XPAY_REGISTRO_PUBLICO_HABILITADO", null)]
    [InlineData("XPAY_REGISTRO_PUBLICO_HABILITADO", "false")]
    [InlineData("XPAY_REGISTRO_PUBLICO_MODALIDAD", null)]
    [InlineData("XPAY_REGISTRO_PUBLICO_MODALIDAD", "WALLET_ACTIVA")]
    [InlineData("XPAY_REGISTRO_PUBLICO_TERMINOS_SHA256", "invalido")]
    [InlineData("XPAY_REGISTRO_PUBLICO_POLITICA_DATOS_VERSION", null)]
    [InlineData("XPAY_REGISTRO_PUBLICO_TERMINOS_URL", "https://app-qa.xpaycol.com/legal/terminos.pdf")]
    [InlineData("Identidad:UrlFrontend", "https://app-qa.xpaycol.com")]
    [InlineData("Identidad:UrlFrontend", "https://app.xpaycol.com:444")]
    public async Task ConfiguracionIncompletaNuncaCreaWalletPorElCaminoLegacy(string clave, string? valor)
    {
        var config = Configuracion();
        config[clave] = valor;
        using var db = new XpayDbContext(new DbContextOptionsBuilder<XpayDbContext>().Options);
        var servicio = Servicio(db, config);
        Assert.Null(servicio.ObtenerConfiguracion());
        // Sin proveedor SQL: cualquier acceso a datos antes del rechazo falla.
        await Assert.ThrowsAsync<RegistroPublicoNoDisponibleException>(() => servicio.RegistrarAsync(Solicitud(null), null));
        await Assert.ThrowsAsync<RegistroPublicoNoDisponibleException>(() => servicio.RegistrarAsync(Solicitud(Consentimiento), null));
        await Assert.ThrowsAsync<RegistroPublicoNoDisponibleException>(() => servicio.VerificarEmailAsync("token"));
        await Assert.ThrowsAsync<RegistroPublicoNoDisponibleException>(() => servicio.ReenviarVerificacionAsync("persona", null));
    }

    [Fact]
    public async Task ProduccionExigeLasDosAceptacionesVigentesAntesDeEscribir()
    {
        using var db = new XpayDbContext(new DbContextOptionsBuilder<XpayDbContext>().Options);
        var servicio = Servicio(db, Configuracion());
        foreach (var consentimiento in new RegistroPublicoConsentimiento?[]
        {
            null, new(false, "1.2", true, "1.2"), new(true, "1.2", false, "1.2"), new(true, "1.1", true, "1.2"),
        })
            await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.RegistrarAsync(Solicitud(consentimiento), null));
    }

    internal static readonly RegistroPublicoConsentimiento Consentimiento = new(true, "1.2", true, "1.2");
    internal static RegistroPublicoRequest Solicitud(RegistroPublicoConsentimiento? consentimiento) =>
        new("persona@example.test", "persona.registro", "Registro#2026x", "3000000000", consentimiento, " De La Hoz ");

    internal static IConfigurationRoot Configuracion()
    {
        var valores = EmailSenderAcsTests.ConfiguracionAcs();
        valores[DocumentosRegistroPublico.ClaveHabilitado] = "true";
        valores[DocumentosRegistroPublico.ClaveModalidad] = DocumentosRegistroPublico.ModalidadLimitada;
        foreach (var (prefijo, archivo, sha) in new[]
        {
            (DocumentosRegistroPublico.PrefijoTerminos, "terminos", new string('a', 64)),
            (DocumentosRegistroPublico.PrefijoPoliticaDatos, "politica", new string('b', 64)),
        })
        {
            valores[prefijo + "_VERSION"] = "1.2";
            valores[prefijo + "_FECHA"] = "2026-11-18";
            valores[prefijo + "_URL"] = $"https://app.xpaycol.com/legal/v1.2/{archivo}.pdf";
            valores[prefijo + "_SHA256"] = sha;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
    }

    private static RegistroPublicoService Servicio(XpayDbContext db, IConfiguration config)
    {
        var ambiente = AmbienteXpayTests.Ambiente("Production");
        return new(db, ambiente, new TokenUsuarioService(db, config, TimeProvider.System),
            EmailSenderAcsTests.Sender(new EmailSenderAcsTests.Transporte(System.Net.HttpStatusCode.Accepted)),
            new PlantillasEmailIdentidad(config, ambiente), TimeProvider.System,
            NullLogger<RegistroPublicoService>.Instance, config);
    }
}
