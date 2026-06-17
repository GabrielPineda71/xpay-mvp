namespace Xpay.Api.DTOs;

public class TransferenciaWalletRequest
{
    public long IdWalletOrigen { get; set; }
    public long IdWalletDestino { get; set; }
    public decimal Valor { get; set; }
    public string? Descripcion { get; set; }
    public long? CreadoPor { get; set; }
}
