namespace Xpay.Api.Models;

public class LibranzaAnticipo
{
    public long     IdAnticipo                       { get; set; }
    public long     IdConvenio                       { get; set; }
    public long     IdEmpleado                       { get; set; }
    public long     IdUsuario                        { get; set; }
    public long     IdWallet                         { get; set; }
    public DateTime FechaSolicitud                   { get; set; }
    public DateOnly? FechaSimulada                   { get; set; }
    public int      DiaPagoCorte                     { get; set; }
    public DateOnly? FechaPagoProgramada             { get; set; }
    public decimal  ValorPagoProgramado              { get; set; }
    public decimal  PorcentajeCupo                   { get; set; }
    public decimal  ValorCupoBase                    { get; set; }
    public decimal  ValorSolicitado                  { get; set; }
    public decimal  ValorComision                    { get; set; }
    public decimal  ValorIva                         { get; set; }
    public decimal  ValorTotalACobrar                { get; set; }
    public decimal  ValorNetoDesembolsado            { get; set; }
    public string   MomentoCobroComision             { get; set; } = string.Empty;
    public string   Estado                           { get; set; } = "CREADO";
    public long?    IdTransaccionLedgerDesembolso    { get; set; }
    public long?    IdTransaccionLedgerPago          { get; set; }
    public string?  ReferenciaPago                   { get; set; }
    public string?  Observaciones                    { get; set; }
    public DateTime CreatedAt                        { get; set; }
    public DateTime? UpdatedAt                       { get; set; }
    public long?    CreatedByUsuario                 { get; set; }
    public long?    UpdatedByUsuario                 { get; set; }
}
