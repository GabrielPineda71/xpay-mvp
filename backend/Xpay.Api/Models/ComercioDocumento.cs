namespace Xpay.Api.Models;

public class ComercioDocumento
{
    public long     IdDocumento            { get; set; }
    public long     IdComercioAliado       { get; set; }
    public string   TipoDocumento          { get; set; } = string.Empty;
    public string   NombreArchivoOriginal  { get; set; } = string.Empty;
    public string   StoragePath            { get; set; } = string.Empty;
    public string?  ContentType            { get; set; }
    public long?    SizeBytes              { get; set; }
    public string   Estado                 { get; set; } = "ACTIVO";
    public string?  Observaciones          { get; set; }
    public DateTime  UploadedAt            { get; set; }
    public long?    UploadedByUsuario      { get; set; }
}
