using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xpay.Api.Tests.Common;
using Xunit;

namespace Xpay.Api.Tests.Services;

// Piloto PROD — corrección 2b, integración SQL del alta controlada en
// ambiente "Production". Comprueba que el alta no omite de forma permanente
// identidad, autenticación ni consentimiento, y que el máximo de altas no se
// supera con solicitudes simultáneas. Guard fail-closed del repo.
[Collection("SqlIntegration")]
public sealed class AltaUsuarioPilotoSqlTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";

    [Fact]
    public async Task Alta_crea_usuario_sin_clave_utilizable_sin_KYC_y_con_consentimiento_auditado()
    {
        if (!TryConnString(out var cs)) return;
        var c = new Creados();
        try
        {
            var idSuper = await SembrarAdminAsync(cs, c, "SUPERUSUARIO");
            var req = Solicitud();

            AltaUsuarioPilotoResponse r;
            await using (var db = NuevoContexto(cs))
                r = await Servicio(db, await MaximoAsync(cs, 1)).CrearAsync(req, idSuper);
            await RegistrarCreadoAsync(cs, c, r.IdUsuario);

            await using var v = NuevoContexto(cs);
            var u = await v.Usuarios.AsNoTracking().SingleAsync(x => x.IdUsuario == r.IdUsuario);
            var p = await v.Personas.AsNoTracking().SingleAsync(x => x.IdPersona == u.IdPersona);

            // Autenticación: activo pero sin clave conocida y con cambio obligatorio.
            Assert.Equal("ACTIVO", u.Estado);
            Assert.True(u.RequiereCambioClave);
            Assert.False(u.EmailVerificado);
            Assert.False(BCrypt.Net.BCrypt.Verify("x", u.PasswordHash));
            Assert.False(BCrypt.Net.BCrypt.Verify(string.Empty, u.PasswordHash));

            // Identidad: sin documento, sin nombres legales, KYC sin iniciar.
            Assert.Equal("NO_INICIADO", u.EstadoKycActual);
            Assert.False(p.IdentidadVerificada);
            Assert.Null(p.TipoDocumento);
            Assert.Null(p.NumeroDocumento);
            Assert.Null(p.PrimerApellido);

            // Wallet PERSONA activa con saldo cero, rol USUARIO_FINAL.
            var w = await v.Wallets.AsNoTracking().SingleAsync(x => x.IdWallet == r.IdWallet);
            Assert.Equal(("PERSONA", "ACTIVA", p.IdPersona), (w.TipoWallet, w.Estado, w.IdPersona!.Value));
            var s = await v.WalletSaldos.AsNoTracking().SingleAsync(x => x.IdWallet == r.IdWallet);
            Assert.Equal((0m, 0m), (s.SaldoDisponible, s.SaldoRetenido));
            var roles = await (from ur in v.UsuarioRoles join ro in v.Roles on ur.IdRol equals ro.IdRol
                               where ur.IdUsuario == r.IdUsuario select ro.Codigo).ToListAsync();
            Assert.Equal(["USUARIO_FINAL"], roles);

            // Consentimiento: versiones y evidencia auditadas, sin email ni celular.
            var aud = await v.Auditorias.AsNoTracking().SingleAsync(a =>
                a.Accion == AltaUsuarioPilotoService.AccionAuditoria && a.IdEntidad == r.IdUsuario.ToString());
            Assert.Equal(idSuper, aud.IdUsuario);
            Assert.Contains("\"versionTerminos\":\"1.0\"", aud.ValorNuevo);
            Assert.Contains(req.ReferenciaEvidencia!, aud.ValorNuevo);
            Assert.DoesNotContain(req.Email!.ToLower(), $"{aud.ValorNuevo}{aud.Observacion}");
            Assert.DoesNotContain(req.Celular!, $"{aud.ValorNuevo}{aud.Observacion}");

            // La clave solo nace por restablecer-clave, con cambio obligatorio.
            await using (var db = NuevoContexto(cs))
            {
                var (_, temporal) = await new UsuarioAdminService(db).RestablecerClaveAsync(r.IdUsuario, idSuper, null);
                var u2 = await db.Usuarios.AsNoTracking().SingleAsync(x => x.IdUsuario == r.IdUsuario);
                Assert.True(BCrypt.Net.BCrypt.Verify(temporal, u2.PasswordHash));
                Assert.True(u2.RequiereCambioClave);
            }
        }
        finally { await LimpiarAsync(cs, c); }
    }

    [Fact]
    public async Task Alta_deshabilitada_o_sin_maximo_no_existe()
    {
        if (!TryConnString(out var cs)) return;
        var c = new Creados();
        try
        {
            var idSuper = await SembrarAdminAsync(cs, c, "SUPERUSUARIO");
            foreach (var (hab, max) in new[] { ((string?)null, (string?)"5"), ("false", "5"), ("true", null), ("true", "0") })
            {
                await using var db = NuevoContexto(cs);
                var svc = new AltaUsuarioPilotoService(db, Config(hab, max), AmbienteXpayTests.Ambiente("Production"));
                await Assert.ThrowsAsync<AltaUsuarioPilotoNoDisponibleException>(() => svc.CrearAsync(Solicitud(), idSuper));
            }
        }
        finally { await LimpiarAsync(cs, c); }
    }

    [Fact]
    public async Task Solo_SUPERUSUARIO_puede_dar_de_alta()
    {
        if (!TryConnString(out var cs)) return;
        var c = new Creados();
        try
        {
            var idAdmin = await SembrarAdminAsync(cs, c, "ADMIN_XPAY");
            await using var db = NuevoContexto(cs);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                Servicio(db, 1_000_000).CrearAsync(Solicitud(), idAdmin));
        }
        finally { await LimpiarAsync(cs, c); }
    }

    [Fact]
    public async Task Altas_simultaneas_no_superan_el_maximo()
    {
        if (!TryConnString(out var cs)) return;
        var c = new Creados();
        try
        {
            var idSuper = await SembrarAdminAsync(cs, c, "SUPERUSUARIO");
            var maximo = await MaximoAsync(cs, 1);   // cabe exactamente una alta más

            var resultados = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
            {
                await using var db = NuevoContexto(cs);
                try { return (long?)(await Servicio(db, maximo).CrearAsync(Solicitud(), idSuper)).IdUsuario; }
                catch (InvalidOperationException ex) when (ex.Message == AltaUsuarioPilotoService.MensajeMaximoAlcanzado) { return null; }
            })));

            foreach (var id in resultados.Where(x => x.HasValue)) await RegistrarCreadoAsync(cs, c, id!.Value);
            Assert.Single(resultados, x => x.HasValue);
        }
        finally { await LimpiarAsync(cs, c); }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private sealed class Creados
    {
        public List<long> Personas { get; } = [];
        public List<long> Usuarios { get; } = [];
        public List<long> Wallets  { get; } = [];
    }

    private static AltaUsuarioPilotoRequest Solicitud()
    {
        var sufijo = Guid.NewGuid().ToString("N")[..10];
        return new AltaUsuarioPilotoRequest
        {
            NombreUsuario = $"piloto_{sufijo}", Email = $"piloto_{sufijo}@ejemplo.test", Celular = $"30{sufijo[..8].GetHashCode() & 0x7FFFFFF:D8}",
            VersionTerminos = "1.0", VersionPoliticaDatos = "1.0", VersionAutorizacionKyc = "1.0",
            FechaAceptacionUtc = DateTime.UtcNow.AddMinutes(-10), ReferenciaEvidencia = $"ACTA-TEST-{sufijo}",
        };
    }

    private static IConfiguration Config(string? habilitada, string? maximo) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [AltaUsuarioPilotoService.ClaveHabilitada]  = habilitada,
            [AltaUsuarioPilotoService.ClaveMaxUsuarios] = maximo,
        }).Build();

    private static AltaUsuarioPilotoService Servicio(XpayDbContext db, int maximo) =>
        new(db, Config("true", maximo.ToString()), AmbienteXpayTests.Ambiente("Production"));

    // El máximo es global (cuenta auditorias): se calcula sobre las ya existentes.
    private static async Task<int> MaximoAsync(string cs, int adicionales)
    {
        await using var v = NuevoContexto(cs);
        return adicionales + await v.Auditorias.CountAsync(a =>
            a.Accion == AltaUsuarioPilotoService.AccionAuditoria && a.Resultado == "EXITOSO");
    }

    private static async Task<long> SembrarAdminAsync(string cs, Creados c, string rol)
    {
        await using var ctx = NuevoContexto(cs);
        var idUnidad = await ctx.Database
            .SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL")
            .SingleAsync();
        var ahora = DateTime.UtcNow;
        var persona = new Persona { IdUnidadNegocio = idUnidad, Celular = "3000000000", Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora };
        ctx.Personas.Add(persona);
        await ctx.SaveChangesAsync();
        c.Personas.Add(persona.IdPersona);

        var usuario = new Usuario
        {
            IdPersona = persona.IdPersona, NombreUsuario = $"altapil_adm_{Guid.NewGuid():N}"[..30],
            PasswordHash = "x", Estado = "ACTIVO", FechaCreacion = ahora,
        };
        ctx.Usuarios.Add(usuario);
        await ctx.SaveChangesAsync();
        c.Usuarios.Add(usuario.IdUsuario);

        var idRol = await ctx.Roles.Where(r => r.Codigo == rol).Select(r => r.IdRol).SingleAsync();
        ctx.UsuarioRoles.Add(new UsuarioRol { IdUsuario = usuario.IdUsuario, IdRol = idRol, Estado = "ACTIVO", FechaAsignacion = ahora });
        await ctx.SaveChangesAsync();
        return usuario.IdUsuario;
    }

    private static async Task RegistrarCreadoAsync(string cs, Creados c, long idUsuario)
    {
        await using var v = NuevoContexto(cs);
        var idPersona = await v.Usuarios.Where(u => u.IdUsuario == idUsuario).Select(u => u.IdPersona).SingleAsync();
        c.Usuarios.Add(idUsuario);
        c.Personas.Add(idPersona);
        c.Wallets.AddRange(await v.Wallets.Where(w => w.IdPersona == idPersona).Select(w => w.IdWallet).ToListAsync());
    }

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL del alta del piloto.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    private static async Task LimpiarAsync(string cs, Creados c)
    {
        try
        {
            await using var ctx = NuevoContexto(cs);
            foreach (var p in c.Personas)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.auditoria WHERE id_persona = {p}");
            foreach (var u in c.Usuarios)
            {
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.auditoria WHERE id_usuario = {u}");
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.usuario_roles WHERE id_usuario = {u}");
            }
            foreach (var w in c.Wallets)
            {
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.wallet_saldos WHERE id_wallet = {w}");
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.wallets WHERE id_wallet = {w}");
            }
            foreach (var u in c.Usuarios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.usuarios WHERE id_usuario = {u}");
            foreach (var p in c.Personas)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.personas WHERE id_persona = {p}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AltaUsuarioPilotoSqlTests] cleanup parcial falló ({ex.GetType().Name}).");
        }
    }
}
