namespace Xpay.Api.Common;

// M2.4a (extensión de captura P0, diseño 175/176/177) — clasificación
// ESTRUCTURAL (NO política crediticia) del par de rutas del proveedor
// `validacion.datosBasicos.<campo>` vs `validacion.informacionDemografica.<campo>`
// para estadoDocumento y rangoEdad.
public enum CarteraDualPathCaptura
{
    // Ninguna ruta contribuye un valor (ambas null o vacías tras Trim).
    Ausente,
    // Exactamente una ruta contribuye, o ambas contribuyen y son iguales tras Trim.
    Presente,
    // Ambas rutas contribuyen y sus Trims difieren (comparación ordinal).
    Conflicto,
}

// Helper PURO y determinista. NO devuelve un valor crediticio. NO modifica los
// raw. Cada raw original se persiste sin cambios en su propia columna; este
// helper sólo decide la clasificación. Comparación ORDINAL tras Trim (no
// dependiente de cultura).
public static class CarteraDualPathResolver
{
    public const string Presente  = "PRESENTE";
    public const string Ausente   = "AUSENTE";
    public const string Conflicto = "CONFLICTO";

    private static bool Contribuye(string? raw) => raw is not null && raw.Trim().Length > 0;

    public static CarteraDualPathCaptura Resolver(string? rawDatosBasicos, string? rawInfoDemografica)
    {
        var a = Contribuye(rawDatosBasicos);
        var b = Contribuye(rawInfoDemografica);

        if (!a && !b)
            return CarteraDualPathCaptura.Ausente;

        if (a && b)
        {
            return string.Equals(
                       rawDatosBasicos!.Trim(),
                       rawInfoDemografica!.Trim(),
                       StringComparison.Ordinal)
                ? CarteraDualPathCaptura.Presente
                : CarteraDualPathCaptura.Conflicto;
        }

        return CarteraDualPathCaptura.Presente;
    }

    public static string ResolverTexto(string? rawDatosBasicos, string? rawInfoDemografica)
        => Resolver(rawDatosBasicos, rawInfoDemografica) switch
        {
            CarteraDualPathCaptura.Presente  => Presente,
            CarteraDualPathCaptura.Conflicto => Conflicto,
            _                                => Ausente,
        };
}
