namespace Xpay.Api.Models;

public class ComercioRepresentanteLegal
{
    public long     IdRepresentante           { get; set; }
    public long     IdComercioAliado          { get; set; }
    public string   TipoDocumento             { get; set; } = string.Empty;
    public string   NumeroDocumento           { get; set; } = string.Empty;
    public string   Nombres                   { get; set; } = string.Empty;
    public string?  Apellidos                 { get; set; }
    public string?  Celular                   { get; set; }
    public string?  Correo                    { get; set; }
    public string?  Cargo                     { get; set; }
    public DateOnly? FechaExpedicionDocumento { get; set; }
    public string   Estado                    { get; set; } = "ACTIVO";
    public DateTime  CreatedAt                { get; set; }
    public DateTime? UpdatedAt                { get; set; }
}
