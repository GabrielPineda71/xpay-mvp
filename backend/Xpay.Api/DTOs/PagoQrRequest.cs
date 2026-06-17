namespace Xpay.Api.DTOs;

public class PagoQrRequest
{
    public string CodigoQr { get; set; } = string.Empty;
    public long IdWalletUsuario { get; set; }
    public decimal Valor { get; set; }
    public string? Descripcion { get; set; }
    public long? CreadoPor { get; set; }
}
