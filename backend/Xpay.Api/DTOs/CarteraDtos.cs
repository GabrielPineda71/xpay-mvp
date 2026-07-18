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

// ── Confirmación real de utilización ────────────────────────────────────
public record ConfirmacionUtilizacionDto(
    long    IdUtilizacion,
    string  TipoUtilizacion,
    decimal ValorCapital,
    string  Estado,
    DateTime FechaDesembolso,
    decimal NuevoSaldoWallet,
    decimal NuevoCupoDisponible,
    List<CuotaSimuladaDto> Cuotas);

// ── Mi cupo (vista usuario) ────────────────────────────────────────────
public record MiCupoOrdinarioDto(
    long    IdCupo,
    long    IdWallet,
    decimal CupoAprobado,
    decimal CupoUsado,
    decimal CupoDisponible,
    string  Estado,
    DateTime FechaAprobacion,
    DateTime? FechaVencimiento);

// ── Mis créditos (vista usuario) ────────────────────────────────────────
public record MiCreditoDto(
    long    IdUtilizacion,
    long    NroCredito,
    string  TipoUtilizacion,
    decimal ValorCapital,
    string  Estado,
    DateTime? FechaDesembolso,
    int     TotalCuotas,
    int     CuotasPagadas,
    decimal SaldoPendiente,
    int?    ProximaCuota,
    decimal? ValorProximaCuota);

public record CuotaDetalleDto(
    long    IdCuota,
    int     NumeroCuota,
    string  FechaVencimiento,
    decimal ValorCapital,
    decimal ValorInteres,
    decimal ValorAval,
    decimal ValorAdmin,
    decimal ValorIva,
    decimal ValorGastosCobranza,
    decimal ValorTotal,
    decimal PagadoCapital,
    decimal PagadoInteres,
    decimal PagadoAval,
    decimal PagadoAdmin,
    decimal PagadoIva,
    decimal SaldoCuota,
    string  Estado);

// ── Pago manual de cuota desde Wallet ───────────────────────────────────
public record PagarCuotaWalletRequest(
    long    IdUtilizacion,
    decimal ValorPago,
    string  Pin);

public record CuotaAfectadaDto(
    long    IdCuota,
    int     NumeroCuota,
    decimal CapitalPagado,
    decimal InteresPagado,
    decimal AvalPagado,
    decimal AdminPagado,
    decimal IvaPagado,
    decimal ValorPagado,
    decimal SaldoCuotaDespues,
    string  Estado);

// ── Compra QR con Cupo Ordinario ────────────────────────────────────────
public record PagarQrConCupoRequest(
    string  QrCode,
    decimal ValorCompra,
    int     PlazoMeses,
    string  Frecuencia,
    string  Pin);

public record PagarQrConCupoResultDto(
    long    IdUtilizacion,
    long    IdVentaQr,
    long?   IdTransaccionLedger,
    decimal ValorCompra,
    decimal NuevoCupoUsado,
    decimal NuevoCupoDisponible,
    string  EstadoUtilizacion,
    string  EstadoVentaQr,
    List<CuotaSimuladaDto> Cuotas);

public record PagoCuotaResultDto(
    long    IdPago,
    long?   IdTransaccionLedger,
    decimal ValorPago,
    decimal SaldoWalletAntes,
    decimal SaldoWalletDespues,
    decimal CupoUsadoAntes,
    decimal CupoUsadoDespues,
    decimal CupoDisponibleAntes,
    decimal CupoDisponibleDespues,
    decimal CapitalPagado,
    decimal InteresesPagados,
    decimal AvalPagado,
    decimal AdminPagado,
    decimal IvaPagado,
    List<CuotaAfectadaDto> CuotasAfectadas);
