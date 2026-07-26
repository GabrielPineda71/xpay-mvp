namespace Xpay.Api.Models;

public class WalletCierreDiarioComercio
{
    public long     IdCierre                 { get; set; }
    public long     IdUnidadNegocio          { get; set; }
    public long     IdComercio               { get; set; }
    public long?    IdComercioAliado         { get; set; }
    public DateOnly FechaCierre              { get; set; }
    public DateTime FechaHoraCorteUtc        { get; set; }
    public string   CodigoUnico              { get; set; } = string.Empty;
    public int      CantidadRecargas         { get; set; }
    public decimal  ValorTotalRecaudado      { get; set; }
    public decimal  ValorLiquidadoAlGenerar  { get; set; }
    public decimal  ValorPendienteAlGenerar  { get; set; }
    public string   Estado                   { get; set; } = "GENERADO";
    public long     GeneradoPorUsuario       { get; set; }
    public DateTime FechaGeneracion          { get; set; }
    public long?    RevisadoPorUsuario       { get; set; }
    public DateTime? FechaRevision           { get; set; }
    public long?    CerradoPorUsuario        { get; set; }
    public DateTime? FechaCerrado            { get; set; }
    public string?  ObservacionesAdmin       { get; set; }
    public DateTime CreatedAt                { get; set; }
}
