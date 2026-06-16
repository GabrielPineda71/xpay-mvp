namespace Xpay.Api.Models;
public class LedgerTransaccion
{
    public long IdTransaccionLedger { get; set; }
    public long IdUnidadNegocio { get; set; }
    public string TipoTransaccion { get; set; } = string.Empty;
    public string? ReferenciaTipo { get; set; }
    public long? ReferenciaId { get; set; }
    public string? Descripcion { get; set; }
    public decimal ValorTotal { get; set; }
    public string Estado { get; set; } = "REGISTRADA";
    public long? CreadoPor { get; set; }
    public DateTime FechaTransaccion { get; set; }
    public DateTime? FechaActualizacion { get; set; }
}
