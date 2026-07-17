namespace Xpay.Api.DTOs;

// ── Parámetros de utilización ──────────────────────────────────────────
public record ParametroUtilizacionDto(
    long    IdParametro,
    string  TipoUtilizacion,
    decimal TasaEmv,
    decimal PorcAval,
    decimal PorcAdmin,
    bool    AplicaIva,
    decimal PorcIva,
    int     PlazoMin,
    int     PlazoMax,
    string  Frecuencia,
    decimal MontoMin,
    decimal MontoMax,
    string  Estado);

public record UpsertParametroUtilizacionRequest(
    decimal TasaEmv,
    decimal PorcAval,
    decimal PorcAdmin,
    bool    AplicaIva,
    decimal PorcIva,
    int     PlazoMin,
    int     PlazoMax,
    string  Frecuencia,
    decimal MontoMin,
    decimal MontoMax);

// ── Gastos de cobranza ─────────────────────────────────────────────────
public record GastosCobranzaDto(
    long    IdGasto,
    int     DiasDesde,
    int?    DiasHasta,
    string  TipoCobro,
    decimal ValorCobro,
    string? Descripcion,
    string  Estado);

public record UpsertGastosCobranzaRequest(
    int     DiasDesde,
    int?    DiasHasta,
    string  TipoCobro,
    decimal ValorCobro,
    string? Descripcion);

// ── Política de crédito ────────────────────────────────────────────────
public record PoliticaCreditoDto(
    long    IdPolitica,
    int?    ScoreDatacreditoMinimo,
    bool    RequiereVeriff,
    decimal CupoMinimo,
    decimal CupoMaximo,
    int     EdadMinima,
    int     EdadMaxima,
    string  Estado,
    DateTime VigenteDesde,
    DateTime? VigenteHasta);

public record UpsertPoliticaCreditoRequest(
    int?    ScoreDatacreditoMinimo,
    bool    RequiereVeriff,
    decimal CupoMinimo,
    decimal CupoMaximo,
    int     EdadMinima,
    int     EdadMaxima);

// ── Cupos ordinarios ───────────────────────────────────────────────────
public record CupoOrdinarioDto(
    long    IdCupo,
    long    IdUsuario,
    string  NombreUsuario,
    long    IdWallet,
    decimal CupoAprobado,
    decimal CupoUsado,
    decimal CupoDisponible,
    string  Estado,
    DateTime FechaAprobacion,
    DateTime? FechaVencimiento,
    string? Observaciones);

public record AsignarCupoRequest(
    long    IdUsuario,
    decimal CupoAprobado,
    DateTime? FechaVencimiento,
    string? Observaciones);

// ── Simulador ──────────────────────────────────────────────────────────
public record SimularUtilizacionRequest(
    string  TipoUtilizacion,         // COMPRA_COMERCIO | AVANCE_WALLET
    decimal ValorCapital,
    int     PlazoMeses,
    string  Frecuencia = "MENSUAL"); // MENSUAL | QUINCENAL

public record CuotaSimuladaDto(
    int     NumeroCuota,
    string  FechaVencimiento,        // yyyy-MM-dd
    decimal ValorCapital,
    decimal ValorInteres,
    decimal ValorAval,
    decimal ValorAdmin,
    decimal ValorIva,
    decimal ValorTotal,
    decimal SaldoCapitalAntes,
    decimal SaldoCapitalDespues);

public record SimulacionResultDto(
    string  TipoUtilizacion,
    decimal ValorCapital,
    decimal TasaEmv,
    decimal PorcAval,
    decimal PorcAdmin,
    bool    AplicaIva,
    decimal PorcIva,
    int     PlazoMeses,
    string  Frecuencia,
    int     TotalCuotas,
    decimal ValorCuota,
    decimal ValorTotalIntereses,
    decimal ValorTotalAval,
    decimal ValorTotalAdmin,
    decimal ValorTotalIva,
    decimal ValorTotalPagar,
    List<CuotaSimuladaDto> Cuotas);

// ── Mi cupo (vista usuario) ────────────────────────────────────────────
public record MiCupoOrdinarioDto(
    long    IdCupo,
    decimal CupoAprobado,
    decimal CupoUsado,
    decimal CupoDisponible,
    string  Estado,
    DateTime FechaAprobacion,
    DateTime? FechaVencimiento);
