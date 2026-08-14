namespace Xpay.Api.Models;
public class CatalogoCiudad
{
    public long IdCiudad { get; set; }
    public long IdDepartamento { get; set; }
    public string CodigoDivipola { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty; // MUNICIPIO / ISLA / AREA_NO_MUNICIPALIZADA — clasificación oficial DANE
    public string Estado { get; set; } = "ACTIVO";
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaActualizacion { get; set; }
}
