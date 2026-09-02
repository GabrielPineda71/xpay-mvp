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

    // M2.3b1 — fase durable del intento: PRE_CALL / ENVIO_INCIERTO / FINALIZADO
    // (ver CarteraIntentoFases). ENVIO_INCIERTO = XPAY cruzó la frontera de
    // no-retry-automático; NO significa "request enviado".
    public string    FaseIntento                   { get; set; } = "PRE_CALL";

    // M2.3b1 — resultado NORMALIZADO de MiDecisor, tal cual lo entrega
    // IMiDecisorClient (MiDecisorResultado). Strings CRUDOS: "-", "", "0",
    // dígitos, null — SIN convertir, SIN interpretar. NO son los campos de
    // decisión de la solicitud. NULL en todo intento sin resultado recibido.
    public bool?     ConInformacion                { get; set; }
    public string?   ScoreRaw                      { get; set; }
    public string?   ViabilidadRaw                 { get; set; }
    public string?   RatingRecaudosRaw             { get; set; }
    public string?   MontoSugeridoRaw              { get; set; }
    public int?      AlertasCount                  { get; set; }
}
