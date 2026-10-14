using System.Text.Json;

namespace Xpay.Api.Common;

// XPAY-702 - propuesta de resolucion manual para solicitudes NO_DECIDIBLE.
//
// Principio: la revision manual NO salta reglas de elegibilidad ni fabrica
// datos que el proveedor no entrego. Clasificacion de causas NO_DECIDIBLE:
//
//   B (solo RECHAZAR): sin informacion / sin score utilizable; documento
//     ausente, en conflicto o desconocido; tipo de documento ausente o
//     desconocido; edad ausente, desconocida o 66+ (no hay factor aprobado);
//     score ausente o fuera de rango; bloque de comportamiento ausente, sin
//     mes de consulta valido, anioMes invalido, mes futuro, duplicado en
//     conflicto o codigo desconocido; viabilidad o rating ausentes o
//     desconocidos.
//
//   A (unico caso aprobable, regla RM1): resultado automatico NO_DECIDIBLE
//     con motivo unico INFORMACION_INSUFICIENTE, cuya causa es que la ventana
//     de 6 meses consecutivos terminada en el ultimo mes reportado esta
//     INCOMPLETA, y todos los meses reales presentes en esa ventana tienen
//     codigo reconocido sin mora (al menos uno N). Todas las demas
//     condiciones del motor deben pasar con los datos reales.
//
// Monto RM1: CarteraDecisionEngine.Evaluar sobre el snapshot REAL, sustituyendo
// solo el vector por una ventana tecnica cuyo peor codigo es "1", lo que aplica
// exactamente el factor de politica FactorComportamientoPeor1 (0,90). La
// ventana tecnica nunca se persiste ni se expone. Si el motor no devuelve
// APROBADA (p. ej. cupo bajo el minimo), solo se permite RECHAZAR.
public sealed record PropuestaRevisionManual(
    bool     PermiteAprobar,
    decimal? MontoPropuesto,
    string   Regla,
    string?  CausaNoAprobable);

public static class CarteraRevisionManualPolicy
{
    public const string ReglaVentanaIncompleta = "RM1_VENTANA_INCOMPLETA_FACTOR_PEOR1";
    public const string ReglaSoloRechazo       = "RM0_SOLO_RECHAZO";

    public const string CausaNoEsNoDecidible   = "NO_ES_NO_DECIDIBLE";
    public const string CausaMotivosNoResolubles = "MOTIVOS_NO_RESOLUBLES";
    public const string CausaComportamientoNoResoluble = "COMPORTAMIENTO_NO_RESOLUBLE";
    public const string CausaMotorNoAprueba    = "MOTOR_NO_APRUEBA_CON_DATOS_REALES";

    public static PropuestaRevisionManual Proponer(
        CarteraDecisionSnapshot snapshot, CarteraPolicyParameters p, CarteraEvaluationContext ctx)
    {
        var auto = CarteraDecisionEngine.Evaluar(snapshot, p, ctx);
        if (auto.Decision != CarteraDecisionCrediticia.NoDecidible)
            return SoloRechazo(CausaNoEsNoDecidible);

        var motivos = auto.MotivosOrdenados;
        if (motivos.Count != 1 || motivos[0] != CarteraMotivoDecision.InformacionInsuficiente)
            return SoloRechazo(CausaMotivosNoResolubles);

        if (!EsVentanaIncompletaSinMora(snapshot, ctx, p.ComportamientoVentanaMeses))
            return SoloRechazo(CausaComportamientoNoResoluble);

        var tecnico = snapshot with
        {
            ComportamientoVectorJson  = VentanaFactorPeor1(ctx, p.ComportamientoVentanaMeses),
            ComportamientoVectorCount = p.ComportamientoVentanaMeses,
        };

        var r = CarteraDecisionEngine.Evaluar(tecnico, p, ctx);
        if (r.Decision != CarteraDecisionCrediticia.Aprobada || r.MontoAprobado is not > 0m)
            return SoloRechazo(CausaMotorNoAprueba);

        return new PropuestaRevisionManual(true, r.MontoAprobado, ReglaVentanaIncompleta, null);
    }

    private static PropuestaRevisionManual SoloRechazo(string causa)
        => new(false, null, ReglaSoloRechazo, causa);

    // Ventana tecnica (no persistida): `meses` periodos consecutivos terminados
    // en M-1 de la consulta, todos "N" salvo el mas antiguo "1". Hace que el
    // motor aplique FactorComportamientoPeor1 sin rechazo por ultimo mes.
    private static string VentanaFactorPeor1(CarteraEvaluationContext ctx, int meses)
    {
        var items = new List<CarteraComportamientoVectorItemRaw>();
        var cur = CarteraDecisionEngine.MesAnterior(ctx.QueryAnio!.Value, ctx.QueryMes!.Value);
        for (var i = 0; i < meses; i++)
        {
            var comp = i == meses - 1 ? "1" : "N";
            items.Insert(0, new CarteraComportamientoVectorItemRaw($"{cur.a}-{cur.m}", comp));
            cur = CarteraDecisionEngine.MesAnterior(cur.a, cur.m);
        }
        return JsonSerializer.Serialize(items);
    }

    // Caso A sobre el vector REAL: integridad completa (misma gramatica y
    // reglas que el motor), ventana terminada en el ultimo mes reportado
    // INCOMPLETA, meses presentes de la ventana sin mora y al menos uno N.
    internal static bool EsVentanaIncompletaSinMora(
        CarteraDecisionSnapshot s, CarteraEvaluationContext ctx, int meses)
    {
        if (s.ComportamientoVectorCount is null || ctx.QueryAnio is null || ctx.QueryMes is null)
            return false;
        if (string.IsNullOrEmpty(s.ComportamientoVectorJson))
            return false;

        List<CarteraComportamientoVectorItemRaw>? items;
        try
        {
            items = JsonSerializer.Deserialize<List<CarteraComportamientoVectorItemRaw>>(
                s.ComportamientoVectorJson);
        }
        catch (JsonException)
        {
            return false;
        }
        if (items is null || items.Count == 0)
            return false;

        var qa = ctx.QueryAnio.Value;
        var qm = ctx.QueryMes.Value;
        var mapa = new Dictionary<(int a, int m), string?>();
        foreach (var it in items)
        {
            if (it is null || !CarteraDecisionEngine.TryParseAnioMes(it.AnioMes, out var y, out var mo))
                return false;
            if (y > qa || (y == qa && mo > qm))
                return false;

            var actual = (it.Comportamiento ?? string.Empty).Trim();
            if (mapa.TryGetValue((y, mo), out var previo))
            {
                var anterior = (previo ?? string.Empty).Trim();
                if (!string.Equals(anterior, actual, StringComparison.Ordinal))
                    return false;
            }
            else
            {
                mapa[(y, mo)] = it.Comportamiento;
            }
        }

        var cur = mapa.Keys.Max();
        var faltantes = 0;
        var conN = 0;
        for (var i = 0; i < meses; i++)
        {
            if (mapa.TryGetValue(cur, out var comp))
            {
                if (!CarteraDecisionEngine.ClasificarComportamiento(comp, out var sev))
                    return false;
                if (sev >= 1)
                    return false;
                if (sev == 0)
                    conN++;
            }
            else
            {
                faltantes++;
            }
            cur = CarteraDecisionEngine.MesAnterior(cur.a, cur.m);
        }

        return faltantes > 0 && conN > 0;
    }
}
