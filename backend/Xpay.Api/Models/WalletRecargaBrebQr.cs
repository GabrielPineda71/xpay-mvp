namespace Xpay.Api.Models;

public sealed class WalletRecargaBrebQr
{
    public long IdRecargaQr { get; set; }
    public long IdWallet { get; set; }
    public long IdUsuario { get; set; }
    public string Referencia { get; set; } = string.Empty;
    public string Estado { get; set; } = "PENDIENTE";
    public string PassportAccountId { get; set; } = string.Empty;
    public string PassportCustomerId { get; set; } = string.Empty;
    public string PassportKeyId { get; set; } = string.Empty;
    public string? PassportQrId { get; set; }
    public string? QrCodeData { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaIntento { get; set; }
    public DateTime? FechaGeneracion { get; set; }
}
