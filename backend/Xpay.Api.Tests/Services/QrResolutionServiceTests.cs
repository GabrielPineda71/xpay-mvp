using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// XPAY-451 — QrResolutionService.ResolverQrActivoAsync, el núcleo READ-ONLY
// compartido entre el preview (GET /api/qr/resolver) y el pago real
// (PagoQrService.PagarQrAsync). Cubre B1-B6 del ticket. Integración SQL
// REAL: mismo criterio que el resto de la suite SqlIntegration — early
// return local sin ConnectionStrings__XpayConnection, ejecución real en CI.
[Collection("SqlIntegration")]
public sealed class QrResolutionServiceTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";

    // ── B1 — QR activo devuelve comercio/tienda correctos. ──────────────
    [Fact]
    public async Task ResolverQrActivo_ConQrActivo_DevuelveComercioYTiendaCorrectos()
    {
        if (!TryConnString(out var cs)) return;
        var seed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var (idComercio, nombreComercialSembrado) = await seed.SembrarComercioAsync(creados, "Comercio Preview");
            var idTienda   = await seed.SembrarTiendaAsync(idComercio, "Tienda Preview", "ACTIVO", creados);
            var codigoQr   = await seed.SembrarQrAsync(idComercio, idTienda, "ACTIVO", creados);

            await using var ctx = NuevoContexto(cs);

            var resultado = await QrResolutionService.ResolverQrActivoAsync(ctx, codigoQr);

            Assert.Equal(codigoQr, resultado.Qr.CodigoQr);
            Assert.Equal(idComercio, resultado.Comercio.IdComercio);
            // XPAY-455 — comparar contra el NombreComercial REALMENTE
            // persistido (con su sufijo GUID de unicidad), no contra el
            // prefijo "Comercio Preview" sin sufijo (ver causa raíz en el
            // comentario de SembrarComercioAsync).
            Assert.Equal(nombreComercialSembrado, resultado.Comercio.NombreComercial);
            Assert.Equal(idTienda, resultado.Tienda.IdTienda);
            Assert.Equal("Tienda Preview", resultado.Tienda.NombreTienda);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── B2 — QR inexistente no se presenta como válido. ─────────────────
    [Fact]
    public async Task ResolverQrActivo_QrInexistente_LanzaExcepcionSinDatos()
    {
        if (!TryConnString(out var cs)) return;
        await using var ctx = NuevoContexto(cs);

        var codigoInexistente = $"QR-NO-EXISTE-{Guid.NewGuid():N}";
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => QrResolutionService.ResolverQrActivoAsync(ctx, codigoInexistente));

        Assert.Contains("no existe", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── B3 — QR inactivo no se presenta como válido. ────────────────────
    [Fact]
    public async Task ResolverQrActivo_QrInactivo_LanzaExcepcion()
    {
        if (!TryConnString(out var cs)) return;
        var seed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var (idComercio, _) = await seed.SembrarComercioAsync(creados, "Comercio QR Inactivo");
            var idTienda   = await seed.SembrarTiendaAsync(idComercio, "Tienda", "ACTIVO", creados);
            var codigoQr   = await seed.SembrarQrAsync(idComercio, idTienda, "INACTIVO", creados);

            await using var ctx = NuevoContexto(cs);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => QrResolutionService.ResolverQrActivoAsync(ctx, codigoQr));

            Assert.Contains("QR", ex.Message);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── B4 — comercio inactivo no se presenta como válido, aunque el QR
    // y la tienda estén ACTIVO. ─────────────────────────────────────────
    [Fact]
    public async Task ResolverQrActivo_ComercioInactivo_LanzaExcepcion()
    {
        if (!TryConnString(out var cs)) return;
        var seed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var (idComercio, _) = await seed.SembrarComercioAsync(creados, "Comercio Inactivo", estado: "INACTIVO");
            var idTienda   = await seed.SembrarTiendaAsync(idComercio, "Tienda", "ACTIVO", creados);
            var codigoQr   = await seed.SembrarQrAsync(idComercio, idTienda, "ACTIVO", creados);

            await using var ctx = NuevoContexto(cs);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => QrResolutionService.ResolverQrActivoAsync(ctx, codigoQr));

            Assert.Contains("comercio", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── B5 — tienda inactiva no se presenta como válida. ────────────────
    [Fact]
    public async Task ResolverQrActivo_TiendaInactiva_LanzaExcepcion()
    {
        if (!TryConnString(out var cs)) return;
        var seed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var (idComercio, _) = await seed.SembrarComercioAsync(creados, "Comercio Tienda Inactiva");
            var idTienda   = await seed.SembrarTiendaAsync(idComercio, "Tienda Cerrada", "INACTIVO", creados);
            var codigoQr   = await seed.SembrarQrAsync(idComercio, idTienda, "ACTIVO", creados);

            await using var ctx = NuevoContexto(cs);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => QrResolutionService.ResolverQrActivoAsync(ctx, codigoQr));

            Assert.Contains("tienda", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── B6 — el receptor lo determina EXCLUSIVAMENTE CodigoQr; no existe
    // ningún parámetro de selector de comercio en la firma del método. ──
    [Fact]
    public async Task ResolverQrActivo_ElReceptorLoDeterminaSoloElCodigoQr_DosComerciosDistintos()
    {
        if (!TryConnString(out var cs)) return;
        var seed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var (idComercioA, _) = await seed.SembrarComercioAsync(creados, "Comercio A");
            var idTiendaA   = await seed.SembrarTiendaAsync(idComercioA, "Tienda A", "ACTIVO", creados);
            var codigoQrA   = await seed.SembrarQrAsync(idComercioA, idTiendaA, "ACTIVO", creados);

            var (idComercioB, _) = await seed.SembrarComercioAsync(creados, "Comercio B");
            var idTiendaB   = await seed.SembrarTiendaAsync(idComercioB, "Tienda B", "ACTIVO", creados);
            var codigoQrB   = await seed.SembrarQrAsync(idComercioB, idTiendaB, "ACTIVO", creados);

            await using var ctx = NuevoContexto(cs);

            // La firma del método (XpayDbContext, string codigoQr) — sin
            // ningún idComercio — es en sí misma la prueba estructural de
            // B6. Aquí se confirma además el comportamiento: cada código
            // resuelve exclusivamente a SU comercio.
            var resultadoA = await QrResolutionService.ResolverQrActivoAsync(ctx, codigoQrA);
            var resultadoB = await QrResolutionService.ResolverQrActivoAsync(ctx, codigoQrB);

            Assert.Equal(idComercioA, resultadoA.Comercio.IdComercio);
            Assert.Equal(idComercioB, resultadoB.Comercio.IdComercio);
            Assert.NotEqual(resultadoA.Comercio.IdComercio, resultadoB.Comercio.IdComercio);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── Infraestructura SQL ──────────────────────────────────────────────

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para XPAY-451 (preview de QR).");
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

        // XPAY-455 — devuelve también el NombreComercial REALMENTE persistido
        // (con su sufijo GUID de unicidad, necesario para no colisionar
        // contra SQL real) en vez de solo el id. Antes, B1 comparaba el
        // resultado contra el prefijo "nombre" sin sufijo, lo cual nunca
        // podía ser exactamente igual al valor sembrado — este método
        // ahora permite que el test compare contra el valor exacto que
        // efectivamente se guardó, sin debilitar la aserción a
        // Contains/StartsWith ni eliminar la unicidad del fixture.
        public async Task<(long IdComercio, string NombreComercial)> SembrarComercioAsync(Sembrados creados, string nombre, string estado = "ACTIVO")
        {
            var idUnidad = await IdUnidadAsync();
            await using var ctx = NuevoContexto(cs);
            var nombreComercial = $"{nombre} {Guid.NewGuid():N}"[..40];
            var comercio = new Comercio
            {
                IdUnidadNegocio = idUnidad,
                NombreComercial = nombreComercial,
                Estado          = estado,
                FechaCreacion   = DateTime.UtcNow,
            };
            ctx.Comercios.Add(comercio);
            await ctx.SaveChangesAsync();
            creados.Comercios.Add(comercio.IdComercio);
            return (comercio.IdComercio, nombreComercial);
        }

        public async Task<long> SembrarTiendaAsync(long idComercio, string nombreTienda, string estado, Sembrados creados)
        {
            await using var ctx = NuevoContexto(cs);
            var tienda = new ComercioTienda
            {
                IdComercio    = idComercio,
                NombreTienda  = nombreTienda,
                Estado        = estado,
                FechaCreacion = DateTime.UtcNow,
            };
            ctx.ComercioTiendas.Add(tienda);
            await ctx.SaveChangesAsync();
            creados.Tiendas.Add(tienda.IdTienda);
            return tienda.IdTienda;
        }

        public async Task<string> SembrarQrAsync(long idComercio, long idTienda, string estado, Sembrados creados)
        {
            await using var ctx = NuevoContexto(cs);
            // XPAY-453 — "QR-451-" (7) + Guid:N (32) = 39 caracteres; el
            // [..40] anterior asumía incorrectamente que la cadena siempre
            // tendría AL MENOS 40 caracteres y lanzaba
            // ArgumentOutOfRangeException en todo entorno con SQL real (ver
            // CI de XPAY-452) — localmente el early-return de
            // TryConnString nunca llegaba a ejecutar esta línea. codigo_qr
            // es NVARCHAR(100): no hace falta truncar, la cadena completa
            // ya es única (GUID) y cabe sin problema.
            var codigo = $"QR-451-{Guid.NewGuid():N}";
            var qr = new QrComercio
            {
                IdComercio    = idComercio,
                IdTienda      = idTienda,
                CodigoQr      = codigo,
                Estado        = estado,
                FechaCreacion = DateTime.UtcNow,
            };
            ctx.QrComercios.Add(qr);
            await ctx.SaveChangesAsync();
            creados.QrComercios.Add(qr.IdQr);
            return codigo;
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
