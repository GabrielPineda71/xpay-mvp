namespace Xpay.Api.Models;

public class CarteraUtilizacion
{
    public long     IdUtilizacion           { get; set; }
    public long     IdCupo                  { get; set; }
    public long     IdUsuario               { get; set; }
    public long     IdWallet                { get; set; }
    public string   TipoUtilizacion         { get; set; } = string.Empty;
    public long?    IdComercioAliado        { get; set; }
    public long?    IdVentaQr               { get; set; }
    public decimal  ValorCapital            { get; set; }
    public decimal  TasaEmv                 { get; set; }
    public decimal  PorcAval                { get; set; }
    public decimal  PorcAdmin               { get; set; }
    public bool     AplicaIva               { get; set; }
    public decimal  PorcIva                 { get; set; }
    public int      PlazoMeses              { get; set; }
    public string   Frecuencia              { get; set; } = "MENSUAL";
    public int      TotalCuotas             { get; set; }
    public decimal  ValorCuota              { get; set; }
    public decimal  ValorTotalAval          { get; set; }
    public decimal  ValorTotalAdmin         { get; set; }
    public decimal  ValorTotalIva           { get; set; }
    public decimal  ValorTotalIntereses     { get; set; }
    public decimal  ValorTotalPagar         { get; set; }
    public string   Estado                  { get; set; } = "SIMULADO";
    public DateTime FechaSolicitud          { get; set; }
    public DateTime? FechaDesembolso        { get; set; }
    public string?  Observaciones           { get; set; }
    public DateTime CreatedAt               { get; set; }
    public DateTime? UpdatedAt              { get; set; }
    public long?    CreatedByUsuario        { get; set; }
}
