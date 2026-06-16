namespace Xpay.Api.Models;
public class Usuario
{
    public long IdUsuario { get; set; }
    public long IdPersona { get; set; }
    public string NombreUsuario { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool EmailVerificado { get; set; }
    public bool CelularVerificado { get; set; }
    public bool RequiereCambioClave { get; set; }
    public int IntentosFallidos { get; set; }
    public DateTime? UltimoIngreso { get; set; }
    public DateTime? FechaBloqueo { get; set; }
    public string? MotivoBloqueo { get; set; }
    public string Estado { get; set; } = "ACTIVO";
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaActualizacion { get; set; }
}
