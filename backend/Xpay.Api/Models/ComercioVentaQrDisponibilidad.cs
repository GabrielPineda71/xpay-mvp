namespace Xpay.Api.Models;

public class ComercioVentaQrDisponibilidad
{
    public long     IdDisponibilidad                 { get; set; }
    public long     IdVentaQr                        { get; set; }
    public long     IdComercioAliado                 { get; set; }
    public long     IdComercioExistente              { get; set; }
    public long     IdWalletComercio                 { get; set; }
    public decimal  ValorBruto                       { get; set; }
    public int      DiasDisponibilidad               { get; set; }
    public decimal  PorcentajeDescuento              { get; set; }
    public decimal  ValorDescuento                   { get; set; }
    public decimal  ValorNetoProgramado              { get; set; }
    public DateTime FechaVenta                       { get; set; }
    public DateTime FechaDisponibleProgramada        { get; set; }
    public string   Estado                           { get; set; } = "NO_DISPONIBLE";
    public string?  TipoLiberacion                   { get; set; }
    public DateTime? FechaLiberacion                 { get; set; }
    public decimal? PorcentajeDescuentoAnticipado    { get; set; }
    public decimal? ValorDescuentoAnticipado         { get; set; }
    public decimal? ValorNetoLiberado                { get; set; }
    public long?    IdTransaccionLedgerLiberacion    { get; set; }
    public string?  Observaciones                    { get; set; }
    public DateTime CreatedAt                        { get; set; }
    public DateTime? UpdatedAt                       { get; set; }
}
