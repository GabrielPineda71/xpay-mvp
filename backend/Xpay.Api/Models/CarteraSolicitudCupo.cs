namespace Xpay.Api.Models;

public class CarteraSolicitudCupo
{
    public long      IdSolicitud                      { get; set; }
    public long      IdUsuario                         { get; set; }
    public long      IdPersona                         { get; set; }
    public decimal   MontoSolicitado                   { get; set; }
    public string    EstadoSolicitud                   { get; set; } = string.Empty;
    public string    DecisionCrediticia                { get; set; } = "PENDIENTE";
    public decimal?  MontoAprobado                     { get; set; }
    public string?   CodigoMotivoDecision              { get; set; }
    public long      IdPoliticaAplicada                { get; set; }
    public int?      ScoreDatacreditoMinimoAplicado    { get; set; }
    public decimal   CupoMinimoAplicado                { get; set; }
    public decimal   CupoMaximoAplicado                { get; set; }
    public int       EdadMinimaAplicada                { get; set; }
    public int       EdadMaximaAplicada                { get; set; }
    public int?      EdadCalculadaAlMomento            { get; set; }
    public int?      ScoreObservado                    { get; set; }
    public string?   EstadoScore                       { get; set; }
    public string?   ViabilidadObservada               { get; set; }
    public string?   RatingRecaudosObservado           { get; set; }
    public decimal?  MontoSugeridoObservado            { get; set; }
    public int       NumeroIntento                     { get; set; } = 1;
    public long?     IdCupoOrdinario                   { get; set; }
    public string    CorrelationId                     { get; set; } = string.Empty;
    public DateTime  FechaSolicitud                    { get; set; }
    public DateTime? FechaDecision                     { get; set; }
    public DateTime? FechaMaterializacionCupo          { get; set; }
    public DateTime  FechaActualizacion                { get; set; }
}
