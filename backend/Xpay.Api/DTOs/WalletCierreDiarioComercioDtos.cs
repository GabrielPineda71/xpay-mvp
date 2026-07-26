namespace Xpay.Api.DTOs;

// ── Preview en vivo (estado ABIERTO virtual — no persiste nada) ────────────
public record PreviewCierreDiarioDto(
    long     IdComercio,
    DateOnly Fecha,
    bool     YaGenerado,
    long?    IdCierreExistente,
    string?  EstadoCierreExistente,
    int      CantidadRecargas,
    decimal  ValorTotalRecaudado,
    decimal  ValorLiquidado,
    decimal  ValorPendiente,
    bool     VistaEnVivo,
    string   Mensaje);

// ── Generar cierre ──────────────────────────────────────────────────────────
public record GenerarCierreDiarioRequest(
    DateOnly Fecha,
    bool     ConfirmacionExplicita = false);

public record GenerarCierreDiarioResultDto(
    long     IdCierre,
    long     IdComercio,
    DateOnly FechaCierre,
    DateTime FechaHoraCorteUtc,
    string   CodigoUnico,
    int      CantidadRecargas,
    decimal  ValorTotalRecaudado,
    decimal  ValorLiquidadoAlGenerar,
    decimal  ValorPendienteAlGenerar,
    string   Estado,
    long     GeneradoPorUsuario,
    DateTime FechaGeneracion,
    string   NotaCorte);

// ── Bloques de totales reutilizables ────────────────────────────────────────
public record TotalesCierreDto(
    int     CantidadRecargas,
    decimal ValorTotal,
    decimal ValorLiquidado,
    decimal ValorPendiente);

public record RecargaEnCierreDto(
    long     IdRecarga,
    long?    IdTienda,
    string?  NombreTienda,
    long     IdUsuarioCajero,
    string?  NombreUsuarioCajero,
    long     IdUsuarioWallet,
    string?  NombreUsuarioWallet,
    decimal  Valor,
    bool     EstabaLiquidadaAlGenerar,
    DateTime FechaRecarga);

// ── Consulta lado comercio — respeta el alcance del solicitante ─────────────
// TotalesComercio solo viene poblado si el solicitante ve todo el comercio
// (ADMIN_COMERCIO). ADMIN_SEDE_COMERCIO y CAJERO reciben únicamente
// MiParticipacion, acotada a su sede o a sus propias recargas — nunca el
// consolidado de otras sedes/cajeros.
public record CierreDiarioDetalleDto(
    long                      IdCierre,
    long                      IdComercio,
    string?                   NombreComercio,
    DateOnly                  FechaCierre,
    DateTime                  FechaHoraCorteUtc,
    string                    CodigoUnico,
    string                    Estado,
    DateTime                  FechaGeneracion,
    DateTime?                 FechaRevision,
    DateTime?                 FechaCerrado,
    TotalesCierreDto?         TotalesComercio,
    TotalesCierreDto          MiParticipacion,
    string                    AlcanceParticipacion, // COMERCIO_COMPLETO | SEDE | PROPIO
    List<RecargaEnCierreDto>  Recargas);

public record CierreDiarioResumenDto(
    long              IdCierre,
    DateOnly          FechaCierre,
    string            Estado,
    string            CodigoUnico,
    TotalesCierreDto  MiParticipacion,
    string            AlcanceParticipacion);

// ── Consulta lado admin XPAY — snapshot vs. situación actual ────────────────
public record CierreDiarioAdminDetalleDto(
    long      IdCierre,
    long      IdComercio,
    string?   NombreComercio,
    long?     IdComercioAliado,
    DateOnly  FechaCierre,
    DateTime  FechaHoraCorteUtc,
    string    CodigoUnico,
    string    Estado,
    int       CantidadRecargas,
    decimal   ValorTotalRecaudado,
    decimal   ValorLiquidadoAlGenerar,
    decimal   ValorPendienteAlGenerar,
    decimal   ValorLiquidadoActual,
    decimal   ValorPendienteActual,
    long      GeneradoPorUsuario,
    string?   NombreGeneradoPor,
    DateTime  FechaGeneracion,
    long?     RevisadoPorUsuario,
    string?   NombreRevisadoPor,
    DateTime? FechaRevision,
    long?     CerradoPorUsuario,
    string?   NombreCerradoPor,
    DateTime? FechaCerrado,
    string?   ObservacionesAdmin,
    List<RecargaEnCierreDto> Recargas);

public record CierreDiarioAdminResumenDto(
    long     IdCierre,
    long     IdComercio,
    string?  NombreComercio,
    DateOnly FechaCierre,
    string   Estado,
    string   CodigoUnico,
    int      CantidadRecargas,
    decimal  ValorTotalRecaudado,
    decimal  ValorLiquidadoAlGenerar,
    decimal  ValorPendienteAlGenerar,
    decimal  ValorLiquidadoActual,
    decimal  ValorPendienteActual);

// ── Revisar / Cerrar (solo ADMIN_XPAY/SUPERUSUARIO) ──────────────────────────
public record RevisarCierreDiarioRequest(string? Observaciones = null);

public record CerrarCierreDiarioRequest(string? Observaciones = null);

public record TransicionCierreDiarioResultDto(
    long      IdCierre,
    string    Estado,
    long      IdUsuarioAdmin,
    DateTime  FechaTransicion);
