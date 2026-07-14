namespace Xpay.Api.Models;

public class ComercioCondicionNegociacion
{
    public long     IdCondicion          { get; set; }
    public long     IdComercioAliado     { get; set; }
    public int      DiasDisponibilidad   { get; set; }
    public decimal  PorcentajeDescuento  { get; set; }
    public bool     AplicaIva            { get; set; }
    public decimal? PorcentajeIva        { get; set; }
    public string   Estado               { get; set; } = "ACTIVO";
    public DateOnly FechaInicio          { get; set; }
    public DateOnly? FechaFin            { get; set; }
    public string?  Observaciones        { get; set; }
    public DateTime CreatedAt            { get; set; }
    public DateTime? UpdatedAt           { get; set; }
    public long?    CreatedByUsuario     { get; set; }
    public long?    UpdatedByUsuario     { get; set; }
}
