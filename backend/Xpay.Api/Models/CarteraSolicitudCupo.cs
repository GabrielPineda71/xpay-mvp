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
    // M2.4a — snapshot NORMALIZADO y purga-seguro del resultado MiDecisor útil,
    // escrito por CarteraConsultaRiesgoStore.ConsumirResultadoRiesgoAsync. NO
    // son un veredicto crediticio (eso vive en DecisionCrediticia, que M2.4a
    // nunca toca). Sobreviven a la purga de crudos del intento (M2.3b3 no toca
    // esta tabla).
    public bool?     ConInformacionObservado           { get; set; }
    public int?      ScoreObservado                    { get; set; }
    public string?   EstadoScore                       { get; set; }
    public string?   ViabilidadObservada               { get; set; }
    public string?   RatingRecaudosObservado           { get; set; }
    public decimal?  MontoSugeridoObservado            { get; set; }
    public int?      AlertasCountObservado             { get; set; }
    // M2.4a — extensión de captura P0 (migración 039). RAW del proveedor, sin
    // normalizar, sin política crediticia. Purga-seguras (la purga de M2.3b3
    // no toca esta tabla). Las materializa ConsumirResultadoRiesgoAsync en la
    // MISMA transacción y AppLock que los 7 observados de arriba.
    public string?   TipoDocumentoObservado                { get; set; }
    public string?   EstadoDocumentoDatosBasicosRaw        { get; set; }
    public string?   EstadoDocumentoInfoDemograficaRaw     { get; set; }
    public string?   EstadoDocumentoCaptura                { get; set; } // PRESENTE / AUSENTE / CONFLICTO
    public string?   RangoEdadDatosBasicosRaw              { get; set; }
    public string?   RangoEdadInfoDemograficaRaw           { get; set; }
    public string?   RangoEdadCaptura                      { get; set; } // PRESENTE / AUSENTE / CONFLICTO
    public string?   ConsultaAnioRaw                       { get; set; }
    public string?   ConsultaMesRaw                        { get; set; }
    public string?   ConsultaDiaRaw                        { get; set; }
    public string?   ComportamientoVectorJson              { get; set; }
    public int?      ComportamientoVectorCount             { get; set; } // NULL = bloque ausente ; 0 = presente vacío
    // M2.4b — señal operacional separada (migración 040). NO es motivo crediticio,
    // NO es fraude probado. NULL = no evaluada/no disponible ; false = evaluada,
    // señal ausente ; true = señal presente (estadoDocumento = "Cancelada por
    // muerte o fallecido").
    public bool?     SenalPosibleSuplantacion             { get; set; }
    public int       NumeroIntento                     { get; set; } = 1;
    public long?     IdCupoOrdinario                   { get; set; }
    public string    CorrelationId                     { get; set; } = string.Empty;
    public DateTime  FechaSolicitud                    { get; set; }
    public DateTime? FechaDecision                     { get; set; }
    public DateTime? FechaMaterializacionCupo          { get; set; }
    public DateTime  FechaActualizacion                { get; set; }
}
