namespace Xpay.Api.Models;

public class ComercioAliado
{
    public long     IdComercioAliado      { get; set; }
    public long?    IdComercioExistente   { get; set; }
    public string   RazonSocial           { get; set; } = string.Empty;
    public string   NombreComercial       { get; set; } = string.Empty;
    public string   Nit                   { get; set; } = string.Empty;
    public string   TipoPersona           { get; set; } = string.Empty;
    public string?  ActividadEconomica    { get; set; }
    public string?  CodigoCiiu            { get; set; }
    public string?  DireccionPrincipal    { get; set; }
    public string?  Ciudad                { get; set; }
    public string?  Departamento          { get; set; }
    public string?  Telefono              { get; set; }
    public string?  Correo                { get; set; }
    public string?  SitioWeb              { get; set; }
    public string   Estado                { get; set; } = "BORRADOR";
    public string?  CondicionesComerciales { get; set; }
    public DateTime FechaSolicitud        { get; set; }
    public DateTime? FechaAprobacion      { get; set; }
    public DateOnly? FechaInicioConvenio  { get; set; }
    public DateOnly? FechaFinConvenio     { get; set; }
    public string?  Observaciones         { get; set; }
    public DateTime  CreatedAt            { get; set; }
    public DateTime? UpdatedAt            { get; set; }
    public long?    CreatedByUsuario      { get; set; }
    public long?    UpdatedByUsuario      { get; set; }
}
