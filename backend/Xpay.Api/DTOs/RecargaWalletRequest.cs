namespace Xpay.Api.DTOs;

public class RecargaWalletRequest
{
    public decimal Valor { get; set; }
    public long? CreadoPor { get; set; }
    public string? ReferenciaExterna { get; set; }
    public string? Observacion { get; set; }
}
