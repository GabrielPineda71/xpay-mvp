namespace Xpay.Api.Common;

// Único punto de conversión UTC → hora Colombia (America/Bogota, UTC-5 fijo,
// sin horario de verano) para texto generado en el backend. Todo timestamp
// estructurado (FechaRecarga, FechaLiquidacion, etc.) se persiste y expone en
// UTC — el frontend lo convierte a hora Colombia con fmtDate. Este helper
// existe solo para los pocos casos donde el backend arma un mensaje de texto
// libre (ComprobanteTexto, NotaCorte) que necesita mostrar la hora ya
// convertida dentro del string. No usar AddHours(-5) suelto en ningún otro
// lugar — siempre a través de aquí.
public static class ColombiaTime
{
    public const int OffsetHoras = -5;

    public static DateTime DesdeUtc(DateTime utc) => utc.AddHours(OffsetHoras);

    public static string FormatoTexto(DateTime utc) => $"{DesdeUtc(utc):yyyy-MM-dd HH:mm} hora Colombia";
}
