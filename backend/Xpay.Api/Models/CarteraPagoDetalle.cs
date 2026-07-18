namespace Xpay.Api.Models;

public class CarteraPagoDetalle
{
    public long     IdDetalle       { get; set; }
    public long     IdPago          { get; set; }
    public long     IdCuota         { get; set; }
    public decimal  ValorCapital    { get; set; }
    public decimal  ValorInteres    { get; set; }
    public decimal  ValorAval       { get; set; }
    public decimal  ValorAdmin      { get; set; }
    public decimal  ValorIva        { get; set; }
    public decimal  ValorTotal      { get; set; }
    public decimal  ValorAplicadoAdmin           { get; set; }
    public decimal  ValorAplicadoIva              { get; set; }
    public decimal  ValorAplicadoGastosCobranza   { get; set; }
    public decimal  ValorAplicadoIvaGastosCobranza { get; set; }
    public DateTime CreatedAt       { get; set; }
}
