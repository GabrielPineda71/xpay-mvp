using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

public sealed class UsuarioAdminIdentidadTests
{
    private static Persona Verificada() => new()
    {
        IdentidadVerificada = true, IdentidadVerificadaProveedor = "VERIFF",
        IdentidadVerificadaFecha = DateTime.UtcNow,
        NombreVerificadoCompleto = "Persona de prueba", ApellidoVerificadoCompleto = "Sintética",
        TipoDocumentoVeriffRaw = "ID_CARD", NumeroDocumentoVerificado = "9000012345"
    };

    [Fact]
    public void SinDocumentoLegal_MuestraIdentidadVeriffConFuente_SinInventarCcNiEscribirLegal()
    {
        var persona = Verificada();
        var dto = UsuarioAdminService.IdentidadParaAdministracion(persona, new Usuario { EstadoKycActual = "APROBADO" });
        Assert.Equal("Persona de prueba Sintética", dto.Nombre);
        Assert.Equal("Documento de identidad", dto.TipoDocumento);
        Assert.Equal("9000012345", dto.NumeroDocumento);
        Assert.Equal("VERIFF", dto.NombreFuente);
        Assert.Equal("VERIFF", dto.DocumentoFuente);
        Assert.True(dto.DocumentoVerificado);
        Assert.Null(persona.TipoDocumento);
        Assert.Null(persona.NumeroDocumento);
        Assert.Null(persona.PrimerNombre);
    }

    [Theory]
    [InlineData(false, "VERIFF", "APROBADO", true)]
    [InlineData(true, "OTRO", "APROBADO", true)]
    [InlineData(true, "VERIFF", "PENDIENTE", true)]
    [InlineData(true, "VERIFF", "APROBADO", false)]
    public void DatosSinEvidenciaConfiable_NoSePresentanComoIdentidad(bool verificada, string proveedor, string kyc, bool fecha)
    {
        var persona = Verificada();
        persona.IdentidadVerificada = verificada;
        persona.IdentidadVerificadaProveedor = proveedor;
        persona.IdentidadVerificadaFecha = fecha ? DateTime.UtcNow : null;
        var dto = UsuarioAdminService.IdentidadParaAdministracion(persona, new Usuario { EstadoKycActual = kyc });
        Assert.Empty(dto.Nombre);
        Assert.Empty(dto.NumeroDocumento);
        Assert.Equal("SIN_DATOS", dto.DocumentoFuente);
        Assert.False(dto.Verificada);
        Assert.False(dto.DocumentoVerificado);
    }

    [Fact]
    public void IdentidadDeclaradaDiscrepante_SeConservaSinPresentarlaComoDocumentoVerificado()
    {
        var persona = Verificada();
        persona.PrimerNombre = "Declarado";
        persona.TipoDocumento = "CC";
        persona.NumeroDocumento = "1000000000";
        var dto = UsuarioAdminService.IdentidadParaAdministracion(persona, new Usuario { EstadoKycActual = "APROBADO" });
        Assert.Equal("Declarado", dto.Nombre);
        Assert.Equal("DECLARADO", dto.NombreFuente);
        Assert.Equal("CC", dto.TipoDocumento);
        Assert.Equal("1000000000", dto.NumeroDocumento);
        Assert.Equal("DECLARADO", dto.DocumentoFuente);
        Assert.False(dto.DocumentoVerificado);
    }
}
