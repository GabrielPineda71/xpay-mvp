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
    string? NombreCajero
);
