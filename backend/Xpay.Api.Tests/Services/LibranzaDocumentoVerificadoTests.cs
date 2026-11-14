using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// Vinculación empleado ↔ usuario: si la persona no tiene documento legal, se
// admite el documento verificado por VERIFF (alta del piloto).
public sealed class LibranzaDocumentoVerificadoTests
{
    private static LibranzaEmpleado Empleado(string tipo = "CC", string numero = "1088123456") =>
        new() { TipoDocumento = tipo, NumeroDocumento = numero };

    private static Persona PersonaVerificada(string? raw = "ID_CARD", string? numero = "1088123456") => new()
    {
        IdentidadVerificada = true,
        IdentidadVerificadaProveedor = "VERIFF",
        TipoDocumentoVeriffRaw = raw,
        NumeroDocumentoVerificado = numero,
    };

    [Fact]
    public void SinDocumentoLegal_CedulaVerificadaIgual_Coincide()
    {
        Assert.True(LibranzaAnticipoService.DocumentoVerificadoCoincide(Empleado(), PersonaVerificada()));
    }

    [Fact]
    public void SinDocumentoLegal_PasaporteVerificado_Coincide()
    {
        Assert.True(LibranzaAnticipoService.DocumentoVerificadoCoincide(
            Empleado("PASAPORTE", "AB123456"), PersonaVerificada("PASSPORT", "AB123456")));
    }

    [Fact]
    public void NumeroDistinto_NoCoincide()
    {
        Assert.False(LibranzaAnticipoService.DocumentoVerificadoCoincide(Empleado(), PersonaVerificada(numero: "1088123457")));
    }

    [Fact]
    public void TipoDistinto_NoCoincide()
    {
        Assert.False(LibranzaAnticipoService.DocumentoVerificadoCoincide(Empleado("CE"), PersonaVerificada()));
    }

    [Fact]
    public void TipoVeriffSinCorrespondencia_NoCoincide()
    {
        Assert.False(LibranzaAnticipoService.DocumentoVerificadoCoincide(Empleado(), PersonaVerificada("DRIVERS_LICENSE")));
    }

    [Fact]
    public void IdentidadNoVerificada_NoCoincide()
    {
        var persona = PersonaVerificada();
        persona.IdentidadVerificada = false;
        Assert.False(LibranzaAnticipoService.DocumentoVerificadoCoincide(Empleado(), persona));
    }

    [Fact]
    public void ProveedorDistintoDeVeriff_NoCoincide()
    {
        var persona = PersonaVerificada();
        persona.IdentidadVerificadaProveedor = "SIMULACION_QA";
        Assert.False(LibranzaAnticipoService.DocumentoVerificadoCoincide(Empleado(), persona));
    }

    [Fact]
    public void ConDocumentoLegal_NoUsaElVerificado()
    {
        var persona = PersonaVerificada();
        persona.TipoDocumento = "CC";
        persona.NumeroDocumento = "999";
        Assert.False(LibranzaAnticipoService.DocumentoVerificadoCoincide(Empleado(), persona));
    }
}
