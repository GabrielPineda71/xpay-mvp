namespace Xpay.Api.Models;

public class ComercioUsuario
{
    public long      IdComercioUsuario    { get; set; }
    public long      IdComercioAliado     { get; set; }
    public long?     IdComercioExistente  { get; set; }
    public long?     IdEstablecimiento    { get; set; }
    public long      IdUsuario            { get; set; }
    public string    RolComercio          { get; set; } = string.Empty;
    public string    Estado               { get; set; } = "ACTIVO";
    public DateTime  CreatedAt            { get; set; }
    public DateTime? UpdatedAt            { get; set; }
    public long?     CreatedByUsuario     { get; set; }
    public long?     UpdatedByUsuario     { get; set; }
}
