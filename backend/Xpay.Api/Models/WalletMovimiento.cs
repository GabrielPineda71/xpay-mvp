namespace Xpay.Api.Models;
public class WalletMovimiento
{
    public long IdMovimientoWallet { get; set; }
    public long IdWallet { get; set; }
    public long? IdTransaccionLedger { get; set; }
    public string TipoMovimiento { get; set; } = string.Empty;
    public string Naturaleza { get; set; } = string.Empty;
    public decimal Valor { get; set; }
    public decimal? SaldoAntes { get; set; }
    public decimal? SaldoDespues { get; set; }
    public string? Descripcion { get; set; }
    public string? ReferenciaTipo { get; set; }
    public long? ReferenciaId { get; set; }
    public string Estado { get; set; } = "APLICADO";
    public long? CreadoPor { get; set; }
    public DateTime FechaMovimiento { get; set; }
}
