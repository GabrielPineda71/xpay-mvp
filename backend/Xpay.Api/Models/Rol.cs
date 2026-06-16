namespace Xpay.Api.Models;
public class Rol
{
    public long IdRol { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public string TipoRol { get; set; } = string.Empty;
    public string Estado { get; set; } = "ACTIVO";
    public DateTime FechaCreacion { get; set; }
}
