namespace Xpay.Api.DTOs;

// ── Acciones disponibles — evita que el frontend infiera permisos por su cuenta ──
public record CajaAccionesDisponiblesDto(
    bool PuedeIniciarCuadre,
    bool PuedeCerrar,
    bool PuedeRevisar,
    bool PuedeVerComprobante,
    bool PuedeCorregirFondoInicial);

// ── Caja — response común a abrir / consulta actual / corregir fondo inicial /
// iniciar cuadre / cerrar / revisar (diseño Fase 70.4, sección 18) ──────────
public record CajaDto(
    long      IdCaja,
    long      IdComercio,
    string?   NombreComercio,
    long?     IdComercioAliado,
    long      IdEstablecimiento,
    string?   NombreEstablecimiento,
    long      IdUsuarioCajero,
    string?   NombreUsuarioCajero,
    DateOnly  FechaOperativa,
    DateTime  FechaAperturaUtc,
    DateTime? FechaCierreUtc,
    string    Estado,
    decimal   FondoInicial,
    decimal?  EfectivoEsperado,
    decimal?  EfectivoContado,
    decimal?  Diferencia,
    string?   TipoDiferencia, // "SOBRANTE"|"FALTANTE"|"EXACTO"|null — derivado, no persistido
    bool      CerradaAutomaticamente,
    string?   ObservacionesCajero,
    long?     RevisadoPorUsuario,
    string?   NombreRevisor,
    DateTime? FechaRevision,
    string?   ObservacionesRevision,
    CajaAccionesDisponiblesDto Acciones);

// ── Listado paginado — GET /api/comercio/cajas, GET /api/comercio/cajas/mis-cajas ──
public record CajaResumenDto(
    long      IdCaja,
    long      IdEstablecimiento,
    string?   NombreEstablecimiento,
    long      IdUsuarioCajero,
    string?   NombreUsuarioCajero,
    DateOnly  FechaOperativa,
    string    Estado,
    decimal   FondoInicial,
    decimal?  EfectivoEsperado,
    decimal?  EfectivoContado,
    decimal?  Diferencia,
    string?   TipoDiferencia);

public record PaginadoDto<T>(
    List<T> Items,
    int     Page,
    int     PageSize,
    int     TotalItems,
    int     TotalPages);

// ── Abrir caja ───────────────────────────────────────────────────────────────
// IdEstablecimiento: obligatorio y validado si el solicitante es ADMIN_COMERCIO;
// ignorado (se deriva del scope) para ADMIN_SEDE_COMERCIO/CAJERO.
public record AbrirCajaRequest(
    decimal FondoInicial,
    long?   IdEstablecimiento = null);

// ── Corregir fondo inicial — solo mientras ABIERTA y sin RECARGA_EFECTIVO ────
public record CorregirFondoInicialRequest(
    decimal FondoInicial,
    string  Motivo);

// ── Cerrar caja ──────────────────────────────────────────────────────────────
public record CerrarCajaRequest(
    decimal EfectivoContado,
    string? Observaciones = null);

// ── Revisar caja — nunca la propia (segregación de funciones) ───────────────
public record RevisarCajaRequest(
    string? Observaciones = null);
