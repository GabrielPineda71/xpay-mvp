namespace Xpay.Api.Models;

public class PassportBrebRetiro
{
    public long    IdBrebRetiro          { get; set; }
    public string  TipoSujeto           { get; set; } = string.Empty; // USUARIO / COMERCIO
    public long?   IdUsuario            { get; set; }
    public long?   IdComercio           { get; set; }
    public long    IdWallet             { get; set; }
    public long    IdBrebLlave          { get; set; }
    public decimal Valor                { get; set; }
    public string  Moneda               { get; set; } = "COP";
    public string  Estado               { get; set; } = "CREADO";
    public string? PassportPaymentId    { get; set; }
    public string? PassportResolutionId { get; set; }
    public string? PassportRecipientId  { get; set; }
    public string  ReferenciaInterna    { get; set; } = string.Empty;
    public string  IdempotencyKey       { get; set; } = string.Empty;
    public DateTime  FechaSolicitud     { get; set; }
    public DateTime? FechaEnvioPassport { get; set; }
    public DateTime? FechaConfirmacion  { get; set; }
    public DateTime? FechaLiquidacion   { get; set; }
    public DateTime? FechaRechazo       { get; set; }
    public string? MotivoRechazo        { get; set; }
    public long?   IdTransaccionLedger  { get; set; }
    public long?   CreatedByUsuario     { get; set; }
    public long?   UpdatedByUsuario     { get; set; }
}
