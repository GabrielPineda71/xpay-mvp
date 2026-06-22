namespace Xpay.Api.DTOs;

public class IniciarKycResponse
{
    public string EstadoKyc  { get; set; } = "PENDIENTE";
    public string SessionId  { get; set; } = string.Empty;
    public string SessionUrl { get; set; } = string.Empty;
}
