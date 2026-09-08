namespace Xpay.Api.Integrations.MiDecisor;

// Resultado NORMALIZADO que el cliente MiDecisor (M2) entregará a la capa
// XPAY, para que ésta no tenga que recorrer el envelope crudo del proveedor.
//
// M1: sólo la forma. SIN DB, SIN lógica de negocio, SIN decisión de crédito.
//
// - Los campos "Raw" se conservan tal cual llegan (string) — la conversión a
//   int/decimal y su interpretación son responsabilidad de XPAY, no de esta
//   capa de integración.
// - NO define APROBADA / RECHAZADA / MontoAprobado / umbrales de score.
//   Convertir score/viabilidad/rating/montoSugerido en una decisión de
//   crédito requiere una regla de producto autorizada (bloqueador 037), que
//   NO forma parte de la integración.
// - `AlertasCount` en vez del texto de las alertas: las alertas son señales
//   de compliance; su detalle se decidirá al persistir (M3), no aquí.
public sealed record MiDecisorResultado(
    // "ACCEPTED" | "PRECONDITION_FAILED" | null (envelope `status`).
    string? EstadoEnvelope,
    // Cadena interna del proveedor, ej. "202 ACCEPTED" (NO es HTTP).
    string? ContentStatus,
    // `informacionRiesgo.conInformacion` — null si no vino el bloque.
    bool?   ConInformacion,
    // `informacionRiesgo.score` sin convertir.
    string? ScoreRaw,
    // "ALTA" | "MEDIA" | "BAJA" | null.
    string? Viabilidad,
    // "A" | "B" | "C" | "D" | "N" | null.
    string? RatingRecaudos,
    // `informacionRiesgo.montoSugerido` sin convertir ("0" = sin sugerencia).
    string? MontoSugeridoRaw,
    // Cantidad de alertas recibidas (0 si vino `[]` o ausente).
    int     AlertasCount)
{
    // M2.4a (captura P0, diseño 175/176/177) — valores RAW adicionales del
    // envelope necesarios para el futuro motor M2.4b. Verbatim de STJ: sin
    // trim, sin normalizar, sin política crediticia. Propiedades no
    // posicionales (default null) para no romper las construcciones existentes.

    // `validacion.datosBasicos.tipoDocumento` (path único PN).
    public string? TipoDocumentoRaw { get; init; }

    // Dual-path estadoDocumento — ruta datosBasicos / informacionDemografica.
    public string? EstadoDocumentoDatosBasicosRaw { get; init; }
    public string? EstadoDocumentoInfoDemograficaRaw { get; init; }

    // Dual-path rangoEdad — ruta datosBasicos / informacionDemografica.
    public string? RangoEdadDatosBasicosRaw { get; init; }
    public string? RangoEdadInfoDemograficaRaw { get; init; }

    // `infoTransaccion.{anio,mes,dia}Consulta` — autoridad temporal del proveedor.
    public string? AnioConsultaRaw { get; init; }
    public string? MesConsultaRaw { get; init; }
    public string? DiaConsultaRaw { get; init; }

    // `comportamientoCrediticio.comportamientoPago.vectorComportamiento[]`.
    // Null cuando el bloque comportamientoPago no vino (ver
    // VectorComportamientoBloquePresente). Orden y duplicados preservados.
    public IReadOnlyList<MiDecisorVectorComportamientoItemRaw>? VectorComportamientoRaw { get; init; }

    // Distingue "bloque comportamientoPago ausente" de "vector presente vacío".
    public bool VectorComportamientoBloquePresente { get; init; }
}

// Elemento RAW del vector, verbatim de STJ.
public sealed record MiDecisorVectorComportamientoItemRaw(
    string? AnioMes,
    string? Comportamiento);
