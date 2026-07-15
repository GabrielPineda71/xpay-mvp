namespace Xpay.Api.DTOs;

// ── Condiciones de negociacion ────────────────────────────────────────────────

public record CondicionNegociacionResponse(
    long    IdCondicion,
    long    IdComercioAliado,
    int     DiasDisponibilidad,
    decimal PorcentajeDescuento,
    bool    AplicaIva,
    decimal PorcentajeIva,
    string  Estado,
    string  FechaInicio,
    string? FechaFin,
    string? Observaciones,
    string  CreatedAt
);

public record CrearCondicionRequest(
    int     DiasDisponibilidad,
    decimal PorcentajeDescuento,
    bool    AplicaIva,
    decimal PorcentajeIva,
    string  FechaInicio,
    string? FechaFin,
    string? Observaciones
);

public record ActualizarCondicionRequest(
    int?    DiasDisponibilidad,
    decimal? PorcentajeDescuento,
    bool?   AplicaIva,
    decimal? PorcentajeIva,
    string? Estado,
    string? FechaInicio,
    string? FechaFin,
    string? Observaciones
);

// ── Parámetros liquidación anticipada ─────────────────────────────────────────

public record ParametroLiquidacionResponse(
    long    IdParametro,
    long?   IdComercioAliado,
    int     DiasFaltantes,
    decimal PorcentajeDescuento,
    bool    AplicaIva,
    decimal PorcentajeIva,
    string  Estado,
    string  CreatedAt
);

public record CrearParametroLiquidacionRequest(
    long?   IdComercioAliado,
    int     DiasFaltantes,
    decimal PorcentajeDescuento,
    bool    AplicaIva,
    decimal PorcentajeIva
);

public record ActualizarParametroLiquidacionRequest(
    decimal? PorcentajeDescuento,
    bool?    AplicaIva,
    decimal? PorcentajeIva,
    string?  Estado
);

// ── Disponibilidad ventas QR ──────────────────────────────────────────────────

public record DisponibilidadVentaResponse(
    long    IdDisponibilidad,
    long    IdVentaQr,
    decimal ValorBruto,
    // Convenio
    decimal PorcentajeDescuentoConvenio,
    decimal ValorDescuentoConvenio,
    bool    AplicaIvaConvenio,
    decimal PorcentajeIvaConvenio,
    decimal ValorIvaConvenio,
    decimal ValorNetoProgramado,
    // Metadata
    int     DiasDisponibilidad,
    string  FechaVenta,
    string  FechaDisponibleProgramada,
    int     DiasFaltantes,
    // Anticipado
    decimal PorcentajeDescuentoAnticipado,
    decimal ValorDescuentoAnticipado,
    bool    AplicaIvaAnticipado,
    decimal PorcentajeIvaAnticipado,
    decimal ValorIvaAnticipado,
    decimal ValorNetoSiLiquidaAhora,
    // Estado
    string  Estado,
    string? TipoLiberacion,
    string? FechaLiberacion,
    decimal? ValorNetoLiberado,
    long?   IdTransaccionLedgerLiberacion
);

public record LiquidarAhoraRequest(
    long    IdComercio,
    string? Observaciones
);

public record LiquidarAhoraResponse(
    long    IdDisponibilidad,
    long    IdVentaQr,
    decimal ValorBruto,
    // Convenio
    decimal ValorDescuentoConvenio,
    decimal ValorIvaConvenio,
    // Anticipado
    decimal ValorDescuentoAnticipado,
    decimal ValorIvaAnticipado,
    // Resultado
    decimal ValorNetoLiberado,
    decimal PorcentajeDescuentoAnticipado,
    int     DiasFaltantes,
    string  Estado,
    string  TipoLiberacion,
    string  FechaLiberacion,
    long    IdTransaccionLedger,
    decimal NuevoSaldoDisponible
);

public record LiberarManualRequest(
    string? Observaciones
);

// ── Mi disponibilidad (vista comercio) ────────────────────────────────────────

public record MiDisponibilidadResponse(
    decimal TotalNoDisponibleBruto,
    decimal TotalDescuentoConvenio,
    decimal TotalIvaConvenio,
    decimal TotalNoDisponibleNetoProgramado,
    decimal TotalDisponibleBruto,
    decimal TotalLiquidado,
    int     CantidadNoDisponible,
    string? ProximaFechaDisponibilidad,
    decimal ValorEstimadoProximaLiberacion
);

// ── Vincular ──────────────────────────────────────────────────────────────────

public record VincularComercioOperativoRequest(long IdComercioExistente);

// ── Importación masiva CSV de parámetros ──────────────────────────────────────

public enum ModoImportacion { VALIDAR, APLICAR }

public record FilaImportacionParametro(
    int     Linea,
    int     DiasFaltantes,
    decimal PorcentajeDescuento,
    bool    AplicaIva,
    decimal PorcentajeIva
);

public record FilaImportacionError(int Linea, string Mensaje);

public record ImportarParametrosResult(
    string  Scope,
    long?   IdComercioAliado,
    string  Modo,
    int     LineasLeidas,
    int     LineasValidas,
    int     LineasConError,
    int     DiasCreados,
    int     DiasActualizados,
    bool    Aplicado,
    List<FilaImportacionError> Errores
);
