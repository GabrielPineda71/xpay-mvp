namespace Xpay.Api.Models;

public class CarteraParametroUtilizacion
{
    public long     IdParametro        { get; set; }
    public string   TipoUtilizacion    { get; set; } = string.Empty; // COMPRA_COMERCIO | AVANCE_WALLET
    public decimal  TasaEmv            { get; set; }
    public decimal  PorcAval           { get; set; }
    public decimal  PorcAdmin          { get; set; }
    public bool     AplicaIva          { get; set; }
    public decimal  PorcIva            { get; set; }
    public int      PlazoMin           { get; set; }
    public int      PlazoMax           { get; set; }
    public string   Frecuencia         { get; set; } = "MENSUAL"; // MENSUAL | QUINCENAL
    public decimal  MontoMin           { get; set; }
    public decimal  MontoMax           { get; set; }
    public string   Estado             { get; set; } = "ACTIVO";
    public DateTime CreatedAt          { get; set; }
    public DateTime? UpdatedAt         { get; set; }
    public long?    CreatedByUsuario   { get; set; }
    public long?    UpdatedByUsuario   { get; set; }
}
