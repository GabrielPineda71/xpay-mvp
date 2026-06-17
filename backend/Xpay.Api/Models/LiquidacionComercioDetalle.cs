namespace Xpay.Api.Models;

public class LiquidacionComercioDetalle
{
    public long IdDetalle { get; set; }
    public long IdLiquidacion { get; set; }
    public long IdVentaQr { get; set; }
    public decimal ValorBruto { get; set; }
    public decimal ValorComision { get; set; }
    public decimal ValorIvaComision { get; set; }
    public decimal ValorNeto { get; set; }
}
