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
    int     AlertasCount);
