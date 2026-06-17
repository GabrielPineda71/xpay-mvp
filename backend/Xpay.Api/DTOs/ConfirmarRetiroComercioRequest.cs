namespace Xpay.Api.DTOs;

public class ConfirmarRetiroComercioRequest
{
    public long IdRetiro { get; set; }
    public string? ReferenciaPago { get; set; }
    public string? Observacion { get; set; }
    public long? CreadoPor { get; set; }
}
