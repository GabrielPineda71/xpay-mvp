namespace Xpay.Api.Models;

public class WalletLiquidacionRecaudoComercio
{
    public long     IdLiquidacion        { get; set; }
    public long     IdUnidadNegocio      { get; set; } = 1;
    public long     IdComercio           { get; set; }
    public long?    IdComercioAliado     { get; set; }
    public long?    IdTienda             { get; set; }
    public long     IdUsuarioAdmin       { get; set; }
    public long?    IdTransaccionLedger  { get; set; }
    public string   MetodoLiquidacion    { get; set; } = string.Empty;
    public decimal  ValorTotal           { get; set; }
    public int      CantidadRecargas     { get; set; }
    public string   Estado               { get; set; } = "APLICADA";
    public string?  ReferenciaExterna    { get; set; }
    public string?  Observaciones        { get; set; }
    public DateTime FechaLiquidacion     { get; set; }
    public DateTime CreatedAt            { get; set; }
}
