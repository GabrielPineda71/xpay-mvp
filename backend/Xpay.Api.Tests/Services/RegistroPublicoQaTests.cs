using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Controllers;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Services;
using Xpay.Api.Tests.Common;
using Xunit;

namespace Xpay.Api.Tests.Services;

public sealed class RegistroPublicoQaTests
{
    private const string CorreoPrueba = "registro@example.test";
    internal const string ShaTerminos = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    internal const string ShaPolitica = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    internal static readonly RegistroPublicoConsentimiento ConsentimientoVigente =
        new(true, "1.1", true, "1.1");

    [Fact]
    public async Task Formulario_ExponeSoloMetadatosPublicos()
    {
        var configuracion = ConfiguracionQa(CorreoPrueba);
        await using var contexto = NuevoContexto();
        var controller = Controlador(contexto, configuracion);

        var resultado = Assert.IsType<OkObjectResult>(controller.ObtenerConfiguracion());
        var json = JsonSerializer.SerializeToElement(resultado.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.True(json.GetProperty("success").GetBoolean());
        var datos = json.GetProperty("data");
        Assert.Equal("1.1", datos.GetProperty("terminos").GetProperty("version").GetString());
        Assert.Equal("2026-11-13", datos.GetProperty("terminos").GetProperty("fecha").GetString());
        Assert.Equal("https://qa.example.test/legal/terminos.pdf", datos.GetProperty("terminos").GetProperty("url").GetString());
        Assert.Equal("https://qa.example.test/legal/politica.pdf", datos.GetProperty("politicaDatos").GetProperty("url").GetString());
        Assert.DoesNotContain("sha256", json.GetRawText());
    }

    [Theory]
    [InlineData("Production", "true", "https://qa.example.test/legal/terminos.pdf", true)]
    [InlineData("QA", null, "https://qa.example.test/legal/terminos.pdf", true)]
    [InlineData("QA", "false", "https://qa.example.test/legal/terminos.pdf", true)]
    [InlineData("QA", "true", "http://qa.example.test/legal/terminos.pdf", true)]
    [InlineData("QA", "true", "https://qa.example.test/legal/terminos.pdf", false)]
    public async Task FormularioNoDisponible_NoPermiteCrearCuenta(string ambiente, string? habilitado, string url, bool conSmtp)
    {
        var configuracion = ConfiguracionQa(CorreoPrueba);
        configuracion[DocumentosRegistroPublico.ClaveHabilitado] = habilitado;
        configuracion[DocumentosRegistroPublico.PrefijoTerminos + "_URL"] = url;
        await using var contexto = NuevoContexto();
        var controller = Controlador(contexto, configuracion, ambiente, conSmtp);

        Assert.IsType<NotFoundObjectResult>(controller.ObtenerConfiguracion());

        var respuesta = await controller.Registrar(Solicitud(CorreoPrueba, ConsentimientoVigente), default);
        Assert.IsType<NotFoundObjectResult>(respuesta);

        if (habilitado == "true")
        {
            var solicitudPrevia = await controller.Registrar(Solicitud(CorreoPrueba, null), default);
            Assert.IsType<NotFoundObjectResult>(solicitudPrevia);
        }
    }

    [Theory]
    [InlineData(null, true, true, CorreoPrueba)]
    [InlineData("1.0", true, true, CorreoPrueba)]
    [InlineData("1.1", false, true, CorreoPrueba)]
    [InlineData("1.1", true, false, CorreoPrueba)]
    [InlineData("1.1", true, true, "otracorreo@example.test")]
    public async Task Formulario_ValidaVersionesCasillasYDestinatarioAntesDeEscribir(
        string? version, bool aceptaTerminos, bool autorizaDatos, string correo)
    {
        var configuracion = ConfiguracionQa(CorreoPrueba);
        await using var contexto = NuevoContexto();
        var controller = Controlador(contexto, configuracion);
        var consentimiento = version is null ? null : new RegistroPublicoConsentimiento(aceptaTerminos, version, autorizaDatos, "1.1");

        var respuesta = await controller.Registrar(Solicitud(correo, consentimiento), default);

        Assert.IsType<BadRequestObjectResult>(respuesta);
    }

    private static RegistroPublicoRequest Solicitud(string correo, RegistroPublicoConsentimiento? consentimiento) =>
        new(correo, "registro.qa", "Registro#2026x", "3000000000", consentimiento);

    private static XpayDbContext NuevoContexto() =>
        new(new DbContextOptionsBuilder<XpayDbContext>().Options);

    private static RegistroPublicoController Controlador(XpayDbContext contexto, IConfiguration configuracion,
        string nombreAmbiente = "QA", bool conSmtp = true)
    {
        var ambiente = AmbienteXpayTests.Ambiente(nombreAmbiente);
        var transporte = new TransporteSmtpEspia();
        IEmailSender sender = conSmtp
            ? new EmailSenderQaSmtp(OpcionesEmailQaSmtp.Desde(configuracion), transporte, NullLogger<EmailSenderQaSmtp>.Instance)
            : new EmailSenderLocalSinEnvio(NullLogger<EmailSenderLocalSinEnvio>.Instance);
        var servicio = new RegistroPublicoService(contexto, ambiente,
            new TokenUsuarioService(contexto, configuracion, TimeProvider.System), sender,
            new PlantillasEmailIdentidad(configuracion, ambiente), TimeProvider.System,
            NullLogger<RegistroPublicoService>.Instance, configuracion);
        return new(servicio) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
    }

    internal static IConfiguration ConfiguracionQa(string correo) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [DocumentosRegistroPublico.ClaveHabilitado] = "true",
            [DocumentosRegistroPublico.PrefijoTerminos + "_VERSION"] = "1.1",
            [DocumentosRegistroPublico.PrefijoTerminos + "_FECHA"] = "2026-11-13",
            [DocumentosRegistroPublico.PrefijoTerminos + "_URL"] = "https://qa.example.test/legal/terminos.pdf",
            [DocumentosRegistroPublico.PrefijoTerminos + "_SHA256"] = ShaTerminos,
            [DocumentosRegistroPublico.PrefijoPoliticaDatos + "_VERSION"] = "1.1",
            [DocumentosRegistroPublico.PrefijoPoliticaDatos + "_FECHA"] = "2026-11-13",
            [DocumentosRegistroPublico.PrefijoPoliticaDatos + "_URL"] = "https://qa.example.test/legal/politica.pdf",
            [DocumentosRegistroPublico.PrefijoPoliticaDatos + "_SHA256"] = ShaPolitica,
            [PlantillasEmailIdentidad.ClaveUrlFrontend] = "https://qa.example.test",
            [PlantillasEmailIdentidad.ClaveRutaVerificacion] = "/verificar-email",
            [OpcionesEmailQaSmtp.ClaveHost] = "smtp.example.test",
            [OpcionesEmailQaSmtp.ClavePuerto] = "587",
            [OpcionesEmailQaSmtp.ClaveUsuario] = "qa-test",
            [OpcionesEmailQaSmtp.ClaveClave] = "clave-ficticia",
            [OpcionesEmailQaSmtp.ClaveRemitente] = "qa@example.test",
            [OpcionesEmailQaSmtp.ClaveDestinatariosPermitidos] = correo,
            [OpcionesEmailQaSmtp.ClaveTimeoutSegundos] = "5",
        }).Build();

    internal sealed class TransporteSmtpEspia : ITransporteSmtpQa
    {
        public List<EnvioSmtpQa> Enviados { get; } = [];

        public Task EnviarAsync(EnvioSmtpQa envio, CancellationToken ct)
        {
            Enviados.Add(envio);
            return Task.CompletedTask;
        }
    }
}
