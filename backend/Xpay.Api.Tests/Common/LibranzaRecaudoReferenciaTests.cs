using Xpay.Api.Common;
using Xunit;

namespace Xpay.Api.Tests.Common;

// Normalización estricta del qr_code_reference devuelto por Passport
// ("CO.COM.VISI.TRXIDP" + referencia XPAY, evidencia M4-T4). Sin DB ni red.
public class LibranzaRecaudoReferenciaTests
{
    private const string Prefijo = LibranzaRecaudoReferencia.PrefijoPassportQr;

    [Fact]
    public void ReferenciaXpayValidaDirecta_SeDevuelveTalCual()
        => Assert.Equal("N5CL361SS887F645", LibranzaRecaudoReferencia.ExtraerDePassport("N5CL361SS887F645"));

    [Fact]
    public void PrefijoExactoMasReferenciaValida_DevuelveLaReferencia()
        => Assert.Equal("VMD47K47G5M1", LibranzaRecaudoReferencia.ExtraerDePassport("CO.COM.VISI.TRXIDPVMD47K47G5M1"));

    [Theory]
    [InlineData("LLAVE")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("CO.COM.VISI.TRXIDVMD47K47G5M1")]                     // prefijo sin la P final
    [InlineData("CO.COM.VISA.TRXIDPVMD47K47G5M1")]                    // prefijo parecido
    [InlineData("XCO.COM.VISI.TRXIDPVMD47K47G5M1")]                   // prefijo desplazado
    [InlineData("co.com.visi.trxidpVMD47K47G5M1")]                    // prefijo en minúsculas
    [InlineData("CO.COM.VISI.TRXIDP")]                                // sin sufijo
    [InlineData("CO.COM.VISI.TRXIDPABC")]                             // sufijo corto
    [InlineData("CO.COM.VISI.TRXIDPABCDEFGHIJKLMNOQRS")]              // sufijo largo (18)
    [InlineData("CO.COM.VISI.TRXIDPvmd47k47g5m1")]                    // sufijo en minúsculas
    [InlineData("CO.COM.VISI.TRXIDPVMD47K47PG5M1")]                   // sufijo con P
    [InlineData("CO.COM.VISI.TRXIDPVMD47K47G5M1 ")]                   // espacio final
    [InlineData("CO.COM.VISI.TRXIDPCO.COM.VISI.TRXIDPVMD47K47G5M1")]  // prefijo duplicado
    public void ValorNoSoportado_DevuelveNull(string? valor)
        => Assert.Null(LibranzaRecaudoReferencia.ExtraerDePassport(valor));

    [Fact]
    public void ReferenciaValidaDistinta_PermaneceDistinta()
    {
        var extraida = LibranzaRecaudoReferencia.ExtraerDePassport(Prefijo + "AAAAAAAAAAAAAAAA");
        Assert.Equal("AAAAAAAAAAAAAAAA", extraida);
        Assert.NotEqual("N5CL361SS887F645", extraida);
    }
}
