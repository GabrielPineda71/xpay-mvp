namespace Xpay.Api.Models;

public class ComercioEstablecimiento
{
    public long     IdEstablecimiento      { get; set; }
    public long     IdComercioAliado       { get; set; }
    public string   NombreEstablecimiento  { get; set; } = string.Empty;
    public string?  Direccion              { get; set; }
    public string?  Ciudad                 { get; set; }
    public string?  Telefono               { get; set; }
    public string?  Responsable            { get; set; }
    public string   Estado                 { get; set; } = "ACTIVO";
    public DateTime  CreatedAt             { get; set; }
    public DateTime? UpdatedAt             { get; set; }
}
