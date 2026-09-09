using Xpay.Api.Common;
using Xpay.Api.Integrations.MiDecisor;
using Xunit;

namespace Xpay.Api.Tests.Services;

// Consentimiento durable V1 — pruebas puras del recurso versionado del texto
// (sin SQL, sin red). ACTA 001 §3.1 · XPAY-197.
public sealed class CarteraAutorizacionConsultaRiesgoTextoTests
{
    // TEST 1 — reproducibilidad del hash del texto aprobado.
    [Fact]
    public void V1_Hash_coincide_con_SHA256_del_texto_exacto()
    {
        var recomputado = CarteraAutorizacionConsultaRiesgoTextos.HashSha256Hex(
            CarteraAutorizacionConsultaRiesgoTextos.V1_Texto);

        Assert.Equal(CarteraAutorizacionConsultaRiesgoTextos.V1_HashSha256, recomputado);
        Assert.Equal(64, CarteraAutorizacionConsultaRiesgoTextos.V1_HashSha256.Length);
        Assert.Matches("^[0-9a-f]{64}$", CarteraAutorizacionConsultaRiesgoTextos.V1_HashSha256);
    }

    [Fact]
    public void V1_Version_es_el_identificador_estable()
        => Assert.Equal("AUTZ_CONSULTA_RIESGO_2026_V1", CarteraAutorizacionConsultaRiesgoTextos.V1_Version);

    [Fact]
    public void V1_Texto_contiene_los_elementos_aprobados_de_ACTA_001_3_1()
    {
        var t = CarteraAutorizacionConsultaRiesgoTextos.V1_Texto;
        Assert.Contains("Ley 1266 de 2008", t);
        Assert.Contains("Habeas Data financiero", t);
        Assert.Contains("centrales de riesgo (DataCrédito, TransUnion, entre otras)", t);
        Assert.Contains("AUTORIZO", t);
        Assert.Contains("confidencialidad", t);
        // No se añadió Ley 1581.
        Assert.DoesNotContain("1581", t);
    }

    [Fact]
    public void EsVersionVigente_solo_para_V1()
    {
        Assert.True(CarteraAutorizacionConsultaRiesgoTextos.EsVersionVigente("AUTZ_CONSULTA_RIESGO_2026_V1"));
        Assert.False(CarteraAutorizacionConsultaRiesgoTextos.EsVersionVigente("AUTZ_CONSULTA_RIESGO_2026_V2"));
        Assert.False(CarteraAutorizacionConsultaRiesgoTextos.EsVersionVigente(""));
        Assert.False(CarteraAutorizacionConsultaRiesgoTextos.EsVersionVigente(null));
    }

    [Fact]
    public void TryObtenerVersion_devuelve_texto_y_hash_solo_de_V1()
    {
        Assert.True(CarteraAutorizacionConsultaRiesgoTextos.TryObtenerVersion(
            "AUTZ_CONSULTA_RIESGO_2026_V1", out var texto, out var hash));
        Assert.Equal(CarteraAutorizacionConsultaRiesgoTextos.V1_Texto, texto);
        Assert.Equal(CarteraAutorizacionConsultaRiesgoTextos.V1_HashSha256, hash);

        Assert.False(CarteraAutorizacionConsultaRiesgoTextos.TryObtenerVersion("otra", out _, out _));
    }

    // REGRESIÓN — el stub fail-closed sigue intacto (Program.cs no cambió).
    [Fact]
    public async Task Stub_AutorizacionConsultaRiesgoNoDisponible_sigue_devolviendo_false()
    {
        var stub = new AutorizacionConsultaRiesgoNoDisponible();
        Assert.False(await stub.TieneAutorizacionVigenteAsync(1, 1));
        Assert.False(await stub.TieneAutorizacionVigenteAsync(999_999, 424_242));
    }
}
