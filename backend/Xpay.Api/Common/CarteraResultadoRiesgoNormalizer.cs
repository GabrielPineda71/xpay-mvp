using System.Globalization;

namespace Xpay.Api.Common;

// M2.4a — vocabulario TÉCNICO de disponibilidad del score observado (columna
// `estado_score VARCHAR(30)` de `cartera_solicitudes_cupo`). NO es un
// veredicto crediticio ni una categoría de riesgo: sólo describe si el
// proveedor entregó un score numérico utilizable.
public static class CarteraEstadoScore
{
    // conInformacion == true y score_raw parsea a un entero no negativo Int32.
    public const string Disponible = "DISPONIBLE";

    // conInformacion != true (false o ausente): el proveedor indicó que NO hay
    // información de riesgo. Todas las observaciones de riesgo quedan NULL.
    public const string SinInformacion = "SIN_INFORMACION";

    // conInformacion == true pero score_raw NO es un entero no negativo
    // ("-", "", ausente, o cualquier no-numérico). El resto de observaciones
    // SÍ pueden tener valor.
    public const string SinDato = "SIN_DATO";
}

// M2.4a — resultado de normalizar los 6 crudos persistidos por M2.3b1 a
// observaciones tipadas de la solicitud. NO se expone en ningún endpoint —
// es un tipo interno del primitivo de consumo.
public sealed record ResultadoRiesgoNormalizado(
    bool?    ConInformacion,
    int?     Score,
    string   EstadoScore,
    string?  Viabilidad,
    string?  RatingRecaudos,
    decimal? MontoSugerido,
    int?     AlertasCount);

// M2.4a — corrupción durable detectada al normalizar un intento MiDecisor
// FINALIZADO con resultado útil: un crudo tiene una forma que la clasificación
// de M2.3b1 no puede haber producido para un intento útil (viabilidad/rating
// fuera del enum documentado, score negativo bien formado o de sólo dígitos
// que desborda Int32, monto negativo / con separadores / decimal textual / que
// excede DECIMAL(18,2)). Fail-closed: se lanza, NO se persiste, sin retry.
public sealed class CarteraConsumoResultadoInvarianteException(string message) : Exception(message);

// M2.4a — normalizador PURO (sin SQL, sin estado, determinista) de los 6
// crudos de MiDecisor. Reglas cerradas en el diseño 109 (PASO 7): no se
// reinterpretan aquí. `conInformacion` tiene precedencia absoluta (AJUSTE #1:
// nunca se deduce SIN_INFORMACION desde el score).
public static class CarteraResultadoRiesgoNormalizer
{
    private static readonly string[] ViabilidadesCanonicas = { "ALTA", "MEDIA", "BAJA" };
    private static readonly string[] RatingsCanonicos       = { "A", "B", "C", "D", "N" };

    // DECIMAL(18,2): hasta 16 dígitos enteros → 9_999_999_999_999_999.99.
    private const decimal MontoMaxDecimal18_2 = 9_999_999_999_999_999.99m;

    public static ResultadoRiesgoNormalizado Normalizar(
        bool? conInformacion,
        string? scoreRaw,
        string? viabilidadRaw,
        string? ratingRecaudosRaw,
        string? montoSugeridoRaw,
        int? alertasCount)
    {
        // ── Precedencia absoluta de conInformacion ────────────────────────
        // false o null → todo el snapshot de riesgo se anula. NO se inspeccionan
        // los demás crudos. NO se lanza por null.
        if (conInformacion != true)
        {
            return new ResultadoRiesgoNormalizado(
                ConInformacion: conInformacion,
                Score: null,
                EstadoScore: CarteraEstadoScore.SinInformacion,
                Viabilidad: null,
                RatingRecaudos: null,
                MontoSugerido: null,
                AlertasCount: null);
        }

        var (score, estadoScore) = NormalizarScore(scoreRaw);

        return new ResultadoRiesgoNormalizado(
            ConInformacion: true,
            Score: score,
            EstadoScore: estadoScore,
            Viabilidad: NormalizarViabilidad(viabilidadRaw),
            RatingRecaudos: NormalizarRating(ratingRecaudosRaw),
            MontoSugerido: NormalizarMonto(montoSugeridoRaw),
            AlertasCount: alertasCount);
    }

    private static (int? score, string estadoScore) NormalizarScore(string? raw)
    {
        var s = raw?.Trim();

        // "-", "", null → sin dato (el proveedor informó pero no entregó score).
        if (string.IsNullOrEmpty(s) || s == "-")
            return (null, CarteraEstadoScore.SinDato);

        if (EsSoloDigitos(s))
        {
            if (int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                return (n, CarteraEstadoScore.Disponible);

            // Cadena de sólo dígitos que desborda Int32 → contradice el dominio
            // esperado del score (fail-closed).
            throw new CarteraConsumoResultadoInvarianteException(
                "score MiDecisor es una cadena de sólo dígitos que desborda Int32.");
        }

        // Número negativo bien formado ("-5") → corrupción (fail-closed).
        if (long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var conSigno)
            && conSigno < 0)
        {
            throw new CarteraConsumoResultadoInvarianteException(
                "score MiDecisor es un número negativo bien formado.");
        }

        // Cualquier otro no-numérico ("abc", "8.5", "1e3", ...) → sin dato.
        return (null, CarteraEstadoScore.SinDato);
    }

    private static string? NormalizarViabilidad(string? raw)
    {
        var s = raw?.Trim();
        if (string.IsNullOrEmpty(s))
            return null;

        foreach (var canon in ViabilidadesCanonicas)
            if (string.Equals(s, canon, StringComparison.Ordinal))
                return canon;

        throw new CarteraConsumoResultadoInvarianteException(
            "viabilidad MiDecisor fuera del enum documentado (ALTA/MEDIA/BAJA).");
    }

    private static string? NormalizarRating(string? raw)
    {
        var s = raw?.Trim();
        if (string.IsNullOrEmpty(s))
            return null;

        foreach (var canon in RatingsCanonicos)
            if (string.Equals(s, canon, StringComparison.Ordinal))
                return canon;

        throw new CarteraConsumoResultadoInvarianteException(
            "ratingRecaudos MiDecisor fuera del enum documentado (A/B/C/D/N).");
    }

    private static decimal? NormalizarMonto(string? raw)
    {
        var s = raw?.Trim();

        // "-", "", null → sin sugerencia.
        if (string.IsNullOrEmpty(s) || s == "-")
            return null;

        // "0" (contrato: "0" = sin sugerencia). Cualquier signo/separador/decimal
        // hace que NO sea sólo dígitos → invariante.
        if (!EsSoloDigitos(s))
            throw new CarteraConsumoResultadoInvarianteException(
                "montoSugerido MiDecisor no es una cadena de sólo dígitos.");

        if (!decimal.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var monto))
            throw new CarteraConsumoResultadoInvarianteException(
                "montoSugerido MiDecisor desborda el rango decimal.");

        // "0" / "00" / ... = sin sugerencia → NULL (NO 0.00m).
        if (monto == 0m)
            return null;

        if (monto > MontoMaxDecimal18_2)
            throw new CarteraConsumoResultadoInvarianteException(
                "montoSugerido MiDecisor excede la precisión de DECIMAL(18,2).");

        return monto;
    }

    private static bool EsSoloDigitos(string s)
    {
        if (s.Length == 0)
            return false;
        foreach (var c in s)
            if (c is < '0' or > '9')
                return false;
        return true;
    }
}
