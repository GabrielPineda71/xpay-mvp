namespace Xpay.Api.Models;
public class Wallet
{
    public long IdWallet { get; set; }
    public long IdUnidadNegocio { get; set; }
    public string TipoWallet { get; set; } = string.Empty;
    public long? IdPersona { get; set; }
    public long? IdComercio { get; set; }
    public string? NombreWallet { get; set; }
    public string Estado { get; set; } = "ACTIVA";
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaActualizacion { get; set; }
}
