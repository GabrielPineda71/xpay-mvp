namespace Xpay.Api.Models;

public class LibranzaUsuarioEmpresa
{
    public long     IdUsuarioEmpresa  { get; set; }
    public long     IdUsuario         { get; set; }
    public long     IdConvenio        { get; set; }
    public string   RolEmpresa        { get; set; } = "ADMIN_EMPRESA";
    public string   Estado            { get; set; } = "ACTIVO";
    public DateTime  CreatedAt        { get; set; }
    public DateTime? UpdatedAt        { get; set; }
    public long?    CreatedByUsuario  { get; set; }
    public long?    UpdatedByUsuario  { get; set; }
}
