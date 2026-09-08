using System.Text.Json;
using Xpay.Api.Common;
using Xunit;

namespace Xpay.Api.Tests.Services;

// ══════════════════════════════════════════════════════════════════════════
// M2.4b — motor de decisión crediticia PURO (CarteraDecisionEngine).
// Sin SQL, sin red, sin proveedor. Tabla de verdad determinística de las
// reglas cerradas en la política working copy (§A–§P) + XPAY-172/173/174/182.
// ══════════════════════════════════════════════════════════════════════════

public sealed class CarteraDecisionEngineTests
{
    private static readonly CarteraPolicyParameters P = CarteraPolicyParameters.Vigente();

    // Contexto de consulta: septiembre 2026 → M-1 = agosto 2026 →
    // ventana requerida = marzo..agosto 2026.
    private static readonly CarteraEvaluationContext Ctx = new(2026, 9);

    private static string VectorJson(params (string anioMes, string? comp)[] elems)
        => JsonSerializer.Serialize(
            elems.Select(e => new CarteraComportamientoVectorItemRaw(e.anioMes, e.comp)).ToList());

    private static string Ventana6(string comp) => VectorJson(
        ("2026-3", comp), ("2026-4", comp), ("2026-5", comp),
        ("2026-6", comp), ("2026-7", comp), ("2026-8", comp));

    // Snapshot base = caso APROBADA limpio (score S5, ALTA/A, 6 meses N).
    private sealed class Snap
    {
        public bool?   ConInformacion = true;
        public int?    Score          = 850;
        public string? EstadoScore    = CarteraEstadoScore.Disponible;
        public string? Viabilidad     = "ALTA";
        public string? Rating         = "A";
        public string? TipoDoc        = "CC";
        public string? EstadoDocDb    = null;
        public string? EstadoDocId    = "Vigente";
        public string? EstadoDocCap   = "PRESENTE";
        public string? RangoEdadDb    = null;
        public string? RangoEdadId    = "36-45";
        public string? RangoEdadCap   = "PRESENTE";
        public string? VectorJson     = Ventana6("N");
        public int?    VectorCount    = 6;

        public CarteraDecisionSnapshot Build() => new(
            ConInformacion, Score, EstadoScore, Viabilidad, Rating, TipoDoc,
            EstadoDocDb, EstadoDocId, EstadoDocCap,
            RangoEdadDb, RangoEdadId, RangoEdadCap,
            VectorJson, VectorCount);
    }

    private static ResultadoDecisionCrediticia Eval(Snap s, CarteraEvaluationContext? ctx = null)
        => CarteraDecisionEngine.Evaluar(s.Build(), P, ctx ?? Ctx);

    // ── APROBADA base ────────────────────────────────────────────────────
    [Fact]
    public void Base_Aprobada_700000()
    {
        var r = Eval(new Snap());
        Assert.Equal(CarteraDecisionCrediticia.Aprobada, r.Decision);
        Assert.Equal(700_000m, r.MontoAprobado);
        Assert.Empty(r.MotivosOrdenados);
        Assert.Null(r.MotivoPrimario);
        Assert.False(r.SenalPosibleSuplantacion);
    }

    // ── Bandas de score + límites ───────────────────────────────────────
    [Theory]
    [InlineData(149, CarteraDecisionCrediticia.NoDecidible, CarteraMotivoDecision.ValorFueraPolitica)]
    [InlineData(150, CarteraDecisionCrediticia.Rechazada,   CarteraMotivoDecision.ScoreInsuficiente)]
    [InlineData(360, CarteraDecisionCrediticia.Rechazada,   CarteraMotivoDecision.ScoreInsuficiente)]
    [InlineData(951, CarteraDecisionCrediticia.NoDecidible, CarteraMotivoDecision.ValorFueraPolitica)]
    public void Score_Limites(int score, string decision, string motivo)
    {
        var r = Eval(new Snap { Score = score });
        Assert.Equal(decision, r.Decision);
        Assert.Equal(motivo, r.MotivoPrimario);
    }

    [Theory]
    [InlineData(361, 300_000)]
    [InlineData(450, 300_000)]
    [InlineData(451, 400_000)]
    [InlineData(600, 400_000)]
    [InlineData(601, 600_000)]
    [InlineData(800, 600_000)]
    [InlineData(801, 700_000)]
    [InlineData(950, 700_000)]
    public void Score_Bandas_BaseAplicada(int score, int montoEsperado)
    {
        // ALTA(1.0) A(1.0) N(1.0) edad(1.0) → raw = base → redondeo/cap no cambian.
        var r = Eval(new Snap { Score = score });
        Assert.Equal(CarteraDecisionCrediticia.Aprobada, r.Decision);
        Assert.Equal((decimal)montoEsperado, r.MontoAprobado);
    }

    [Fact]
    public void Score_Ausente_NoDecidible_Info()
    {
        var r = Eval(new Snap { Score = null });
        Assert.Equal(CarteraDecisionCrediticia.NoDecidible, r.Decision);
        Assert.Equal(CarteraMotivoDecision.InformacionInsuficiente, r.MotivoPrimario);
    }

    // ── Viabilidad ──────────────────────────────────────────────────────
    [Fact]
    public void Viabilidad_MEDIA_factor_0_80()
    {
        var r = Eval(new Snap { Score = 801, Viabilidad = "MEDIA" }); // base 700000 * 0.8 = 560000
        Assert.Equal(CarteraDecisionCrediticia.Aprobada, r.Decision);
        Assert.Equal(560_000m, r.MontoAprobado);
    }

    [Fact]
    public void Viabilidad_BAJA_Rechazo()
    {
        var r = Eval(new Snap { Viabilidad = "BAJA" });
        Assert.Equal(CarteraDecisionCrediticia.Rechazada, r.Decision);
        Assert.Equal(CarteraMotivoDecision.ViabilidadBaja, r.MotivoPrimario);
    }

    [Fact]
    public void Viabilidad_Missing_NoDecidible()
        => Assert.Equal(CarteraMotivoDecision.InformacionInsuficiente,
            Eval(new Snap { Viabilidad = null }).MotivoPrimario);

    // ── Rating ──────────────────────────────────────────────────────────
    [Theory]
    [InlineData("C")]
    [InlineData("D")]
    [InlineData("N")]
    public void Rating_CDN_Rechazo(string rating)
    {
        var r = Eval(new Snap { Rating = rating });
        Assert.Equal(CarteraDecisionCrediticia.Rechazada, r.Decision);
        Assert.Equal(CarteraMotivoDecision.RatingRecaudosInsuficiente, r.MotivoPrimario);
    }

    [Fact]
    public void Rating_B_factor_0_80()
    {
        var r = Eval(new Snap { Score = 801, Rating = "B" }); // 700000 * 0.8 = 560000
        Assert.Equal(560_000m, r.MontoAprobado);
    }

    [Fact]
    public void Rating_Missing_NoDecidible()
        => Assert.Equal(CarteraMotivoDecision.InformacionInsuficiente,
            Eval(new Snap { Rating = null }).MotivoPrimario);

    // ── Documento (estado) ──────────────────────────────────────────────
    [Theory]
    [InlineData("Cancelada")]
    [InlineData("No expedida")]
    [InlineData("En trámite")]
    public void EstadoDoc_NoVigente_Rechazo_SinSuplantacion(string estado)
    {
        var r = Eval(new Snap { EstadoDocId = estado });
        Assert.Equal(CarteraDecisionCrediticia.Rechazada, r.Decision);
        Assert.Equal(CarteraMotivoDecision.DocNoVigente, r.MotivoPrimario);
        Assert.False(r.SenalPosibleSuplantacion);
    }

    [Fact]
    public void EstadoDoc_Muerte_Rechazo_ConSuplantacion()
    {
        var r = Eval(new Snap { EstadoDocId = "Cancelada por muerte o fallecido" });
        Assert.Equal(CarteraDecisionCrediticia.Rechazada, r.Decision);
        Assert.Equal(CarteraMotivoDecision.DocNoVigente, r.MotivoPrimario);
        Assert.True(r.SenalPosibleSuplantacion);
    }

    [Fact]
    public void EstadoDoc_Conflicto_NoDecidible_SuplantacionNull()
    {
        var r = Eval(new Snap { EstadoDocCap = "CONFLICTO", EstadoDocDb = "Vigente", EstadoDocId = "Cancelada" });
        Assert.Equal(CarteraDecisionCrediticia.NoDecidible, r.Decision);
        Assert.Equal(CarteraMotivoDecision.InformacionInsuficiente, r.MotivoPrimario);
        Assert.Null(r.SenalPosibleSuplantacion);
    }

    [Fact]
    public void EstadoDoc_Ausente_NoDecidible()
    {
        var r = Eval(new Snap { EstadoDocCap = "AUSENTE", EstadoDocId = null });
        Assert.Equal(CarteraMotivoDecision.InformacionInsuficiente, r.MotivoPrimario);
    }

    [Fact]
    public void EstadoDoc_Desconocido_NoDecidible_Valor()
    {
        var r = Eval(new Snap { EstadoDocId = "Suspendida" });
        Assert.Equal(CarteraMotivoDecision.ValorFueraPolitica, r.MotivoPrimario);
    }

    [Fact]
    public void EstadoDoc_Vigente_dualPath_datosBasicos()
    {
        var r = Eval(new Snap { EstadoDocCap = "PRESENTE", EstadoDocDb = "Vigente", EstadoDocId = null });
        Assert.Equal(CarteraDecisionCrediticia.Aprobada, r.Decision);
    }

    // ── Tipo de documento ───────────────────────────────────────────────
    [Theory]
    [InlineData("CC")]
    [InlineData("1")]
    [InlineData("CÉDULA DE CIUDADANÍA")]
    [InlineData("Cédula de ciudadanía y NUIP")]
    public void TipoDoc_CC_Aceptado(string raw)
        => Assert.Equal(CarteraDecisionCrediticia.Aprobada, Eval(new Snap { TipoDoc = raw }).Decision);

    [Theory]
    [InlineData("CE")]
    [InlineData("PAS")]
    [InlineData("5")]
    public void TipoDoc_Reconocido_NoCC_Rechazo(string raw)
    {
        var r = Eval(new Snap { TipoDoc = raw });
        Assert.Equal(CarteraDecisionCrediticia.Rechazada, r.Decision);
        Assert.Equal(CarteraMotivoDecision.TipoDocNoAceptado, r.MotivoPrimario);
    }

    [Fact]
    public void TipoDoc_Missing_NoDecidible_Info()
        => Assert.Equal(CarteraMotivoDecision.InformacionInsuficiente,
            Eval(new Snap { TipoDoc = "   " }).MotivoPrimario);

    [Fact]
    public void TipoDoc_Desconocido_NoDecidible_Valor()
        => Assert.Equal(CarteraMotivoDecision.ValorFueraPolitica,
            Eval(new Snap { TipoDoc = "XYZ" }).MotivoPrimario);

    // ── Edad ────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("18-21")]
    [InlineData("46-55")]
    [InlineData("56-65")]
    public void Edad_BandaElegible_Continua(string banda)
        => Assert.Equal(CarteraDecisionCrediticia.Aprobada, Eval(new Snap { RangoEdadId = banda }).Decision);

    [Fact]
    public void Edad_66_Rechazo()
    {
        var r = Eval(new Snap { RangoEdadId = "66" });
        Assert.Equal(CarteraDecisionCrediticia.Rechazada, r.Decision);
        Assert.Equal(CarteraMotivoDecision.EdadFueraPolitica, r.MotivoPrimario);
    }

    [Theory]
    [InlineData("-")]
    public void Edad_SinInfo_NoDecidible(string val)
        => Assert.Equal(CarteraMotivoDecision.InformacionInsuficiente,
            Eval(new Snap { RangoEdadId = val }).MotivoPrimario);

    [Fact]
    public void Edad_Ausente_NoDecidible()
        => Assert.Equal(CarteraMotivoDecision.InformacionInsuficiente,
            Eval(new Snap { RangoEdadCap = "AUSENTE", RangoEdadId = null }).MotivoPrimario);

    [Fact]
    public void Edad_Desconocido_NoDecidible_Valor()
        => Assert.Equal(CarteraMotivoDecision.ValorFueraPolitica,
            Eval(new Snap { RangoEdadId = "70-80" }).MotivoPrimario);

    // ── conInformacion / estadoScore ────────────────────────────────────
    [Fact]
    public void ConInformacion_False_NoDecidible()
    {
        var r = Eval(new Snap { ConInformacion = false, EstadoScore = CarteraEstadoScore.SinInformacion });
        Assert.Equal(CarteraDecisionCrediticia.NoDecidible, r.Decision);
        Assert.Equal(CarteraMotivoDecision.InformacionInsuficiente, r.MotivoPrimario);
        // dedup: no debe haber 2 INFORMACION_INSUFICIENTE por conInfo + estadoScore.
        Assert.Single(r.MotivosOrdenados, CarteraMotivoDecision.InformacionInsuficiente);
    }

    [Theory]
    [InlineData(CarteraEstadoScore.SinInformacion)]
    [InlineData(CarteraEstadoScore.SinDato)]
    public void EstadoScore_SinDato_NoDecidible(string estadoScore)
        => Assert.Equal(CarteraMotivoDecision.InformacionInsuficiente,
            Eval(new Snap { EstadoScore = estadoScore }).MotivoPrimario);

    // ── Comportamiento — anioMes / ventana ──────────────────────────────
    [Fact]
    public void Comp_YYYY_MM_zeroPad_ok()
    {
        var v = VectorJson(("2026-03", "N"), ("2026-04", "N"), ("2026-05", "N"),
                           ("2026-06", "N"), ("2026-07", "N"), ("2026-08", "N"));
        Assert.Equal(CarteraDecisionCrediticia.Aprobada, Eval(new Snap { VectorJson = v, VectorCount = 6 }).Decision);
    }

    [Theory]
    [InlineData("2026/3")]
    [InlineData("Mar-2026")]
    [InlineData("26-3")]
    [InlineData("2026-13")]
    [InlineData("2026-0")]
    [InlineData(" 2026-3")]
    public void Comp_AnioMes_Invalido_NoDecidible(string anioMesMalo)
    {
        var v = VectorJson((anioMesMalo, "N"), ("2026-4", "N"), ("2026-5", "N"),
                           ("2026-6", "N"), ("2026-7", "N"), ("2026-8", "N"));
        var r = Eval(new Snap { VectorJson = v, VectorCount = 6 });
        Assert.Equal(CarteraDecisionCrediticia.NoDecidible, r.Decision);
        Assert.Equal(CarteraMotivoDecision.InformacionInsuficiente, r.MotivoPrimario);
    }

    [Fact]
    public void Comp_MesFuturo_NoDecidible()
    {
        var v = VectorJson(("2026-3", "N"), ("2026-4", "N"), ("2026-5", "N"),
                           ("2026-6", "N"), ("2026-7", "N"), ("2026-8", "N"), ("2026-10", "N"));
        Assert.Equal(CarteraDecisionCrediticia.NoDecidible,
            Eval(new Snap { VectorJson = v, VectorCount = 7 }).Decision);
    }

    [Fact]
    public void Comp_MesFaltante_NoDecidible()
    {
        var v = VectorJson(("2026-3", "N"), ("2026-4", "N"), ("2026-5", "N"),
                           ("2026-6", "N"), ("2026-8", "N")); // falta 2026-7
        Assert.Equal(CarteraDecisionCrediticia.NoDecidible,
            Eval(new Snap { VectorJson = v, VectorCount = 5 }).Decision);
    }

    [Fact]
    public void Comp_BloqueAusente_NoDecidible()
        => Assert.Equal(CarteraMotivoDecision.InformacionInsuficiente,
            Eval(new Snap { VectorJson = null, VectorCount = null }).MotivoPrimario);

    [Fact]
    public void Comp_DuplicadoIgual_Dedup_ok()
    {
        var v = VectorJson(("2026-3", "N"), ("2026-3", "N"), ("2026-4", "N"), ("2026-5", "N"),
                           ("2026-6", "N"), ("2026-7", "N"), ("2026-8", "N"));
        Assert.Equal(CarteraDecisionCrediticia.Aprobada, Eval(new Snap { VectorJson = v, VectorCount = 7 }).Decision);
    }

    [Fact]
    public void Comp_DuplicadoConflicto_NoDecidible()
    {
        var v = VectorJson(("2026-3", "N"), ("2026-3", "1"), ("2026-4", "N"), ("2026-5", "N"),
                           ("2026-6", "N"), ("2026-7", "N"), ("2026-8", "N"));
        Assert.Equal(CarteraDecisionCrediticia.NoDecidible, Eval(new Snap { VectorJson = v, VectorCount = 7 }).Decision);
    }

    [Fact]
    public void Comp_FueraDeOrden_ok()
    {
        var v = VectorJson(("2026-8", "N"), ("2026-3", "N"), ("2026-6", "N"),
                           ("2026-4", "N"), ("2026-7", "N"), ("2026-5", "N"));
        Assert.Equal(CarteraDecisionCrediticia.Aprobada, Eval(new Snap { VectorJson = v, VectorCount = 6 }).Decision);
    }

    [Fact]
    public void Comp_MuchosElementos_sinCap()
    {
        var lista = new List<(string, string?)>();
        for (var a = 2015; a <= 2026; a++)
            for (var m = 1; m <= 12; m++)
                if (a < 2026 || m <= 8)
                    lista.Add(($"{a}-{m}", "N"));
        Assert.True(lista.Count > 120);
        Assert.Equal(CarteraDecisionCrediticia.Aprobada,
            Eval(new Snap { VectorJson = VectorJson(lista.ToArray()), VectorCount = lista.Count }).Decision);
    }

    // ── Comportamiento — factores / rechazos ────────────────────────────
    [Fact]
    public void Comp_Peor_1_factor_0_90()
    {
        var v = VectorJson(("2026-3", "1"), ("2026-4", "N"), ("2026-5", "N"),
                           ("2026-6", "N"), ("2026-7", "N"), ("2026-8", "N"));
        var r = Eval(new Snap { Score = 801, VectorJson = v, VectorCount = 6 }); // 700000 * 0.9 = 630000
        Assert.Equal(CarteraDecisionCrediticia.Aprobada, r.Decision);
        Assert.Equal(630_000m, r.MontoAprobado);
    }

    [Fact]
    public void Comp_Peor_3_Rechazo_Insuficiente()
    {
        var v = VectorJson(("2026-3", "3"), ("2026-4", "N"), ("2026-5", "N"),
                           ("2026-6", "N"), ("2026-7", "N"), ("2026-8", "N"));
        var r = Eval(new Snap { VectorJson = v, VectorCount = 6 });
        Assert.Equal(CarteraDecisionCrediticia.Rechazada, r.Decision);
        Assert.Equal(CarteraMotivoDecision.ComportamientoPagoInsuficiente, r.MotivoPrimario);
    }

    [Fact]
    public void Comp_UltimoMes_1_Rechazo_UltimoMesNoAlDia()
    {
        var v = VectorJson(("2026-3", "N"), ("2026-4", "N"), ("2026-5", "N"),
                           ("2026-6", "N"), ("2026-7", "N"), ("2026-8", "1"));
        var r = Eval(new Snap { VectorJson = v, VectorCount = 6 });
        Assert.Equal(CarteraDecisionCrediticia.Rechazada, r.Decision);
        Assert.Equal(new[] { CarteraMotivoDecision.ComportamientoPagoUltimoMesNoAlDia }, r.MotivosOrdenados);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("6")]
    [InlineData("C")]
    [InlineData("D")]
    public void Comp_UltimoMes_2plus_KeepBoth(string ultimo)
    {
        var v = VectorJson(("2026-3", "N"), ("2026-4", "N"), ("2026-5", "N"),
                           ("2026-6", "N"), ("2026-7", "N"), ("2026-8", ultimo));
        var r = Eval(new Snap { VectorJson = v, VectorCount = 6 });
        Assert.Equal(CarteraDecisionCrediticia.Rechazada, r.Decision);
        Assert.Equal(new[]
        {
            CarteraMotivoDecision.ComportamientoPagoUltimoMesNoAlDia,
            CarteraMotivoDecision.ComportamientoPagoInsuficiente,
        }, r.MotivosOrdenados);
    }

    [Fact]
    public void Comp_UltimoMes_NoData_Ignora()
    {
        var v = VectorJson(("2026-3", "N"), ("2026-4", "N"), ("2026-5", "N"),
                           ("2026-6", "N"), ("2026-7", "N"), ("2026-8", "-"));
        Assert.Equal(CarteraDecisionCrediticia.Aprobada, Eval(new Snap { VectorJson = v, VectorCount = 6 }).Decision);
    }

    [Theory]
    [InlineData("-")]
    [InlineData("")]
    [InlineData(" ")]
    public void Comp_TodosNoData_factor_1_00(string noData)
    {
        var r = Eval(new Snap { VectorJson = Ventana6(noData), VectorCount = 6 });
        Assert.Equal(CarteraDecisionCrediticia.Aprobada, r.Decision);
        Assert.Equal(700_000m, r.MontoAprobado);
    }

    [Fact]
    public void Comp_CodigoDesconocido_NoDecidible_Valor()
    {
        var v = VectorJson(("2026-3", "Z"), ("2026-4", "N"), ("2026-5", "N"),
                           ("2026-6", "N"), ("2026-7", "N"), ("2026-8", "N"));
        var r = Eval(new Snap { VectorJson = v, VectorCount = 6 });
        Assert.Equal(CarteraDecisionCrediticia.NoDecidible, r.Decision);
        Assert.Equal(CarteraMotivoDecision.ValorFueraPolitica, r.MotivoPrimario);
    }

    // ── Fórmula ─────────────────────────────────────────────────────────
    [Fact]
    public void Formula_raw_menor_min_Rechazo_ResultadoScore()
    {
        // S2 base 300000 * MEDIA(0.8) * B(0.8) * N(1.0) = 192000 < 200000.
        var r = Eval(new Snap { Score = 400, Viabilidad = "MEDIA", Rating = "B" });
        Assert.Equal(CarteraDecisionCrediticia.Rechazada, r.Decision);
        Assert.Equal(CarteraMotivoDecision.ResultadoScoreInsuficiente, r.MotivoPrimario);
        Assert.Equal(0m, r.MontoAprobado);
    }

    [Fact]
    public void Formula_redondeo_hacia_arriba()
    {
        // S3 base 400000 * MEDIA(0.8) * B(0.8) * comportamiento peor=1 (0.9) = 230400.
        // 230400 >= 200000 → redondeo hacia arriba al siguiente múltiplo de 1000 → 231000.
        var v = VectorJson(("2026-3", "1"), ("2026-4", "N"), ("2026-5", "N"),
                           ("2026-6", "N"), ("2026-7", "N"), ("2026-8", "N"));
        var r = Eval(new Snap { Score = 500, Viabilidad = "MEDIA", Rating = "B", VectorJson = v, VectorCount = 6 });
        Assert.Equal(CarteraDecisionCrediticia.Aprobada, r.Decision);
        Assert.Equal(231_000m, r.MontoAprobado);
    }

    [Fact]
    public void Formula_cap_maximo_700000()
    {
        var r = Eval(new Snap { Score = 950 }); // base 700000, factores 1.0 → 700000, cap 700000
        Assert.Equal(700_000m, r.MontoAprobado);
    }

    // ── Multi-reason / NO_DECIDIBLE + rechazo simultáneo ────────────────
    [Fact]
    public void MultiRechazo_ordenPrecedencia_H()
    {
        // rango edad 66 (grupo 3) + viabilidad BAJA (grupo 6) + rating N (grupo 7)
        var r = Eval(new Snap { RangoEdadId = "66", Viabilidad = "BAJA", Rating = "N" });
        Assert.Equal(CarteraDecisionCrediticia.Rechazada, r.Decision);
        Assert.Equal(new[]
        {
            CarteraMotivoDecision.EdadFueraPolitica,
            CarteraMotivoDecision.ViabilidadBaja,
            CarteraMotivoDecision.RatingRecaudosInsuficiente,
        }, r.MotivosOrdenados);
        Assert.Equal(CarteraMotivoDecision.EdadFueraPolitica, r.MotivoPrimario);
    }

    [Fact]
    public void NoDecidible_gana_sobre_rechazo_simultaneo_pero_conserva_rechazos()
    {
        // score 200 (RECHAZO SCORE_INSUFICIENTE) + estadoDocumento ausente (NO_DECIDIBLE).
        var r = Eval(new Snap { Score = 200, EstadoDocCap = "AUSENTE", EstadoDocId = null });
        Assert.Equal(CarteraDecisionCrediticia.NoDecidible, r.Decision);
        Assert.Equal(CarteraMotivoDecision.InformacionInsuficiente, r.MotivoPrimario);
        // el rechazo determinable se conserva DESPUÉS, para auditoría.
        Assert.Contains(CarteraMotivoDecision.ScoreInsuficiente, r.MotivosOrdenados);
        Assert.Equal(CarteraMotivoDecision.InformacionInsuficiente, r.MotivosOrdenados[0]);
        Assert.Null(r.MontoAprobado);
    }

    [Fact]
    public void NoDecidible_orden_por_gate_info_luego_valor()
    {
        // estadoDocumento ausente (gate 1 → INFORMACION_INSUFICIENTE) + score fuera de rango (gate 4 → VALOR_FUERA_POLITICA)
        var r = Eval(new Snap { EstadoDocCap = "AUSENTE", EstadoDocId = null, Score = 5000 });
        Assert.Equal(CarteraDecisionCrediticia.NoDecidible, r.Decision);
        Assert.Equal(new[]
        {
            CarteraMotivoDecision.InformacionInsuficiente,
            CarteraMotivoDecision.ValorFueraPolitica,
        }, r.MotivosOrdenados);
    }

    [Fact]
    public void Sin_codigos_duplicados()
    {
        // varios gates → INFORMACION_INSUFICIENTE, debe aparecer una sola vez.
        var r = Eval(new Snap { Viabilidad = null, Rating = null, TipoDoc = "  " });
        Assert.Equal(CarteraDecisionCrediticia.NoDecidible, r.Decision);
        Assert.Single(r.MotivosOrdenados);
        Assert.Equal(CarteraMotivoDecision.InformacionInsuficiente, r.MotivosOrdenados[0]);
    }

    // ── Query month ─────────────────────────────────────────────────────
    [Fact]
    public void QueryMonth_ausente_comportamiento_NoDecidible()
    {
        var ctx = CarteraEvaluationContext.DesdeConsultaRaw(null, null);
        var r = Eval(new Snap(), ctx);
        Assert.Equal(CarteraDecisionCrediticia.NoDecidible, r.Decision);
        Assert.Equal(CarteraMotivoDecision.InformacionInsuficiente, r.MotivoPrimario);
    }

    [Fact]
    public void QueryMonth_invalido_raw_no_interpreta()
    {
        Assert.Null(CarteraEvaluationContext.DesdeConsultaRaw("25", "11").QueryAnio);
        Assert.Null(CarteraEvaluationContext.DesdeConsultaRaw("2025", "13").QueryMes);
        Assert.Equal(2025, CarteraEvaluationContext.DesdeConsultaRaw("2025", "5").QueryAnio);
        Assert.Equal(5, CarteraEvaluationContext.DesdeConsultaRaw("2025", "05").QueryMes);
    }
}
