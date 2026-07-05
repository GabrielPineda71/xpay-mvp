namespace Xpay.Api.Models;

public class LibranzaEmpleado
{
    public long     IdEmpleado               { get; set; }
    public long     IdConvenio               { get; set; }
    public string   TipoDocumento            { get; set; } = string.Empty;
    public string   NumeroDocumento          { get; set; } = string.Empty;
    public string   Nombres                  { get; set; } = string.Empty;
    public string?  Apellidos                { get; set; }
    public string?  Celular                  { get; set; }
    public string?  Correo                   { get; set; }
    public string?  Cargo                    { get; set; }
    public decimal  SalarioMensual           { get; set; }
    public string   PeriodicidadPago         { get; set; } = string.Empty;
    public int?     DiaPago1                 { get; set; }
    public int?     DiaPago2                 { get; set; }
    public int?     DiaPago3                 { get; set; }
    public DateOnly? FechaIngreso            { get; set; }
    public string   Estado                   { get; set; } = "ACTIVO";
    public decimal  CupoPreliminar           { get; set; }
    public DateTime? FechaUltimoCalculoCupo  { get; set; }
    public string   OrigenCarga              { get; set; } = "MANUAL";
    public string?  LoteImportacion          { get; set; }
    public string?  Observaciones            { get; set; }
    public DateTime  CreatedAt               { get; set; }
    public DateTime? UpdatedAt               { get; set; }
    public long?    CreatedByUsuario         { get; set; }
    public long?    UpdatedByUsuario         { get; set; }
}
