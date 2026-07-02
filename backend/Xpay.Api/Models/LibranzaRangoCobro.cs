namespace Xpay.Api.Models;

public class LibranzaRangoCobro
{
    public long     IdRango           { get; set; }
    public long     IdConvenio        { get; set; }
    public decimal  ValorDesde        { get; set; }
    public decimal  ValorHasta        { get; set; }
    public string   TipoCobro         { get; set; } = string.Empty;
    public decimal  ValorCobro        { get; set; }
    public bool     AplicaIva         { get; set; } = true;
    public string   Estado            { get; set; } = "ACTIVO";
    public DateTime  CreatedAt        { get; set; }
    public DateTime? UpdatedAt        { get; set; }
    public long?    CreatedByUsuario  { get; set; }
    public long?    UpdatedByUsuario  { get; set; }
}
