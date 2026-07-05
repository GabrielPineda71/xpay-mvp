namespace Xpay.Api.Models;

public class LibranzaEmpleadoCortesPago
{
    public long     IdCortePago          { get; set; }
    public long     IdEmpleado           { get; set; }
    public int      NumeroCorte          { get; set; }
    public int      DiaPago              { get; set; }
    public decimal  ValorPagoProgramado  { get; set; }
    public string   Estado               { get; set; } = "ACTIVO";
    public DateTime CreatedAt            { get; set; }
    public DateTime? UpdatedAt           { get; set; }
    public long?    CreatedByUsuario     { get; set; }
    public long?    UpdatedByUsuario     { get; set; }
}
