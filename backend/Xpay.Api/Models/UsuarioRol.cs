namespace Xpay.Api.Models;
public class UsuarioRol
{
    public long IdUsuario { get; set; }
    public long IdRol { get; set; }
    public DateTime FechaAsignacion { get; set; }
    public long? AsignadoPor { get; set; }
    public string Estado { get; set; } = "ACTIVO";
}
