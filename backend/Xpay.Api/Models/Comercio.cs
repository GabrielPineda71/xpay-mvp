namespace Xpay.Api.Models;

public class Comercio
{
    public long IdComercio { get; set; }
    public long IdUnidadNegocio { get; set; }
    public string NombreComercial { get; set; } = string.Empty;
    public string? RazonSocial { get; set; }
    public string? Nit { get; set; }
    public string Estado { get; set; } = "ACTIVO";
    public DateTime FechaCreacion { get; set; }
    public long? IdWalletComercio { get; set; }
}
