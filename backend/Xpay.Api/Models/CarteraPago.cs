namespace Xpay.Api.Models;

public class CarteraPago
{
    public long     IdPago              { get; set; }
    public long     IdUtilizacion       { get; set; }
    public long     IdUsuario           { get; set; }
    public long     IdWallet            { get; set; }
    public decimal  ValorPago           { get; set; }
    public DateTime FechaPago           { get; set; }
    public string   TipoPago            { get; set; } = "CUOTA_NORMAL";
    public string   Estado              { get; set; } = "REGISTRADO";
    public string?  Observaciones       { get; set; }
    public DateTime CreatedAt           { get; set; }
    public long?    CreatedByUsuario    { get; set; }
}
