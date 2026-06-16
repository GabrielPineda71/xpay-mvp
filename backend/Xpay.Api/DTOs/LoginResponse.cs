namespace Xpay.Api.DTOs;

public class LoginResponse
{
    public long IdUsuario { get; set; }
    public long IdPersona { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
}
