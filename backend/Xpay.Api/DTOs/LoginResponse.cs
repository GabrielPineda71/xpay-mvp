namespace Xpay.Api.DTOs;

public class LoginResponse
{
    public long IdUsuario { get; set; }
    public long IdPersona { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public string Token { get; set; } = string.Empty;
    public bool RequiereCambioClave { get; set; }
    // D10 — tiene una asociación ACTIVA en libranza_usuarios_empresa. Solo
    // orienta la navegación del frontend; la autorización real sigue en
    // LibranzaEmpresaController (asociación + rol_empresa en cada petición).
    public bool EsUsuarioEmpresa { get; set; }
}
