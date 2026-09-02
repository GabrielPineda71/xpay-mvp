using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Exceptions;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// ══════════════════════════════════════════════════════════════════════════
// Hardening de concurrencia de CarteraOrdinariaService.AsignarCupoAsync
// (endpoint admin ACTIVO POST /admin/cupos) frente a
// CarteraMaterializacionCupoStore.MaterializarCupoAsync (M2.4c, DORMIDO) y
// frente a otra asignación admin del mismo usuario, sobre
// cartera_cupos_ordinarios.
//
// El admin ahora: BeginTransaction → AppLock XPAY:CARTERA_CUPO:{idUsuario}
// (compartido con TX2) → re-lectura autoritativa WITH (UPDLOCK, ROWLOCK) →
// write-set INALTERADO → commit; rollback + ChangeTracker.Clear en error;
// contención transitoria → CarteraCupoConcurrenteException → 409 en el
// controller.
//
// Integración SQL REAL: sp_getapplock, hints de bloqueo y transacciones no los
// soportan los providers InMemory/SQLite. Guard fail-closed idéntico al resto
// de la suite: local sin ConnectionStrings__XpayConnection → early-return
// (PASS, no SKIP); en CI sin la variable → FALLA. Reutiliza la colección
// SqlIntegration definida en CarteraConsultaRiesgoConcurrencyTests. SIN red,
// SIN proveedor, SIN token, SIN credenciales, SIN cédulas. NO activa M2.4c ni
// modifica CarteraMaterializacionCupoStore.
//
// Las carreras se prueban de forma DETERMINISTA con un "holder externo" del
// AppLock (un XpayDbContext dedicado con transacción abierta que retiene
// sp_getapplock): el admin no puede alcanzar su lectura autoritativa del cupo
// hasta poseer el lock. NO se usa Thread.Sleep/Task.Delay como prueba de
// orden — sólo como ventana corta para verificar "aún bloqueado".
// ══════════════════════════════════════════════════════════════════════════

[Collection("SqlIntegration")]
public sealed class CarteraAsignarCupoConcurrencyTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";
    private const long IdAdmin = 1;

    // ── T1 — admin CREATE (usuario sin cupo) ────────────────────────────
    [Fact]
    public async Task T1_AdminCreate_CreaCupoConWriteSetExacto()
    {
        if (!TryConnString(out var cs)) return;
        var seed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var u = await seed.SembrarUsuarioAsync(creados);
            var venc = DateTime.UtcNow.Date.AddDays(90);

            await using var ctx = NuevoContexto(cs);
            var antes = DateTime.UtcNow;
            var dto = await NuevoServicio(ctx).AsignarCupoAsync(
                new AsignarCupoRequest(u.IdUsuario, 2_000_000m, venc, "alta inicial"), IdAdmin);
            var despues = DateTime.UtcNow;

            Assert.Equal(u.IdUsuario, dto.IdUsuario);
            Assert.Equal(u.NombreUsuario, dto.NombreUsuario);
            Assert.Equal(u.IdWallet, dto.IdWallet);
            Assert.Equal(2_000_000m, dto.CupoAprobado);
            Assert.Equal(0m, dto.CupoUsado);
            Assert.Equal("ACTIVO", dto.Estado);

            await using var v = NuevoContexto(cs);
            var cupo = await v.CarteraCuposOrdinarios.AsNoTracking().SingleAsync(c => c.IdUsuario == u.IdUsuario);
            Assert.Equal(u.IdWallet, cupo.IdWallet);
            Assert.Equal(2_000_000m, cupo.CupoAprobado);
            Assert.Equal(0m, cupo.CupoUsado);
            Assert.Equal("ACTIVO", cupo.Estado);
            Assert.Equal(venc, cupo.FechaVencimiento);
            Assert.Equal(IdAdmin, cupo.AprobadoPorUsuario);
            Assert.Equal("alta inicial", cupo.Observaciones);
            Assert.NotNull(cupo.UpdatedAt);
            Assert.True(cupo.CreatedAt >= antes.AddSeconds(-5) && cupo.CreatedAt <= despues.AddSeconds(5));
            Assert.True(cupo.FechaAprobacion >= antes.AddSeconds(-5) && cupo.FechaAprobacion <= despues.AddSeconds(5));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── T2 — admin UPDATE preserva lo que no es write-set ───────────────
    [Fact]
    public async Task T2_AdminUpdate_PreservaCamposFueraDelWriteSet()
    {
        if (!TryConnString(out var cs)) return;
        var seed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var u = await seed.SembrarUsuarioAsync(creados);
            var idCupo = await seed.SembrarCupoAsync(u.IdUsuario, u.IdWallet, 1_000_000m, 333_000m, "ACTIVO", creados);

            CarteraCupoOrdinario before;
            await using (var q = NuevoContexto(cs))
                before = await q.CarteraCuposOrdinarios.AsNoTracking().SingleAsync(c => c.IdCupo == idCupo);

            var venc = DateTime.UtcNow.Date.AddDays(30);
            await using var ctx = NuevoContexto(cs);
            await NuevoServicio(ctx).AsignarCupoAsync(
                new AsignarCupoRequest(u.IdUsuario, 4_500_000m, venc, "ajuste admin"), IdAdmin);

            await using var v = NuevoContexto(cs);
            var after = await v.CarteraCuposOrdinarios.AsNoTracking().SingleAsync(c => c.IdCupo == idCupo);

            // write-set: cambia
            Assert.Equal(4_500_000m, after.CupoAprobado);
            Assert.Equal(venc, after.FechaVencimiento);
            Assert.Equal(IdAdmin, after.AprobadoPorUsuario);
            Assert.Equal("ajuste admin", after.Observaciones);
            Assert.NotEqual(before.UpdatedAt, after.UpdatedAt);

            // fuera del write-set: se preserva EXACTO
            Assert.Equal(before.IdCupo, after.IdCupo);
            Assert.Equal(333_000m, after.CupoUsado);
            Assert.Equal("ACTIVO", after.Estado);
            Assert.Equal(before.IdWallet, after.IdWallet);
            Assert.Equal(before.FechaAprobacion, after.FechaAprobacion);
            Assert.Equal(before.CreatedAt, after.CreatedAt);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── T3 — admin bloqueado hasta tener el AppLock; luego escribe sobre
    //         estado autoritativo re-leído (no sobre snapshot previo) ─────
    [Fact]
    public async Task T3_AdminNoLeeElCupoAntesDelAppLock()
    {
        if (!TryConnString(out var cs)) return;
        var seed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var u = await seed.SembrarUsuarioAsync(creados);
            await seed.SembrarCupoAsync(u.IdUsuario, u.IdWallet, 3_000_000m, 0m, "ACTIVO", creados);

            await using var ctxHolder = NuevoContexto(cs);
            await using var txHolder = await ctxHolder.Database.BeginTransactionAsync();
            var code = await AppLockHelper.AdquirirAsync(ctxHolder, $"XPAY:CARTERA_CUPO:{u.IdUsuario}");
            Assert.True(code is 0 or 1);

            await using var ctxAdmin = NuevoContexto(cs);
            var adminTask = NuevoServicio(ctxAdmin).AsignarCupoAsync(
                new AsignarCupoRequest(u.IdUsuario, 4_000_000m, null, "override admin"), IdAdmin);

            // Ventana corta SÓLO para verificar "aún bloqueado" (no como prueba de orden).
            await Task.Delay(1500);
            Assert.False(adminTask.IsCompleted);

            // Otro escritor con lock commitea una subida a 5M mientras el admin espera.
            await ctxHolder.Database.ExecuteSqlAsync(
                $"UPDATE cartera_cupos_ordinarios SET cupo_aprobado = {5_000_000m} WHERE id_usuario = {u.IdUsuario}");
            await txHolder.CommitAsync(); // libera el AppLock

            var dto = await adminTask;

            // El admin escribió su valor EXPLÍCITO de request sobre el estado
            // autoritativo re-leído. NO se afirma "el final debe ser 5M": el
            // admin puede reducirlo legítimamente (semántica de override
            // existente). Lo probado: no hay escritura basada en el snapshot
            // previo al AppLock (si la hubiera, no habría podido siquiera leer
            // el cupo mientras el lock estaba retenido).
            Assert.Equal(4_000_000m, dto.CupoAprobado);

            await using var v = NuevoContexto(cs);
            var cupo = await v.CarteraCuposOrdinarios.AsNoTracking().SingleAsync(c => c.IdUsuario == u.IdUsuario);
            Assert.Equal(4_000_000m, cupo.CupoAprobado);
            Assert.Equal(0m, cupo.CupoUsado);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── T4 — admin primero, luego TX2 real: TX2 re-lee el valor del admin
    //         bajo el mismo AppLock y aplica Math.Max ───────────────────
    [Fact]
    public async Task T4_AdminLuegoTx2Real_Tx2ReLeeValorAdmin_YAplicaMax()
    {
        if (!TryConnString(out var cs)) return;
        var seed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var u = await seed.SembrarUsuarioAsync(creados);
            var idSol = await seed.SembrarSolicitudElegibleAsync(u, montoAprobado: 5_000_000m, creados);

            await using (var ctxAdmin = NuevoContexto(cs))
                await NuevoServicio(ctxAdmin).AsignarCupoAsync(
                    new AsignarCupoRequest(u.IdUsuario, 3_000_000m, null, "alta admin"), IdAdmin);

            await using var ctxTx2 = NuevoContexto(cs);
            var r = await new CarteraMaterializacionCupoStore(ctxTx2).MaterializarCupoAsync(idSol, default);
            Assert.Equal(ResultadoMaterializacionCupo.Materializado, r);

            await using var v = NuevoContexto(cs);
            var cupo = await v.CarteraCuposOrdinarios.AsNoTracking().SingleAsync(c => c.IdUsuario == u.IdUsuario);
            Assert.Equal(5_000_000m, cupo.CupoAprobado); // TX2 vio el 3M del admin y subió a 5M
            Assert.Equal(0m, cupo.CupoUsado);
            var sol = await v.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(s => s.IdSolicitud == idSol);
            Assert.Equal(cupo.IdCupo, sol.IdCupoOrdinario);
            Assert.Equal("APROBADA", sol.EstadoSolicitud);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── T5 — CREATE ↔ CREATE: el AppLock evita la violación de UNIQUE ───
    [Fact]
    public async Task T5_CreateCreate_AppLockEvitaViolacionUnique()
    {
        if (!TryConnString(out var cs)) return;
        var seed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var u = await seed.SembrarUsuarioAsync(creados); // sin cupo

            await using var ctxHolder = NuevoContexto(cs);
            await using var txHolder = await ctxHolder.Database.BeginTransactionAsync();
            var code = await AppLockHelper.AdquirirAsync(ctxHolder, $"XPAY:CARTERA_CUPO:{u.IdUsuario}");
            Assert.True(code is 0 or 1);

            await using var ctxAdmin = NuevoContexto(cs);
            var adminTask = NuevoServicio(ctxAdmin).AsignarCupoAsync(
                new AsignarCupoRequest(u.IdUsuario, 2_500_000m, null, "admin create"), IdAdmin);

            await Task.Delay(1500);
            Assert.False(adminTask.IsCompleted);

            // El "otro" CREATE (exactamente el estado committeado que un TX2 CREATE produce).
            await ctxHolder.Database.ExecuteSqlAsync(
                $"INSERT INTO cartera_cupos_ordinarios (id_usuario, id_wallet, cupo_aprobado, cupo_usado, estado, fecha_aprobacion, created_at) VALUES ({u.IdUsuario}, {u.IdWallet}, {9_000_000m}, {0m}, 'ACTIVO', SYSUTCDATETIME(), SYSUTCDATETIME())");
            await txHolder.CommitAsync();

            var dto = await adminTask; // NO debe lanzar unique violation
            Assert.Equal(2_500_000m, dto.CupoAprobado); // el admin re-leyó y tomó rama UPDATE

            await using var v = NuevoContexto(cs);
            Assert.Equal(1, await v.CarteraCuposOrdinarios.AsNoTracking().CountAsync(c => c.IdUsuario == u.IdUsuario));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── T6 — admin ↔ admin: se serializan; el último request serializado
    //         persiste su write-set COMPLETO (sin mezcla de campos) ──────
    [Fact]
    public async Task T6_AdminAdmin_SeSerializan_SinWriteTorn()
    {
        if (!TryConnString(out var cs)) return;
        var seed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var u = await seed.SembrarUsuarioAsync(creados); // sin cupo

            await using var ctxHolder = NuevoContexto(cs);
            await using var txHolder = await ctxHolder.Database.BeginTransactionAsync();
            var code = await AppLockHelper.AdquirirAsync(ctxHolder, $"XPAY:CARTERA_CUPO:{u.IdUsuario}");
            Assert.True(code is 0 or 1);

            await using var ctxA = NuevoContexto(cs);
            await using var ctxB = NuevoContexto(cs);
            var taskA = NuevoServicio(ctxA).AsignarCupoAsync(
                new AsignarCupoRequest(u.IdUsuario, 1_000_000m, null, "req-A"), IdAdmin);
            var taskB = NuevoServicio(ctxB).AsignarCupoAsync(
                new AsignarCupoRequest(u.IdUsuario, 7_000_000m, null, "req-B"), IdAdmin);

            await Task.Delay(1500);
            Assert.False(taskA.IsCompleted);
            Assert.False(taskB.IsCompleted);

            await txHolder.CommitAsync();
            await Task.WhenAll(taskA, taskB); // ninguno lanza

            await using var v = NuevoContexto(cs);
            var cupos = await v.CarteraCuposOrdinarios.AsNoTracking()
                .Where(c => c.IdUsuario == u.IdUsuario).ToListAsync();
            Assert.Single(cupos);
            var c = cupos[0];
            Assert.True(
                (c.CupoAprobado == 1_000_000m && c.Observaciones == "req-A") ||
                (c.CupoAprobado == 7_000_000m && c.Observaciones == "req-B"),
                $"write torn: CupoAprobado={c.CupoAprobado} Observaciones={c.Observaciones}");
            Assert.Equal(IdAdmin, c.AprobadoPorUsuario);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── T7 — usuarios distintos: locks por usuario no serializan
    //         operaciones independientes ────────────────────────────────
    [Fact]
    public async Task T7_UsuariosDistintos_NoExclusionGlobal()
    {
        if (!TryConnString(out var cs)) return;
        var seed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var u1 = await seed.SembrarUsuarioAsync(creados);
            var u2 = await seed.SembrarUsuarioAsync(creados);

            await using var ctx1 = NuevoContexto(cs);
            await using var ctx2 = NuevoContexto(cs);
            var dtos = await Task.WhenAll(
                NuevoServicio(ctx1).AsignarCupoAsync(new AsignarCupoRequest(u1.IdUsuario, 1_500_000m, null, "u1"), IdAdmin),
                NuevoServicio(ctx2).AsignarCupoAsync(new AsignarCupoRequest(u2.IdUsuario, 2_500_000m, null, "u2"), IdAdmin));

            Assert.Equal(1_500_000m, dtos[0].CupoAprobado);
            Assert.Equal(2_500_000m, dtos[1].CupoAprobado);

            await using var v = NuevoContexto(cs);
            var c1 = await v.CarteraCuposOrdinarios.AsNoTracking().SingleAsync(c => c.IdUsuario == u1.IdUsuario);
            var c2 = await v.CarteraCuposOrdinarios.AsNoTracking().SingleAsync(c => c.IdUsuario == u2.IdUsuario);
            Assert.NotEqual(c1.IdCupo, c2.IdCupo);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── T8 — contención sostenida > LockTimeoutMs → excepción de
    //         concurrencia dedicada; nada escrito ───────────────────────
    [Fact]
    public async Task T8_ContencionSostenida_LanzaCarteraCupoConcurrenteException_NadaEscrito()
    {
        if (!TryConnString(out var cs)) return;
        var seed = new SeedContext(cs);
        var creados = new Sembrados();
        try
        {
            var u = await seed.SembrarUsuarioAsync(creados); // sin cupo

            await using var ctxHolder = NuevoContexto(cs);
            await using var txHolder = await ctxHolder.Database.BeginTransactionAsync();
            var code = await AppLockHelper.AdquirirAsync(ctxHolder, $"XPAY:CARTERA_CUPO:{u.IdUsuario}");
            Assert.True(code is 0 or 1);

            await using var ctxAdmin = NuevoContexto(cs);
            var adminTask = NuevoServicio(ctxAdmin).AsignarCupoAsync(
                new AsignarCupoRequest(u.IdUsuario, 2_000_000m, null, "timeout"), IdAdmin);

            // Retener MÁS que AppLockHelper.LockTimeoutMs (5000 ms) para forzar -1.
            await Task.Delay(TimeSpan.FromSeconds(7));

            await Assert.ThrowsAsync<CarteraCupoConcurrenteException>(() => adminTask);

            await using var v = NuevoContexto(cs);
            Assert.Equal(0, await v.CarteraCuposOrdinarios.AsNoTracking().CountAsync(c => c.IdUsuario == u.IdUsuario));

            await txHolder.RollbackAsync();
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ════════════════════ infraestructura de test ════════════════════════

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;

        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi,
            $"{EnvConnString} es obligatoria en CI para las pruebas de concurrencia de AsignarCupoAsync.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    private static CarteraOrdinariaService NuevoServicio(XpayDbContext ctx)
        => new(ctx, new PagoQrService(ctx, NullLogger<PagoQrService>.Instance), NullLogger<CarteraOrdinariaService>.Instance);

    private sealed record SeedUser(long IdUsuario, long IdPersona, long IdWallet, string NombreUsuario);

    private sealed class Sembrados
    {
        public List<long> Personas { get; } = new();
        public List<long> Usuarios { get; } = new();
        public List<long> Wallets { get; } = new();
        public List<long> Solicitudes { get; } = new();
        public List<long> Cupos { get; } = new();
    }

    private sealed class SeedContext(string cs)
    {
        private long? _idUnidad;
        private long? _idPolitica;

        private async Task<long> IdUnidadAsync()
        {
            if (_idUnidad is not null) return _idUnidad.Value;
            await using var ctx = NuevoContexto(cs);
            _idUnidad = await ctx.Database
                .SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL")
                .SingleAsync();
            return _idUnidad.Value;
        }

        private async Task<long> IdPoliticaAsync()
        {
            if (_idPolitica is not null) return _idPolitica.Value;
            await using var ctx = NuevoContexto(cs);
            _idPolitica = await ctx.CarteraPoliticasCredito.AsNoTracking()
                .Where(p => p.Estado == "ACTIVO")
                .OrderBy(p => p.IdPolitica)
                .Select(p => p.IdPolitica)
                .FirstAsync();
            return _idPolitica.Value;
        }

        public async Task<SeedUser> SembrarUsuarioAsync(Sembrados creados, string walletEstado = "ACTIVA")
        {
            var idUnidad = await IdUnidadAsync();
            await using var ctx = NuevoContexto(cs);
            var ahora = DateTime.UtcNow;
            var sufijo = Guid.NewGuid().ToString("N")[..12];
            var doc = $"75{(uint)Guid.NewGuid().GetHashCode() % 10_000_000:D7}";

            var persona = new Persona
            {
                IdUnidadNegocio = idUnidad,
                TipoDocumento = "CC",
                NumeroDocumento = doc,
                PrimerNombre = "CupoTest",
                PrimerApellido = "Sintetico",
                Celular = "3000000000",
                Pais = "Colombia",
                Estado = "ACTIVA",
                FechaCreacion = ahora,
            };
            ctx.Personas.Add(persona);
            await ctx.SaveChangesAsync();
            creados.Personas.Add(persona.IdPersona);

            var usuario = new Usuario
            {
                IdPersona = persona.IdPersona,
                NombreUsuario = $"cupo_test_{sufijo}",
                PasswordHash = "x",
                Estado = "ACTIVO",
                FechaCreacion = ahora,
            };
            ctx.Usuarios.Add(usuario);
            await ctx.SaveChangesAsync();
            creados.Usuarios.Add(usuario.IdUsuario);

            var wallet = new Wallet
            {
                IdUnidadNegocio = idUnidad,
                TipoWallet = "PERSONA",
                IdPersona = persona.IdPersona,
                NombreWallet = $"w_{sufijo}",
                Estado = walletEstado,
                FechaCreacion = ahora,
            };
            ctx.Wallets.Add(wallet);
            await ctx.SaveChangesAsync();
            creados.Wallets.Add(wallet.IdWallet);

            return new SeedUser(usuario.IdUsuario, persona.IdPersona, wallet.IdWallet, usuario.NombreUsuario);
        }

        public async Task<long> SembrarCupoAsync(
            long idUsuario, long idWallet, decimal aprobado, decimal usado, string estado, Sembrados creados)
        {
            await using var ctx = NuevoContexto(cs);
            var ahora = DateTime.UtcNow;
            var cupo = new CarteraCupoOrdinario
            {
                IdUsuario = idUsuario,
                IdWallet = idWallet,
                CupoAprobado = aprobado,
                CupoUsado = usado,
                Estado = estado,
                FechaAprobacion = ahora,
                CreatedAt = ahora,
            };
            ctx.CarteraCuposOrdinarios.Add(cupo);
            await ctx.SaveChangesAsync();
            creados.Cupos.Add(cupo.IdCupo);
            return cupo.IdCupo;
        }

        // Solicitud lista para CarteraMaterializacionCupoStore.MaterializarCupoAsync
        // (T4): APROBADA_PENDIENTE_CUPO + decision APROBADA + monto_aprobado.
        public async Task<long> SembrarSolicitudElegibleAsync(SeedUser u, decimal montoAprobado, Sembrados creados)
        {
            var idPolitica = await IdPoliticaAsync();
            await using var ctx = NuevoContexto(cs);
            var ahora = DateTime.UtcNow;
            var sufijo = Guid.NewGuid().ToString("N")[..12];

            var solicitud = new CarteraSolicitudCupo
            {
                IdUsuario = u.IdUsuario,
                IdPersona = u.IdPersona,
                MontoSolicitado = 500_000m,
                EstadoSolicitud = "APROBADA_PENDIENTE_CUPO",
                DecisionCrediticia = "APROBADA",
                MontoAprobado = montoAprobado,
                IdPoliticaAplicada = idPolitica,
                CupoMinimoAplicado = 0m,
                CupoMaximoAplicado = 10_000_000m,
                EdadMinimaAplicada = 18,
                EdadMaximaAplicada = 99,
                NumeroIntento = 1,
                CorrelationId = $"cupo-sol-{sufijo}",
                FechaSolicitud = ahora,
                FechaActualizacion = ahora,
            };
            ctx.CarteraSolicitudesCupo.Add(solicitud);
            await ctx.SaveChangesAsync();
            creados.Solicitudes.Add(solicitud.IdSolicitud);
            return solicitud.IdSolicitud;
        }
    }

    private static async Task LimpiarAsync(string cs, Sembrados creados)
    {
        try
        {
            await using var ctx = NuevoContexto(cs);
            foreach (var id in creados.Solicitudes)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.cartera_solicitudes_cupo WHERE id_solicitud = {id}");
            foreach (var id in creados.Cupos)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.cartera_cupos_ordinarios WHERE id_cupo = {id}");
            foreach (var id in creados.Usuarios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.cartera_cupos_ordinarios WHERE id_usuario = {id}");
            foreach (var id in creados.Wallets)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.wallet_saldos WHERE id_wallet = {id}");
            foreach (var id in creados.Wallets)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.wallets WHERE id_wallet = {id}");
            foreach (var id in creados.Usuarios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.usuarios WHERE id_usuario = {id}");
            foreach (var id in creados.Personas)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.personas WHERE id_persona = {id}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[CarteraAsignarCupoConcurrencyTests] cleanup parcial falló ({ex.GetType().Name}). " +
                $"personas=[{string.Join(",", creados.Personas)}] usuarios=[{string.Join(",", creados.Usuarios)}] " +
                $"cupos=[{string.Join(",", creados.Cupos)}] solicitudes=[{string.Join(",", creados.Solicitudes)}]");
        }
    }
}
