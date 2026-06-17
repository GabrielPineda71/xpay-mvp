namespace Xpay.Api.Models;

public class QrComercio
{
    public long IdQr { get; set; }
    public long IdComercio { get; set; }
    public long IdTienda { get; set; }
    public string CodigoQr { get; set; } = string.Empty;
    public string Estado { get; set; } = "ACTIVO";
    public DateTime FechaCreacion { get; set; }
}
