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
    // 054: null en TODOS los campos identifica exclusivamente vínculos legacy.
    // Los nuevos requieren revisión manual durable y una huella de las
    // condiciones e identidad efectivamente revisadas, sin automatismos.
    public string? RevisionHuella { get; set; }
    public DateTime? RevisionFecha { get; set; }
    public long? RevisionPorUsuario { get; set; }
    public string? RevisionMetodo { get; set; }
    public string? RevisionReferencia { get; set; }
}
