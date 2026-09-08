using System.Text.Json.Serialization;

namespace Xpay.Api.Integrations.MiDecisor;

// Proyección MÍNIMA del envelope de respuesta de MiDecisor para Persona
// Natural — sólo lo que M2/M3 necesitan (Swagger MiDecisor.yaml, schemas
// `ConsultaPNResponse` / `RespuestaPN` / `InformacionRiesgoPN`; verificado
// contra `Ejemplo Salida MiDecisor PN.json`).
//
// NO se modelan los cientos de campos del Swagger (validacion,
// comportamientoCrediticio, endeudamiento, sugerencias, datos demográficos,
// etc.): no son necesarios para el resultado que XPAY va a persistir.
//
// TRANSPORTE DEFENSIVO — todo nullable; `Score` y `MontoSugerido` se
// mantienen como STRING tal cual llegan del proveedor (el contrato los
// declara `type: string`; `montoSugerido` "0" = sin sugerencia). La
// conversión a int/decimal y su significado interno viven en la capa XPAY,
// NO en este contrato externo.
//
// Los nombres JSON del proveedor se fijan EXPLÍCITAMENTE con
// [JsonPropertyName]: el cliente de M2 hará `JsonSerializer.Deserialize` con
// sus propias opciones y NO debe depender de ninguna naming policy ni de
// case-insensitivity implícitas.
//
// `response` / `message`: el Swagger (`ConsultaPNResponse`) los documenta a
// nivel envelope; el ejemplo real (`Ejemplo Salida MiDecisor PN.json`) los
// trae DENTRO de `content`. M1 los modela en AMBOS niveles de forma
// defensiva y sin decidir cuál prevalece.

public sealed class MiDecisorRespuestaEnvelope
{
    // Envelope: "ACCEPTED" (consulta OK) | "PRECONDITION_FAILED" (error de request).
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("content")]
    public MiDecisorContent? Content { get; set; }

    // Documentados por Swagger a nivel envelope; en el payload real observado
    // vienen dentro de content (ver MiDecisorContent). Se modelan aquí también.
    [JsonPropertyName("response")]
    public string? Response { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public sealed class MiDecisorContent
{
    // Cadena interna, ej. "202 ACCEPTED". NO es un código HTTP de transporte.
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("infoTransaccion")]
    public MiDecisorInfoTransaccion? InfoTransaccion { get; set; }

    [JsonPropertyName("respuesta")]
    public MiDecisorRespuestaPN? Respuesta { get; set; }

    // Ubicación observada en el ejemplo real (además del nivel envelope).
    [JsonPropertyName("response")]
    public string? Response { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public sealed class MiDecisorInfoTransaccion
{
    [JsonPropertyName("resultado")]
    public string? Resultado { get; set; }

    [JsonPropertyName("msjExcepcion")]
    public string? MsjExcepcion { get; set; }

    // Pares { clave, valor } por módulo (ej. CC=00, HC=13, TX=02).
    [JsonPropertyName("codigosRespuesta")]
    public List<MiDecisorCodigoRespuesta>? CodigosRespuesta { get; set; }

    // M2.4a (captura P0) — autoridad temporal de la consulta (mes de consulta
    // del proveedor). STRING crudo tal cual, sin validar gramática.
    [JsonPropertyName("anioConsulta")]
    public string? AnioConsulta { get; set; }

    [JsonPropertyName("mesConsulta")]
    public string? MesConsulta { get; set; }

    [JsonPropertyName("diaConsulta")]
    public string? DiaConsulta { get; set; }
}

public sealed class MiDecisorCodigoRespuesta
{
    [JsonPropertyName("clave")]
    public string? Clave { get; set; }

    [JsonPropertyName("valor")]
    public string? Valor { get; set; }
}

public sealed class MiDecisorRespuestaPN
{
    [JsonPropertyName("informacionRiesgo")]
    public MiDecisorInformacionRiesgoPN? InformacionRiesgo { get; set; }

    // M2.4a (captura P0) — proyección MÍNIMA: sólo los sub-bloques con campos
    // P0 (tipoDocumento / estadoDocumento / rangoEdad / vectorComportamiento).
    // NO se modelan número de documento, nombres, fecha de nacimiento, género,
    // dirección, ni ningún otro campo del envelope.
    [JsonPropertyName("validacion")]
    public MiDecisorValidacionPN? Validacion { get; set; }

    [JsonPropertyName("comportamientoCrediticio")]
    public MiDecisorComportamientoCrediticioPN? ComportamientoCrediticio { get; set; }
}

// M2.4a (captura P0) — sub-estructuras PN mínimas.
public sealed class MiDecisorValidacionPN
{
    [JsonPropertyName("datosBasicos")]
    public MiDecisorDatosBasicosPN? DatosBasicos { get; set; }

    [JsonPropertyName("informacionDemografica")]
    public MiDecisorInformacionDemograficaPN? InformacionDemografica { get; set; }
}

public sealed class MiDecisorDatosBasicosPN
{
    // Path único PN. STRING crudo — NO normalizar a "CC".
    [JsonPropertyName("tipoDocumento")]
    public string? TipoDocumento { get; set; }

    // Ruta dual-path (candidata A). STRING crudo.
    [JsonPropertyName("estadoDocumento")]
    public string? EstadoDocumento { get; set; }

    // Ruta dual-path (candidata A). STRING crudo.
    [JsonPropertyName("rangoEdad")]
    public string? RangoEdad { get; set; }
}

public sealed class MiDecisorInformacionDemograficaPN
{
    // Ruta dual-path (candidata B). STRING crudo.
    [JsonPropertyName("estadoDocumento")]
    public string? EstadoDocumento { get; set; }

    // Ruta dual-path (candidata B). STRING crudo.
    [JsonPropertyName("rangoEdad")]
    public string? RangoEdad { get; set; }
}

public sealed class MiDecisorComportamientoCrediticioPN
{
    [JsonPropertyName("comportamientoPago")]
    public MiDecisorComportamientoPagoPN? ComportamientoPago { get; set; }
}

public sealed class MiDecisorComportamientoPagoPN
{
    // Array crudo. Se preserva orden y duplicados; NO se normaliza.
    [JsonPropertyName("vectorComportamiento")]
    public List<MiDecisorVectorComportamientoItem>? VectorComportamiento { get; set; }
}

public sealed class MiDecisorVectorComportamientoItem
{
    [JsonPropertyName("anioMes")]
    public string? AnioMes { get; set; }

    [JsonPropertyName("comportamiento")]
    public string? Comportamiento { get; set; }
}

public sealed class MiDecisorInformacionRiesgoPN
{
    [JsonPropertyName("conInformacion")]
    public bool? ConInformacion { get; set; }

    [JsonPropertyName("msjExcepcion")]
    public string? MsjExcepcion { get; set; }

    // STRING según contrato — no convertir aquí.
    [JsonPropertyName("score")]
    public string? Score { get; set; }

    // Enum documentado: "ALTA" | "MEDIA" | "BAJA".
    [JsonPropertyName("viabilidad")]
    public string? Viabilidad { get; set; }

    // Enum documentado: "A" | "B" | "C" | "D" | "N".
    [JsonPropertyName("ratingRecaudos")]
    public string? RatingRecaudos { get; set; }

    // STRING según contrato ("0" = sin sugerencia) — no convertir aquí.
    [JsonPropertyName("montoSugerido")]
    public string? MontoSugerido { get; set; }

    [JsonPropertyName("alertas")]
    public List<MiDecisorAlerta>? Alertas { get; set; }
}

public sealed class MiDecisorAlerta
{
    // Único campo documentado que XPAY necesita conservar de cada alerta.
    // El payload real trae además `colocacion` / `modificacion` (fechas);
    // qué persistir de las alertas se decide en M3.
    [JsonPropertyName("alerta")]
    public string? Alerta { get; set; }
}
