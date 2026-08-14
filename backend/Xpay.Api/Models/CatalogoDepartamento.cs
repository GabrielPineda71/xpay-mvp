namespace Xpay.Api.Models;
public class CatalogoDepartamento
{
    public long IdDepartamento { get; set; }
    public long IdPais { get; set; }
    public string CodigoDivipola { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Estado { get; set; } = "ACTIVO";
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaActualizacion { get; set; }
}
