namespace Xpay.Api.Models;

public class LiquidacionComercio
{
    public long IdLiquidacion { get; set; }
    public long IdUnidadNegocio { get; set; }
    public long IdComercio { get; set; }
    public long IdWalletComercio { get; set; }
    public long? IdTransaccionLedger { get; set; }
    public decimal ValorBruto { get; set; }
    public decimal ValorComision { get; set; }
    public decimal ValorIvaComision { get; set; }
    public decimal ValorNeto { get; set; }
    public string Estado { get; set; } = "APLICADA";
    public DateTime FechaLiquidacion { get; set; }
    public long? CreadoPor { get; set; }
}
