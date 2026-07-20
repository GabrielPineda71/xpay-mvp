namespace Xpay.Api.DTOs;

// ── Recaudos pendientes ──────────────────────────────────────────────────
public record RecaudoPendienteComercioDto(
    long     IdRecarga,
    long     IdComercio,
    string?  NombreComercio,
    long?    IdComercioAliado,
    long?    IdTienda,
    string?  NombreTienda,
    long     IdUsuarioCajero,
    string?  NombreUsuarioCajero,
    long     IdUsuarioWallet,
    string?  NombreUsuarioWallet,
    long     IdWallet,
    decimal  Valor,
    string   Estado,
    DateTime FechaRecarga,
    string?  Observaciones);

public record ResumenRecaudosPendientesDto(
    long     IdComercio,
    string?  NombreComercio,
    long?    IdTienda,
    string?  NombreTienda,
    int      CantidadRecargas,
    decimal  ValorTotalPendiente);

// ── Liquidación de recaudos ──────────────────────────────────────────────
public record LiquidarRecaudosComercioRequest(
    long[]  IdsRecarga,
    string  MetodoLiquidacion,
    string? ReferenciaExterna,
    string? Observaciones);

public record LiquidarRecaudosComercioResultDto(
    long     IdLiquidacion,
    long?    IdTransaccionLedger,
    long     IdComercio,
    long?    IdTienda,
    string   MetodoLiquidacion,
    decimal  ValorTotal,
    int      CantidadRecargas,
    string   Estado,
    long     IdUsuarioAdmin,
    DateTime FechaLiquidacion,
    string   ComprobanteTexto);
