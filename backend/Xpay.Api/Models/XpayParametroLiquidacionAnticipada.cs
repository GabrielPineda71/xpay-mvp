namespace Xpay.Api.Models;

public class XpayParametroLiquidacionAnticipada
{
    public long     IdParametro          { get; set; }
    public long?    IdComercioAliado     { get; set; }
    public int      DiasFaltantes        { get; set; }
    public decimal  PorcentajeDescuento  { get; set; }
    public bool     AplicaIva            { get; set; }
    public decimal? PorcentajeIva        { get; set; }
    public string   Estado               { get; set; } = "ACTIVO";
    public DateTime CreatedAt            { get; set; }
    public DateTime? UpdatedAt           { get; set; }
    public long?    CreatedByUsuario     { get; set; }
    public long?    UpdatedByUsuario     { get; set; }
}
