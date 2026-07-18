namespace Xpay.Api.Models;

public class VentaQr
{
    public long IdVentaQr { get; set; }
    public long IdUnidadNegocio { get; set; }
    public long IdComercio { get; set; }
    public long IdTienda { get; set; }
    public long IdQr { get; set; }
    public long IdWalletUsuario { get; set; }
    public long? IdTransaccionLedger { get; set; }
    public decimal ValorBruto { get; set; }
    public decimal ValorComision { get; set; }
    public decimal ValorIvaComision { get; set; }
    public decimal ValorNetoComercio { get; set; }
    public string Estado { get; set; } = "CONTINGENCIA";
    public string? Referencia { get; set; }
    public string? Descripcion { get; set; }
    public DateTime FechaVenta { get; set; }
    public long? IdTransaccionLiquidacion { get; set; }
}
