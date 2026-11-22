using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xpay.Api.Data;
using Xpay.Api.Services;
using Xpay.Api.Tests.Common;
using Xunit;

namespace Xpay.Api.Tests.Services;

public sealed class ConsentimientoBiometricoCompatibilidadTests
{
    [Theory]
    [InlineData("1.2", "1.2")]
    [InlineData("1.3", "1.2")]
    [InlineData("1.2", "1.3")]
    [InlineData("1.3", "1.3")]
    public void Bundle_activo_y_evidencia_historica_son_compatibles_sin_reescribir(string activo, string aceptado)
    {
        using var db = SinProveedor();
        var servicio = Servicio(db, Configuracion(activo));
        var documento = Assert.IsType<DocumentoRegistroPublico>(servicio.ObtenerDocumento());
        var original = JsonSerializer.Serialize(Evidencia(aceptado));
        Assert.True(servicio.Habilitado);
        Assert.Equal(activo, documento.DatosPublicos.Version);
        Assert.True(ConsentimientoBiometricoService.EvidenciaCompatible("EXITOSO", original, documento));
        Assert.Equal(activo == aceptado, ConsentimientoBiometricoService.EvidenciaVigente("EXITOSO", original, documento));
        Assert.Equal(original, JsonSerializer.Serialize(Evidencia(aceptado)));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("fechaDocumento")]
    [InlineData("sha256")]
    [InlineData("urlDocumento")]
    [InlineData("textoCasillaSha256")]
    [InlineData("autorizaBiometria")]
    [InlineData("tipoConsentimiento")]
    [InlineData("canal")]
    public void No_mezcla_metadatos_entre_bundles_ni_amplia_el_proposito(string campo)
    {
        using var db = SinProveedor();
        var documento = Servicio(db, Configuracion("1.3")).ObtenerDocumento()!;
        var datos = Evidencia("1.2");
        datos[campo] = campo switch {
            "version" => "1.3", "fechaDocumento" => PreparacionWalletConfiguracion.Fecha,
            "sha256" => PreparacionWalletConfiguracion.AutorizacionSha256,
            "urlDocumento" => "https://app.xpaycol.com/legal/v1.3/autorizaciones.pdf",
            "textoCasillaSha256" => new string('a',64), "autorizaBiometria" => false,
            "tipoConsentimiento" => "PUBLICIDAD", "canal" => "ADMIN", _ => throw new Exception() };
        Assert.False(ConsentimientoBiometricoService.EvidenciaCompatible("EXITOSO", JsonSerializer.Serialize(datos), documento));
    }

    [Theory]
    [InlineData("REVOCADO", "1.2")]
    [InlineData("REVOCADO", "1.3")]
    [InlineData("ERROR", "1.2")]
    public void Ultima_revocacion_no_se_convierte_en_una_autorizacion_por_compatibilidad(string resultado, string version)
    {
        using var db = SinProveedor();
        Assert.False(ConsentimientoBiometricoService.EvidenciaCompatible(resultado, JsonSerializer.Serialize(Evidencia(version)),
            Servicio(db, Configuracion("1.3")).ObtenerDocumento()!));
    }

    [Theory]
    [InlineData("XPAY_AUTORIZACION_KYC_FECHA", "2026-11-18")]
    [InlineData("XPAY_AUTORIZACION_KYC_SHA256", ConsentimientoBiometricoService.ShaDocumentoAprobado)]
    [InlineData("XPAY_AUTORIZACION_KYC_URL", "https://app.xpaycol.com/legal/v1.2/autorizaciones.pdf")]
    [InlineData("XPAY_AUTORIZACION_KYC_VERSION", "1.4")]
    public async Task Configuracion_nueva_parcial_o_arbitraria_cierra_C_sin_consultar_SQL(string campo, string? valor)
    {
        using var db = SinProveedor(); var config = Configuracion("1.3"); config[campo] = valor;
        var servicio = Servicio(db, config);
        Assert.False(servicio.Habilitado); Assert.Null(servicio.ObtenerDocumento());
        Assert.False(await servicio.TieneConsentimientoVigenteAsync(1));
        Assert.False(await servicio.PuedeIniciarKycAsync(1));
    }

    [Fact]
    public async Task Nueva_aceptacion_exige_version_activa_exacta_sin_abrir_SQL()
    {
        using var db = SinProveedor(); var servicio = Servicio(db, Configuracion("1.3"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.AutorizarAsync(1, new(true,"1.2"),null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.AutorizarAsync(1, new(false,"1.3"),null));
    }

    [Theory]
    [InlineData("terminos.pdf")]
    [InlineData("politica.pdf")]
    [InlineData("autorizaciones.pdf")]
    public void Los_tres_PDF_13_coinciden_con_sus_huellas_fijas_del_codigo(string archivo)
    {
        var root = new DirectoryInfo(Path.GetDirectoryName(ArchivoFuente())!);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName,"frontend/xpay-admin/public/legal"))) root = root.Parent;
        Assert.NotNull(root);
        var path = Path.Combine(root!.FullName,"frontend/xpay-admin/public/legal/v1.3",archivo);
        Assert.True(File.Exists(path),"Falta el PDF versionado de publicación: "+archivo);
        var esperado = archivo switch {
            "terminos.pdf" => PreparacionWalletConfiguracion.TerminosSha256,
            "politica.pdf" => PreparacionWalletConfiguracion.PoliticaDatosSha256,
            _ => PreparacionWalletConfiguracion.AutorizacionSha256 };
        Assert.Equal(64,esperado.Length);
        Assert.Equal(esperado,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
        Assert.Equal("1.3",PreparacionWalletConfiguracion.Version);
        Assert.Equal("2026-11-21",PreparacionWalletConfiguracion.Fecha);
    }

    internal static IConfigurationRoot Configuracion(string version)
    {
        var c = ConsentimientoBiometricoTests.Configuracion();
        if (version == "1.3") {
            c[ConsentimientoBiometricoService.PrefijoDocumento+"_VERSION"] = PreparacionWalletConfiguracion.Version;
            c[ConsentimientoBiometricoService.PrefijoDocumento+"_FECHA"] = PreparacionWalletConfiguracion.Fecha;
            c[ConsentimientoBiometricoService.PrefijoDocumento+"_SHA256"] = PreparacionWalletConfiguracion.AutorizacionSha256;
            c[ConsentimientoBiometricoService.PrefijoDocumento+"_URL"] = "https://app.xpaycol.com/legal/v1.3/autorizaciones.pdf";
        }
        return c;
    }

    internal static Dictionary<string,object> Evidencia(string version)
    {
        var nuevo = version == "1.3";
        return new() {
            ["tipoConsentimiento"] = ConsentimientoBiometricoService.TipoConsentimiento,
            ["tipoDocumento"] = ConsentimientoBiometricoService.TipoDocumento, ["autorizaBiometria"] = true, ["version"] = version,
            ["sha256"] = nuevo ? PreparacionWalletConfiguracion.AutorizacionSha256 : ConsentimientoBiometricoService.ShaDocumentoAprobado,
            ["fechaDocumento"] = nuevo ? PreparacionWalletConfiguracion.Fecha : ConsentimientoBiometricoService.FechaDocumentoAprobado,
            ["urlDocumento"] = $"https://app.xpaycol.com/legal/v{version}/autorizaciones.pdf",
            ["textoCasillaSha256"] = ConsentimientoBiometricoService.ShaTextoCasilla, ["canal"] = ConsentimientoBiometricoService.Canal };
    }
    private static XpayDbContext SinProveedor() => new(new DbContextOptionsBuilder<XpayDbContext>().Options);
    private static ConsentimientoBiometricoService Servicio(XpayDbContext db,IConfiguration c) => new(db,c,AmbienteXpayTests.Ambiente("Production"),TimeProvider.System);
    private static string ArchivoFuente([CallerFilePath] string archivo="") => archivo;
}
