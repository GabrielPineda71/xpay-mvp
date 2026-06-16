namespace Xpay.Api.Models;
public class LedgerCuenta
{
    public long IdCuenta { get; set; }
    public long IdUnidadNegocio { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string TipoCuenta { get; set; } = string.Empty;
    public string? SubtipoCuenta { get; set; }
    public string Naturaleza { get; set; } = string.Empty;
    public bool PermiteMovimiento { get; set; }
    public string Estado { get; set; } = "ACTIVA";
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaActualizacion { get; set; }
}
