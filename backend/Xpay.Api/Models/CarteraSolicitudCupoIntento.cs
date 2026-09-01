namespace Xpay.Api.Models;

public class CarteraSolicitudCupoIntento
{
    public long      IdIntento                     { get; set; }
    public long      IdSolicitud                   { get; set; }
    public int       NumeroIntento                 { get; set; }
    public Guid      IdempotencyKey                { get; set; }
    public DateTime  FechaInicio                   { get; set; }
    public DateTime? FechaFin                      { get; set; }
    public string?   ResultadoTecnico              { get; set; }
    public int?      HttpStatusObservado           { get; set; }
    public string?   ContentStatusObservado        { get; set; }
    public string    CorrelationId                 { get; set; } = string.Empty;
    public bool      EsIntentoConResultadoUtil     { get; set; }
}
