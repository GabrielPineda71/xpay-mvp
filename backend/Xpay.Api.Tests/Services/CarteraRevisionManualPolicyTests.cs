using System.Text.Json;
using Xpay.Api.Common;
using Xunit;

namespace Xpay.Api.Tests.Services;

// XPAY-702 - politica de revision manual (O1 + O4). Solo el caso A
// (ventana incompleta sin mora, demas condiciones reales OK) permite aprobar.
public sealed class CarteraRevisionManualPolicyTests
{
    private static readonly CarteraPolicyParameters P = CarteraPolicyParameters.Vigente();
    private static readonly CarteraEvaluationContext Ctx = new(2026, 9);

    private static string Vec(params (string anioMes, string? comp)[] e)
        => JsonSerializer.Serialize(
            e.Select(x => new CarteraComportamientoVectorItemRaw(x.anioMes, x.comp)).ToList());

    // Base: todo OK con datos reales; ventana incompleta (5 meses N hasta 2026-7).
    private static CarteraDecisionSnapshot Base() => new(
        ConInformacionObservado: true,
        ScoreObservado: 850,
        EstadoScore: CarteraEstadoScore.Disponible,
        ViabilidadObservada: "ALTA",
        RatingRecaudosObservado: "A",
        TipoDocumentoObservado: "CC",
        EstadoDocumentoDatosBasicosRaw: "Vigente",
        EstadoDocumentoInfoDemograficaRaw: null,
        EstadoDocumentoCaptura: "PRESENTE",
        RangoEdadDatosBasicosRaw: "36-45",
        RangoEdadInfoDemograficaRaw: null,
        RangoEdadCaptura: "PRESENTE",
        ComportamientoVectorJson: Vec(("2026-3", "N"), ("2026-4", "N"), ("2026-5", "N"),
                                      ("2026-6", "N"), ("2026-7", "N")),
        ComportamientoVectorCount: 5);

    private static decimal MontoEsperado(decimal baseScore, decimal fV, decimal fR)
    {
        var raw = baseScore * fV * fR * P.FactorComportamientoPeor1 * P.FactorEdadElegible;
        return Math.Min(Math.Ceiling(raw / P.RedondeoUnidad) * P.RedondeoUnidad, P.CupoMaximo);
    }

    [Fact]
    public void CasoA_VentanaIncompleta_SinMora_PermiteAprobar_ConFactorPeor1()
    {
        var auto = CarteraDecisionEngine.Evaluar(Base(), P, Ctx);
        Assert.Equal(CarteraDecisionCrediticia.NoDecidible, auto.Decision);

        var r = CarteraRevisionManualPolicy.Proponer(Base(), P, Ctx);

        Assert.True(r.PermiteAprobar);
        Assert.Equal(CarteraRevisionManualPolicy.ReglaVentanaIncompleta, r.Regla);
        Assert.Equal(MontoEsperado(P.BandaS5Base, P.FactorViabilidadAlta, P.FactorRatingA), r.MontoPropuesto);
        Assert.Null(r.CausaNoAprobable);
    }

    [Fact]
    public void CasoA_HuecoDentroDeVentana_SinMora_PermiteAprobar()
    {
        var s = Base() with
        {
            ComportamientoVectorJson = Vec(("2026-2", "N"), ("2026-3", "N"), ("2026-4", "N"),
                                           ("2026-6", "N"), ("2026-7", "N")),
        };
        var r = CarteraRevisionManualPolicy.Proponer(s, P, Ctx);
        Assert.True(r.PermiteAprobar);
    }

    [Fact]
    public void CasoA_MediaB_MontoCalculadoPorPolitica()
    {
        var s = Base() with { ScoreObservado = 791, ViabilidadObservada = "MEDIA", RatingRecaudosObservado = "B" };
        var r = CarteraRevisionManualPolicy.Proponer(s, P, Ctx);
        Assert.True(r.PermiteAprobar);
        Assert.Equal(MontoEsperado(P.BandaS4Base, P.FactorViabilidadMedia, P.FactorRatingB), r.MontoPropuesto);
    }

    private static void AssertSoloRechazo(CarteraDecisionSnapshot s, string causa, CarteraEvaluationContext? ctx = null)
    {
        var r = CarteraRevisionManualPolicy.Proponer(s, P, ctx ?? Ctx);
        Assert.False(r.PermiteAprobar);
        Assert.Null(r.MontoPropuesto);
        Assert.Equal(CarteraRevisionManualPolicy.ReglaSoloRechazo, r.Regla);
        Assert.Equal(causa, r.CausaNoAprobable);
    }

    private const string Comp = CarteraRevisionManualPolicy.CausaComportamientoNoResoluble;

    [Fact]
    public void B_MoraDentroDeVentanaIncompleta_SoloRechazo()
        => AssertSoloRechazo(Base() with
        {
            ComportamientoVectorJson = Vec(("2026-3", "N"), ("2026-4", "1"), ("2026-5", "N"),
                                           ("2026-6", "N"), ("2026-7", "N")),
        }, Comp);

    [Fact]
    public void B_VentanaIncompletaSoloNoData_SinNingunN_SoloRechazo()
        => AssertSoloRechazo(Base() with
        {
            ComportamientoVectorJson = Vec(("2026-5", "-"), ("2026-6", "-"), ("2026-7", "-")),
            ComportamientoVectorCount = 3,
        }, Comp);

    [Fact]
    public void B_BloqueComportamientoAusente_SoloRechazo()
        => AssertSoloRechazo(Base() with { ComportamientoVectorJson = null, ComportamientoVectorCount = null }, Comp);

    [Fact]
    public void B_SinMesDeConsultaValido_SoloRechazo()
        => AssertSoloRechazo(Base(), Comp, new CarteraEvaluationContext(null, null));

    [Fact]
    public void B_AnioMesInvalido_SoloRechazo()
        => AssertSoloRechazo(Base() with
        {
            ComportamientoVectorJson = Vec(("2026/3", "N"), ("2026-4", "N"), ("2026-5", "N")),
        }, Comp);

    [Fact]
    public void B_MesFuturo_SoloRechazo()
        => AssertSoloRechazo(Base() with
        {
            ComportamientoVectorJson = Vec(("2026-6", "N"), ("2026-7", "N"), ("2026-10", "N")),
        }, Comp);

    [Fact]
    public void B_DuplicadoEnConflicto_SoloRechazo()
        => AssertSoloRechazo(Base() with
        {
            ComportamientoVectorJson = Vec(("2026-6", "N"), ("2026-6", "1"), ("2026-7", "N")),
        }, Comp);

    [Fact]
    public void B_CodigoDesconocidoEnVentana_SoloRechazo()
        => AssertSoloRechazo(Base() with
        {
            ComportamientoVectorJson = Vec(("2026-5", "N"), ("2026-6", "Z"), ("2026-7", "N")),
        }, Comp);

    private const string Motor   = CarteraRevisionManualPolicy.CausaMotorNoAprueba;
    private const string Motivos = CarteraRevisionManualPolicy.CausaMotivosNoResolubles;
    private const string NoNd    = CarteraRevisionManualPolicy.CausaNoEsNoDecidible;

    private static string VentanaCompletaN()
        => Vec(("2026-2", "N"), ("2026-3", "N"), ("2026-4", "N"),
               ("2026-5", "N"), ("2026-6", "N"), ("2026-7", "N"));

    // Sin score utilizable: nunca aprobar, aunque el comportamiento califique.
    [Fact]
    public void B_SinScore_SoloRechazo()
        => AssertSoloRechazo(Base() with
        {
            ScoreObservado = null,
            EstadoScore = CarteraEstadoScore.SinDato,
        }, Motor);

    [Fact]
    public void B_SinInformacion_SoloRechazo()
        => AssertSoloRechazo(Base() with
        {
            ConInformacionObservado = false,
            ScoreObservado = null,
            EstadoScore = CarteraEstadoScore.SinInformacion,
        }, Motor);

    // No se inventa documento, tipo, edad, viabilidad ni rating.
    [Fact]
    public void B_DocumentoAusente_SoloRechazo()
        => AssertSoloRechazo(Base() with { EstadoDocumentoCaptura = "AUSENTE" }, Motor);

    [Fact]
    public void B_TipoDocumentoAusente_SoloRechazo()
        => AssertSoloRechazo(Base() with { TipoDocumentoObservado = null }, Motor);

    [Fact]
    public void B_EdadAusente_SoloRechazo()
        => AssertSoloRechazo(Base() with { RangoEdadCaptura = "AUSENTE" }, Motor);

    [Fact]
    public void B_ViabilidadAusente_SoloRechazo()
        => AssertSoloRechazo(Base() with { ViabilidadObservada = null }, Motor);

    [Fact]
    public void B_RatingAusente_SoloRechazo()
        => AssertSoloRechazo(Base() with { RatingRecaudosObservado = null }, Motor);

    // Edad 66+: no existe factor aprobado; nunca se asume elegible.
    [Fact]
    public void B_Edad66_ConVentanaIncompleta_SoloRechazo()
        => AssertSoloRechazo(Base() with { RangoEdadDatosBasicosRaw = "66" }, Motivos);

    [Fact]
    public void B_Edad66_ConVentanaCompleta_SoloRechazo()
        => AssertSoloRechazo(Base() with
        {
            RangoEdadDatosBasicosRaw = "66",
            ComportamientoVectorJson = VentanaCompletaN(),
            ComportamientoVectorCount = 6,
        }, Motivos);

    [Fact]
    public void B_ScoreBandaRechazo_SoloRechazo()
        => AssertSoloRechazo(Base() with { ScoreObservado = 300 }, Motivos);

    [Fact]
    public void B_TipoDocumentoDesconocido_SoloRechazo()
        => AssertSoloRechazo(Base() with { TipoDocumentoObservado = "XYZ" }, Motivos);

    // Cupo calculado bajo el minimo de politica: no se asigna CupoMinimo.
    [Fact]
    public void B_CupoBajoMinimo_SoloRechazo()
        => AssertSoloRechazo(Base() with
        {
            ScoreObservado = 400,
            ViabilidadObservada = "MEDIA",
            RatingRecaudosObservado = "B",
        }, Motor);

    [Fact]
    public void DecisionAutomaticaAprobada_NoAplica()
        => AssertSoloRechazo(Base() with
        {
            ComportamientoVectorJson = VentanaCompletaN(),
            ComportamientoVectorCount = 6,
        }, NoNd);

    [Fact]
    public void DecisionAutomaticaRechazada_NoAplica()
        => AssertSoloRechazo(Base() with
        {
            ViabilidadObservada = "BAJA",
            ComportamientoVectorJson = VentanaCompletaN(),
            ComportamientoVectorCount = 6,
        }, NoNd);

    // La propuesta no modifica el snapshot real (la ventana tecnica es interna).
    [Fact]
    public void Proponer_NoModificaElSnapshotReal_EsDeterminista()
    {
        var s = Base();
        var copia = s with { };

        var r1 = CarteraRevisionManualPolicy.Proponer(s, P, Ctx);
        var r2 = CarteraRevisionManualPolicy.Proponer(s, P, Ctx);

        Assert.Equal(copia, s);
        Assert.Equal(r1, r2);
    }
}
