namespace Xpay.Api.Common;

// Cartera Ordinaria — originación de cupo. Identificadores internos
// estables para las columnas `estado_solicitud` y `decision_crediticia`
// de `cartera_solicitudes_cupo`.
//
// ESTRUCTURAL (ETAPA 2): enumera los valores autorizados. NO implica
// ninguna transición automática, regla de elegibilidad, scoring ni
// decisión de rechazo. La lógica que produzca APROBADA / RECHAZADA se
// implementa en una etapa posterior (motor de política).

// Estados de workflow de la solicitud.
public static class CarteraSolicitudCupoEstados
{
    public const string Recibida              = "RECIBIDA";
    public const string Validando             = "VALIDANDO";
    public const string ConsultandoRiesgo     = "CONSULTANDO_RIESGO";
    public const string EnEvaluacion          = "EN_EVALUACION";
    public const string AprobadaPendienteCupo = "APROBADA_PENDIENTE_CUPO";
    public const string Aprobada              = "APROBADA";
    public const string Rechazada             = "RECHAZADA";
    public const string ErrorProveedor        = "ERROR_PROVEEDOR";
}

// Resultado conceptual de la decisión crediticia.
public static class CarteraDecisionCrediticia
{
    public const string Pendiente = "PENDIENTE";
    public const string Aprobada  = "APROBADA";
    public const string Rechazada = "RECHAZADA";
}
