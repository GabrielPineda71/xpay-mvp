namespace Xpay.Api.Models;

public class LibranzaImportacionEmpleados
{
    public long     IdImportacion        { get; set; }
    public long     IdConvenio           { get; set; }
    public string?  NombreArchivo        { get; set; }
    public string   LoteImportacion      { get; set; } = string.Empty;
    public int      TotalFilas           { get; set; }
    public int      FilasValidas         { get; set; }
    public int      FilasError           { get; set; }
    public int      EmpleadosCreados     { get; set; }
    public int      EmpleadosActualizados { get; set; }
    public string   Estado               { get; set; } = "PROCESADA";
    public string?  ErroresJson          { get; set; }
    public DateTime  CreatedAt           { get; set; }
    public long?    CreatedByUsuario     { get; set; }
}
