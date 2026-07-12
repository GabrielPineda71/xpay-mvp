namespace Xpay.Api.DTOs;

// ── Condiciones de negociacion ────────────────────────────────────────────────

public record CondicionNegociacionResponse(
    long    IdCondicion,
    long    IdComercioAliado,
    int     DiasDisponibilidad,
    decimal PorcentajeDescuento,
    bool    AplicaIva,
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
    string  FechaInicio,
    string? FechaFin,
    string? Observaciones
);

public record ActualizarCondicionRequest(
    int?    DiasDisponibilidad,
    decimal? PorcentajeDescuento,
    bool?   AplicaIva,
    string? Estado,
    string? FechaInicio,
    string? FechaFin,
    string? Observaciones
);

// ── Parámetros liquidación anticipada ─────────────────────────────────────────

public record ParametroLiquidacionResponse(
    long    IdParametro,
    int     DiasFaltantes,
    decimal PorcentajeDescuento,
    string  Estado,
    string  CreatedAt
);

public record CrearParametroLiquidacionRequest(
    int     DiasFaltantes,
    decimal PorcentajeDescuento
);

public record ActualizarParametroLiquidacionRequest(
    decimal? PorcentajeDescuento,
    string?  Estado
);

// ── Disponibilidad ventas QR ──────────────────────────────────────────────────

public record DisponibilidadVentaResponse(
    long    IdDisponibilidad,
    long    IdVentaQr,
    decimal ValorBruto,
    decimal ValorDescuento,
    decimal ValorNetoProgramado,
    int     DiasDisponibilidad,
    decimal PorcentajeDescuento,
    string  FechaVenta,
    string  FechaDisponibleProgramada,
    int     DiasFaltantes,
    decimal TasaAnticipada,
    decimal ValorDescuentoAnticipado,
    decimal ValorNetoSiLiquidaAhora,
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
    decimal ValorDescuentoConvenio,
    decimal ValorDescuentoAnticipado,
    decimal ValorNetoLiberado,
    decimal TasaAnticipada,
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
    decimal TotalNoDisponibleNetoProgramado,
    decimal TotalDisponibleBruto,
    decimal TotalLiquidado,
    int     CantidadNoDisponible,
    string? ProximaFechaDisponibilidad,
    decimal ValorEstimadoProximaLiberacion
);

// ── Vincular ──────────────────────────────────────────────────────────────────

public record VincularComercioOperativoRequest(long IdComercioExistente);
