using System.Text.RegularExpressions;

namespace Xpay.Api.Common;

// Diseño A — mapeo cerrado de document.type + document.country de Veriff al
// TipoDocumento legal de XPAY (personas.tipo_documento). Puro: sin BD, sin
// logger, sin reloj. Cualquier combinación que no esté listada aquí NO se
// mapea — nunca se adivina un tipo documental.
//
// Contrato Veriff (decision webhook): document.type ∈ {PASSPORT, ID_CARD,
// RESIDENCE_PERMIT, DRIVERS_LICENSE, VISA, OTHER}; document.country ISO 3166
// alfa-2. Para CC se admiten además puntos de miles ASCII estrictos.
//
// RESIDENCE_PERMIT + CO → CE queda FUERA a propósito hasta confirmar con
// Veriff cómo clasifica la cédula de extranjería colombiana.
internal static class TipoDocumentoVeriffMapper
{
    // personas.numero_documento es VARCHAR(30) (001/032). Más largo → se
    // rechaza, nunca se trunca.
    internal const int LongitudMaximaNumero = 30;
    internal const int LongitudMaximaCruda = 64;
    private static readonly Regex PuntosDeMiles = new(@"\A[0-9]{1,3}(?:\.[0-9]{3})+\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    // Comparable de identidad, no autorización para mapear un tipo/país.
    // Únicamente quita puntos cuando toda la cadena es un número ASCII con
    // grupos exactos de miles; jamás elimina separadores arbitrarios.
    internal static string? NumeroComparable(string? raw)
    {
        if (raw is null || raw.Length > LongitudMaximaCruda) return null;
        var numero = raw.Trim();
        if (numero.Length is > 0 and <= LongitudMaximaNumero && numero.All(char.IsAsciiLetterOrDigit))
            return numero.ToUpperInvariant();
        if (!PuntosDeMiles.IsMatch(numero)) return null;
        var canonico = numero.Replace(".", "", StringComparison.Ordinal);
        return canonico.Length <= LongitudMaximaNumero ? canonico : null;
    }

    internal enum Resultado
    {
        Mapeado,
        TipoNoMapeable,
        NumeroInvalido,
    }

    internal readonly record struct DocumentoLegal(Resultado Resultado, string? TipoDocumento, string? NumeroDocumento);

    internal static DocumentoLegal Mapear(string? tipoVeriff, string? paisVeriff, string? numeroVeriff)
    {
        var tipo = (tipoVeriff ?? string.Empty).Trim().ToUpperInvariant();
        var pais = (paisVeriff ?? string.Empty).Trim().ToUpperInvariant();

        string? tipoXpay = (tipo, pais) switch
        {
            ("ID_CARD", "CO")                    => "CC",
            ("PASSPORT", { Length: 2 })          => "PASAPORTE",
            _                                    => null,
        };

        if (tipoXpay is null)
            return new DocumentoLegal(Resultado.TipoNoMapeable, null, null);

        var numero = tipoXpay == "CC"
            ? NumeroComparable(numeroVeriff)
            : numeroVeriff is { Length: <= LongitudMaximaCruda }
                ? numeroVeriff.Trim() : null;
        var valido = numero is { Length: > 0 and <= LongitudMaximaNumero }
            && (tipoXpay == "CC" ? numero.All(char.IsAsciiDigit) : numero.All(char.IsAsciiLetterOrDigit));

        return valido
            ? new DocumentoLegal(Resultado.Mapeado, tipoXpay, numero!.ToUpperInvariant())
            : new DocumentoLegal(Resultado.NumeroInvalido, null, null);
    }
}
