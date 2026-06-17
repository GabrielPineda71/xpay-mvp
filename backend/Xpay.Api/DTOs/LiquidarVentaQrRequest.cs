namespace Xpay.Api.DTOs;

public class LiquidarVentaQrRequest
{
    public long IdVentaQr { get; set; }
    public long? CreadoPor { get; set; }
    public string? Observacion { get; set; }
}
