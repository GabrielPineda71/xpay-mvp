namespace Xpay.Api.Models;

public class WalletCierreDiarioComercioDetalle
{
    public long     IdDetalle                 { get; set; }
    public long     IdCierre                  { get; set; }
    public long     IdRecarga                 { get; set; }
    public decimal  Valor                     { get; set; }
    public bool     EstabaLiquidadaAlGenerar  { get; set; }
    public DateTime CreatedAt                 { get; set; }
}
