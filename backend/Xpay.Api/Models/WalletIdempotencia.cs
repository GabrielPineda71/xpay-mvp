namespace Xpay.Api.Models;

public class WalletIdempotencia
{
    public long      IdIdempotencia      { get; set; }
    public long      IdUsuario           { get; set; }
    public string    Endpoint            { get; set; } = string.Empty;
    public Guid      IdempotencyKey      { get; set; }
    public byte[]    RequestHash         { get; set; } = Array.Empty<byte>();
    public string    Estado              { get; set; } = "EN_PROCESO";
    public short?    HttpStatus          { get; set; }
    public long?     IdRecurso           { get; set; }
    public long?     IdTransaccionLedger { get; set; }
    public string?   RespuestaDataJson   { get; set; }
    public DateTime  FechaCreacion       { get; set; }
    public DateTime? FechaCompletado     { get; set; }
    public DateTime  FechaExpiracion     { get; set; }
}
