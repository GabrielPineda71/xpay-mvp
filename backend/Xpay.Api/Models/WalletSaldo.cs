namespace Xpay.Api.Models;
public class WalletSaldo
{
    public long IdWallet { get; set; }
    public decimal SaldoDisponible { get; set; }
    public decimal SaldoRetenido { get; set; }
    public decimal SaldoTransito { get; set; }
    public decimal SaldoContingencia { get; set; }
    public DateTime FechaActualizacion { get; set; }
}
