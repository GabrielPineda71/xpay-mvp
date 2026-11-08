namespace Xpay.Api.Models;

// P0-B — ver database/048_usuario_tokens.sql. Token de un solo uso
// (VERIFICAR_EMAIL / RECUPERAR_CLAVE). Solo se persiste el SHA-256 del token;
// el token en claro nunca se guarda ni se registra en logs.
public class UsuarioToken
{
    public long IdToken { get; set; }
    public long IdUsuario { get; set; }
    public string Proposito { get; set; } = string.Empty;
    public byte[] TokenHash { get; set; } = Array.Empty<byte>();
    public string EmailDestino { get; set; } = string.Empty;
    public DateTime FechaCreacion { get; set; }
    public DateTime FechaExpiracion { get; set; }
    public DateTime? FechaUso { get; set; }
    public DateTime? FechaInvalidacion { get; set; }
    public string? IpSolicitud { get; set; }
}
