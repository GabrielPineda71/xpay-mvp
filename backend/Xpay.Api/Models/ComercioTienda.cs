namespace Xpay.Api.Models;

public class ComercioTienda
{
    public long IdTienda { get; set; }
    public long IdComercio { get; set; }
    public string NombreTienda { get; set; } = string.Empty;
    public string? Ciudad { get; set; }
    public string? Direccion { get; set; }
    public string Estado { get; set; } = "ACTIVO";
    public DateTime FechaCreacion { get; set; }
}
