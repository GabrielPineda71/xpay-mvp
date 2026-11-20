using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xpay.Api.Tests.Common;
using Xunit;

namespace Xpay.Api.Tests.Services;

[Collection("SqlIntegration")]
public sealed class HabilitacionCuentaPublicaSqlTests
{
    [Fact]
    public async Task Habilita_misma_cuenta_una_sola_vez_sin_dinero_y_conserva_aceptaciones()
    {
        if (!Conexion(out var cs)) return;
        await using var f = new Fixture(cs);
        var admin = await f.CrearUsuarioAsync(admin: true);
        var usuario = await f.CrearUsuarioAsync();
        await f.AutorizarAsync(usuario);
        int ledgerAntes, movimientosAntes;
        await using (var db = f.Db())
        {
            ledgerAntes = await db.LedgerTransacciones.CountAsync();
            movimientosAntes = await db.WalletMovimientos.CountAsync();
        }
        long wallet;
        await using (var db = f.Db())
        {
            Assert.True(await RegistroPublicoService.EsCuentaLimitadaAsync(db, usuario));
            await using var tx = await db.Database.BeginTransactionAsync();
            wallet = await f.Servicio(db).HabilitarEnTransaccionAsync(usuario, admin);
            Assert.Equal(wallet, await f.Servicio(db).HabilitarEnTransaccionAsync(usuario, admin));
            await tx.CommitAsync();
        }
        await using var v = f.Db();
        var u = await v.Usuarios.SingleAsync(x => x.IdUsuario == usuario);
        Assert.Equal(1, u.VersionSeguridad);
        Assert.Equal(1, await v.Usuarios.CountAsync(x => x.IdPersona == u.IdPersona));
        Assert.Equal(wallet, (await v.Wallets.SingleAsync(x => x.IdPersona == u.IdPersona)).IdWallet);
        var saldo = await v.WalletSaldos.SingleAsync(x => x.IdWallet == wallet);
        Assert.Equal(0m, saldo.SaldoDisponible);
        Assert.Equal(0m, saldo.SaldoRetenido);
        Assert.False(await RegistroPublicoService.EsCuentaLimitadaAsync(v, usuario));
        Assert.True(await v.Auditorias.AnyAsync(a => a.IdUsuario == usuario
            && a.Accion == RegistroPublicoService.AccionAceptarTerminos));
        Assert.Single(await v.Auditorias.Where(a => a.Accion == HabilitacionCuentaPublicaService.AccionHabilitar
            && a.IdEntidad == usuario.ToString()).ToListAsync());
        Assert.Equal(ledgerAntes, await v.LedgerTransacciones.CountAsync());
        Assert.Equal(movimientosAntes, await v.WalletMovimientos.CountAsync());
    }

    [Fact]
    public async Task Fallo_de_vinculo_revierte_wallet_plaza_y_habilitacion()
    {
        if (!Conexion(out var cs)) return;
        await using var f = new Fixture(cs);
        var admin = await f.CrearUsuarioAsync(admin: true);
        var usuario = await f.CrearUsuarioAsync();
        await f.AutorizarAsync(usuario);
        await using (var db = f.Db())
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await f.Servicio(db).HabilitarEnTransaccionAsync(usuario, admin);
            await tx.RollbackAsync(); // el aprobador no pudo crear el vínculo
        }
        await using var v = f.Db();
        var u = await v.Usuarios.SingleAsync(x => x.IdUsuario == usuario);
        Assert.True(await RegistroPublicoService.EsCuentaLimitadaAsync(v, usuario));
        Assert.Equal(0, u.VersionSeguridad);
        Assert.False(await v.Wallets.AnyAsync(w => w.IdPersona == u.IdPersona));
        Assert.False(await HabilitacionCuentaPublicaService.TieneHabilitacionAsync(v, usuario));
    }

    [Theory]
    [InlineData("EMAIL")]
    [InlineData("WHATSAPP")]
    [InlineData("KYC")]
    [InlineData("CONSENTIMIENTO")]
    [InlineData("ROL_ADMIN")]
    [InlineData("DOC_DUPLICADO")]
    public async Task Requisitos_incompletos_no_crean_wallet_ni_consumen_plaza(string falta)
    {
        if (!Conexion(out var cs)) return;
        await using var f = new Fixture(cs);
        var admin = await f.CrearUsuarioAsync(admin: falta != "ROL_ADMIN");
        var usuario = await f.CrearUsuarioAsync();
        if (falta != "CONSENTIMIENTO") await f.AutorizarAsync(usuario);
        await using (var db = f.Db())
        {
            var u = await db.Usuarios.SingleAsync(x => x.IdUsuario == usuario);
            if (falta == "EMAIL") u.EmailVerificado = false;
            if (falta == "WHATSAPP") u.CelularVerificado = false;
            if (falta == "KYC")
                await db.KycVerificaciones.Where(k => k.IdUsuario == usuario).ExecuteDeleteAsync();
            if (falta == "DOC_DUPLICADO")
            {
                var original = await db.Personas.SingleAsync(p => p.IdPersona == u.IdPersona);
                var duplicate = new Persona
                {
                    IdUnidadNegocio = original.IdUnidadNegocio, Celular = "3000000000", Estado = "ACTIVA",
                    // El índice de documento verificado ya impide duplicados
                    // exactos. Este caso cubre una identidad legal histórica
                    // con separadores que normaliza al documento verificado.
                    TipoDocumento = "CC", NumeroDocumento = original.NumeroDocumentoVerificado![..4] + "-" + original.NumeroDocumentoVerificado[4..],
                    FechaCreacion = DateTime.UtcNow,
                };
                db.Personas.Add(duplicate);
                await db.SaveChangesAsync();
                f.Personas.Add(duplicate.IdPersona);
            }
            await db.SaveChangesAsync();
        }
        await using (var db = f.Db())
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await Assert.ThrowsAnyAsync<Exception>(() => f.Servicio(db).HabilitarEnTransaccionAsync(usuario, admin));
        }
        await using var v = f.Db();
        Assert.False(await HabilitacionCuentaPublicaService.TieneHabilitacionAsync(v, usuario));
        var persona = await v.Usuarios.Where(u => u.IdUsuario == usuario).Select(u => u.IdPersona).SingleAsync();
        Assert.False(await v.Wallets.AnyAsync(w => w.IdPersona == persona));
    }

    [Fact]
    public async Task Consentimiento_C_es_durable_idempotente_y_retoma_sesion_propia_sin_HTTP_ni_Wallet()
    {
        if (!Conexion(out var cs)) return;
        await using var f = new Fixture(cs);
        var usuario = await f.CrearUsuarioAsync();
        const string url = "https://magic.veriff.me/v/fixture-sin-llamadas-reales";
        await using (var db = f.Db())
        {
            await db.Usuarios.Where(u => u.IdUsuario == usuario).ExecuteUpdateAsync(s => s.SetProperty(u => u.EstadoKycActual, "PENDIENTE"));
            await db.KycVerificaciones.Where(k => k.IdUsuario == usuario).ExecuteUpdateAsync(s => s
                .SetProperty(k => k.EstadoKyc, "PENDIENTE").SetProperty(k => k.Decision, (string?)null)
                .SetProperty(k => k.SessionId, "fixture-sesion-propia").SetProperty(k => k.SessionUrl, url));
        }
        var http = new SinProveedorHttp();
        await using (var db = f.Db())
        {
            var kyc = new KycService(db, Fixture.Config(), http, NullLogger<KycService>.Instance, AmbienteXpayTests.Ambiente("Production"));
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => kyc.CreateVeriffSessionAsync(usuario));
            Assert.Equal(KycService.MensajeConsentimientoKycPendiente, ex.Message);
            Assert.Null((await kyc.GetMiEstadoAsync(usuario)).SessionUrl);
        }
        await f.AutorizarAsync(usuario);
        await f.AutorizarAsync(usuario);
        await using (var db = f.Db())
        {
            Assert.Equal(1, await db.Auditorias.CountAsync(a => a.IdUsuario == usuario && a.Accion == ConsentimientoBiometricoService.AccionAutorizar));
            var kyc = new KycService(db, Fixture.Config(), http, NullLogger<KycService>.Instance, AmbienteXpayTests.Ambiente("Production"));
            Assert.Equal(url, (await kyc.CreateVeriffSessionAsync(usuario)).SessionUrl);
            Assert.Equal(url, (await kyc.GetMiEstadoAsync(usuario)).SessionUrl);
            Assert.True(await RegistroPublicoService.EsCuentaLimitadaAsync(db, usuario));
            var persona = await db.Usuarios.Where(u => u.IdUsuario == usuario).Select(u => u.IdPersona).SingleAsync();
            Assert.False(await db.Wallets.AnyAsync(w => w.IdPersona == persona));
            // Modificar el correo invalida su verificación; conservar C no
            // permite retomar ni crear KYC hasta confirmar el nuevo correo.
            await db.Usuarios.Where(u => u.IdUsuario == usuario).ExecuteUpdateAsync(s => s.SetProperty(u => u.EmailVerificado, false));
            Assert.Null((await kyc.GetMiEstadoAsync(usuario)).SessionUrl);
            await Assert.ThrowsAsync<InvalidOperationException>(() => kyc.CreateVeriffSessionAsync(usuario));
        }
        Assert.Equal(0, http.Llamadas);
    }

    [Fact]
    public async Task Capacidad_50_combina_altas_historicas_y_transiciones_concurrentes()
    {
        if (!Conexion(out var cs)) return;
        await using var f = new Fixture(cs);
        var admin = await f.CrearUsuarioAsync(admin: true);
        var usuarios = new List<long>();
        for (var i = 0; i < 3; i++)
        {
            var usuario = await f.CrearUsuarioAsync();
            await f.AutorizarAsync(usuario);
            usuarios.Add(usuario);
        }
        await using (var db = f.Db())
        {
            var existentes = await HabilitacionCuentaPublicaService.ContarPlazasAsync(db);
            Assert.InRange(existentes, 0, 49);
            for (var i = existentes; i < 49; i++) db.Auditorias.Add(new Auditoria
            {
                IdUsuario = admin, Modulo = "USUARIOS", Accion = AltaUsuarioPilotoService.AccionAuditoria,
                Entidad = "usuarios", IdEntidad = "fixture-capacidad", Resultado = "EXITOSO", FechaEvento = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        var resultados = await Task.WhenAll(usuarios.Select(async usuario =>
        {
            await using var db = f.Db();
            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                await f.Servicio(db).HabilitarEnTransaccionAsync(usuario, admin);
                await tx.CommitAsync();
                return true;
            }
            catch (InvalidOperationException ex) when (ex.Message == AltaUsuarioPilotoService.MensajeMaximoAlcanzado)
            { return false; }
        }));
        Assert.Single(resultados, r => r);
        await using var v = f.Db();
        Assert.Equal(50, await HabilitacionCuentaPublicaService.ContarPlazasAsync(v));
        Assert.Equal(1, await v.Auditorias.CountAsync(a => a.Accion == HabilitacionCuentaPublicaService.AccionHabilitar
            && a.IdUsuario == admin));
    }

    [Fact]
    public async Task Cuenta_publica_habilitada_conserva_contactos_obligatorios_aunque_falte_el_canal_o_se_apague_flag_legacy()
    {
        if (!Conexion(out var cs)) return;
        await using var f = new Fixture(cs);
        var legacy = await f.CrearUsuarioAsync(admin: true);
        var publica = await f.CrearUsuarioAsync();
        await f.AutorizarAsync(publica);
        await using (var db = f.Db())
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await f.Servicio(db).HabilitarEnTransaccionAsync(publica, legacy);
            await tx.CommitAsync();
            await db.Usuarios.Where(u => u.IdUsuario == publica || u.IdUsuario == legacy)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.CelularVerificado, false));
        }
        foreach (var id in new[] { publica, legacy })
        {
            await using var db = f.Db();
            var off = new ConfigurationBuilder().Build();
            using var servicios = new ServiceCollection().AddSingleton(db)
                .AddSingleton(new RequisitoContactosVerificados(off, Array.Empty<ICanalCodigoContacto>())).BuildServiceProvider();
            var http = new DefaultHttpContext
            {
                RequestServices = servicios,
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("idUsuario", id.ToString())], "fixture")),
            };
            var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
            var context = new ActionExecutingContext(action, [], new Dictionary<string, object?>(), new object());
            var ejecutada = false;
            await new RequiereContactosVerificadosAttribute().OnActionExecutionAsync(context, () =>
            {
                ejecutada = true;
                return Task.FromResult(new ActionExecutedContext(action, [], new object()));
            });
            if (id == publica)
            {
                Assert.False(ejecutada);
                Assert.Equal(428, Assert.IsType<ObjectResult>(context.Result).StatusCode);
            }
            else Assert.True(ejecutada); // el piloto histórico mantiene su política
        }
    }

    [Fact]
    public async Task Flag_cerrado_o_sin_transaccion_no_activa_cuenta()
    {
        if (!Conexion(out var cs)) return;
        await using var f = new Fixture(cs);
        await using var db = f.Db();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Servicio(db).HabilitarEnTransaccionAsync(1, 1));
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var svc = new HabilitacionCuentaPublicaService(db, config,
            new ConsentimientoBiometricoService(db, config, AmbienteXpayTests.Ambiente("Production"), TimeProvider.System));
        await Assert.ThrowsAsync<AltaPublicaNoDisponibleException>(() => svc.HabilitarEnTransaccionAsync(1, 1));
    }

    private static bool Conexion(out string cs)
    {
        cs = Environment.GetEnvironmentVariable("ConnectionStrings__XpayConnection") ?? "";
        Assert.False(string.IsNullOrWhiteSpace(cs) && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")),
            "Las pruebas de habilitación necesitan SQL aislado en CI.");
        return !string.IsNullOrWhiteSpace(cs);
    }

    private sealed class Fixture(string cs) : IAsyncDisposable
    {
        public List<long> Personas { get; } = [];
        private readonly List<long> usuarios = [];
        public static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [HabilitacionCuentaPublicaService.ClaveHabilitada] = "true",
            [AltaUsuarioPilotoService.ClaveHabilitada] = "true", [AltaUsuarioPilotoService.ClaveMaxUsuarios] = "50",
            [PlantillasEmailIdentidad.ClaveUrlFrontend] = "https://app.xpaycol.com/",
            ["XPAY_AUTORIZACION_KYC_VERSION"] = "1.2", ["XPAY_AUTORIZACION_KYC_FECHA"] = "2026-11-18",
            ["XPAY_AUTORIZACION_KYC_URL"] = "https://app.xpaycol.com/legal/v1.2/autorizaciones.pdf",
            ["XPAY_AUTORIZACION_KYC_SHA256"] = ConsentimientoBiometricoService.ShaDocumentoAprobado,
            ["VERIFF_API_KEY"] = "fixture-no-es-una-credencial", ["VERIFF_SHARED_SECRET"] = "fixture-no-secreto-real",
            ["VERIFF_BASE_URL"] = "https://stationapi.veriff.com/", ["VERIFF_CALLBACK_URL"] = "https://app.xpaycol.com/perfil",
        }).Build();
        public XpayDbContext Db() => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);
        public HabilitacionCuentaPublicaService Servicio(XpayDbContext db) => new(db, Config(),
            new ConsentimientoBiometricoService(db, Config(), AmbienteXpayTests.Ambiente("Production"), TimeProvider.System));

        public async Task<long> CrearUsuarioAsync(bool admin = false)
        {
            await using var db = Db();
            var sufijo = Guid.NewGuid().ToString("N");
            var numero = "78" + Random.Shared.NextInt64(1000000000, 9999999999);
            var unidad = await db.Database.SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo='XPAY_COL'").SingleAsync();
            var p = new Persona
            {
                IdUnidadNegocio = unidad, TipoDocumento = "CC", NumeroDocumento = numero, NumeroDocumentoVerificado = numero,
                TipoDocumentoVeriffRaw = "ID_CARD", IdentidadVerificada = true, IdentidadVerificadaProveedor = "VERIFF",
                IdentidadVerificadaFecha = DateTime.UtcNow,
                Celular = "3000000000", Email = $"alta.{sufijo}@example.test", Estado = "ACTIVA", FechaCreacion = DateTime.UtcNow,
            };
            db.Personas.Add(p); await db.SaveChangesAsync(); Personas.Add(p.IdPersona);
            var u = new Usuario
            {
                IdPersona = p.IdPersona, NombreUsuario = "alta_" + sufijo, PasswordHash = "fixture-no-login",
                Estado = "ACTIVO", EstadoKycActual = "APROBADO", EmailVerificado = true, CelularVerificado = true,
                FechaCreacion = DateTime.UtcNow,
            };
            db.Usuarios.Add(u); await db.SaveChangesAsync(); usuarios.Add(u.IdUsuario);
            var codigoRol = admin ? "SUPERUSUARIO" : "USUARIO_FINAL";
            var rol = await db.Roles.SingleAsync(r => r.Codigo == codigoRol);
            db.UsuarioRoles.Add(new UsuarioRol { IdUsuario = u.IdUsuario, IdRol = rol.IdRol, Estado = "ACTIVO", FechaAsignacion = DateTime.UtcNow });
            db.KycVerificaciones.Add(new KycVerificacion
            { IdUsuario = u.IdUsuario, IdPersona = p.IdPersona, Proveedor = "VERIFF", EstadoKyc = "APROBADO", Decision = "approved", EsActual = true, FechaCreacion = DateTime.UtcNow });
            if (!admin) db.Auditorias.Add(new Auditoria
            {
                IdUsuario = u.IdUsuario, IdPersona = p.IdPersona, Modulo = "USUARIOS", Accion = RegistroPublicoService.AccionAceptarTerminos,
                Entidad = "usuarios", IdEntidad = u.IdUsuario.ToString(), Resultado = "EXITOSO", FechaEvento = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(); return u.IdUsuario;
        }

        public async Task AutorizarAsync(long idUsuario)
        {
            await using var db = Db();
            await new ConsentimientoBiometricoService(db, Config(), AmbienteXpayTests.Ambiente("Production"), TimeProvider.System)
                .AutorizarAsync(idUsuario, new(true, "1.2"), "127.0.0.1");
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = Db();
            await db.Auditorias.Where(a => (a.IdUsuario != null && usuarios.Contains(a.IdUsuario.Value))
                || (a.IdPersona != null && Personas.Contains(a.IdPersona.Value))).ExecuteDeleteAsync();
            var wallets = db.Wallets.Where(w => w.IdPersona != null && Personas.Contains(w.IdPersona.Value)).Select(w => w.IdWallet);
            await db.WalletSaldos.Where(s => wallets.Contains(s.IdWallet)).ExecuteDeleteAsync();
            await db.Wallets.Where(w => w.IdPersona != null && Personas.Contains(w.IdPersona.Value)).ExecuteDeleteAsync();
            await db.KycVerificaciones.Where(k => usuarios.Contains(k.IdUsuario)).ExecuteDeleteAsync();
            await db.UsuarioRoles.Where(r => usuarios.Contains(r.IdUsuario)).ExecuteDeleteAsync();
            await db.Usuarios.Where(u => usuarios.Contains(u.IdUsuario)).ExecuteDeleteAsync();
            await db.Personas.Where(p => Personas.Contains(p.IdPersona)).ExecuteDeleteAsync();
        }
    }

    private sealed class SinProveedorHttp : IHttpClientFactory
    {
        public int Llamadas { get; private set; }
        public HttpClient CreateClient(string name)
        {
            Llamadas++;
            throw new InvalidOperationException("Esta prueba no permite llamadas al proveedor.");
        }
    }
}
