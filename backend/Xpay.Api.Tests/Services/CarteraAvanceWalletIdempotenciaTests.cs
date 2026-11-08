using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Common;
using Xpay.Api.Controllers;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Exceptions;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// XPAY-712 - idempotencia durable de ConfirmarAvanceWalletAsync
// (B-G + limite actual de respuesta). SQL local; sin conexion,
// sale temprano (patron TryConnString del repo). Datos sinteticos
// aislados por usuario/wallet propios y limpiados en finally.
[Collection("SqlIntegration")]
public sealed class CarteraAvanceWalletIdempotenciaTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";
    private const string Endpoint = IdempotencyEndpoints.CarteraAvanceWallet;
    private const decimal CupoAprobado = 700000m;

    private static SimularUtilizacionRequest Req(decimal valor = 100000m, int plazo = 1)
        => new("AVANCE_WALLET", valor, plazo, "MENSUAL");

    // B
    [Fact]
    public async Task Primera_ejecucion_un_efecto_y_completada()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var u = await SembrarAsync(cs, s);
            var r = await Svc(cs).ConfirmarAvanceWalletAsync(Req(), u.IdUsuario, Guid.NewGuid());

            Assert.False(r.Replayed);
            var e = await EfectosAsync(cs, u);
            Assert.Equal((1, 1, 1), (e.Utilizaciones, e.LedgerTx, e.MovWallet));
            Assert.Equal(100000m, e.CupoUsado);
            Assert.Equal(100000m, e.Saldo);

            var idem = Assert.Single(e.Idem);
            Assert.Equal("COMPLETADA", idem.Estado);
            Assert.Equal((short)200, idem.HttpStatus);
            Assert.Equal(r.Data.IdUtilizacion, idem.IdRecurso);
            Assert.NotNull(idem.IdTransaccionLedger);
            Assert.False(string.IsNullOrEmpty(idem.RespuestaDataJson));
        }
        finally { await LimpiarAsync(cs, s); }
    }

    // C
    [Fact]
    public async Task Replay_misma_key_devuelve_original_sin_efectos()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var u = await SembrarAsync(cs, s);
            var key = Guid.NewGuid();
            var r1 = await Svc(cs).ConfirmarAvanceWalletAsync(Req(), u.IdUsuario, key);
            var r2 = await Svc(cs).ConfirmarAvanceWalletAsync(Req(), u.IdUsuario, key);

            Assert.True(r2.Replayed);
            Assert.Equal(r1.Data.IdUtilizacion, r2.Data.IdUtilizacion);
            Assert.Equal(r1.Data.ValorCapital, r2.Data.ValorCapital);
            Assert.Equal(r1.Data.Estado, r2.Data.Estado);
            Assert.Equal(r1.Data.FechaDesembolso, r2.Data.FechaDesembolso);
            Assert.Equal(r1.Data.NuevoSaldoWallet, r2.Data.NuevoSaldoWallet);
            Assert.Equal(r1.Data.NuevoCupoDisponible, r2.Data.NuevoCupoDisponible);
            Assert.Equal(r1.Data.Cuotas, r2.Data.Cuotas);

            var e = await EfectosAsync(cs, u);
            Assert.Equal((1, 1, 1), (e.Utilizaciones, e.LedgerTx, e.MovWallet));
            Assert.Equal(100000m, e.CupoUsado);
            Assert.Single(e.Idem);
        }
        finally { await LimpiarAsync(cs, s); }
    }

    // D (servicio)
    [Fact]
    public async Task Misma_key_payload_distinto_conflicto_sin_efectos()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var u = await SembrarAsync(cs, s);
            var key = Guid.NewGuid();
            await Svc(cs).ConfirmarAvanceWalletAsync(Req(), u.IdUsuario, key);

            await Assert.ThrowsAsync<IdempotencyConflictException>(
                () => Svc(cs).ConfirmarAvanceWalletAsync(Req(200000m), u.IdUsuario, key));

            var e = await EfectosAsync(cs, u);
            Assert.Equal((1, 1, 1), (e.Utilizaciones, e.LedgerTx, e.MovWallet));
            Assert.Equal(100000m, e.CupoUsado);
        }
        finally { await LimpiarAsync(cs, s); }
    }

    // C/D (HTTP): 200, replay 200 + header, conflicto 409
    [Fact]
    public async Task Controller_replay_200_y_conflicto_409()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var u = await SembrarAsync(cs, s);
            var key = Guid.NewGuid();

            var c1 = Controller(cs, u.IdUsuario, key);
            Assert.IsType<OkObjectResult>(await c1.ConfirmarAvanceWallet(Req()));
            Assert.False(c1.Response.Headers.ContainsKey("Idempotent-Replayed"));

            var c2 = Controller(cs, u.IdUsuario, key);
            Assert.IsType<OkObjectResult>(await c2.ConfirmarAvanceWallet(Req()));
            Assert.Equal("true", c2.Response.Headers["Idempotent-Replayed"].ToString());

            var c3 = Controller(cs, u.IdUsuario, key);
            Assert.IsType<ConflictObjectResult>(await c3.ConfirmarAvanceWallet(Req(200000m)));

            var e = await EfectosAsync(cs, u);
            Assert.Equal((1, 1, 1), (e.Utilizaciones, e.LedgerTx, e.MovWallet));
        }
        finally { await LimpiarAsync(cs, s); }
    }

    // E (servicio + HTTP 503): reserva EN_PROCESO confirmada por otro camino.
    [Fact]
    public async Task En_proceso_no_resoluble_unavailable_y_503()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var u = await SembrarAsync(cs, s);
            var key = Guid.NewGuid();
            var hash = IdempotencyHashHelper.ComputeAvanceWalletHash(
                u.IdUsuario, "AVANCE_WALLET", 100000m, 1, "MENSUAL");
            await using (var ctx = NuevoContexto(cs))
            {
                ctx.WalletIdempotencias.Add(
                    IdempotencyStore.NuevaReserva(u.IdUsuario, Endpoint, key, hash));
                await ctx.SaveChangesAsync();
            }

            await Assert.ThrowsAsync<IdempotencyUnavailableException>(
                () => Svc(cs).ConfirmarAvanceWalletAsync(Req(), u.IdUsuario, key));

            var http = await Controller(cs, u.IdUsuario, key).ConfirmarAvanceWallet(Req());
            Assert.Equal(503, Assert.IsType<ObjectResult>(http).StatusCode);

            var e = await EfectosAsync(cs, u);
            Assert.Equal((0, 0, 0), (e.Utilizaciones, e.LedgerTx, e.MovWallet));
            Assert.Equal(0m, e.CupoUsado);
            Assert.Equal("EN_PROCESO", Assert.Single(e.Idem).Estado);
        }
        finally { await LimpiarAsync(cs, s); }
    }

    // F
    [Fact]
    public async Task Error_de_negocio_rollback_total()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var u = await SembrarAsync(cs, s);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Svc(cs).ConfirmarAvanceWalletAsync(Req(800000m), u.IdUsuario, Guid.NewGuid()));
            Assert.Contains("supera tu cupo disponible", ex.Message);

            var e = await EfectosAsync(cs, u);
            Assert.Equal((0, 0, 0), (e.Utilizaciones, e.LedgerTx, e.MovWallet));
            Assert.Equal(0m, e.CupoUsado);
            Assert.Equal(0m, e.Saldo);
            Assert.Empty(e.Idem);
        }
        finally { await LimpiarAsync(cs, s); }
    }

    // G: dos requests reales en paralelo con la misma key.
    [Fact]
    public async Task Concurrencia_misma_key_maximo_un_efecto()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var u = await SembrarAsync(cs, s);
            var key = Guid.NewGuid();

            async Task<string> Intento()
            {
                try
                {
                    var r = await Svc(cs).ConfirmarAvanceWalletAsync(Req(), u.IdUsuario, key);
                    return r.Replayed ? "REPLAY" : "EJECUTADO";
                }
                catch (IdempotencyUnavailableException) { return "UNAVAILABLE"; }
            }

            var res = await Task.WhenAll(Task.Run(Intento), Task.Run(Intento));

            Assert.Equal(1, res.Count(x => x == "EJECUTADO"));
            Assert.Contains(res.Single(x => x != "EJECUTADO"), new[] { "REPLAY", "UNAVAILABLE" });

            var e = await EfectosAsync(cs, u);
            Assert.Equal((1, 1, 1), (e.Utilizaciones, e.LedgerTx, e.MovWallet));
            Assert.Equal(100000m, e.CupoUsado);
            Assert.Equal("COMPLETADA", Assert.Single(e.Idem).Estado);
        }
        finally { await LimpiarAsync(cs, s); }
    }

    // Extra - DOCUMENTA la limitacion ACTUAL (respuesta_data_json
    // NVARCHAR(1000)): 12 cuotas exceden el limite y la operacion
    // falla en cerrado con rollback total. NO es especificacion
    // permanente; si el limite se amplia, este test debe cambiar.
    [Fact]
    public async Task Limite_actual_respuesta_grande_falla_cerrado()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var u = await SembrarAsync(cs, s);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Svc(cs).ConfirmarAvanceWalletAsync(Req(plazo: 12), u.IdUsuario, Guid.NewGuid()));
            Assert.Contains("límite de almacenamiento de idempotencia", ex.Message);

            var e = await EfectosAsync(cs, u);
            Assert.Equal((0, 0, 0), (e.Utilizaciones, e.LedgerTx, e.MovWallet));
            Assert.Equal(0m, e.CupoUsado);
            Assert.Equal(0m, e.Saldo);
            Assert.Empty(e.Idem);
        }
        finally { await LimpiarAsync(cs, s); }
    }

    // ── helpers ────────────────────────────────────────────────

    private sealed record SeedUser(long IdUsuario, long IdWallet);

    private sealed record Efectos(
        int Utilizaciones, int LedgerTx, int MovWallet,
        decimal CupoUsado, decimal Saldo, List<WalletIdempotencia> Idem);

    private sealed class Sembrados
    {
        public List<long> Personas { get; } = new();
        public List<long> Usuarios { get; } = new();
        public List<long> Wallets { get; } = new();
        public List<(long IdCupo, long IdUsuario)> Cupos { get; } = new();
    }

    private static CarteraOrdinariaService Svc(string cs)
    {
        var ctx = NuevoContexto(cs);
        return new CarteraOrdinariaService(
            ctx,
            new PagoQrService(ctx, NullLogger<PagoQrService>.Instance),
            NullLogger<CarteraOrdinariaService>.Instance);
    }

    private static CarteraOrdinariaController Controller(string cs, long idUsuario, Guid key)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["Idempotency-Key"] = key.ToString();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("idUsuario", idUsuario.ToString()) }, "test"));
        var c = new CarteraOrdinariaController(
            Svc(cs), null!, null!, null!, null!, null!, null!);
        c.ControllerContext = new ControllerContext { HttpContext = http };
        return c;
    }

    private static async Task<SeedUser> SembrarAsync(string cs, Sembrados s)
    {
        await using var ctx = NuevoContexto(cs);
        var ahora = DateTime.UtcNow;
        var sufijo = Guid.NewGuid().ToString("N")[..12];
        var idUnidad = await ctx.Database.SqlQueryRaw<long>(
            "SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL").SingleAsync();

        // Las migraciones pueden sembrar cupos con id_usuario fijo (p. ej. 021
        // para ids 3 y 4) sin usuario real. Si el id recien asignado ya tiene
        // un cupo, NO se toca: se descarta ese usuario y se crea otro.
        Persona persona = null!;
        Usuario usuario = null!;
        for (var intento = 1; ; intento++)
        {
            persona = new Persona
            {
                IdUnidadNegocio = idUnidad, TipoDocumento = "CC",
                NumeroDocumento = $"77{(uint)Guid.NewGuid().GetHashCode() % 10_000_000:D7}",
                PrimerNombre = "AvanceTest", PrimerApellido = "Sintetico", Celular = "3000000000",
                Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
            };
            ctx.Personas.Add(persona);
            await ctx.SaveChangesAsync();
            s.Personas.Add(persona.IdPersona);

            usuario = new Usuario
            {
                IdPersona = persona.IdPersona, NombreUsuario = $"avance_test_{Guid.NewGuid().ToString("N")[..12]}",
                PasswordHash = "x", Estado = "ACTIVO", FechaCreacion = ahora,
            };
            ctx.Usuarios.Add(usuario);
            await ctx.SaveChangesAsync();
            s.Usuarios.Add(usuario.IdUsuario);

            var idUsuario = usuario.IdUsuario;
            if (!await ctx.CarteraCuposOrdinarios.AsNoTracking().AnyAsync(c => c.IdUsuario == idUsuario))
                break;
            if (intento == 3)
                throw new InvalidOperationException(
                    "Fixture: 3 usuarios sinteticos consecutivos con cupo preexistente; no se puede aislar el test.");
        }

        var wallet = new Wallet
        {
            IdUnidadNegocio = idUnidad, TipoWallet = "PERSONA", IdPersona = persona.IdPersona,
            NombreWallet = $"w_{sufijo}", Estado = "ACTIVA", FechaCreacion = ahora,
        };
        ctx.Wallets.Add(wallet);
        await ctx.SaveChangesAsync();
        s.Wallets.Add(wallet.IdWallet);

        ctx.WalletSaldos.Add(new WalletSaldo { IdWallet = wallet.IdWallet, FechaActualizacion = ahora });
        var cupo = new CarteraCupoOrdinario
        {
            IdUsuario = usuario.IdUsuario, IdWallet = wallet.IdWallet,
            CupoAprobado = CupoAprobado, CupoUsado = 0m, Estado = "ACTIVO",
            FechaAprobacion = ahora, CreatedAt = ahora,
        };
        ctx.CarteraCuposOrdinarios.Add(cupo);
        await ctx.SaveChangesAsync();
        s.Cupos.Add((cupo.IdCupo, usuario.IdUsuario));

        return new SeedUser(usuario.IdUsuario, wallet.IdWallet);
    }

    // Conteos SOLO del usuario/wallet sembrados por el test.
    private static async Task<Efectos> EfectosAsync(string cs, SeedUser u)
    {
        await using var ctx = NuevoContexto(cs);
        var utils = await ctx.CarteraUtilizaciones.AsNoTracking()
            .Where(x => x.IdUsuario == u.IdUsuario).Select(x => (long?)x.IdUtilizacion).ToListAsync();
        var ledger = await ctx.LedgerTransacciones.AsNoTracking()
            .CountAsync(x => x.ReferenciaTipo == "cartera_utilizaciones" && utils.Contains(x.ReferenciaId));
        var mov = await ctx.WalletMovimientos.AsNoTracking().CountAsync(x => x.IdWallet == u.IdWallet);
        var cupo = await ctx.CarteraCuposOrdinarios.AsNoTracking()
            .Where(x => x.IdUsuario == u.IdUsuario).Select(x => x.CupoUsado).SingleAsync();
        var saldo = await ctx.WalletSaldos.AsNoTracking()
            .Where(x => x.IdWallet == u.IdWallet).Select(x => x.SaldoDisponible).SingleAsync();
        var idem = await ctx.WalletIdempotencias.AsNoTracking()
            .Where(x => x.IdUsuario == u.IdUsuario && x.Endpoint == Endpoint).ToListAsync();
        return new Efectos(utils.Count, ledger, mov, cupo, saldo, idem);
    }

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para la prueba SQL de idempotencia AVANCE_WALLET.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    private static async Task LimpiarAsync(string cs, Sembrados s)
    {
        if (string.IsNullOrWhiteSpace(cs)) return;
        try
        {
            await using var ctx = NuevoContexto(cs);
            // Solo filas creadas por el test: wallets propias y cupos por id_cupo.
            // Nunca por id_usuario generico (no toca semillas de migraciones).
            foreach (var id in s.Wallets)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.wallet_movimientos WHERE id_wallet = {id}");
            foreach (var (idCupo, idUsuario) in s.Cupos)
            {
                await ctx.Database.ExecuteSqlAsync(
                    $"DELETE FROM dbo.wallet_idempotencia WHERE id_usuario = {idUsuario} AND endpoint = {Endpoint}");
                await ctx.Database.ExecuteSqlAsync(
                    $"DELETE FROM dbo.ledger_movimientos WHERE id_transaccion_ledger IN (SELECT id_transaccion_ledger FROM dbo.ledger_transacciones WHERE referencia_tipo = 'cartera_utilizaciones' AND referencia_id IN (SELECT id_utilizacion FROM dbo.cartera_utilizaciones WHERE id_cupo = {idCupo}))");
                await ctx.Database.ExecuteSqlAsync(
                    $"DELETE FROM dbo.ledger_transacciones WHERE referencia_tipo = 'cartera_utilizaciones' AND referencia_id IN (SELECT id_utilizacion FROM dbo.cartera_utilizaciones WHERE id_cupo = {idCupo})");
                await ctx.Database.ExecuteSqlAsync(
                    $"DELETE FROM dbo.cartera_cuotas WHERE id_utilizacion IN (SELECT id_utilizacion FROM dbo.cartera_utilizaciones WHERE id_cupo = {idCupo})");
                await ctx.Database.ExecuteSqlAsync(
                    $"DELETE FROM dbo.cartera_utilizaciones WHERE id_cupo = {idCupo}");
                await ctx.Database.ExecuteSqlAsync(
                    $"DELETE FROM dbo.cartera_cupos_ordinarios WHERE id_cupo = {idCupo}");
            }
            foreach (var id in s.Wallets)
            {
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.wallet_saldos WHERE id_wallet = {id}");
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.wallets WHERE id_wallet = {id}");
            }
            foreach (var id in s.Usuarios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.usuarios WHERE id_usuario = {id}");
            foreach (var id in s.Personas)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.personas WHERE id_persona = {id}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[CarteraAvanceWalletIdempotenciaTests] cleanup parcial falló ({ex.GetType().Name}). " +
                $"usuarios=[{string.Join(",", s.Usuarios)}] wallets=[{string.Join(",", s.Wallets)}]");
        }
    }
}
