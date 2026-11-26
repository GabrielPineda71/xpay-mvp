using Xpay.Api.Integrations.MiDecisor;
using Xunit;

namespace Xpay.Api.Tests.Integrations.MiDecisor;

public sealed class MiDecisorExternalHistoryTests
{
    [Fact]
    public void SubjectFingerprintUsesStrictSharedCanonicalDocument()
    {
        Assert.Equal(MiDecisorSujeto.Huella("CC","1234567"),MiDecisorSujeto.Huella(" cc ","1.234.567"));
        Assert.Equal(MiDecisorSujeto.Huella("PAS","1234567"),MiDecisorSujeto.Huella("PASAPORTE","1234567"));
        Assert.NotEqual(MiDecisorSujeto.Huella("CC","1234567"),MiDecisorSujeto.Huella("CE","1234567"));
        Assert.Throws<ArgumentException>(() => MiDecisorSujeto.Huella("CC","12.34.567"));
        Assert.Throws<ArgumentException>(() => MiDecisorSujeto.Huella("CC","１２３４５６７"));
    }

    [Fact]
    public void ExternalReferenceStartsItsOwnSixCalendarMonthWindowWithoutDecisionData()
    {
        var date = new DateTime(2026,8,31,3,41,5,DateTimeKind.Utc);
        var snapshot=new RiesgoProduccionSnapshot(null,false) { UltimaConsultaExternaUtc=date };
        var boundary=date.AddMonths(6);
        Assert.False(ReglaReconsultaMiDecisor.Permite(snapshot,boundary.AddTicks(-1)));
        Assert.True(ReglaReconsultaMiDecisor.Permite(snapshot,boundary));
        Assert.Null(snapshot.UltimaRespuestaUtc);
        Assert.DoesNotContain(typeof(MiDecisorConsultaExternaRegistro).GetProperties(), p=>p.Name.Contains("Score")||p.Name.Contains("Monto")||p.Name.Contains("Solicitud")||p.Name.Contains("Consent"));
    }

    [Fact]
    public void PendingOrUncertainQueryBlocksIndependentlyOfSixMonthDate()
        => Assert.False(ReglaReconsultaMiDecisor.Permite(new(null,false) { TieneConsultaPendiente=true },DateTime.UtcNow));

    [Fact]
    public void ManualEvidenceCannotPretendExactVendorTimeAndCannotBeFuture()
    {
        var now=new DateTime(2026,11,25,4,0,0,DateTimeKind.Utc);
        MiDecisorConsultaExternaRegistro Record(DateTime date,bool exact) => new()
        {
            IdPersonaOrigen=1, RegistradoPorUsuario=2, TipoDocumento="CC",NumeroDocumento="1234567",
            FechaReferenciaUtc=date, FechaExacta=exact, Origen=MiDecisorConsultaExternaStore.ConfirmacionManual,
            EvidenciaSha256=new string('a',64),
        };
        MiDecisorConsultaExternaStore.Validar(Record(now,false),now);
        Assert.Throws<ArgumentException>(()=>MiDecisorConsultaExternaStore.Validar(Record(now,true),now));
        Assert.Throws<ArgumentException>(()=>MiDecisorConsultaExternaStore.Validar(Record(now.AddTicks(1),false),now));
        Assert.Throws<ArgumentException>(()=>MiDecisorConsultaExternaStore.Validar(Record(DateTime.SpecifyKind(now,DateTimeKind.Unspecified),false),now));
        var record=Record(now,false);
        Assert.True(MiDecisorConsultaExternaStore.VinculoDocumental(record,null,null,"ID_CARD","1.234.567"));
        Assert.True(MiDecisorConsultaExternaStore.VinculoDocumental(record,"CC","1234567",null,null));
        Assert.False(MiDecisorConsultaExternaStore.VinculoDocumental(record,"CC","7654321","ID_CARD","7654321"));
        Assert.False(MiDecisorConsultaExternaStore.VinculoDocumental(record,"CE","1234567",null,null));
    }
}
