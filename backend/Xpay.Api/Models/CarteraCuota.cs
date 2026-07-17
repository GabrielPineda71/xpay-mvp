namespace Xpay.Api.Models;

public class CarteraCuota
{
    public long     IdCuota                 { get; set; }
    public long     IdUtilizacion           { get; set; }
    public int      NumeroCuota             { get; set; }
    public DateOnly FechaVencimiento        { get; set; }
    public decimal  ValorCapital            { get; set; }
    public decimal  ValorInteres            { get; set; }
    public decimal  ValorAval               { get; set; }
    public decimal  ValorAdmin              { get; set; }
    public decimal  ValorIva                { get; set; }
    public decimal  ValorTotal              { get; set; }
    public decimal  SaldoCapitalAntes       { get; set; }
    public decimal  SaldoCapitalDespues     { get; set; }
    public string   Estado                  { get; set; } = "PENDIENTE";
    public DateTime? FechaPago              { get; set; }
    public long?    IdPago                  { get; set; }
    public DateTime CreatedAt               { get; set; }
    public DateTime? UpdatedAt              { get; set; }
}
