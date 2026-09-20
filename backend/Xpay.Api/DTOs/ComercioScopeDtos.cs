namespace Xpay.Api.DTOs;

// ── Scope del usuario comercio ────────────────────────────────────────────────

public record ComercioScope(
    long    IdUsuario,
    string  RolComercio,
    long    IdComercioAliado,
    long?   IdComercioExistente,
    long?   IdEstablecimiento,
    bool    PuedeVerTodoComercio,
    bool    PuedeDisponerRecursos,
    bool    PuedeLiquidarAnticipado,
    bool    PuedeEnviarBreb,
    bool    PuedeAnularVentasDiaActual,
    bool    PuedeGenerarQr
);

// ── Usuarios operativos ────────────────────────────────────────────────────────

public record ComercioUsuarioOperativoResponse(
    long    IdComercioUsuario,
    long    IdComercioAliado,
    long?   IdComercioExistente,
    long?   IdEstablecimiento,
    string? NombreEstablecimiento,
    long    IdUsuario,
    string  NombreUsuario,
    string  RolComercio,
    string  Estado,
    string  CreatedAt
);

public class CrearComercioUsuarioRequest
{
    public long    IdUsuario          { get; set; }
    public string  RolComercio        { get; set; } = string.Empty;
    public long?   IdEstablecimiento  { get; set; }
}

public class ActualizarComercioUsuarioRequest
{
    public string? RolComercio       { get; set; }
    public long?   IdEstablecimiento { get; set; }
    public string? Estado            { get; set; }
}

// ── Ventas QR contexto ────────────────────────────────────────────────────────

public record VentaQrContextoResponse(
    long   IdContexto,
    long   IdVentaQr,
    long   IdComercioAliado,
    long   IdComercioExistente,
    long?  IdEstablecimiento,
    string? NombreEstablecimiento,
    long?  IdCajeroUsuario,
    string? NombreCajero,
    string CreatedAt
);

// ── Dashboard / totales ───────────────────────────────────────────────────────

public record DashboardComercioResponse(
    ComercioScope       Scope,
    decimal             SaldoDisponible,
    int                 TotalVentas,
    decimal             ValorTotalVentas,
    int                 VentasContingencia,
    int                 VentasLiquidadas,
    int                 VentasNoDisponibles,
    decimal             ValorNoDisponible,
    string?             ProximaDisponibilidad
);

public record TotalesComercioResponse(
    string  Periodo,
    int     TotalVentas,
    decimal ValorBruto,
    decimal ValorComision,
    decimal ValorNeto,
    List<TotalesPorSede>     PorSede,
    List<TotalesPorCajero>   PorCajero
);

public record TotalesPorSede(
    long    IdEstablecimiento,
    string  NombreEstablecimiento,
    int     TotalVentas,
    decimal ValorBruto
);

public record TotalesPorCajero(
    long    IdCajeroUsuario,
    string  NombreCajero,
    int     TotalVentas,
    decimal ValorBruto
);

public class BackfillDemoContextoRequest
{
    public long IdComercioAliado    { get; set; }
    public long IdComercioExistente { get; set; }
    public long IdEstablecimiento   { get; set; }
}

public record VentaConContextoResponse(
    long    IdVentaQr,
    decimal ValorBruto,
    string  Estado,
    string  FechaVenta,
    long?   IdEstablecimiento,
    string? NombreEstablecimiento,
    long?   IdCajeroUsuario,
    string? NombreCajero,
    // XPAY-438 — extensión aditiva para la notificación operacional QR.
    // Estructural, desde VentaQr.IdTienda → comercio_tiendas; nunca inferido
    // de descripcion/establecimiento/caja. IdTienda es NOT NULL en VentaQr
    // (siempre se resuelve al pagar), NombreTienda puede ser null (tienda
    // borrada o sin nombre) — el consumidor debe tolerarlo.
    long    IdTienda,
    string? NombreTienda
);

// XPAY-438A — baseline de primer uso de la notificación operacional QR.
// Extensión mínima commerce-wide (sección 2 del ticket): evita que
// CommerceNotificationsContext tenga que drenar TODO el historial (con un
// tope de páginas) solo para descubrir el IdVentaQr más reciente — una
// única consulta MAX(id_venta_qr) por comercio, sin importar cuántas
// ventas históricas existan (0, 100, 2.000 o 100.000). 0 significa "el
// comercio no tiene ninguna VentaQr todavía".
public record UltimoIdVentaQrResponse(long IdVentaQr);
