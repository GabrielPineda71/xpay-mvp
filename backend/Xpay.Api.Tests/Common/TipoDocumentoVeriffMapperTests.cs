using System.Text.Json;
using Xpay.Api.Common;
using Xpay.Api.Services;
using Xunit;
using R = Xpay.Api.Common.TipoDocumentoVeriffMapper.Resultado;

namespace Xpay.Api.Tests.Common;

// Diseño A — mapeo cerrado Veriff → TipoDocumento XPAY y lectura de
// document.country en el parser. Puro, sin BD.
public sealed class TipoDocumentoVeriffMapperTests
{
    [Theory]
    [InlineData("ID_CARD",  "CO", "1234567890", "CC",        "1234567890")]
    [InlineData("id_card",  "co", " 1234567 ",  "CC",        "1234567")]
    [InlineData("PASSPORT", "CO", "ab123456",   "PASAPORTE", "AB123456")]
    [InlineData("PASSPORT", "US", "X1234567",   "PASAPORTE", "X1234567")]
    public void Combinaciones_cerradas_se_mapean(string tipo, string pais, string numero, string tipoEsperado, string numeroEsperado)
    {
        var r = TipoDocumentoVeriffMapper.Mapear(tipo, pais, numero);
        Assert.Equal(R.Mapeado, r.Resultado);
        Assert.Equal(tipoEsperado, r.TipoDocumento);
        Assert.Equal(numeroEsperado, r.NumeroDocumento);
    }

    [Theory]
    [InlineData("ID_CARD",          "VE")]
    [InlineData("ID_CARD",          null)]
    [InlineData("RESIDENCE_PERMIT", "CO")]   // CE pendiente de confirmar con Veriff
    [InlineData("DRIVERS_LICENSE",  "CO")]
    [InlineData("VISA",             "CO")]
    [InlineData("OTHER",            "CO")]
    [InlineData("PASSPORT",         null)]
    [InlineData("PASSPORT",         "COL")]
    [InlineData(null,               "CO")]
    public void Combinaciones_fuera_del_mapeo_no_se_mapean(string? tipo, string? pais)
    {
        var r = TipoDocumentoVeriffMapper.Mapear(tipo, pais, "1234567");
        Assert.Equal(R.TipoNoMapeable, r.Resultado);
        Assert.Null(r.TipoDocumento);
        Assert.Null(r.NumeroDocumento);
    }

    [Theory]
    [InlineData("ID_CARD",  "CO", "")]
    [InlineData("ID_CARD",  "CO", null)]
    [InlineData("ID_CARD",  "CO", "12345A")]       // CC solo dígitos
    [InlineData("ID_CARD",  "CO", "1.234.567")]
    [InlineData("PASSPORT", "CO", "AB-123")]
    [InlineData("PASSPORT", "CO", "A123456789012345678901234567890")] // 31: no se trunca
    public void Numero_invalido_o_largo_se_rechaza_sin_truncar(string tipo, string pais, string? numero)
    {
        var r = TipoDocumentoVeriffMapper.Mapear(tipo, pais, numero);
        Assert.Equal(R.NumeroInvalido, r.Resultado);
        Assert.Null(r.NumeroDocumento);
    }

    [Fact]
    public void Numero_de_30_caracteres_es_el_maximo_aceptado()
    {
        var numero = new string('7', TipoDocumentoVeriffMapper.LongitudMaximaNumero);
        var r = TipoDocumentoVeriffMapper.Mapear("ID_CARD", "CO", numero);
        Assert.Equal(R.Mapeado, r.Resultado);
        Assert.Equal(numero, r.NumeroDocumento);
    }

    [Fact]
    public void Parser_lee_document_country_y_conserva_lastName_completo()
    {
        using var json = JsonDocument.Parse("""
            {"id":"s1","status":"approved",
             "person":{"firstName":"ANA MARIA","lastName":"PRUEBA SINTETICA"},
             "document":{"type":"ID_CARD","number":"1234567","country":"CO"}}
            """);

        var parsed = KycService.ParseVeriffDecision(json.RootElement)!;

        Assert.Equal("CO", parsed.DocumentCountry);
        // lastName llega como un único campo: el parser no lo separa.
        Assert.Equal("PRUEBA SINTETICA", parsed.LastName);
    }

    [Fact]
    public void Parser_sin_country_deja_DocumentCountry_null()
    {
        using var json = JsonDocument.Parse("""{"id":"s1","document":{"type":"ID_CARD","number":"1"}}""");
        Assert.Null(KycService.ParseVeriffDecision(json.RootElement)!.DocumentCountry);
    }
}
