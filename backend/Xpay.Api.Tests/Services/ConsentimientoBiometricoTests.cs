using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xpay.Api.Tests.Common;
using Xunit;

namespace Xpay.Api.Tests.Services;

public sealed class ConsentimientoBiometricoTests
{
    [Fact]
    public void DocumentoEsElPdfAprobadoYVersionadoIncluidoEnLaWeb()
    {
        using var db = SinProveedor();
        var service = Servicio(db, Configuracion());
        var documento = Assert.IsType<DocumentoRegistroPublico>(service.ObtenerDocumento());
        Assert.True(service.Habilitado);
        Assert.Equal("1.2", documento.DatosPublicos.Version);
        Assert.Equal(new DateOnly(2026, 11, 18), documento.DatosPublicos.Fecha);
        Assert.Equal("https://app.xpaycol.com/legal/v1.2/autorizaciones.pdf", documento.DatosPublicos.Url);

        var directorio = new DirectoryInfo(Path.GetDirectoryName(ArchivoFuente())!);
        while (directorio is not null && !File.Exists(Path.Combine(directorio.FullName,
            "frontend/xpay-admin/public/legal/v1.2/autorizaciones.pdf"))) directorio = directorio.Parent;
        Assert.NotNull(directorio);
        var pdf = File.ReadAllBytes(Path.Combine(directorio!.FullName,
            "frontend/xpay-admin/public/legal/v1.2/autorizaciones.pdf"));
        Assert.Equal(documento.Sha256, Convert.ToHexString(SHA256.HashData(pdf)).ToLowerInvariant());
    }

    [Theory]
    [InlineData("XPAY_ALTA_PUBLICA_NOMINA_HABILITADA", null)]
    [InlineData("XPAY_ALTA_PUBLICA_NOMINA_HABILITADA", "false")]
    [InlineData("XPAY_ALTA_PUBLICA_NOMINA_HABILITADA", "TRUE")]
    [InlineData("XPAY_AUTORIZACION_KYC_VERSION", null)]
    [InlineData("XPAY_AUTORIZACION_KYC_VERSION", "1.3")]
    [InlineData("XPAY_AUTORIZACION_KYC_FECHA", "2026-11-19")]
    [InlineData("XPAY_AUTORIZACION_KYC_SHA256", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("XPAY_AUTORIZACION_KYC_URL", "https://app-qa.xpaycol.com/legal/v1.2/autorizaciones.pdf")]
    [InlineData("XPAY_AUTORIZACION_KYC_URL", "http://app.xpaycol.com/legal/v1.2/autorizaciones.pdf")]
    [InlineData("XPAY_AUTORIZACION_KYC_URL", "https://app.xpaycol.com:444/legal/v1.2/autorizaciones.pdf")]
    [InlineData("XPAY_AUTORIZACION_KYC_URL", "https://user@app.xpaycol.com/legal/v1.2/autorizaciones.pdf")]
    [InlineData("XPAY_AUTORIZACION_KYC_URL", "https://app.xpaycol.com/legal/v1.2/autorizaciones.pdf?version=1.2")]
    [InlineData("XPAY_AUTORIZACION_KYC_URL", "https://app.xpaycol.com/legal/v1.2/terminos.pdf")]
    [InlineData("Identidad:UrlFrontend", "https://app-qa.xpaycol.com/")]
    [InlineData("Identidad:UrlFrontend", "https://app.xpaycol.com:444/")]
    [InlineData("Identidad:UrlFrontend", "https://app.xpaycol.com/mi-wallet")]
    [InlineData("Identidad:UrlFrontend", "https://app.xpaycol.com/#otro")]
    public async Task ConfiguracionInvalidaNuncaConsultaDatosNiAceptaNiIniciaKyc(string clave, string? valor)
    {
        using var db = SinProveedor();
        var config = Configuracion(); config[clave] = valor;
        var service = Servicio(db, config);
        Assert.False(service.Habilitado);
        Assert.False(await service.TieneConsentimientoVigenteAsync(123));
        Assert.False(await service.PuedeIniciarKycAsync(123));
        var respuesta = await service.ObtenerEstadoAsync(123);
        Assert.False(respuesta.Habilitado);
        Assert.False(respuesta.Aceptado);
        await Assert.ThrowsAsync<ConsentimientoBiometricoNoDisponibleException>(() =>
            service.AutorizarAsync(123, new(true, "1.2"), null));
    }

    [Theory]
    [InlineData(false, "1.2")]
    [InlineData(true, null)]
    [InlineData(true, "")]
    [InlineData(true, "1.1")]
    [InlineData(true, "1.2 ")]
    public async Task SinActoExpresoYVersionExactaNoSeEscribeNiSeAbreTransaccion(bool autoriza, string? version)
    {
        using var db = SinProveedor();
        var service = Servicio(db, Configuracion());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AutorizarAsync(123, new(autoriza, version), "203.0.113.8"));
    }

    [Fact]
    public void EvidenciaExigeCasillaCConDocumentoTextoCanalYResultadoVigentes()
    {
        using var db = SinProveedor();
        var documento = Servicio(db, Configuracion()).ObtenerDocumento()!;
        var evidencia = Evidencia();
        Assert.True(ConsentimientoBiometricoService.EvidenciaVigente("EXITOSO", JsonSerializer.Serialize(evidencia), documento));
        Assert.False(ConsentimientoBiometricoService.EvidenciaVigente("REVOCADO", JsonSerializer.Serialize(evidencia), documento));
        foreach (var propiedad in evidencia.Keys.ToArray())
        {
            var faltante = Evidencia(); faltante.Remove(propiedad);
            Assert.False(ConsentimientoBiometricoService.EvidenciaVigente("EXITOSO", JsonSerializer.Serialize(faltante), documento));
        }
        foreach (var (propiedad, invalido) in new (string, object)[]
        {
            ("tipoConsentimiento", "ACEPTAR_TERMINOS_REGISTRO"), ("version", "1.1"),
            ("sha256", new string('a',64)), ("canal", "ADMIN"), ("autorizaBiometria", false),
            ("autorizaBiometria", "true"), ("urlDocumento", "https://otra.example.test/autorizaciones.pdf"),
            ("textoCasillaSha256", new string('b',64)),
        })
        {
            var modificada = Evidencia(); modificada[propiedad] = invalido;
            Assert.False(ConsentimientoBiometricoService.EvidenciaVigente("EXITOSO", JsonSerializer.Serialize(modificada), documento));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("true")]
    public void EvidenciaAusenteOMalformadaNoEquivaleAAutorizacion(string? valor)
    {
        using var db = SinProveedor();
        Assert.False(ConsentimientoBiometricoService.EvidenciaVigente("EXITOSO", valor,
            Servicio(db, Configuracion()).ObtenerDocumento()!));
    }

    [Fact]
    public void RetomarConservaUnaSesionVeriffPendienteDelMismoTitular()
    {
        var sesion = new KycVerificacion
        {
            IdPersona = 8, IdUsuario = 9, Proveedor = "VERIFF", EsActual = true,
            EstadoKyc = "PENDIENTE", SessionId = "sesion-sintetica", SessionUrl = "https://saas.veriff.com/v/solo-prueba",
        };
        Assert.Same(sesion, KycService.SesionPublicaPendiente([sesion], 8));
        Assert.Null(KycService.SesionPublicaPendiente([], 8));
        Assert.Null(KycService.SesionPublicaPendiente([sesion,sesion], 8));
        Assert.Null(KycService.SesionPublicaPendiente([sesion], 7));
        sesion.EstadoKyc = "RECHAZADO";
        Assert.Null(KycService.SesionPublicaPendiente([sesion], 8));
        sesion.EstadoKyc = "PENDIENTE"; sesion.Proveedor = "SIMULACION_QA";
        Assert.Null(KycService.SesionPublicaPendiente([sesion], 8));
        sesion.Proveedor = "VERIFF"; sesion.EsActual = false;
        Assert.Null(KycService.SesionPublicaPendiente([sesion], 8));
        sesion.EsActual = true; sesion.SessionUrl = null;
        Assert.Null(KycService.SesionPublicaPendiente([sesion], 8));
    }

    internal static IConfigurationRoot Configuracion()
    {
        return new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {
            [HabilitacionCuentaPublicaService.ClaveHabilitada] = "true",
            [PlantillasEmailIdentidad.ClaveUrlFrontend] = "https://app.xpaycol.com/",
            [ConsentimientoBiometricoService.PrefijoDocumento+"_VERSION"] = "1.2",
            [ConsentimientoBiometricoService.PrefijoDocumento+"_FECHA"] = "2026-11-18",
            [ConsentimientoBiometricoService.PrefijoDocumento+"_URL"] = "https://app.xpaycol.com/legal/v1.2/autorizaciones.pdf",
            [ConsentimientoBiometricoService.PrefijoDocumento+"_SHA256"] = ConsentimientoBiometricoService.ShaDocumentoAprobado,
        }).Build();
    }

    private static Dictionary<string,object> Evidencia() => new()
    {
        ["tipoConsentimiento"] = ConsentimientoBiometricoService.TipoConsentimiento,
        ["tipoDocumento"] = ConsentimientoBiometricoService.TipoDocumento,
        ["autorizaBiometria"] = true, ["version"] = "1.2",
        ["sha256"] = ConsentimientoBiometricoService.ShaDocumentoAprobado,
        ["fechaDocumento"] = "2026-11-18", ["urlDocumento"] = "https://app.xpaycol.com/legal/v1.2/autorizaciones.pdf",
        ["textoCasillaSha256"] = ConsentimientoBiometricoService.ShaTextoCasilla,
        ["canal"] = ConsentimientoBiometricoService.Canal,
    };

    private static XpayDbContext SinProveedor() => new(new DbContextOptionsBuilder<XpayDbContext>().Options);
    private static string ArchivoFuente([CallerFilePath] string archivo = "") => archivo;
    private static ConsentimientoBiometricoService Servicio(XpayDbContext db, IConfiguration config) =>
        new(db, config, AmbienteXpayTests.Ambiente("Production"), TimeProvider.System);
}
