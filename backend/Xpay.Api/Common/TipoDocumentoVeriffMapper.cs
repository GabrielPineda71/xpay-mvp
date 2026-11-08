namespace Xpay.Api.Common;

// Diseño A — mapeo cerrado de document.type + document.country de Veriff al
// TipoDocumento legal de XPAY (personas.tipo_documento). Puro: sin BD, sin
// logger, sin reloj. Cualquier combinación que no esté listada aquí NO se
// mapea — nunca se adivina un tipo documental.
//
// Contrato Veriff (decision webhook): document.type ∈ {PASSPORT, ID_CARD,
// RESIDENCE_PERMIT, DRIVERS_LICENSE, VISA, OTHER}; document.country ISO 3166
// alfa-2; document.number solo [a-zA-Z0-9].
//
// RESIDENCE_PERMIT + CO → CE queda FUERA a propósito hasta confirmar con
// Veriff cómo clasifica la cédula de extranjería colombiana.
internal static class TipoDocumentoVeriffMapper
{
    // personas.numero_documento es VARCHAR(30) (001/032). Más largo → se
    // rechaza, nunca se trunca.
    internal const int LongitudMaximaNumero = 30;

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

        var numero = (numeroVeriff ?? string.Empty).Trim().ToUpperInvariant();

        var valido = numero.Length > 0
            && numero.Length <= LongitudMaximaNumero
            && (tipoXpay == "CC"
                ? numero.All(char.IsAsciiDigit)
                : numero.All(char.IsAsciiLetterOrDigit));

        return valido
            ? new DocumentoLegal(Resultado.Mapeado, tipoXpay, numero)
            : new DocumentoLegal(Resultado.NumeroInvalido, null, null);
    }
}
