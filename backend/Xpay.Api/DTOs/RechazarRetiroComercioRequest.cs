namespace Xpay.Api.DTOs;

public class RechazarRetiroComercioRequest
{
    public long IdRetiro { get; set; }
    public string? MotivoRechazo { get; set; }
    public string? Observacion { get; set; }
    public long? CreadoPor { get; set; }
}
