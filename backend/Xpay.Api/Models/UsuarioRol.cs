namespace Xpay.Api.Models;
public class UsuarioRol
{
    public long IdUsuario { get; set; }
    public long IdRol { get; set; }
    public DateTime FechaAsignacion { get; set; }
    public long? AsignadoPor { get; set; }
    public string Estado { get; set; } = "ACTIVO";
    // Fase USUARIOS-ADMIN-4 (migración 030): revocación sin borrar la fila.
    public DateTime? FechaRevocacion { get; set; }
    public long? RevocadoPor { get; set; }
    public string? Observacion { get; set; }
}
