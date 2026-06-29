namespace Xpay.Api.Models;

public class PassportWebhookEvent
{
    public long    IdEvent               { get; set; }
    public string  Provider              { get; set; } = "PASSPORT";
    public string? EventType             { get; set; }
    public string? PassportPaymentId     { get; set; }
    public long?   IdBrebRetiro          { get; set; }
    public string? PayloadHash           { get; set; }
    public string? PayloadSanitizedJson  { get; set; }
    public bool    SignatureValid         { get; set; }
    public bool    Processed             { get; set; }
    public DateTime  FechaRecibido       { get; set; }
    public DateTime? FechaProcesado      { get; set; }
    public string? ErrorMessage          { get; set; }
}
