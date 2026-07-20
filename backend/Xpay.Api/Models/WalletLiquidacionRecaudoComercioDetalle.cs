namespace Xpay.Api.Models;

public class WalletLiquidacionRecaudoComercioDetalle
{
    public long     IdDetalle       { get; set; }
    public long     IdLiquidacion   { get; set; }
    public long     IdRecarga       { get; set; }
    public decimal  Valor           { get; set; }
    public DateTime CreatedAt       { get; set; }
}
