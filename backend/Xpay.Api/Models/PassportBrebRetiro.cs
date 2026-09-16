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
    // XPAY-373 (043) — snapshot INMUTABLE del vencimiento de la resolución
    // que ESTE retiro usó al crearse (copiado desde PassportBrebLlave.
    // PassportResolutionExpiresAtUtc en el momento de la reserva) —
    // deliberadamente independiente de la caché mutable de la llave, que
    // pudo haberse refrescado después. Se verifica antes de enviar el
    // Payment (BrebPaymentService.EnviarPaymentAsync) — nunca se reutiliza
    // silenciosamente una resolución vencida.
    public DateTime? PassportResolutionExpiresAtUtc { get; set; }
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
