using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// XPAY-451 §10 — identidad mínima del pagador en la notificación
// operacional QR (hallazgo P3 de XPAY-450: la notificación no identificaba
// quién pagó). Cubre B7-B9 del ticket, sobre el modo incremental de
// ComercioScopeService.ListarVentasAsync. Integración SQL REAL, mismo
// criterio SqlIntegration del resto de la suite.
[Collection("SqlIntegration")]
public sealed class ComercioScopeServicePagadorDisplayTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";

    // ── B7 — el resultado incremental incluye el display mínimo del
    // pagador, en formato "PrimerNombre + inicial de PrimerApellido". ───
    [Fact]
    public async Task ListarVentasIncremental_IncluyePagadorDisplayMinimo()
    {
        if (!TryConnString(out var cs)) return;
        var seed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var idUnidad   = await seed.IdUnidadAsync();
            var idComercio = await seed.SembrarComercioAsync(creados);
            var idTienda   = await seed.SembrarTiendaAsync(idComercio, "Tienda P3", creados);
            var idQr       = await seed.SembrarQrAsync(idComercio, idTienda, creados);
            var idWallet   = await seed.SembrarWalletConPersonaAsync(idUnidad, "Gabriel", "Pineda", creados);
            var idVenta    = await seed.SembrarVentaAsync(idUnidad, idComercio, idTienda, idQr, idWallet, 100m, creados);

            await using var ctx = NuevoContexto(cs);
            var svc = new ComercioScopeService(ctx, NullLogger<ComercioScopeService>.Instance);

            var result = await svc.ListarVentasAsync(ScopeAdminComercio(idComercio), null, null, null, null, desdeIdVentaQr: idVenta - 1);

            var item = Assert.Single(result);
            Assert.Equal("Gabriel P.", item.PagadorDisplay);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // Fallback: sin PrimerNombre registrado → "Cliente XPAY" (nunca el
    // username técnico).
    [Fact]
    public async Task ListarVentasIncremental_SinPrimerNombre_UsaFallbackClienteXpayNoUsername()
    {
        if (!TryConnString(out var cs)) return;
        var seed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var idUnidad   = await seed.IdUnidadAsync();
            var idComercio = await seed.SembrarComercioAsync(creados);
            var idTienda   = await seed.SembrarTiendaAsync(idComercio, "Tienda P3b", creados);
            var idQr       = await seed.SembrarQrAsync(idComercio, idTienda, creados);
            var idWallet   = await seed.SembrarWalletConPersonaAsync(idUnidad, primerNombre: null, primerApellido: null, creados);
            var idVenta    = await seed.SembrarVentaAsync(idUnidad, idComercio, idTienda, idQr, idWallet, 50m, creados);

            await using var ctx = NuevoContexto(cs);
            var svc = new ComercioScopeService(ctx, NullLogger<ComercioScopeService>.Instance);

            var result = await svc.ListarVentasAsync(ScopeAdminComercio(idComercio), null, null, null, null, desdeIdVentaQr: idVenta - 1);

            var item = Assert.Single(result);
            Assert.Equal("Cliente XPAY", item.PagadorDisplay);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── B8 — el aislamiento entre comercios (ya probado en XPAY-438 B1/B2/
    // B6) se mantiene con el campo nuevo: comercio B nunca ve ventas ni
    // identidad de comercio A. ───────────────────────────────────────────
    [Fact]
    public async Task ListarVentasIncremental_MantieneAislamientoEntreComercios()
    {
        if (!TryConnString(out var cs)) return;
        var seed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var idUnidad = await seed.IdUnidadAsync();

            var idComercioA = await seed.SembrarComercioAsync(creados);
            var idTiendaA   = await seed.SembrarTiendaAsync(idComercioA, "Tienda A", creados);
            var idQrA       = await seed.SembrarQrAsync(idComercioA, idTiendaA, creados);
            var idWalletA   = await seed.SembrarWalletConPersonaAsync(idUnidad, "Ana", "Torres", creados);
            var idVentaA    = await seed.SembrarVentaAsync(idUnidad, idComercioA, idTiendaA, idQrA, idWalletA, 10_000m, creados);

            var idComercioB = await seed.SembrarComercioAsync(creados);
            var idTiendaB   = await seed.SembrarTiendaAsync(idComercioB, "Tienda B", creados);
            var idQrB       = await seed.SembrarQrAsync(idComercioB, idTiendaB, creados);
            var idWalletB   = await seed.SembrarWalletConPersonaAsync(idUnidad, "Beto", "Ruiz", creados);
            await seed.SembrarVentaAsync(idUnidad, idComercioB, idTiendaB, idQrB, idWalletB, 20_000m, creados);

            await using var ctx = NuevoContexto(cs);
            var svc = new ComercioScopeService(ctx, NullLogger<ComercioScopeService>.Instance);

            var resultB = await svc.ListarVentasAsync(ScopeCajero(idComercioB), null, null, null, null, desdeIdVentaQr: idVentaA - 1);

            Assert.DoesNotContain(resultB, r => r.PagadorDisplay == "Ana P." || r.PagadorDisplay == "Ana T.");
            Assert.All(resultB, r => Assert.NotEqual(idVentaA, r.IdVentaQr));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── B9 — el DTO nunca expone documento/email/teléfono/username, ni
    // siquiera indirectamente (verificado por reflexión sobre el tipo). ──
    [Fact]
    public void VentaConContextoResponse_NoExponeDocumentoEmailTelefonoUsername()
    {
        var propiedades = typeof(VentaConContextoResponse).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(propiedades, n => n.Contains("Documento", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propiedades, n => n.Contains("Email", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propiedades, n => n.Contains("Correo", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propiedades, n => n.Contains("Telefono", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propiedades, n => n.Contains("Celular", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propiedades, n => n == "NombreUsuario");
        Assert.DoesNotContain(propiedades, n => n == "IdWalletUsuario");
        Assert.Contains(propiedades, n => n == "PagadorDisplay");
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
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para XPAY-451 (identidad del pagador).");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    private sealed class Sembrados
    {
        public List<long> VentasQr  { get; } = [];
        public List<long> QrComercios { get; } = [];
        public List<long> Tiendas   { get; } = [];
        public List<long> Comercios { get; } = [];
        public List<long> Wallets   { get; } = [];
        public List<long> Personas  { get; } = [];
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
            var comercio = new Comercio
            {
                IdUnidadNegocio = idUnidad,
                NombreComercial = $"Comercio XPAY-451 {Guid.NewGuid():N}"[..40],
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
                CodigoQr      = $"QR-451-PD-{Guid.NewGuid():N}"[..40],
                Estado        = "ACTIVO",
                FechaCreacion = DateTime.UtcNow,
            };
            ctx.QrComercios.Add(qr);
            await ctx.SaveChangesAsync();
            creados.QrComercios.Add(qr.IdQr);
            return qr.IdQr;
        }

        public async Task<long> SembrarWalletConPersonaAsync(
            long idUnidad, string? primerNombre, string? primerApellido, Sembrados creados)
        {
            await using var ctx = NuevoContexto(cs);
            var ahora = DateTime.UtcNow;
            var sufijo = Guid.NewGuid().ToString("N")[..10];

            var persona = new Persona
            {
                IdUnidadNegocio = idUnidad,
                TipoDocumento   = "CC",
                NumeroDocumento = $"77{(uint)Guid.NewGuid().GetHashCode() % 10_000_000:D7}",
                PrimerNombre    = primerNombre,
                PrimerApellido  = primerApellido,
                Celular         = "3000000000",
                Pais            = "Colombia",
                Estado          = "ACTIVA",
                FechaCreacion   = ahora,
            };
            ctx.Personas.Add(persona);
            await ctx.SaveChangesAsync();
            creados.Personas.Add(persona.IdPersona);

            var wallet = new Wallet
            {
                IdUnidadNegocio = idUnidad,
                TipoWallet      = "PERSONA",
                IdPersona       = persona.IdPersona,
                NombreWallet    = $"w_pd_{sufijo}",
                Estado          = "ACTIVA",
                FechaCreacion   = ahora,
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
            foreach (var id in creados.Personas)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.personas WHERE id_persona = {id}");
            foreach (var id in creados.Comercios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.comercios WHERE id_comercio = {id}");
        }
        catch
        {
            // best-effort, mismo criterio que el resto de la suite SqlIntegration.
        }
    }
}
