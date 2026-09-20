using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// XPAY-447 — ComercioScopeService.ObtenerQrComercioAsync (GET /api/comercio/
// mi-qr). Fix del bug confirmado en XPAY-446: MiComercioPage.tsx mostraba un
// código QR hardcodeado ("QR-DEMO-XPAY-QA-001") ajeno al comercio realmente
// autenticado. Cubre B1-B7 del ticket. Integración SQL REAL: la consulta
// hace join sobre datos reales de qr_comercios/comercio_tiendas, no
// soportado por providers InMemory/SQLite (mismo criterio ya establecido en
// ComercioScopeServiceVentasIncrementalTests.cs). Guard fail-closed
// idéntico al resto de la suite: local sin ConnectionStrings__XpayConnection
// → early-return (PASS, no SKIP); en CI sin la variable → FALLA.
//
// Deliberadamente construye ComercioScope a mano (sin pasar por
// ComercioUsuario/ComercioAliado/ResolverScopeUnicoAsync) — el objetivo aquí
// es probar el comportamiento de ObtenerQrComercioAsync en sí (query/scope/
// DTO), no la resolución de scope (ya cubierta en otras piezas del sistema).
[Collection("SqlIntegration")]
public sealed class ComercioScopeServiceQrComercioTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";

    // ── B1/B2 — cada comercio obtiene SOLO sus propios QR. ──────────────────
    [Fact]
    public async Task ObtenerQrComercio_DevuelveSoloLosQrDelComercioDelScope()
    {
        if (!TryConnString(out var cs)) return;
        var seed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var idComercioA = await seed.SembrarComercioAsync(creados);
            var idTiendaA   = await seed.SembrarTiendaAsync(idComercioA, "Tienda A", creados);
            var qrA         = await seed.SembrarQrAsync(idComercioA, idTiendaA, "QR-TEST-A-001", "ACTIVO", creados);

            var idComercioB = await seed.SembrarComercioAsync(creados);
            var idTiendaB   = await seed.SembrarTiendaAsync(idComercioB, "Tienda B", creados);
            var qrB         = await seed.SembrarQrAsync(idComercioB, idTiendaB, "QR-TEST-B-001", "ACTIVO", creados);

            await using var ctx = NuevoContexto(cs);
            var svc = new ComercioScopeService(ctx, NullLogger<ComercioScopeService>.Instance);

            var resultA = await svc.ObtenerQrComercioAsync(ScopeAdminComercio(idComercioA));
            var resultB = await svc.ObtenerQrComercioAsync(ScopeAdminComercio(idComercioB));

            var itemA = Assert.Single(resultA);
            Assert.Equal(qrA, itemA.IdQr);
            Assert.StartsWith("QR-TEST-A-001-", itemA.CodigoQr);
            Assert.DoesNotContain(resultA, r => r.IdQr == qrB); // B1: A nunca ve el QR de B.

            var itemB = Assert.Single(resultB);
            Assert.Equal(qrB, itemB.IdQr);
            Assert.DoesNotContain(resultB, r => r.IdQr == qrA); // B2: B nunca ve el QR de A.
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── B3 — el endpoint no acepta ningún selector de comercio: el scope
    // manipulado (rol/IdEstablecimiento distintos) sigue devolviendo
    // exclusivamente lo del IdComercioExistente del propio scope. ─────────
    [Fact]
    public async Task ObtenerQrComercio_NoExisteSelectorDeComercio_SoloUsaScopeIdComercioExistente()
    {
        if (!TryConnString(out var cs)) return;
        var seed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var idComercioA = await seed.SembrarComercioAsync(creados);
            var idTiendaA   = await seed.SembrarTiendaAsync(idComercioA, "Tienda A", creados);
            var qrA         = await seed.SembrarQrAsync(idComercioA, idTiendaA, "QR-TEST-A-002", "ACTIVO", creados);

            var idComercioB = await seed.SembrarComercioAsync(creados);
            var idTiendaB   = await seed.SembrarTiendaAsync(idComercioB, "Tienda B", creados);
            await seed.SembrarQrAsync(idComercioB, idTiendaB, "QR-TEST-B-002", "ACTIVO", creados);

            await using var ctx = NuevoContexto(cs);
            var svc = new ComercioScopeService(ctx, NullLogger<ComercioScopeService>.Instance);

            // Un CAJERO de A, con un IdEstablecimiento cualquiera (no existe
            // ningún parámetro en la firma del método para "pedir" el
            // comercio B) — el método sólo lee scope.IdComercioExistente.
            var scopeCajeroA = ScopeCajero(idComercioA) with { IdEstablecimiento = 999_999_999 };
            var result = await svc.ObtenerQrComercioAsync(scopeCajeroA);

            var item = Assert.Single(result);
            Assert.Equal(qrA, item.IdQr); // sigue siendo el de A, nunca el de B.
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── B4 — comercio sin QR activos obtiene lista vacía, nunca datos de
    // otro comercio. ─────────────────────────────────────────────────────
    [Fact]
    public async Task ObtenerQrComercio_SinQrActivos_DevuelveListaVacia()
    {
        if (!TryConnString(out var cs)) return;
        var seed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var idComercioSinQr = await seed.SembrarComercioAsync(creados); // sin tienda ni QR.

            var idComercioOtro = await seed.SembrarComercioAsync(creados);
            var idTiendaOtro   = await seed.SembrarTiendaAsync(idComercioOtro, "Tienda Otro", creados);
            await seed.SembrarQrAsync(idComercioOtro, idTiendaOtro, "QR-TEST-OTRO-001", "ACTIVO", creados);

            await using var ctx = NuevoContexto(cs);
            var svc = new ComercioScopeService(ctx, NullLogger<ComercioScopeService>.Instance);

            var result = await svc.ObtenerQrComercioAsync(ScopeAdminComercio(idComercioSinQr));

            Assert.Empty(result);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── B5 — un QR INACTIVO del mismo comercio no aparece. ──────────────
    [Fact]
    public async Task ObtenerQrComercio_QrInactivo_NoAparece()
    {
        if (!TryConnString(out var cs)) return;
        var seed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var idComercio = await seed.SembrarComercioAsync(creados);
            var idTienda   = await seed.SembrarTiendaAsync(idComercio, "Tienda X", creados);
            var qrActivo   = await seed.SembrarQrAsync(idComercio, idTienda, "QR-TEST-ACTIVO-001", "ACTIVO", creados);
            var qrInactivo = await seed.SembrarQrAsync(idComercio, idTienda, "QR-TEST-INACTIVO-001", "INACTIVO", creados);

            await using var ctx = NuevoContexto(cs);
            var svc = new ComercioScopeService(ctx, NullLogger<ComercioScopeService>.Instance);

            var result = await svc.ObtenerQrComercioAsync(ScopeAdminComercio(idComercio));

            var item = Assert.Single(result);
            Assert.Equal(qrActivo, item.IdQr);
            Assert.DoesNotContain(result, r => r.IdQr == qrInactivo);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── B6/B7 — múltiples QR activos del mismo comercio, en distintas
    // tiendas, se devuelven TODOS y correctamente asociados a su tienda. ──
    [Fact]
    public async Task ObtenerQrComercio_MultiplesQrYTiendas_DevuelveTodosConSuTiendaCorrecta()
    {
        if (!TryConnString(out var cs)) return;
        var seed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var idComercio = await seed.SembrarComercioAsync(creados);
            var idTienda1  = await seed.SembrarTiendaAsync(idComercio, "Tienda Norte", creados);
            var idTienda2  = await seed.SembrarTiendaAsync(idComercio, "Tienda Sur", creados);
            var qr1        = await seed.SembrarQrAsync(idComercio, idTienda1, "QR-TEST-MULTI-001", "ACTIVO", creados);
            var qr2        = await seed.SembrarQrAsync(idComercio, idTienda2, "QR-TEST-MULTI-002", "ACTIVO", creados);

            await using var ctx = NuevoContexto(cs);
            var svc = new ComercioScopeService(ctx, NullLogger<ComercioScopeService>.Instance);

            var result = await svc.ObtenerQrComercioAsync(ScopeAdminComercio(idComercio));

            Assert.Equal(2, result.Count); // B6: ambos, ninguno se pierde.

            var item1 = Assert.Single(result, r => r.IdQr == qr1);
            Assert.Equal(idTienda1, item1.IdTienda);
            Assert.Equal("Tienda Norte", item1.NombreTienda);

            var item2 = Assert.Single(result, r => r.IdQr == qr2);
            Assert.Equal(idTienda2, item2.IdTienda); // B7: cada QR mantiene SU propia tienda, no se mezclan.
            Assert.Equal("Tienda Sur", item2.NombreTienda);
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
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para XPAY-447 (QR real del comercio).");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    private sealed class Sembrados
    {
        public List<long> QrComercios { get; } = [];
        public List<long> Tiendas     { get; } = [];
        public List<long> Comercios   { get; } = [];
    }

    private sealed class SeedContext(string cs)
    {
        private long? _idUnidad;

        private async Task<long> IdUnidadAsync()
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
                NombreComercial = $"Comercio XPAY-447 {sufijo}",
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

        // codigoQrPrefijo se combina con un sufijo aleatorio — codigo_qr tiene
        // UNIQUE INDEX global (IX_qr_comercios_codigo); un literal fijo
        // colisionaría contra datos huérfanos de una corrida previa fallida
        // que no llegó a limpiar (LimpiarAsync es best-effort).
        public async Task<long> SembrarQrAsync(long idComercio, long idTienda, string codigoQrPrefijo, string estado, Sembrados creados)
        {
            await using var ctx = NuevoContexto(cs);
            var qr = new QrComercio
            {
                IdComercio    = idComercio,
                IdTienda      = idTienda,
                CodigoQr      = $"{codigoQrPrefijo}-{Guid.NewGuid():N}"[..40],
                Estado        = estado,
                FechaCreacion = DateTime.UtcNow,
            };
            ctx.QrComercios.Add(qr);
            await ctx.SaveChangesAsync();
            creados.QrComercios.Add(qr.IdQr);
            return qr.IdQr;
        }
    }

    private static async Task LimpiarAsync(string cs, Sembrados creados)
    {
        try
        {
            await using var ctx = NuevoContexto(cs);
            foreach (var id in creados.QrComercios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.qr_comercios WHERE id_qr = {id}");
            foreach (var id in creados.Tiendas)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.comercio_tiendas WHERE id_tienda = {id}");
            foreach (var id in creados.Comercios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.comercios WHERE id_comercio = {id}");
        }
        catch
        {
            // best-effort, mismo criterio que el resto de la suite SqlIntegration.
        }
    }
}
