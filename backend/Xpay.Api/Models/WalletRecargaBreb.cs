namespace Xpay.Api.Models;

public sealed class WalletRecargaBreb
{
    public long IdRecarga { get; set; }
    public long IdRecargaQr { get; set; }
    public long IdWallet { get; set; }
    public string PassportPaymentId { get; set; } = string.Empty;
    public decimal Valor { get; set; }
    public string Moneda { get; set; } = "COP";
    public string Estado { get; set; } = "APLICADA";
    public long IdTransaccionLedger { get; set; }
    public long IdEventoSettled { get; set; }
    public DateTime Fecha { get; set; }
}
