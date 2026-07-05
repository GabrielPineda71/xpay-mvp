namespace Xpay.Api.Models;

public class LibranzaEmpleadoUsuario
{
    public long     IdEmpleadoUsuario  { get; set; }
    public long     IdEmpleado         { get; set; }
    public long     IdUsuario          { get; set; }
    public long?    IdWallet           { get; set; }
    public string   Estado             { get; set; } = "ACTIVO";
    public DateTime CreatedAt          { get; set; }
    public long?    CreatedByUsuario   { get; set; }
}
