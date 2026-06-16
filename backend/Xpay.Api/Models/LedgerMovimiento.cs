namespace Xpay.Api.Models;
public class LedgerMovimiento
{
    public long IdMovimientoLedger { get; set; }
    public long IdTransaccionLedger { get; set; }
    public long IdCuenta { get; set; }
    public string Naturaleza { get; set; } = string.Empty;
    public decimal Valor { get; set; }
    public string? Concepto { get; set; }
    public string? ReferenciaTipo { get; set; }
    public long? ReferenciaId { get; set; }
    public string? Descripcion { get; set; }
    public DateTime FechaMovimiento { get; set; }
}
