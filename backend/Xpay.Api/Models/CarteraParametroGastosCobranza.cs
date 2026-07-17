namespace Xpay.Api.Models;

public class CarteraParametroGastosCobranza
{
    public long     IdGasto        { get; set; }
    public int      DiasDesde      { get; set; }
    public int?     DiasHasta      { get; set; }
    public string   TipoCobro      { get; set; } = "FIJO"; // FIJO | PORCENTAJE
    public decimal  ValorCobro     { get; set; }
    public string?  Descripcion    { get; set; }
    public string   Estado         { get; set; } = "ACTIVO";
    public DateTime CreatedAt      { get; set; }
    public DateTime? UpdatedAt     { get; set; }
}
