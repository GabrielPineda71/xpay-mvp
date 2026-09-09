namespace Xpay.Api.Common;

// M2.4b — Cartera Ordinaria. Vocabulario CONTROLADO de códigos de motivo de la
// decisión crediticia (columna `codigo_motivo` de
// cartera_solicitud_cupo_motivos_decision y `codigo_motivo_decision` de
// cartera_solicitudes_cupo). Cerrado en XPAY-182. Sin CHECK en DB en esta
// fase: la disciplina de vocabulario vive aquí, en código.
//
// Precedencia §H para RECHAZOS determinables (política working copy):
//   1 DOC_NO_VIGENTE
//   2 TIPO_DOC_NO_ACEPTADO
//   3 (vacío — EDAD_FUERA_POLITICA retirado para nuevas decisiones 66+, XPAY-190 / ACTA 001)
//   4 SCORE_INSUFICIENTE
//   5 COMPORTAMIENTO_PAGO_ULTIMO_MES_NO_AL_DIA / COMPORTAMIENTO_PAGO_INSUFICIENTE
//   6 VIABILIDAD_BAJA
//   7 RATING_RECAUDOS_INSUFICIENTE
//   8 RESULTADO_SCORE_INSUFICIENTE
//
// NO_DECIDIBLE (no son rechazo): INFORMACION_INSUFICIENTE, VALOR_FUERA_POLITICA,
// EDAD_REQUIERE_REVISION_MANUAL (edad 66+, XPAY-190 / ACTA 001).
//
// `POSIBLE_SUPLANTACION` NO está aquí a propósito: es una señal operacional
// separada (senal_posible_suplantacion), no un motivo crediticio (§O.4).
public static class CarteraMotivoDecision
{
    public const string DocNoVigente                       = "DOC_NO_VIGENTE";
    public const string TipoDocNoAceptado                   = "TIPO_DOC_NO_ACEPTADO";
    // EDAD_FUERA_POLITICA — HISTÓRICO. El motor ya NO lo emite para edad 66+
    // (XPAY-190 / ACTA 001 → causa NO_DECIDIBLE EdadRequiereRevisionManual). Se
    // conserva la constante por compatibilidad y para decodificar decisiones
    // históricas ya persistidas. NO eliminar.
    public const string EdadFueraPolitica                   = "EDAD_FUERA_POLITICA";
    public const string ScoreInsuficiente                   = "SCORE_INSUFICIENTE";
    public const string ComportamientoPagoUltimoMesNoAlDia  = "COMPORTAMIENTO_PAGO_ULTIMO_MES_NO_AL_DIA";
    public const string ComportamientoPagoInsuficiente      = "COMPORTAMIENTO_PAGO_INSUFICIENTE";
    public const string ViabilidadBaja                      = "VIABILIDAD_BAJA";
    public const string RatingRecaudosInsuficiente          = "RATING_RECAUDOS_INSUFICIENTE";
    public const string ResultadoScoreInsuficiente          = "RESULTADO_SCORE_INSUFICIENTE";
    public const string InformacionInsuficiente             = "INFORMACION_INSUFICIENTE";
    public const string ValorFueraPolitica                  = "VALOR_FUERA_POLITICA";
    // M2.4b — edad 66+ (XPAY-190 / ACTA 001): NO es rechazo, NO es dato faltante,
    // NO es valor inválido/fuera de dominio. Es una causa NO_DECIDIBLE: la
    // solicitud pasa a PENDIENTE_REVISION_MANUAL con monto NULL.
    public const string EdadRequiereRevisionManual          = "EDAD_REQUIERE_REVISION_MANUAL";
}
