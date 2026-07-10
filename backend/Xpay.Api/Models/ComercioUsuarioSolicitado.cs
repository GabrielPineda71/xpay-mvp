namespace Xpay.Api.Models;

public class ComercioUsuarioSolicitado
{
    public long     IdUsuarioSolicitado { get; set; }
    public long     IdComercioAliado    { get; set; }
    public long?    IdEstablecimiento   { get; set; }
    public long?    IdUsuario           { get; set; }
    public string   Nombres             { get; set; } = string.Empty;
    public string?  Correo              { get; set; }
    public string?  Celular             { get; set; }
    public string   RolSolicitado       { get; set; } = string.Empty;
    public string   Estado              { get; set; } = "PENDIENTE_CREACION";
    public DateTime  CreatedAt          { get; set; }
    public DateTime? UpdatedAt          { get; set; }
}
