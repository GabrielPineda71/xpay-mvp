using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Xpay.Api.Common;

// M2.4b — Cartera Ordinaria. Motor de decisión crediticia PURO y DETERMINÍSTICO.
//
// Consume EXCLUSIVAMENTE el snapshot durable M2.4a (proyectado en
// CarteraDecisionSnapshot) + parámetros de política inyectables + un contexto
// de evaluación que aporta el mes de consulta del proveedor ya interpretado.
//
// SIN DbContext, SIN HTTP, SIN DateTime.UtcNow, SIN random, SIN side effects.
// NO persiste. NO llama a MiDecisor. NO usa timestamps XPAY como fallback para
// el mes de consulta. NO introduce reglas crediticias nuevas: aplica las ya
// cerradas en la política working copy (§A–§P) y en XPAY-172/173/174/182.
//
// Contradicción "conservadora" (XPAY-182): si existe AL MENOS UNA causa
// NO_DECIDIBLE, la Decision final es NO_DECIDIBLE aunque existan rechazos
// determinables; los rechazos se conservan en la lista para auditoría pero NO
// desplazan al primer motivo NO_DECIDIBLE como primario.
public sealed class CarteraDecisionInvarianteException(string message) : Exception(message);

// Snapshot proyectado que el motor necesita. Todos los campos provienen de
// cartera_solicitudes_cupo (observados M2.4a + P0 de la migración 039).
public sealed record CarteraDecisionSnapshot(
    bool?    ConInformacionObservado,
    int?     ScoreObservado,
    string?  EstadoScore,
    string?  ViabilidadObservada,
    string?  RatingRecaudosObservado,
    string?  TipoDocumentoObservado,
    string?  EstadoDocumentoDatosBasicosRaw,
    string?  EstadoDocumentoInfoDemograficaRaw,
    string?  EstadoDocumentoCaptura,
    string?  RangoEdadDatosBasicosRaw,
    string?  RangoEdadInfoDemograficaRaw,
    string?  RangoEdadCaptura,
    string?  ComportamientoVectorJson,
    int?     ComportamientoVectorCount);

// Mes de consulta del proveedor YA interpretado (infoTransaccion.{anio,mes}Consulta).
// NULL cuando el proveedor no entregó una autoridad temporal válida.
public sealed record CarteraEvaluationContext(int? QueryAnio, int? QueryMes)
{
    // Interpreta los raw de la consulta según contrato: año exactamente 4
    // dígitos ; mes 1..12. Cualquier desviación → (null, null).
    public static CarteraEvaluationContext DesdeConsultaRaw(string? anioRaw, string? mesRaw)
    {
        var a = anioRaw?.Trim() ?? string.Empty;
        var m = mesRaw?.Trim() ?? string.Empty;

        int? anio = null;
        if (a.Length == 4 && a.All(char.IsAsciiDigit) && int.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out var av))
            anio = av;

        int? mes = null;
        if (m.Length is >= 1 and <= 2 && m.All(char.IsAsciiDigit) && int.TryParse(m, NumberStyles.None, CultureInfo.InvariantCulture, out var mv) && mv is >= 1 and <= 12)
            mes = mv;

        return (anio is null || mes is null)
            ? new CarteraEvaluationContext(null, null)
            : new CarteraEvaluationContext(anio, mes);
    }
}

// Parámetros de política vigente. Valores por defecto = política working copy
// (§A.1 bandas, §A.2 rango proveedor, §B/§C/§D factores, §F/§G fórmula/redondeo).
// Todos INYECTABLES — no hay constantes de negocio hardcodeadas en la lógica.
public sealed record CarteraPolicyParameters(
    int     ScoreMinConfig,
    int     ScoreMaxConfig,
    int     BandaS1Hasta,
    int     BandaS2Hasta,
    int     BandaS3Hasta,
    int     BandaS4Hasta,
    decimal BandaS2Base,
    decimal BandaS3Base,
    decimal BandaS4Base,
    decimal BandaS5Base,
    decimal FactorViabilidadAlta,
    decimal FactorViabilidadMedia,
    decimal FactorRatingA,
    decimal FactorRatingB,
    decimal FactorComportamientoPeorN,
    decimal FactorComportamientoPeor1,
    decimal FactorEdadElegible,
    decimal CupoMinimo,
    decimal RedondeoUnidad,
    decimal CupoMaximo,
    int     ComportamientoVentanaMeses)
{
    public static CarteraPolicyParameters Vigente() => new(
        ScoreMinConfig:            150,
        ScoreMaxConfig:            950,
        BandaS1Hasta:              360,
        BandaS2Hasta:              450,
        BandaS3Hasta:              600,
        BandaS4Hasta:              800,
        BandaS2Base:               300_000m,
        BandaS3Base:               400_000m,
        BandaS4Base:               600_000m,
        BandaS5Base:               700_000m,
        FactorViabilidadAlta:      1.00m,
        FactorViabilidadMedia:     0.80m,
        FactorRatingA:             1.00m,
        FactorRatingB:             0.80m,
        FactorComportamientoPeorN: 1.00m,
        FactorComportamientoPeor1: 0.90m,
        FactorEdadElegible:        1.00m,
        CupoMinimo:                200_000m,
        RedondeoUnidad:            1_000m,
        CupoMaximo:                700_000m,
        ComportamientoVentanaMeses: 6);
}

// Resultado del motor. NO se persiste desde aquí.
public sealed record ResultadoDecisionCrediticia(
    string                Decision,               // CarteraDecisionCrediticia.{Aprobada,Rechazada,NoDecidible}
    decimal?              MontoAprobado,          // >0 APROBADA ; 0 RECHAZADA ; null NO_DECIDIBLE
    IReadOnlyList<string> MotivosOrdenados,       // orden 1..N (sin huecos, sin códigos duplicados) ; vacío si APROBADA
    string?              MotivoPrimario,          // null si APROBADA ; = MotivosOrdenados[0]
    bool?                SenalPosibleSuplantacion);

public static class CarteraDecisionEngine
{
    public static ResultadoDecisionCrediticia Evaluar(
        CarteraDecisionSnapshot snapshot,
        CarteraPolicyParameters p,
        CarteraEvaluationContext context)
    {
        var nd = new List<string>();                 // motivos NO_DECIDIBLE, en orden de gate
        var rej = new List<(int grupo, string codigo)>();  // rechazos determinables con grupo §H
        bool? suplantacion = null;

        decimal? baseScore = null;
        decimal? fViabilidad = null;
        decimal? fRating = null;
        decimal? fComportamiento = null;
        var fEdad = p.FactorEdadElegible;

        // ── Gate 0 — información general (conInformacion + estadoScore) ─────
        if (snapshot.ConInformacionObservado != true)
            nd.Add(CarteraMotivoDecision.InformacionInsuficiente);

        switch (snapshot.EstadoScore)
        {
            case CarteraEstadoScore.Disponible:
                break;
            case CarteraEstadoScore.SinInformacion:
            case CarteraEstadoScore.SinDato:
            case null:
                nd.Add(CarteraMotivoDecision.InformacionInsuficiente);
                break;
            default:
                nd.Add(CarteraMotivoDecision.ValorFueraPolitica);
                break;
        }

        // ── Gate 1 — estadoDocumento (§H grupo 1) ─────────────────────────
        var estadoDocCaptura = snapshot.EstadoDocumentoCaptura;
        if (!string.Equals(estadoDocCaptura, CarteraDualPathResolver.Presente, StringComparison.Ordinal))
        {
            // AUSENTE / CONFLICTO / null → sin dato usable ; señal no evaluable.
            nd.Add(CarteraMotivoDecision.InformacionInsuficiente);
        }
        else
        {
            var val = NormalizarFrase(ResolverDualPath(
                snapshot.EstadoDocumentoDatosBasicosRaw, snapshot.EstadoDocumentoInfoDemograficaRaw));

            switch (val)
            {
                case "VIGENTE":
                    suplantacion = false;
                    break;
                case "CANCELADA POR MUERTE O FALLECIDO":
                    rej.Add((1, CarteraMotivoDecision.DocNoVigente));
                    suplantacion = true;
                    break;
                case "CANCELADA":
                case "NO EXPEDIDA":
                case "EN TRAMITE":
                    rej.Add((1, CarteraMotivoDecision.DocNoVigente));
                    suplantacion = false;
                    break;
                case "":
                    nd.Add(CarteraMotivoDecision.InformacionInsuficiente);
                    break;
                default:
                    nd.Add(CarteraMotivoDecision.ValorFueraPolitica);
                    suplantacion = false;
                    break;
            }
        }

        // ── Gate 2 — tipoDocumento (§H grupo 2) ───────────────────────────
        var tipoRaw = snapshot.TipoDocumentoObservado;
        if (string.IsNullOrWhiteSpace(tipoRaw))
            nd.Add(CarteraMotivoDecision.InformacionInsuficiente);
        else if (EsCedulaCiudadania(tipoRaw))
        { /* CC → continúa */ }
        else if (EsTipoDocumentoReconocido(tipoRaw))
            rej.Add((2, CarteraMotivoDecision.TipoDocNoAceptado));
        else
            nd.Add(CarteraMotivoDecision.ValorFueraPolitica);

        // ── Gate 3 — rangoEdad (§H grupo 3) ──────────────────────────────
        var edadCaptura = snapshot.RangoEdadCaptura;
        if (!string.Equals(edadCaptura, CarteraDualPathResolver.Presente, StringComparison.Ordinal))
        {
            nd.Add(CarteraMotivoDecision.InformacionInsuficiente);
        }
        else
        {
            var val = (ResolverDualPath(snapshot.RangoEdadDatosBasicosRaw, snapshot.RangoEdadInfoDemograficaRaw) ?? string.Empty).Trim();
            switch (val)
            {
                case "18-21":
                case "22-28":
                case "29-35":
                case "36-45":
                case "46-55":
                case "56-65":
                    fEdad = p.FactorEdadElegible;
                    break;
                case "66":
                    rej.Add((3, CarteraMotivoDecision.EdadFueraPolitica));
                    break;
                case "-":
                case "":
                    nd.Add(CarteraMotivoDecision.InformacionInsuficiente);
                    break;
                default:
                    nd.Add(CarteraMotivoDecision.ValorFueraPolitica);
                    break;
            }
        }

        // ── Gate 4 — score (§H grupo 4) ──────────────────────────────────
        if (snapshot.ScoreObservado is null)
        {
            nd.Add(CarteraMotivoDecision.InformacionInsuficiente);
        }
        else
        {
            var sc = snapshot.ScoreObservado.Value;
            if (sc < p.ScoreMinConfig || sc > p.ScoreMaxConfig)
                nd.Add(CarteraMotivoDecision.ValorFueraPolitica);
            else if (sc <= p.BandaS1Hasta)
                rej.Add((4, CarteraMotivoDecision.ScoreInsuficiente));
            else if (sc <= p.BandaS2Hasta)
                baseScore = p.BandaS2Base;
            else if (sc <= p.BandaS3Hasta)
                baseScore = p.BandaS3Base;
            else if (sc <= p.BandaS4Hasta)
                baseScore = p.BandaS4Base;
            else
                baseScore = p.BandaS5Base;
        }

        // ── Gate 5 — comportamiento de pago (§H grupo 5) ─────────────────
        var comp = EvaluarComportamiento(snapshot, context, p);
        if (comp.NoDecidibleCodigo is not null)
            nd.Add(comp.NoDecidibleCodigo);
        else
        {
            foreach (var c in comp.Rechazos)
                rej.Add((5, c));
            fComportamiento = comp.Factor;
        }

        // ── Gate 6 — viabilidad (§H grupo 6) ─────────────────────────────
        var via = snapshot.ViabilidadObservada;
        if (via is null)
            nd.Add(CarteraMotivoDecision.InformacionInsuficiente);
        else if (string.Equals(via, "ALTA", StringComparison.Ordinal))
            fViabilidad = p.FactorViabilidadAlta;
        else if (string.Equals(via, "MEDIA", StringComparison.Ordinal))
            fViabilidad = p.FactorViabilidadMedia;
        else if (string.Equals(via, "BAJA", StringComparison.Ordinal))
            rej.Add((6, CarteraMotivoDecision.ViabilidadBaja));
        else
            nd.Add(CarteraMotivoDecision.ValorFueraPolitica);

        // ── Gate 7 — rating de recaudos (§H grupo 7) ─────────────────────
        var rat = snapshot.RatingRecaudosObservado;
        if (rat is null)
            nd.Add(CarteraMotivoDecision.InformacionInsuficiente);
        else if (string.Equals(rat, "A", StringComparison.Ordinal))
            fRating = p.FactorRatingA;
        else if (string.Equals(rat, "B", StringComparison.Ordinal))
            fRating = p.FactorRatingB;
        else if (rat is "C" or "D" or "N")
            rej.Add((7, CarteraMotivoDecision.RatingRecaudosInsuficiente));
        else
            nd.Add(CarteraMotivoDecision.ValorFueraPolitica);

        // ── Veredicto ────────────────────────────────────────────────────
        if (nd.Count > 0)
        {
            var motivos = DedupPreservandoOrden(
                nd.Concat(rej.OrderBy(x => x.grupo).Select(x => x.codigo)));
            return new ResultadoDecisionCrediticia(
                CarteraDecisionCrediticia.NoDecidible, null, motivos, motivos[0], suplantacion);
        }

        if (rej.Count > 0)
        {
            var motivos = DedupPreservandoOrden(rej.OrderBy(x => x.grupo).Select(x => x.codigo));
            return new ResultadoDecisionCrediticia(
                CarteraDecisionCrediticia.Rechazada, 0m, motivos, motivos[0], suplantacion);
        }

        // Todos los gates pasaron con factor → fórmula (§F/§G).
        var raw = baseScore!.Value
                * fViabilidad!.Value
                * fRating!.Value
                * fComportamiento!.Value
                * fEdad;

        if (raw < p.CupoMinimo)
        {
            var motivos = new[] { CarteraMotivoDecision.ResultadoScoreInsuficiente };
            return new ResultadoDecisionCrediticia(
                CarteraDecisionCrediticia.Rechazada, 0m, motivos, motivos[0], suplantacion);
        }

        var redondeado = Math.Ceiling(raw / p.RedondeoUnidad) * p.RedondeoUnidad;
        var monto = Math.Min(redondeado, p.CupoMaximo);

        return new ResultadoDecisionCrediticia(
            CarteraDecisionCrediticia.Aprobada, monto, Array.Empty<string>(), null, suplantacion);
    }

    // ── Comportamiento de pago (§D / §D.2) ───────────────────────────────
    private sealed record ComportamientoResultado(
        string? NoDecidibleCodigo, IReadOnlyList<string> Rechazos, decimal? Factor);

    private static readonly ComportamientoResultado NdInfo =
        new(CarteraMotivoDecision.InformacionInsuficiente, Array.Empty<string>(), null);
    private static readonly ComportamientoResultado NdValor =
        new(CarteraMotivoDecision.ValorFueraPolitica, Array.Empty<string>(), null);

    private static ComportamientoResultado EvaluarComportamiento(
        CarteraDecisionSnapshot snapshot, CarteraEvaluationContext ctx, CarteraPolicyParameters p)
    {
        if (snapshot.ComportamientoVectorCount is null)          // bloque comportamientoPago ausente
            return NdInfo;
        if (ctx.QueryAnio is null || ctx.QueryMes is null)       // sin autoridad temporal → no se puede determinar M-1
            return NdInfo;

        var qa = ctx.QueryAnio.Value;
        var qm = ctx.QueryMes.Value;

        List<CarteraComportamientoVectorItemRaw> items;
        try
        {
            items = string.IsNullOrEmpty(snapshot.ComportamientoVectorJson)
                ? new List<CarteraComportamientoVectorItemRaw>()
                : JsonSerializer.Deserialize<List<CarteraComportamientoVectorItemRaw>>(snapshot.ComportamientoVectorJson!)
                  ?? new List<CarteraComportamientoVectorItemRaw>();
        }
        catch (JsonException)
        {
            return NdInfo;
        }

        var mapa = new Dictionary<(int a, int m), string?>();
        foreach (var it in items)
        {
            if (!TryParseAnioMes(it?.AnioMes, out var y, out var mo))
                return NdInfo;                                   // §D.2.6 — anioMes inválido invalida el vector

            if (y > qa || (y == qa && mo > qm))
                return NdInfo;                                   // §D.2.7 — mes futuro

            var comp = it!.Comportamiento;
            if (mapa.TryGetValue((y, mo), out var prev))
            {
                var pt = (prev ?? string.Empty).Trim();
                var ct = (comp ?? string.Empty).Trim();
                if (!string.Equals(pt, ct, StringComparison.Ordinal))
                    return NdInfo;                               // §D.2.5 — duplicado en conflicto
                // mismo mes + mismo comportamiento → deduplicar (se conserva el primero)
            }
            else
            {
                mapa[(y, mo)] = comp;
            }
        }

        // Ventana objetivo: `ventanaMeses` meses consecutivos terminando en M-1.
        var (u1a, u1m) = MesAnterior(qa, qm);                    // M-1
        var ventana = new List<(int a, int m)>();
        var cur = (a: u1a, m: u1m);
        for (var i = 0; i < p.ComportamientoVentanaMeses; i++)
        {
            ventana.Insert(0, cur);
            cur = MesAnterior(cur.a, cur.m);
        }

        foreach (var w in ventana)
            if (!mapa.ContainsKey(w))
                return NdInfo;                                   // §D.2.3 — mes requerido faltante / <6 / hueco

        // Clasificar los `ventanaMeses` meses. severidad: N=0, 1..6, C=7, D=8. NO-DATA = -1.
        var sev = new int[p.ComportamientoVentanaMeses];
        for (var i = 0; i < ventana.Count; i++)
        {
            if (!ClasificarComportamiento(mapa[ventana[i]], out var s))
                return NdValor;                                  // código desconocido en la ventana
            sev[i] = s;
        }

        var ultimoSev = sev[^1];                                 // M-1
        var rechazos = new List<string>();

        // Paso 1 — último mes requerido en mora (1..6/C/D).
        if (ultimoSev >= 1)
            rechazos.Add(CarteraMotivoDecision.ComportamientoPagoUltimoMesNoAlDia);

        // Paso 2 — peor comportamiento CON DATA en la ventana.
        var conData = sev.Where(s => s >= 0).ToList();
        var peorSev = conData.Count == 0 ? 0 : conData.Max();

        // INSUFICIENTE si (último mes ∈ {2..6,C,D}) o (peor con data ∈ {2..6,C,D}).
        if (ultimoSev >= 2 || peorSev >= 2)
        {
            if (!rechazos.Contains(CarteraMotivoDecision.ComportamientoPagoInsuficiente))
                rechazos.Add(CarteraMotivoDecision.ComportamientoPagoInsuficiente);
        }

        decimal? factor = null;
        if (rechazos.Count == 0)
        {
            // Sin rechazo: factor por el peor con data (o 1.00 si todos son no-data / peor N).
            factor = peorSev switch
            {
                <= 0 => p.FactorComportamientoPeorN,
                1    => p.FactorComportamientoPeor1,
                _    => p.FactorComportamientoPeorN, // inalcanzable (>=2 habría producido rechazo)
            };
        }

        return new ComportamientoResultado(null, rechazos, factor);
    }

    private static (int a, int m) MesAnterior(int a, int m) => m == 1 ? (a - 1, 12) : (a, m - 1);

    // Gramática aceptada EXCLUSIVAMENTE: YYYY-M / YYYY-MM. Año exactamente 4
    // dígitos ASCII ; mes 1..12. Sin trim, sin fechas completas, sin slashes,
    // sin nombres, sin espacios, sin interpretación flexible.
    private static bool TryParseAnioMes(string? s, out int anio, out int mes)
    {
        anio = 0;
        mes = 0;
        if (s is null)
            return false;

        var dash = s.IndexOf('-');
        if (dash != 4 || dash == s.Length - 1)
            return false;

        var yPart = s[..4];
        var mPart = s[5..];

        if (yPart.Length != 4 || !yPart.All(char.IsAsciiDigit))
            return false;
        if (mPart.Length is < 1 or > 2 || !mPart.All(char.IsAsciiDigit))
            return false;

        anio = int.Parse(yPart, CultureInfo.InvariantCulture);
        mes = int.Parse(mPart, CultureInfo.InvariantCulture);
        return mes is >= 1 and <= 12;
    }

    // severidad: N=0 ; 1..6 ; C=7 ; D=8 ; NO-DATA ("", "-", " ") = -1.
    // Devuelve false si el valor no es reconocible (código desconocido).
    private static bool ClasificarComportamiento(string? raw, out int severidad)
    {
        var t = (raw ?? string.Empty).Trim();
        if (t.Length == 0 || t == "-")
        {
            severidad = -1;
            return true;
        }

        severidad = t switch
        {
            "N" => 0,
            "1" => 1,
            "2" => 2,
            "3" => 3,
            "4" => 4,
            "5" => 5,
            "6" => 6,
            "C" => 7,
            "D" => 8,
            _   => int.MinValue,
        };
        if (severidad == int.MinValue)
        {
            severidad = -1;
            return false;
        }
        return true;
    }

    // ── Documento / edad — normalización ────────────────────────────────
    private static string? ResolverDualPath(string? rawDatosBasicos, string? rawInfoDemografica)
    {
        var db = (rawDatosBasicos ?? string.Empty).Trim();
        return db.Length > 0 ? rawDatosBasicos : rawInfoDemografica;
    }

    private static string NormalizarFrase(string? raw)
        => SinAcentos((raw ?? string.Empty).Trim()).ToUpperInvariant();

    private static string SinAcentos(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static readonly HashSet<string> TiposDocumentoReconocidos = new(StringComparer.Ordinal)
    {
        "1", "CC", "CEDULA DE CIUDADANIA", "CEDULA DE CIUDADANIA Y NUIP",
        "2", "NIT", "NUMERO DE IDENTIFICACION TRIBUTARIA",
        "3", "PJE", "PERSONA JURIDICA DEL EXTRANJERO",
        "4", "CE", "CEDULA DE EXTRANJERIA",
        "5", "PAS", "PASAPORTE",
        "6", "PPT/CD", "PPT", "CD",
        "PERMISO POR PROTECCION TEMPORAL/ CARNE DIPLOMATICO",
        "PERMISO POR PROTECCION TEMPORAL/CARNE DIPLOMATICO",
        "7", "TI", "TARJETA DE IDENTIDAD",
        "8", "DNI", "DOCUMENTO NACIONAL DE IDENTIDAD",
        "9", "PEP", "PERMISO ESPECIAL DE PERMANENCIA",
    };

    private static readonly HashSet<string> CedulaCiudadaniaRepresentaciones = new(StringComparer.Ordinal)
    {
        "1", "CC", "CEDULA DE CIUDADANIA", "CEDULA DE CIUDADANIA Y NUIP",
    };

    private static bool EsCedulaCiudadania(string? raw)
        => CedulaCiudadaniaRepresentaciones.Contains(NormalizarFrase(raw));

    private static bool EsTipoDocumentoReconocido(string? raw)
        => TiposDocumentoReconocidos.Contains(NormalizarFrase(raw));

    private static List<string> DedupPreservandoOrden(IEnumerable<string> src)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var res = new List<string>();
        foreach (var s in src)
            if (seen.Add(s))
                res.Add(s);
        return res;
    }
}
