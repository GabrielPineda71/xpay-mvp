namespace Xpay.Api.Models;

public class WalletCajaMovimiento
{
    public long     IdMovimiento   { get; set; }
    public long     IdCaja         { get; set; }
    public long?    IdRecarga      { get; set; }
    public string   TipoMovimiento { get; set; } = string.Empty;
    public string   Naturaleza     { get; set; } = string.Empty;
    public decimal  Valor          { get; set; }
    public string?  Observaciones  { get; set; }
    public DateTime CreatedAt      { get; set; }
}
