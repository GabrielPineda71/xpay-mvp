namespace Xpay.Api.Models;

public class KycVerificacion
{
    public long    IdKycVerificacion  { get; set; }
    public long    IdUsuario          { get; set; }
    public long?   IdPersona          { get; set; }
    public string  Proveedor          { get; set; } = "VERIFF";
    public string  EstadoKyc          { get; set; } = "NO_INICIADO";
    public string? SessionId          { get; set; }
    public string? SessionUrl         { get; set; }
    public string? Decision           { get; set; }
    public string? Reason             { get; set; }
    public string? VendorData         { get; set; }
    public bool    EsActual           { get; set; } = true;
    public DateTime  FechaCreacion       { get; set; }
    public DateTime? FechaActualizacion  { get; set; }
    public DateTime? FechaDecision       { get; set; }
}
