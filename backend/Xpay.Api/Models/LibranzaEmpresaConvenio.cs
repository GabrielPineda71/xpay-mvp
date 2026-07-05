namespace Xpay.Api.Models;

public class LibranzaEmpresaConvenio
{
    public long     IdConvenio           { get; set; }
    public string   NombreEmpresa        { get; set; } = string.Empty;
    public string   Nit                  { get; set; } = string.Empty;
    public string?  RepresentanteLegal   { get; set; }
    public string?  EmailContacto        { get; set; }
    public string?  TelefonoContacto     { get; set; }
    public string?  Direccion            { get; set; }
    public string   Estado               { get; set; } = "ACTIVO";
    public int?     DiaPago1                  { get; set; }
    public int?     DiaPago2                  { get; set; }
    public int?     DiaPago3                  { get; set; }
    public bool     PermiteAnticipodiaPago    { get; set; }
    public string   PeriodicidadPago     { get; set; } = string.Empty;
    public decimal  PorcentajeMaximoCupo { get; set; }
    public string?  Observaciones        { get; set; }
    public DateTime FechaInicio          { get; set; }
    public DateTime? FechaFin            { get; set; }
    public DateTime  CreatedAt           { get; set; }
    public DateTime? UpdatedAt           { get; set; }
    public long?    CreatedByUsuario     { get; set; }
    public long?    UpdatedByUsuario     { get; set; }
}
