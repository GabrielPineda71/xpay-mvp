namespace Xpay.Api.Common;

// M2.3a — vocabulario del resultado técnico de un intento de consulta de
// riesgo (columna `resultado_tecnico VARCHAR(30)` de
// `cartera_solicitud_cupo_intentos`; sin CHECK constraint — ver migración 035).
//
// Todos los valores <= 30 caracteres. NO representa una decisión de crédito
// (eso vive en `decision_crediticia`, que M2.3a nunca toca). NO se deriva de
// score/monto: la clasificación usa exclusivamente la semántica estructurada
// de MiDecisorResultado (ConInformacion) y el tipo de excepción de dominio.
public static class CarteraConsultaRiesgoResultados
{
    // HTTP 200, envelope ACCEPTED.
    public const string Aceptada           = "ACEPTADA";         // ConInformacion == true
    public const string SinInformacion     = "SIN_INFORMACION";  // ConInformacion != true (false o ausente)

    // El proveedor respondió y rechazó / negó el acceso.
    public const string RechazadaProveedor = "RECHAZADA_PROVEEDOR"; // envelope PRECONDITION_FAILED
    public const string ErrorAutenticacion = "ERROR_AUTENTICACION"; // HTTP 401/403 o fallo de token

    // Errores que impiden interpretar el resultado; NO evidencia de que el
    // proveedor haya procesado una consulta de red.
    public const string ErrorConfiguracion   = "ERROR_CONFIGURACION";
    public const string ErrorProtocolo       = "ERROR_PROTOCOLO";
    public const string ErrorValidacionLocal = "ERROR_VALIDACION_LOCAL";

    // No se puede saber si el proveedor procesó/facturó la consulta
    // (timeout / red / 429 / 5xx, o cancelación del caller durante la llamada).
    public const string ResultadoIncierto = "RESULTADO_INCIERTO";
}

// Resultado NORMALIZADO que devuelve el orquestador a su llamador (tests /
// futuro flujo). NO expone score/monto crudos — el orquestador no los
// interpreta.
public sealed record ConsultaRiesgoResultado(
    string EstadoSolicitud,
    string ResultadoTecnico,
    bool   EsResultadoUtil);

// M2.3b1 — fases durables del intento (columna `fase_intento VARCHAR(20)` de
// `cartera_solicitud_cupo_intentos`, migración 036; CHECK enumera estos 3).
public static class CarteraIntentoFases
{
    // Intento insertado PRE-CALL por solicitar-cupo; aún nada enviado.
    public const string PreCall = "PRE_CALL";

    // XPAY cruzó la frontera después de la cual NO puede hacer retry
    // automático porque el proveedor PUEDE O NO haber sido contactado.
    // NO significa "request enviado" — un crash entre el commit de esta
    // fase y SendAsync deja el intento aquí sin que la llamada saliera.
    public const string EnvioIncierto = "ENVIO_INCIERTO";

    // El intento se completó (resultado_tecnico + fecha_fin persistidos).
    public const string Finalizado = "FINALIZADO";
}
