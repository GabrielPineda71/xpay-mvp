using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// XPAY-438 — modo notificación operacional commerce-wide de
// ComercioScopeService.ListarVentasAsync (GET /api/comercio/ventas
// ?desdeIdVentaQr=N). Cubre B1-B10 del ticket. Integración SQL REAL: la
// consulta hace JOIN/scope sobre datos reales de ventas_qr/comercio_tiendas,
// no soportado por providers InMemory/SQLite (mismo criterio ya establecido
// en la suite de concurrencia de Cartera). Guard fail-closed idéntico al
// resto de la suite: local sin ConnectionStrings__XpayConnection →
// early-return (PASS, no SKIP); en CI sin la variable → FALLA.
//
// Deliberadamente construye ComercioScope a mano (sin pasar por
// ComercioUsuario/ComercioAliado/ResolverScopeUnicoAsync) — el objetivo aquí
// es probar el comportamiento de ListarVentasAsync en sí (query/scope/DTO),
// no la resolución de scope (ya cubierta en otras piezas del sistema). Por
// la misma razón NO siembra comercio_ventas_qr_contexto en ningún test —
// B8 depende explícitamente de que el modo incremental funcione SIN esa
// tabla poblada.
[Collection("SqlIntegration")]
public sealed class ComercioScopeServiceVentasIncrementalTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";

    // ── B1 — modo histórico (sin desdeIdVentaQr) conserva el comportamiento
    // previo: orden DESC por fecha, respeta PuedeVerTodoComercio, y el DTO
    // extendido con IdTienda/NombreTienda no rompe nada existente. ─────────
    [Fact]
    public async Task ModoHistorico_SinCursor_ConservaComportamientoPrevioYExponeTienda()
    {
        if (!TryConnString(out var cs)) return;
        var seed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var idUnidad   = await seed.IdUnidadAsync();
            var idComercio = await seed.SembrarComercioAsync(creados);
            var idTienda   = await seed.SembrarTiendaAsync(idComercio, "Tienda Centro", creados);
            var idQr       = await seed.SembrarQrAsync(idComercio, idTienda, creados);
            var idWallet   = await seed.SembrarWalletAsync(idUnidad, creados);

            var v1 = await seed.SembrarVentaAsync(idUnidad, idComercio, idTienda, idQr, idWallet, 50_000m, creados);
            var v2 = await seed.SembrarVentaAsync(idUnidad, idComercio, idTienda, idQr, idWallet, 75_000m, creados);

            await using var ctx = NuevoContexto(cs);
            var svc = new ComercioScopeService(ctx, NullLogger<ComercioScopeService>.Instance);

            var scope = ScopeAdminComercio(idComercio);
            var result = await svc.ListarVentasAsync(scope, filtroSede: null, filtroCajero: null,
                fechaDesde: null, fechaHasta: null, desdeIdVentaQr: null);

            var ids = result.Select(r => r.IdVentaQr).ToList();
            Assert.Contains(v1, ids);
            Assert.Contains(v2, ids);

            var item = result.Single(r => r.IdVentaQr == v2);
            Assert.Equal(idTienda, item.IdTienda);
            Assert.Equal("Tienda Centro", item.NombreTienda);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── B2/B6 — el modo incremental sólo devuelve ventas del IdComercio del
    // scope autenticado; un cursor "manipulado" tomado del rango de ID de
    // OTRO comercio no filtra ni expone las ventas de ese otro comercio. ──
    [Fact]
    public async Task ModoIncremental_SoloComercioDelScope_CursorDeOtroComercioNoFiltraNiExpone()
    {
        if (!TryConnString(out var cs)) return;
        var seed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var idUnidad = await seed.IdUnidadAsync();

            // Comercio B primero → su venta obtiene un IdVentaQr MENOR.
            var idComercioB = await seed.SembrarComercioAsync(creados);
            var idTiendaB   = await seed.SembrarTiendaAsync(idComercioB, "Tienda B", creados);
            var idQrB       = await seed.SembrarQrAsync(idComercioB, idTiendaB, creados);
            var idWalletB   = await seed.SembrarWalletAsync(idUnidad, creados);
            var ventaB      = await seed.SembrarVentaAsync(idUnidad, idComercioB, idTiendaB, idQrB, idWalletB, 10_000m, creados);

            // Comercio A después → su venta obtiene un IdVentaQr MAYOR que ventaB.
            var idComercioA = await seed.SembrarComercioAsync(creados);
            var idTiendaA   = await seed.SembrarTiendaAsync(idComercioA, "Tienda A", creados);
            var idQrA       = await seed.SembrarQrAsync(idComercioA, idTiendaA, creados);
            var idWalletA   = await seed.SembrarWalletAsync(idUnidad, creados);
            var ventaA      = await seed.SembrarVentaAsync(idUnidad, idComercioA, idTiendaA, idQrA, idWalletA, 20_000m, creados);

            await using var ctx = NuevoContexto(cs);
            var svc = new ComercioScopeService(ctx, NullLogger<ComercioScopeService>.Instance);

            // Scope del comercio A (CAJERO — PuedeVerTodoComercio:false, para
            // probar también que el modo incremental no depende de esa
            // bandera). Cursor = ventaB - 1: si el filtro por comercio NO se
            // aplicara, ventaB también calificaría (ventaB > ventaB-1).
            var scopeA = ScopeCajero(idComercioA);
            var result = await svc.ListarVentasAsync(scopeA, null, null, null, null, desdeIdVentaQr: ventaB - 1);

            var ids = result.Select(r => r.IdVentaQr).ToList();
            Assert.Contains(ventaA, ids);      // propia venta del comercio A, sí aparece
            Assert.DoesNotContain(ventaB, ids); // venta de otro comercio, NUNCA debe aparecer
            Assert.All(result, r => Assert.Equal(20_000m, r.ValorBruto)); // solo datos de A en el set devuelto
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── B3/B4 — el cursor sólo devuelve IdVentaQr > cursor, en orden
    // ascendente. ─────────────────────────────────────────────────────────
    [Fact]
    public async Task ModoIncremental_CursorFiltraMayorQueYOrdenaAscendente()
    {
        if (!TryConnString(out var cs)) return;
        var seed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var idUnidad    = await seed.IdUnidadAsync();
            var idComercio  = await seed.SembrarComercioAsync(creados);
            var idTienda    = await seed.SembrarTiendaAsync(idComercio, "Tienda Ascendente", creados);
            var idQr        = await seed.SembrarQrAsync(idComercio, idTienda, creados);
            var idWallet    = await seed.SembrarWalletAsync(idUnidad, creados);

            var v1 = await seed.SembrarVentaAsync(idUnidad, idComercio, idTienda, idQr, idWallet, 1_000m, creados);
            var v2 = await seed.SembrarVentaAsync(idUnidad, idComercio, idTienda, idQr, idWallet, 2_000m, creados);
            var v3 = await seed.SembrarVentaAsync(idUnidad, idComercio, idTienda, idQr, idWallet, 3_000m, creados);

            await using var ctx = NuevoContexto(cs);
            var svc = new ComercioScopeService(ctx, NullLogger<ComercioScopeService>.Instance);
            var scope = ScopeAdminComercio(idComercio);

            var result = await svc.ListarVentasAsync(scope, null, null, null, null, desdeIdVentaQr: v1);

            Assert.Equal(new[] { v2, v3 }, result.Select(r => r.IdVentaQr).ToArray()); // orden ASC, v1 excluido
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── B5 — tope defensivo de 100 filas por request. ───────────────────
    [Fact]
    public async Task ModoIncremental_TopeDe100Filas()
    {
        if (!TryConnString(out var cs)) return;
        var seed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var idUnidad   = await seed.IdUnidadAsync();
            var idComercio = await seed.SembrarComercioAsync(creados);
            var idTienda   = await seed.SembrarTiendaAsync(idComercio, "Tienda Volumen", creados);
            var idQr       = await seed.SembrarQrAsync(idComercio, idTienda, creados);
            var idWallet   = await seed.SembrarWalletAsync(idUnidad, creados);

            var ids = await seed.SembrarVentasEnLoteAsync(idUnidad, idComercio, idTienda, idQr, idWallet, cantidad: 105, creados);
            var minId = ids.Min();

            await using var ctx = NuevoContexto(cs);
            var svc = new ComercioScopeService(ctx, NullLogger<ComercioScopeService>.Instance);
            var scope = ScopeAdminComercio(idComercio);

            var result = await svc.ListarVentasAsync(scope, null, null, null, null, desdeIdVentaQr: minId - 1);

            Assert.Equal(100, result.Count);
            var ordenados = ids.OrderBy(x => x).ToList();
            Assert.Equal(ordenados.Take(100).ToArray(), result.Select(r => r.IdVentaQr).ToArray());
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── B7 — el DTO incluye IdTienda/NombreTienda correctos en modo
    // incremental. ────────────────────────────────────────────────────────
    [Fact]
    public async Task ModoIncremental_DtoIncluyeTiendaCorrecta()
    {
        if (!TryConnString(out var cs)) return;
        var seed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var idUnidad   = await seed.IdUnidadAsync();
            var idComercio = await seed.SembrarComercioAsync(creados);
            var idTienda   = await seed.SembrarTiendaAsync(idComercio, "Sucursal Norte", creados);
            var idQr       = await seed.SembrarQrAsync(idComercio, idTienda, creados);
            var idWallet   = await seed.SembrarWalletAsync(idUnidad, creados);
            var venta      = await seed.SembrarVentaAsync(idUnidad, idComercio, idTienda, idQr, idWallet, 120_000m, creados);

            await using var ctx = NuevoContexto(cs);
            var svc = new ComercioScopeService(ctx, NullLogger<ComercioScopeService>.Instance);
            var scope = ScopeAdminComercio(idComercio);

            var result = await svc.ListarVentasAsync(scope, null, null, null, null, desdeIdVentaQr: venta - 1);
            var item = Assert.Single(result);

            Assert.Equal(venta, item.IdVentaQr);
            Assert.Equal(120_000m, item.ValorBruto);
            Assert.Equal(idTienda, item.IdTienda);
            Assert.Equal("Sucursal Norte", item.NombreTienda);
            // Sección modo commerce-wide: sede/cajero no aplican aquí.
            Assert.Null(item.IdEstablecimiento);
            Assert.Null(item.IdCajeroUsuario);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── B8 — el modo incremental commerce-wide NO depende de
    // comercio_ventas_qr_contexto (IdEstablecimiento/IdCajeroUsuario) — la
    // venta aparece aunque esa tabla no tenga NINGUNA fila para ella
    // (situación real de producción, confirmada en XPAY-436/437). ────────
    [Fact]
    public async Task ModoIncremental_NoDependeDeContextoEstablecimientoCajero()
    {
        if (!TryConnString(out var cs)) return;
        var seed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var idUnidad   = await seed.IdUnidadAsync();
            var idComercio = await seed.SembrarComercioAsync(creados);
            var idTienda   = await seed.SembrarTiendaAsync(idComercio, "Tienda Sin Contexto", creados);
            var idQr       = await seed.SembrarQrAsync(idComercio, idTienda, creados);
            var idWallet   = await seed.SembrarWalletAsync(idUnidad, creados);
            var venta      = await seed.SembrarVentaAsync(idUnidad, idComercio, idTienda, idQr, idWallet, 30_000m, creados);
            // Deliberadamente NO se inserta ninguna fila en
            // comercio_ventas_qr_contexto para esta venta.

            await using var ctx = NuevoContexto(cs);
            var svc = new ComercioScopeService(ctx, NullLogger<ComercioScopeService>.Instance);

            // Rol CAJERO, con IdEstablecimiento que NO coincide con nada real
            // — en el modo histórico esto habría dado 0 resultados (bug
            // documentado en XPAY-436/437); en incremental debe funcionar.
            var scopeCajero = ScopeCajero(idComercio) with { IdEstablecimiento = 999_999_999 };
            var result = await svc.ListarVentasAsync(scopeCajero, null, null, null, null, desdeIdVentaQr: venta - 1);

            var item = Assert.Single(result);
            Assert.Equal(venta, item.IdVentaQr);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── B9/B10 — LECTURA pura: ninguna escritura en ventas_qr,
    // comercio_ventas_qr_contexto ni wallet_movimientos como efecto de
    // llamar al modo incremental. ────────────────────────────────────────
    [Fact]
    public async Task ModoIncremental_EsLecturaPura_SinEscribirVentasContextoNiWalletMovimientos()
    {
        if (!TryConnString(out var cs)) return;
        var seed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var idUnidad   = await seed.IdUnidadAsync();
            var idComercio = await seed.SembrarComercioAsync(creados);
            var idTienda   = await seed.SembrarTiendaAsync(idComercio, "Tienda Lectura", creados);
            var idQr       = await seed.SembrarQrAsync(idComercio, idTienda, creados);
            var idWallet   = await seed.SembrarWalletAsync(idUnidad, creados);
            await seed.SembrarVentaAsync(idUnidad, idComercio, idTienda, idQr, idWallet, 40_000m, creados);

            await using var ctxAntes = NuevoContexto(cs);
            var ventasAntes    = await ctxAntes.VentasQr.CountAsync();
            var contextoAntes  = await ctxAntes.ComercioVentasQrContexto.CountAsync();
            var movsAntes      = await ctxAntes.WalletMovimientos.CountAsync();

            await using var ctx = NuevoContexto(cs);
            var svc = new ComercioScopeService(ctx, NullLogger<ComercioScopeService>.Instance);
            var scope = ScopeAdminComercio(idComercio);
            await svc.ListarVentasAsync(scope, null, null, null, null, desdeIdVentaQr: 0);

            await using var ctxDespues = NuevoContexto(cs);
            Assert.Equal(ventasAntes,   await ctxDespues.VentasQr.CountAsync());
            Assert.Equal(contextoAntes, await ctxDespues.ComercioVentasQrContexto.CountAsync());
            Assert.Equal(movsAntes,     await ctxDespues.WalletMovimientos.CountAsync());
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── XPAY-438A §2 — ObtenerUltimoIdVentaQrAsync (baseline de primer uso
    // sin drenar historial). Corrige el riesgo real encontrado en la
    // revisión: el baseline anterior drenaba por páginas (tope 2.000 filas)
    // y quedaba congelado en una venta antigua para un comercio con más de
    // 2.000 VentaQr. ─────────────────────────────────────────────────────

    [Fact]
    public async Task ObtenerUltimoIdVentaQr_SinVentas_Devuelve0()
    {
        if (!TryConnString(out var cs)) return;
        var seed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var idComercio = await seed.SembrarComercioAsync(creados); // sin ninguna VentaQr.

            await using var ctx = NuevoContexto(cs);
            var svc = new ComercioScopeService(ctx, NullLogger<ComercioScopeService>.Instance);
            var scope = ScopeAdminComercio(idComercio);

            var idVentaQr = await svc.ObtenerUltimoIdVentaQrAsync(scope);

            Assert.Equal(0, idVentaQr);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task ObtenerUltimoIdVentaQr_ConMasDe2000Ventas_DevuelveLaMasRecienteNoLaNumero2000()
    {
        if (!TryConnString(out var cs)) return;
        var seed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var idUnidad   = await seed.IdUnidadAsync();
            var idComercio = await seed.SembrarComercioAsync(creados);
            var idTienda   = await seed.SembrarTiendaAsync(idComercio, "Tienda Volumen 438A", creados);
            var idQr       = await seed.SembrarQrAsync(idComercio, idTienda, creados);
            var idWallet   = await seed.SembrarWalletAsync(idUnidad, creados);

            var ids = await seed.SembrarVentasEnLoteAsync(idUnidad, idComercio, idTienda, idQr, idWallet, cantidad: 2050, creados);
            var maxIdReal = ids.Max();

            await using var ctx = NuevoContexto(cs);
            var svc = new ComercioScopeService(ctx, NullLogger<ComercioScopeService>.Instance);
            var scope = ScopeAdminComercio(idComercio);

            var idVentaQr = await svc.ObtenerUltimoIdVentaQrAsync(scope);

            // Prueba directa del hallazgo del ticket: el baseline debe ser la
            // venta MÁS RECIENTE real (fila #2.050), nunca la #2.000 (el tope
            // que tenía el mecanismo de drenaje por páginas ya retirado del
            // baseline).
            Assert.Equal(maxIdReal, idVentaQr);
            Assert.NotEqual(ids.OrderBy(x => x).Take(2000).Max(), idVentaQr);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task ObtenerUltimoIdVentaQr_SoloConsideraElComercioDelScope()
    {
        if (!TryConnString(out var cs)) return;
        var seed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var idUnidad = await seed.IdUnidadAsync();

            var idComercioA = await seed.SembrarComercioAsync(creados);
            var idTiendaA   = await seed.SembrarTiendaAsync(idComercioA, "Tienda A 438A", creados);
            var idQrA       = await seed.SembrarQrAsync(idComercioA, idTiendaA, creados);
            var idWalletA   = await seed.SembrarWalletAsync(idUnidad, creados);
            var ventaA      = await seed.SembrarVentaAsync(idUnidad, idComercioA, idTiendaA, idQrA, idWalletA, 10_000m, creados);

            // Comercio B, sembrado DESPUÉS → su venta tiene un IdVentaQr MAYOR
            // que el de A (prueba real de aislamiento, no solo "está vacío").
            var idComercioB = await seed.SembrarComercioAsync(creados);
            var idTiendaB   = await seed.SembrarTiendaAsync(idComercioB, "Tienda B 438A", creados);
            var idQrB       = await seed.SembrarQrAsync(idComercioB, idTiendaB, creados);
            var idWalletB   = await seed.SembrarWalletAsync(idUnidad, creados);
            await seed.SembrarVentaAsync(idUnidad, idComercioB, idTiendaB, idQrB, idWalletB, 20_000m, creados);

            await using var ctx = NuevoContexto(cs);
            var svc = new ComercioScopeService(ctx, NullLogger<ComercioScopeService>.Instance);
            var scopeA = ScopeCajero(idComercioA);

            var idVentaQr = await svc.ObtenerUltimoIdVentaQrAsync(scopeA);

            Assert.Equal(ventaA, idVentaQr); // nunca la de B, aunque su id sea mayor.
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── Helpers de scope ─────────────────────────────────────────────────

    private static ComercioScope ScopeAdminComercio(long idComercio) => new(
        IdUsuario: 1, RolComercio: "ADMIN_COMERCIO", IdComercioAliado: 1,
        IdComercioExistente: idComercio, IdEstablecimiento: null,
        PuedeVerTodoComercio: true, PuedeDisponerRecursos: true,
        PuedeLiquidarAnticipado: true, PuedeEnviarBreb: true,
        PuedeAnularVentasDiaActual: true, PuedeGenerarQr: false);

    private static ComercioScope ScopeCajero(long idComercio) => new(
        IdUsuario: 2, RolComercio: "CAJERO", IdComercioAliado: 1,
        IdComercioExistente: idComercio, IdEstablecimiento: null,
        PuedeVerTodoComercio: false, PuedeDisponerRecursos: false,
        PuedeLiquidarAnticipado: false, PuedeEnviarBreb: false,
        PuedeAnularVentasDiaActual: false, PuedeGenerarQr: true);

    // ── Infraestructura SQL ──────────────────────────────────────────────

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para XPAY-438 (notificación operacional QR).");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    private sealed class Sembrados
    {
        public List<long> VentasQr      { get; } = [];
        public List<long> QrComercios   { get; } = [];
        public List<long> Tiendas       { get; } = [];
        public List<long> Comercios     { get; } = [];
        public List<long> Wallets       { get; } = [];
    }

    private sealed class SeedContext(string cs)
    {
        private long? _idUnidad;

        public async Task<long> IdUnidadAsync()
        {
            if (_idUnidad is not null) return _idUnidad.Value;
            await using var ctx = NuevoContexto(cs);
            _idUnidad = await ctx.Database
                .SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL")
                .SingleAsync();
            return _idUnidad.Value;
        }

        public async Task<long> SembrarComercioAsync(Sembrados creados)
        {
            var idUnidad = await IdUnidadAsync();
            await using var ctx = NuevoContexto(cs);
            var sufijo = Guid.NewGuid().ToString("N")[..10];
            var comercio = new Comercio
            {
                IdUnidadNegocio = idUnidad,
                NombreComercial = $"Comercio XPAY-438 {sufijo}",
                Estado          = "ACTIVO",
                FechaCreacion   = DateTime.UtcNow,
            };
            ctx.Comercios.Add(comercio);
            await ctx.SaveChangesAsync();
            creados.Comercios.Add(comercio.IdComercio);
            return comercio.IdComercio;
        }

        public async Task<long> SembrarTiendaAsync(long idComercio, string nombreTienda, Sembrados creados)
        {
            await using var ctx = NuevoContexto(cs);
            var tienda = new ComercioTienda
            {
                IdComercio    = idComercio,
                NombreTienda  = nombreTienda,
                Estado        = "ACTIVO",
                FechaCreacion = DateTime.UtcNow,
            };
            ctx.ComercioTiendas.Add(tienda);
            await ctx.SaveChangesAsync();
            creados.Tiendas.Add(tienda.IdTienda);
            return tienda.IdTienda;
        }

        public async Task<long> SembrarQrAsync(long idComercio, long idTienda, Sembrados creados)
        {
            await using var ctx = NuevoContexto(cs);
            var qr = new QrComercio
            {
                IdComercio    = idComercio,
                IdTienda      = idTienda,
                CodigoQr      = $"QR-438-{Guid.NewGuid():N}",
                Estado        = "ACTIVO",
                FechaCreacion = DateTime.UtcNow,
            };
            ctx.QrComercios.Add(qr);
            await ctx.SaveChangesAsync();
            creados.QrComercios.Add(qr.IdQr);
            return qr.IdQr;
        }

        public async Task<long> SembrarWalletAsync(long idUnidad, Sembrados creados)
        {
            await using var ctx = NuevoContexto(cs);
            var wallet = new Wallet
            {
                IdUnidadNegocio = idUnidad,
                TipoWallet      = "PERSONA",
                IdPersona       = null,
                NombreWallet    = $"w_438_{Guid.NewGuid():N}"[..20],
                Estado          = "ACTIVA",
                FechaCreacion   = DateTime.UtcNow,
            };
            ctx.Wallets.Add(wallet);
            await ctx.SaveChangesAsync();
            creados.Wallets.Add(wallet.IdWallet);
            return wallet.IdWallet;
        }

        public async Task<long> SembrarVentaAsync(
            long idUnidad, long idComercio, long idTienda, long idQr, long idWallet, decimal valorBruto, Sembrados creados)
        {
            await using var ctx = NuevoContexto(cs);
            var venta = new VentaQr
            {
                IdUnidadNegocio   = idUnidad,
                IdComercio        = idComercio,
                IdTienda          = idTienda,
                IdQr              = idQr,
                IdWalletUsuario   = idWallet,
                ValorBruto        = valorBruto,
                ValorComision     = 0,
                ValorIvaComision  = 0,
                ValorNetoComercio = valorBruto,
                Estado            = "CONTINGENCIA",
                FechaVenta        = DateTime.UtcNow,
            };
            ctx.VentasQr.Add(venta);
            await ctx.SaveChangesAsync();
            creados.VentasQr.Add(venta.IdVentaQr);
            return venta.IdVentaQr;
        }

        public async Task<List<long>> SembrarVentasEnLoteAsync(
            long idUnidad, long idComercio, long idTienda, long idQr, long idWallet, int cantidad, Sembrados creados)
        {
            await using var ctx = NuevoContexto(cs);
            var ventas = Enumerable.Range(0, cantidad).Select(_ => new VentaQr
            {
                IdUnidadNegocio   = idUnidad,
                IdComercio        = idComercio,
                IdTienda          = idTienda,
                IdQr              = idQr,
                IdWalletUsuario   = idWallet,
                ValorBruto        = 1_000m,
                ValorComision     = 0,
                ValorIvaComision  = 0,
                ValorNetoComercio = 1_000m,
                Estado            = "CONTINGENCIA",
                FechaVenta        = DateTime.UtcNow,
            }).ToList();
            ctx.VentasQr.AddRange(ventas);
            await ctx.SaveChangesAsync();
            var ids = ventas.Select(v => v.IdVentaQr).ToList();
            creados.VentasQr.AddRange(ids);
            return ids;
        }
    }

    private static async Task LimpiarAsync(string cs, Sembrados creados)
    {
        try
        {
            await using var ctx = NuevoContexto(cs);
            foreach (var id in creados.VentasQr)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.ventas_qr WHERE id_venta_qr = {id}");
            foreach (var id in creados.QrComercios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.qr_comercios WHERE id_qr = {id}");
            foreach (var id in creados.Tiendas)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.comercio_tiendas WHERE id_tienda = {id}");
            foreach (var id in creados.Wallets)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.wallets WHERE id_wallet = {id}");
            foreach (var id in creados.Comercios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.comercios WHERE id_comercio = {id}");
        }
        catch
        {
            // best-effort, mismo criterio que el resto de la suite SqlIntegration.
        }
    }
}
