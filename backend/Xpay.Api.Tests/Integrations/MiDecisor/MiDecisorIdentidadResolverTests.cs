using Xpay.Api.DTOs;
using Xpay.Api.Integrations.MiDecisor;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Integrations.MiDecisor;

public sealed class MiDecisorIdentidadResolverTests
{
    private static readonly DateTime Fecha=new(2026,11,1,1,2,3,DateTimeKind.Utc);
    private static Persona Persona() => new()
    {
        IdPersona=3, Estado="ACTIVA", IdentidadVerificada=true, IdentidadVerificadaProveedor="VERIFF",
        NombreVerificadoCompleto="SINTETICO",ApellidoVerificadoCompleto="DE LA PRUEBA OTRO",
        TipoDocumentoVeriffRaw="ID_CARD",NumeroDocumentoVerificado="1.234.567",
        PrimerApellido="DE LA PRUEBA",SegundoApellido="OTRO",
    };
    private static KycVerificacion Sesion() => new()
    {
        IdKycVerificacion=4,IdUsuario=2,IdPersona=3,EsActual=true,Proveedor="VERIFF",EstadoKyc="APROBADO",Decision="approved",
        SessionId="synthetic-session",AttemptIdVeriff="synthetic-attempt",DecisionTimeVeriff=Fecha,VendorData="synthetic-vendor",
    };
    private static VeriffDecisionParsed Decision() => new("synthetic-session","synthetic-attempt","approved",null,Fecha,
        "synthetic-vendor","SINTETICO","DE LA PRUEBA OTRO","ID_CARD","1.234.567",null,"CO");

    [Fact]
    public void LegacyLegalFieldsMayBeAbsentButVerifiedDecisionAndExplicitSurnameRemainRequired()
    {
        var p=Persona(); var identity=MiDecisorIdentidadResolver.CrearDesdeDecision(2,p,Sesion(),Decision());
        Assert.NotNull(identity);
        Assert.Equal("CC",identity!.TipoDocumento); Assert.Equal("1234567",identity.NumeroDocumento);
        Assert.Equal("DE LA PRUEBA",identity.PrimerApellido);
        Assert.Null(p.NumeroDocumento); Assert.Null(p.TipoDocumento);
    }

    [Theory]
    [InlineData(null)] [InlineData("US")] [InlineData("XX")]
    public void ResidenceNeverSuppliesDocumentCountry(string? documentCountry)
    {
        var p=Persona(); p.Pais="CO";
        Assert.Null(MiDecisorIdentidadResolver.CrearDesdeDecision(2,p,Sesion(),Decision() with { DocumentCountry=documentCountry }));
    }

    [Fact]
    public void MissingOrConflictingExplicitSurnameIsNotInferredBySplittingVerifiedName()
    {
        var p=Persona(); p.PrimerApellido=null;
        Assert.Null(MiDecisorIdentidadResolver.CrearDesdeDecision(2,p,Sesion(),Decision()));
        p.PrimerApellido="OTRO";
        Assert.Null(MiDecisorIdentidadResolver.CrearDesdeDecision(2,p,Sesion(),Decision()));
    }

    [Fact]
    public void ConflictingLegalIdentityOrChangedCurrentDecisionFailsClosed()
    {
        var p=Persona(); p.TipoDocumento="CC";p.NumeroDocumento="7654321";
        Assert.Null(MiDecisorIdentidadResolver.CrearDesdeDecision(2,p,Sesion(),Decision()));
        p.NumeroDocumento="1234567";
        Assert.Null(MiDecisorIdentidadResolver.CrearDesdeDecision(2,p,Sesion(),Decision() with { AttemptId="other" }));
        Assert.Null(MiDecisorIdentidadResolver.CrearDesdeDecision(2,p,Sesion(),Decision() with { Decision="declined" }));
        Assert.Null(MiDecisorIdentidadResolver.CrearDesdeDecision(9,p,Sesion(),Decision()));
    }

    [Fact]
    public void TransversalHistoryMatchesLegalOrVerifiedCanonicalDocumentButRejectsMalformedGrouping()
    {
        var identity=MiDecisorIdentidadResolver.CrearDesdeDecision(2,Persona(),Sesion(),Decision())!;
        Assert.True(RiesgoProduccionStore.DocumentoCoincide(identity,"CC","1.234.567",null,null));
        Assert.True(RiesgoProduccionStore.DocumentoCoincide(identity,"CC","\t1.234.567\u00a0",null,null));
        Assert.True(RiesgoProduccionStore.DocumentoCoincide(identity,null,null,"ID_CARD","1234567"));
        Assert.False(RiesgoProduccionStore.DocumentoCoincide(identity,"CE","1234567",null,null));
        Assert.False(RiesgoProduccionStore.DocumentoCoincide(identity,"CC","12.34.567",null,null));
    }
}
