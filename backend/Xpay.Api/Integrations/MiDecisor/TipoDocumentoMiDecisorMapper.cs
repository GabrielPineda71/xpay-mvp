namespace Xpay.Api.Integrations.MiDecisor;

// Mapeo PURO de tipo de documento XPAY → código string de MiDecisor para
// Persona Natural. Sólo equivalencias INEQUÍVOCAMENTE documentadas en el
// Swagger de MiDecisor (schema `ConsultaRequest.tipoIdentificacion`).
//
// PN documentados:  CC→"1"  CE→"4"  PAS→"5"  CD→"6"  TI→"7"  DNI→"8"  PEP→"9"
// (PEP aquí = "Permiso Especial de Permanencia" según el Swagger de MiDecisor,
//  NO "Persona Expuesta Políticamente".)
//
// - No lanza ante un tipo no soportado: devuelve false (patrón Try). La
//   decisión de qué hacer con un tipo no mapeable (rechazar, pedir dato,
//   etc.) es de producto y vive en M2/M3, no aquí.
// - No incluye NIT ("2") ni PJE ("3"): son Persona Jurídica, fuera del flujo PN.
// - Normalización conservadora: trim + mayúsculas invariantes. No adivina
//   equivalencias (p. ej. no asume "CÉDULA" → CC).
public static class TipoDocumentoMiDecisorMapper
{
    // Claves ya normalizadas (trim + upper invariante).
    private static readonly IReadOnlyDictionary<string, string> PersonaNatural =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CC"]  = "1",
            ["CE"]  = "4",
            ["PAS"] = "5",
            ["CD"]  = "6",
            ["TI"]  = "7",
            ["DNI"] = "8",
            ["PEP"] = "9",
        };

    // Devuelve true y el código MiDecisor si el tipo XPAY es un documento de
    // Persona Natural soportado; false en cualquier otro caso (null, vacío,
    // tipo desconocido, o tipo de Persona Jurídica).
    public static bool TryMapPersonaNatural(string? tipoDocumentoXpay, out string codigoMiDecisor)
    {
        codigoMiDecisor = string.Empty;
        if (string.IsNullOrWhiteSpace(tipoDocumentoXpay))
            return false;

        var clave = tipoDocumentoXpay.Trim().ToUpperInvariant();
        return PersonaNatural.TryGetValue(clave, out codigoMiDecisor!);
    }

    // Sólo lectura, para tests/diagnóstico. No expone nada sensible.
    public static IReadOnlyDictionary<string, string> MapeosPersonaNatural => PersonaNatural;
}
