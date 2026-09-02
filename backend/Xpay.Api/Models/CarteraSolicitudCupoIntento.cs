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

    // M2.3b3 — marca de auditoría de PURGA de los 6 campos crudos de arriba
    // (columna `resultado_purgado_utc DATETIME2 NULL`, migración 037).
    // NULL = no se ha aplicado una operación formal de purga. NOT NULL = una
    // purga formal (NULL de los 6 crudos) se aplicó en ese instante UTC;
    // inmutable. NO se infiere por nulabilidad de los crudos: un intento sin
    // resultado recibido tiene los crudos NULL pero resultado_purgado_utc NULL.
    public DateTime? ResultadoPurgadoUtc           { get; set; }

    // M2.4a — marca de CONSUMO durable de los 6 crudos de arriba (columna
    // `resultado_consumido_utc DATETIME2 NULL`, migración 038). NULL = no se
    // ha completado un consumo durable. NOT NULL = el resultado de este intento
    // se normalizó y se persistió como observaciones de la solicitud
    // (con_informacion_observado / score_observado / estado_score /
    // viabilidad_observada / rating_recaudos_observado / monto_sugerido_observado
    // / alertas_count_observado) en ese instante UTC; inmutable. NO es
    // fecha_decision (no hay veredicto), NO inicia el reloj de retención, NO
    // autoriza la purga por sí sola.
    public DateTime? ResultadoConsumidoUtc         { get; set; }
}
