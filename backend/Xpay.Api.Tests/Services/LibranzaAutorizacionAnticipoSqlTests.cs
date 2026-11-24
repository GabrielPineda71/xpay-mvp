using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Controllers;
using Xpay.Api.DTOs;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// Solo titulares, nómina y dinero sintéticos de la base aislada/CI.
[Collection("SqlIntegration")]
public sealed class LibranzaAutorizacionAnticipoSqlTests
{
    [Fact]
    public async Task Publica_vinculada_requiere_decision_explicita_auditada_sin_crear_cupo_o_dinero()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new Semillero(cs); await s.IniciarAsync(); var cuenta = await s.EmpleadoAsync();
        await using var db = Contexto(cs); var svc = new LibranzaAutorizacionAnticipoService(db);
        var foto = await FotoAsync(db); Assert.False(await svc.PuedeAccederAsync(cuenta.Usuario));
        var fila = Assert.Single((await svc.ListarAsync(s.Admin, s.Convenio)).Items);
        Assert.Equal("PENDIENTE", fila.Estado); Assert.True(fila.PuedeAutorizar);
        var p = Peticion(fila); p.ConfirmaAutorizacion = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CambiarAsync(s.Admin, p, true));
        Assert.False(await db.LibranzaAutorizacionesAnticipo.AnyAsync(a => a.IdUsuario == cuenta.Usuario));
        p.ConfirmaAutorizacion = true;
        var autorizada = await svc.CambiarAsync(s.Admin, p, true);
        Assert.Equal("AUTORIZADO", autorizada.Estado); Assert.True(await svc.PuedeAccederAsync(cuenta.Usuario));
        Assert.Equal(1, await db.Auditorias.CountAsync(a => a.IdUsuario == s.Admin && a.Accion == LibranzaAutorizacionAnticipoService.AccionAutorizar));
        var registro = await db.LibranzaAutorizacionesAnticipo.AsNoTracking().SingleAsync(a => a.IdUsuario == cuenta.Usuario);
        Assert.Equal(cuenta.Vinculo, registro.IdEmpleadoUsuario); Assert.Equal(cuenta.Wallet, registro.IdWallet);
        Assert.Equal(s.Admin, registro.ModificadoPorUsuario); Assert.Equal(foto, await FotoAsync(db));
        Assert.Equal("AUTORIZADO", (await svc.CambiarAsync(s.Admin, Peticion(autorizada), true)).Estado);
        Assert.Equal(1, await db.Auditorias.CountAsync(a => a.IdUsuario == s.Admin && a.Accion == LibranzaAutorizacionAnticipoService.AccionAutorizar));
    }

    [Fact]
    public async Task Revocar_cierra_acceso_y_replay_anterior_no_revive_el_permiso()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new Semillero(cs); await s.IniciarAsync(); var c = await s.EmpleadoAsync();
        await using var db = Contexto(cs); var svc = new LibranzaAutorizacionAnticipoService(db);
        var vieja = Peticion(Assert.Single((await svc.ListarAsync(s.Admin, s.Convenio)).Items));
        var activa = await svc.CambiarAsync(s.Admin, vieja, true); var foto = await FotoAsync(db);
        var revocada = await svc.CambiarAsync(s.Admin, Peticion(activa), false);
        Assert.Equal("REVOCADO", revocada.Estado); Assert.False(await svc.PuedeAccederAsync(c.Usuario));
        await Assert.ThrowsAsync<AutorizacionAnticipoConflictoException>(() => svc.CambiarAsync(s.Admin, vieja, true));
        Assert.False(await svc.PuedeAccederAsync(c.Usuario)); Assert.Equal(foto, await FotoAsync(db));
        Assert.Equal(1, await db.Auditorias.CountAsync(a => a.IdUsuario == s.Admin && a.Accion == LibranzaAutorizacionAnticipoService.AccionRevocar));
    }

    [Fact]
    public async Task Importacion_de_nomina_no_revoca_permiso_pero_invalida_formulario_abierto()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new Semillero(cs); await s.IniciarAsync(); var c = await s.EmpleadoAsync();
        await using var db = Contexto(cs); var svc = new LibranzaAutorizacionAnticipoService(db);
        var activa = await svc.CambiarAsync(s.Admin, Peticion(Assert.Single((await svc.ListarAsync(s.Admin, s.Convenio)).Items)), true);
        await db.LibranzaEmpleados.Where(e => e.IdEmpleado == c.Empleado).ExecuteUpdateAsync(x => x
            .SetProperty(e => e.UpdatedAt, DateTime.UtcNow.AddSeconds(1))
            .SetProperty(e => e.FechaUltimoCalculoCupo, DateTime.UtcNow)
            .SetProperty(e => e.CupoPreliminar, 700_000m));
        Assert.True(await svc.PuedeAccederAsync(c.Usuario));
        await Assert.ThrowsAsync<AutorizacionAnticipoConflictoException>(() => svc.CambiarAsync(s.Admin, Peticion(activa), false));
        var actual = Assert.Single((await svc.ListarAsync(s.Admin, s.Convenio)).Items);
        Assert.Equal("AUTORIZADO", actual.Estado); Assert.NotEqual(activa.Huella, actual.Huella);
        Assert.Equal("REVOCADO", (await svc.CambiarAsync(s.Admin, Peticion(actual), false)).Estado);
    }

    [Fact]
    public async Task Permiso_de_Anticipo_no_abre_Cartera_Ordinaria_ni_usa_claims_de_presentacion()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new Semillero(cs); await s.IniciarAsync(); var c = await s.EmpleadoAsync();
        await using var db = Contexto(cs); var svc = new LibranzaAutorizacionAnticipoService(db);
        await svc.CambiarAsync(s.Admin, Peticion(Assert.Single((await svc.ListarAsync(s.Admin, s.Convenio)).Items)), true);
        Assert.True(await FiltroPermiteAsync(db, c.Usuario, typeof(LibranzaClienteController)));
        Assert.False(await FiltroPermiteAsync(db, c.Usuario, typeof(CarteraOrdinariaController)));
        Assert.False(await db.CarteraCuposOrdinarios.AnyAsync(a => a.IdUsuario == c.Usuario));
    }

    [Fact]
    public async Task Dos_aprobaciones_con_la_misma_foto_solo_registran_una_decision()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new Semillero(cs); await s.IniciarAsync(); var c = await s.EmpleadoAsync();
        await using var lectura = Contexto(cs);
        var foto = Peticion(Assert.Single((await new LibranzaAutorizacionAnticipoService(lectura).ListarAsync(s.Admin, s.Convenio)).Items));
        async Task<Exception?> Intentar()
        {
            await using var db = Contexto(cs);
            try { await new LibranzaAutorizacionAnticipoService(db).CambiarAsync(s.Admin, foto, true); return null; }
            catch (Exception ex) { return ex; }
        }
        var resultados = await Task.WhenAll(Intentar(), Intentar());
        Assert.Equal(1, resultados.Count(e => e is null));
        Assert.IsType<AutorizacionAnticipoConflictoException>(Assert.Single(resultados.OfType<Exception>()));
        Assert.Equal(1, await lectura.Auditorias.CountAsync(a => a.IdUsuario == s.Admin && a.Accion == LibranzaAutorizacionAnticipoService.AccionAutorizar));
        Assert.Equal(1, await lectura.LibranzaAutorizacionesAnticipo.Where(a => a.IdUsuario == c.Usuario).Select(a => a.Revision).SingleAsync());
    }

    [Fact]
    public async Task Falla_de_commit_no_confirma_una_habilitacion()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new Semillero(cs); await s.IniciarAsync(); var c = await s.EmpleadoAsync();
        await using var lectura = Contexto(cs);
        var foto = Peticion(Assert.Single((await new LibranzaAutorizacionAnticipoService(lectura).ListarAsync(s.Admin, s.Convenio)).Items));
        await using (var db = Contexto(cs, new FallarCommit()))
            await Assert.ThrowsAsync<InvalidOperationException>(() => new LibranzaAutorizacionAnticipoService(db).CambiarAsync(s.Admin, foto, true));
        Assert.False(await lectura.LibranzaAutorizacionesAnticipo.AnyAsync(a => a.IdUsuario == c.Usuario));
        Assert.False(await lectura.Auditorias.AnyAsync(a => a.IdUsuario == s.Admin && a.Accion == LibranzaAutorizacionAnticipoService.AccionAutorizar));
    }

    [Theory]
    [InlineData("ADMIN_EMPRESA")]
    [InlineData("USUARIO_FINAL")]
    [InlineData("CARTERA_XPAY")]
    public async Task Otros_perfiles_no_pueden_consultar_ni_autorizar(string rol)
    {
        if (!Conexion(out var cs)) return;
        await using var s = new Semillero(cs); await s.IniciarAsync(); await s.EmpleadoAsync();
        var otro = rol == "ADMIN_EMPRESA" ? await s.AdministradorEmpresaAsync() : await s.UsuarioRolAsync(rol);
        await using var db = Contexto(cs); var svc = new LibranzaAutorizacionAnticipoService(db);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.ListarAsync(otro, s.Convenio));
        var fila = Assert.Single((await svc.ListarAsync(s.Admin, s.Convenio)).Items);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.CambiarAsync(otro, Peticion(fila), true));
        Assert.False(await db.LibranzaAutorizacionesAnticipo.AnyAsync(a => a.IdEmpleadoUsuario == fila.IdEmpleadoUsuario));
    }

    [Fact]
    public async Task Rol_admin_revocado_despues_de_listar_no_autoriza()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new Semillero(cs); await s.IniciarAsync(); await s.EmpleadoAsync();
        await using var db = Contexto(cs); var svc = new LibranzaAutorizacionAnticipoService(db);
        var p = Peticion(Assert.Single((await svc.ListarAsync(s.Admin, s.Convenio)).Items));
        await db.UsuarioRoles.Where(r => r.IdUsuario == s.Admin).ExecuteUpdateAsync(x => x.SetProperty(r => r.Estado, "INACTIVO"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.CambiarAsync(s.Admin, p, true));
        Assert.False(await db.LibranzaAutorizacionesAnticipo.AnyAsync(a => a.IdEmpleadoUsuario == p.IdEmpleadoUsuario));
    }

    [Theory]
    [InlineData("correo")]
    [InlineData("empleado")]
    [InlineData("convenio")]
    [InlineData("wallet")]
    [InlineData("identidad")]
    [InlineData("vinculo")]
    public async Task Requisitos_cambiados_invalidan_foto_y_permiso_actual(string campo)
    {
        if (!Conexion(out var cs)) return;
        await using var s = new Semillero(cs); await s.IniciarAsync(); var c = await s.EmpleadoAsync();
        await using var db = Contexto(cs); var svc = new LibranzaAutorizacionAnticipoService(db);
        var foto = Peticion(Assert.Single((await svc.ListarAsync(s.Admin, s.Convenio)).Items));
        await svc.CambiarAsync(s.Admin, foto, true);
        switch (campo)
        {
            case "correo": await db.Usuarios.Where(u => u.IdUsuario == c.Usuario).ExecuteUpdateAsync(x => x.SetProperty(u => u.EmailVerificado, false)); break;
            case "empleado": await db.LibranzaEmpleados.Where(e => e.IdEmpleado == c.Empleado).ExecuteUpdateAsync(x => x.SetProperty(e => e.Estado, "INACTIVO")); break;
            case "convenio": await db.LibranzaEmpresasConvenio.Where(e => e.IdConvenio == s.Convenio).ExecuteUpdateAsync(x => x.SetProperty(e => e.FechaFin, DateTime.UtcNow.AddDays(-2))); break;
            case "wallet": await db.Wallets.Where(w => w.IdWallet == c.Wallet).ExecuteUpdateAsync(x => x.SetProperty(w => w.Estado, "INACTIVA")); break;
            case "identidad": await db.KycVerificaciones.Where(k => k.IdUsuario == c.Usuario && k.EsActual).ExecuteUpdateAsync(x => x.SetProperty(k => k.Decision, "declined")); break;
            case "vinculo": await db.LibranzaEmpleadoUsuarios.Where(v => v.IdEmpleadoUsuario == c.Vinculo).ExecuteUpdateAsync(x => x.SetProperty(v => v.Estado, "INACTIVO")); break;
        }
        Assert.False(await svc.PuedeAccederAsync(c.Usuario));
        await Assert.ThrowsAsync<AutorizacionAnticipoConflictoException>(() => svc.CambiarAsync(s.Admin, foto, true));
        // El cierre siempre se puede realizar si el vínculo aún existe activo,
        // aun cuando correo, KYC, convenio o Wallet ya no permitan autorizar.
        if (campo != "vinculo")
        {
            var actual = Assert.Single((await svc.ListarAsync(s.Admin, s.Convenio)).Items);
            Assert.True(actual.PuedeRevocar);
            Assert.Equal("REVOCADO", (await svc.CambiarAsync(s.Admin, Peticion(actual), false)).Estado);
        }
    }

    [Fact]
    public async Task Piloto_sin_historial_preserva_flujo_pero_revocacion_con_Wallet_null_no_se_evade_reenlazando()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new Semillero(cs); await s.IniciarAsync(); var c = await s.EmpleadoAsync(publico: false);
        await using var db = Contexto(cs); var svc = new LibranzaAutorizacionAnticipoService(db);
        Assert.True(await svc.PuedeAccederAsync(c.Usuario));
        await db.LibranzaEmpleadoUsuarios.Where(v => v.IdEmpleadoUsuario == c.Vinculo).ExecuteUpdateAsync(x => x.SetProperty(v => v.IdWallet, (long?)null));
        var foto = Assert.Single((await svc.ListarAsync(s.Admin, s.Convenio)).Items);
        Assert.True(foto.PuedeRevocar);
        await svc.CambiarAsync(s.Admin, Peticion(foto), false); Assert.False(await svc.PuedeAccederAsync(c.Usuario));
        await db.LibranzaEmpleadoUsuarios.Where(v => v.IdEmpleadoUsuario == c.Vinculo).ExecuteUpdateAsync(x => x.SetProperty(v => v.Estado, "INACTIVO"));
        db.LibranzaEmpleadoUsuarios.Add(new() { IdEmpleado = c.Empleado, IdUsuario = c.Usuario, IdWallet = c.Wallet,
            Estado = "ACTIVO", CreatedAt = DateTime.UtcNow }); await db.SaveChangesAsync();
        Assert.False(await svc.PuedeAccederAsync(c.Usuario));
    }

    [Fact]
    public async Task Sin_evidencia_durable_no_hay_permiso_aunque_la_fila_diga_autorizado()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new Semillero(cs); await s.IniciarAsync(); var c = await s.EmpleadoAsync();
        await using var db = Contexto(cs); var svc = new LibranzaAutorizacionAnticipoService(db);
        await svc.CambiarAsync(s.Admin, Peticion(Assert.Single((await svc.ListarAsync(s.Admin, s.Convenio)).Items)), true);
        await db.Auditorias.Where(a => a.IdUsuario == s.Admin && a.Accion == LibranzaAutorizacionAnticipoService.AccionAutorizar).ExecuteDeleteAsync();
        Assert.False(await svc.PuedeAccederAsync(c.Usuario));
        var fila = Assert.Single((await svc.ListarAsync(s.Admin, s.Convenio)).Items); Assert.Equal("REVISION_REQUERIDA", fila.Estado);
        Assert.True(fila.PuedeAutorizar); await svc.CambiarAsync(s.Admin, Peticion(fila), true);
        Assert.True(await svc.PuedeAccederAsync(c.Usuario));
    }

    [Fact]
    public async Task Auditoria_fallida_no_deja_permiso_ni_acceso()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new Semillero(cs); await s.IniciarAsync(); var c = await s.EmpleadoAsync();
        await using var lectura = Contexto(cs); var foto = Peticion(Assert.Single((await new LibranzaAutorizacionAnticipoService(lectura).ListarAsync(s.Admin, s.Convenio)).Items));
        await using (var db = Contexto(cs, new FallarAuditoria()))
        {
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => new LibranzaAutorizacionAnticipoService(db).CambiarAsync(s.Admin, foto, true));
            Assert.IsType<InvalidOperationException>(ex.InnerException);
        }
        Assert.False(await lectura.LibranzaAutorizacionesAnticipo.AnyAsync(a => a.IdUsuario == c.Usuario));
        Assert.False(await new LibranzaAutorizacionAnticipoService(lectura).PuedeAccederAsync(c.Usuario));
    }

    [Fact]
    public async Task Solicitud_directa_tras_revocar_revalida_dentro_TX_y_no_mueve_dinero()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new Semillero(cs); await s.IniciarAsync(); var c = await s.EmpleadoAsync();
        await using var db = Contexto(cs); var svc = new LibranzaAutorizacionAnticipoService(db);
        var activa = await svc.CambiarAsync(s.Admin, Peticion(Assert.Single((await svc.ListarAsync(s.Admin, s.Convenio)).Items)), true);
        await svc.CambiarAsync(s.Admin, Peticion(activa), false); var foto = await FotoAsync(db);
        var anticipo = new LibranzaAnticipoService(db, NullLogger<LibranzaAnticipoService>.Instance,
            new(new ConfigurationBuilder().Build(), Xpay.Api.Tests.Common.AmbienteXpayTests.Ambiente("QA")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => anticipo.SolicitarAnticipoAsync(c.Usuario,
            new() { ValorSolicitado = 100_000m }, Guid.NewGuid()));
        Assert.Equal(foto, await FotoAsync(db));
        Assert.False(await db.WalletIdempotencias.AnyAsync(i => i.IdUsuario == c.Usuario));
    }

    [Theory]
    [InlineData(50_000, "inferior al mínimo")]
    [InlineData(900_000, "supera el cupo disponible")]
    [InlineData(100_000, "rango de cobro")]
    public async Task Autorizacion_no_omite_limites_del_convenio_ni_comisiones(int valor, string mensaje)
    {
        if (!Conexion(out var cs)) return;
        await using var s = new Semillero(cs); await s.IniciarAsync(); var c = await s.EmpleadoAsync(); await s.ParametrosAsync(c);
        await using var db = Contexto(cs); var svc = new LibranzaAutorizacionAnticipoService(db);
        await svc.CambiarAsync(s.Admin, Peticion(Assert.Single((await svc.ListarAsync(s.Admin, s.Convenio)).Items)), true);
        var foto = await FotoAsync(db);
        var anticipo = new LibranzaAnticipoService(db, NullLogger<LibranzaAnticipoService>.Instance,
            new(new ConfigurationBuilder().Build(), Xpay.Api.Tests.Common.AmbienteXpayTests.Ambiente("QA")));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => anticipo.SolicitarAnticipoAsync(c.Usuario,
            new() { ValorSolicitado = valor, FechaSimulada = "2026-11-24" }, Guid.NewGuid()));
        Assert.Contains(mensaje, ex.Message); Assert.Equal(foto, await FotoAsync(db));
    }

    [Fact]
    public async Task Revocacion_espera_lock_del_desembolso_y_toda_operacion_posterior_ve_el_cierre()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new Semillero(cs); await s.IniciarAsync(); var c = await s.EmpleadoAsync();
        await using var db = Contexto(cs); var svc = new LibranzaAutorizacionAnticipoService(db);
        var activa = await svc.CambiarAsync(s.Admin, Peticion(Assert.Single((await svc.ListarAsync(s.Admin, s.Convenio)).Items)), true);
        await using var dinero = Contexto(cs); await using var tx = await dinero.Database.BeginTransactionAsync();
        var bloqueador = ((SqlConnection)dinero.Database.GetDbConnection()).ServerProcessId;
        _ = await dinero.LibranzaEmpleados.FromSqlInterpolated($"SELECT * FROM dbo.libranza_empleados WITH(UPDLOCK,ROWLOCK) WHERE id_empleado={c.Empleado}").SingleAsync();
        await new LibranzaAutorizacionAnticipoService(dinero).ExigirEnDesembolsoAsync(c.Usuario, c.Empleado, c.Vinculo);
        var espera = new AvisarLockEmpleado();
        await using var administracion = Contexto(cs, espera);
        var revocar = new LibranzaAutorizacionAnticipoService(administracion).CambiarAsync(s.Admin, Peticion(activa), false);
        var spid = await espera.Intento.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var reloj = Stopwatch.StartNew(); var bloqueoObservado = false;
        await using (var observador = Contexto(cs))
        {
            while (reloj.Elapsed < TimeSpan.FromSeconds(10) && !revocar.IsCompleted)
            {
                bloqueoObservado = await observador.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*) AS Value FROM sys.dm_exec_requests WHERE session_id={0} AND blocking_session_id={1} AND wait_type LIKE 'LCK_M_%'", spid, bloqueador).SingleAsync() == 1;
                if (bloqueoObservado) break;
                await Task.Delay(25);
            }
        }
        await tx.CommitAsync();
        Assert.Equal("REVOCADO", (await revocar.WaitAsync(TimeSpan.FromSeconds(10))).Estado);
        Assert.True(bloqueoObservado, "La revocación debe esperar realmente en SQL al lock del desembolso, no solo iniciar una tarea.");
        await using var nueva = Contexto(cs); await using var nuevaTx = await nueva.Database.BeginTransactionAsync();
        _ = await nueva.LibranzaEmpleados.FromSqlInterpolated($"SELECT * FROM dbo.libranza_empleados WITH(UPDLOCK,ROWLOCK) WHERE id_empleado={c.Empleado}").SingleAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new LibranzaAutorizacionAnticipoService(nueva).ExigirEnDesembolsoAsync(c.Usuario, c.Empleado, c.Vinculo));
    }

    private static CambiarAutorizacionAnticipoRequest Peticion(LibranzaAutorizacionAnticipoResponse fila) => new()
    { IdEmpleadoUsuario = fila.IdEmpleadoUsuario, Huella = fila.Huella, ConfirmaAutorizacion = true, ReferenciaRevision = "Acta sintética de revisión" };
    private static XpayDbContext Contexto(string cs, IInterceptor? interceptor = null)
    { var b = new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs); if (interceptor is not null) b.AddInterceptors(interceptor); return new(b.Options); }
    private static bool Conexion(out string cs)
    { cs = Environment.GetEnvironmentVariable("ConnectionStrings__XpayConnection") ?? ""; if (cs.Length > 0) return true;
        Assert.False(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")), "SQL local/CI obligatorio para esta suite."); return false; }
    private static async Task<string> FotoAsync(XpayDbContext db) => string.Join('|', await db.WalletMovimientos.CountAsync(),
        await db.LedgerTransacciones.CountAsync(), await db.LibranzaAnticipos.CountAsync(), await db.WalletSaldos.SumAsync(s => s.SaldoDisponible),
        await db.CarteraCuposOrdinarios.CountAsync(), await db.Wallets.CountAsync(), await db.WalletPreparaciones.CountAsync());
    private static async Task<bool> FiltroPermiteAsync(XpayDbContext db, long usuario, Type controlador)
    {
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton(db).BuildServiceProvider(),
            User = new(new ClaimsIdentity([new Claim("idUsuario", usuario.ToString()),
                new Claim("esRegistroPublico", "false"), new Claim(ClaimTypes.Role, "SUPERUSUARIO")], "fixture")) };
        var contexto = new ResourceExecutingContext(new ActionContext(http, new RouteData(),
            new ControllerActionDescriptor { ControllerTypeInfo = controlador.GetTypeInfo() }), new List<IFilterMetadata>(), new List<IValueProviderFactory>());
        var ejecutado = false;
        await controlador.GetCustomAttribute<AltaWalletSinCreditoAttribute>()!.OnResourceExecutionAsync(contexto, () =>
        { ejecutado = true; return Task.FromResult(new ResourceExecutedContext(contexto, contexto.Filters)); });
        if (!ejecutado) Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(contexto.Result).StatusCode);
        return ejecutado;
    }
    private sealed class FallarCommit : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData d,
            InterceptionResult r, CancellationToken ct = default) => throw new InvalidOperationException("Fallo sintético de commit");
    }
    private sealed class FallarAuditoria : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData d, InterceptionResult<DbDataReader> r, CancellationToken ct = default)
        { if (command.CommandText.Contains("INSERT INTO [auditoria]", StringComparison.Ordinal)) throw new InvalidOperationException("Fallo sintético de auditoría"); return new(r); }
    }
    private sealed class AvisarLockEmpleado : DbCommandInterceptor
    {
        public TaskCompletionSource<int> Intento { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData d, InterceptionResult<DbDataReader> r, CancellationToken ct = default)
        { if (command.CommandText.Contains("UPDLOCK", StringComparison.Ordinal) && command.CommandText.Contains("libranza_empleados", StringComparison.Ordinal)) Intento.TrySetResult(((SqlConnection)command.Connection!).ServerProcessId); return new(r); }
    }
    private sealed record Cuenta(long Usuario, long Persona, long Empleado, long Wallet, long Vinculo);
    private sealed class Semillero(string cs) : IAsyncDisposable
    {
        private readonly PreparacionWalletSqlTests.Semillero _cuentas = new(cs);
        private readonly List<long> _usuarios = []; private readonly List<long> _empleados = [];
        public long Admin { get; private set; } public long Convenio { get; private set; }
        public async Task IniciarAsync()
        {
            Admin = await UsuarioRolAsync("ADMIN_XPAY"); await using var db = Contexto(cs);
            var c = new LibranzaEmpresaConvenio { NombreEmpresa = "Autorización sintética " + Guid.NewGuid().ToString("N"), Nit = Guid.NewGuid().ToString("N")[..15],
                Estado = "ACTIVO", FechaInicio = DateTime.UtcNow.AddDays(-1), PorcentajeMaximoCupo = 30m, PeriodicidadPago = "MENSUAL", CreatedAt = DateTime.UtcNow };
            db.LibranzaEmpresasConvenio.Add(c); await db.SaveChangesAsync(); Convenio = c.IdConvenio;
        }
        public async Task<long> UsuarioRolAsync(string rol)
        { var c = await _cuentas.CuentaAsync(publica: false, rol: rol); _usuarios.Add(c.Usuario); return c.Usuario; }
        public async Task<long> AdministradorEmpresaAsync()
        {
            var id = await UsuarioRolAsync("USUARIO_FINAL"); await using var db = Contexto(cs);
            db.LibranzaUsuariosEmpresa.Add(new() { IdUsuario = id, IdConvenio = Convenio, RolEmpresa = "ADMIN_EMPRESA", Estado = "ACTIVO", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(); return id;
        }
        public async Task<Cuenta> EmpleadoAsync(bool publico = true)
        {
            PreparacionWalletSqlTests.Cuenta c; long wallet;
            if (publico)
            {
                var config = PreparacionWalletSqlTests.Configuracion(); c = await _cuentas.ListaAsync(config);
                await using var activacion = Contexto(cs); var login = await PreparacionWalletSqlTests.Servicio(activacion, config).ActivarAsync(c.Usuario, new(), null);
                wallet = await activacion.Wallets.Where(w => w.IdPersona == c.Persona && w.TipoWallet == "PERSONA" && w.Estado == "ACTIVA").Select(w => w.IdWallet).SingleAsync();
                Assert.False(login.RequierePerfil);
            }
            else
            {
                c = await _cuentas.CuentaAsync(publica: false); await _cuentas.AprobarIdentidadAsync(c); await _cuentas.ConfirmarCorreoAsync(c);
                await using var legado = Contexto(cs); var unidad = await legado.Personas.Where(p => p.IdPersona == c.Persona).Select(p => p.IdUnidadNegocio).SingleAsync();
                var w = new Wallet { IdUnidadNegocio = unidad, IdPersona = c.Persona, TipoWallet = "PERSONA", Estado = "ACTIVA", FechaCreacion = DateTime.UtcNow };
                legado.Wallets.Add(w); await legado.SaveChangesAsync(); wallet = w.IdWallet;
                legado.WalletSaldos.Add(new() { IdWallet = wallet, SaldoDisponible = 0m, FechaActualizacion = DateTime.UtcNow }); await legado.SaveChangesAsync();
            }
            _usuarios.Add(c.Usuario); await using var db = Contexto(cs);
            var e = new LibranzaEmpleado { IdConvenio = Convenio, TipoDocumento = "PASAPORTE", NumeroDocumento = c.Documento,
                Nombres = "Titular sintético", SalarioMensual = 2_000_000m, CupoPreliminar = 600_000m,
                PeriodicidadPago = "MENSUAL", Estado = "ACTIVO", CreatedAt = DateTime.UtcNow };
            db.LibranzaEmpleados.Add(e); await db.SaveChangesAsync(); _empleados.Add(e.IdEmpleado);
            var v = new LibranzaEmpleadoUsuario { IdEmpleado = e.IdEmpleado, IdUsuario = c.Usuario, IdWallet = wallet, Estado = "ACTIVO", CreatedByUsuario = Admin, CreatedAt = DateTime.UtcNow };
            db.LibranzaEmpleadoUsuarios.Add(v); await db.SaveChangesAsync(); return new(c.Usuario, c.Persona, e.IdEmpleado, wallet, v.IdEmpleadoUsuario);
        }
        public async Task ParametrosAsync(Cuenta c)
        {
            await using var db = Contexto(cs);
            db.LibranzaParametrosEmpresas.Add(new() { IdConvenio = Convenio, PorcentajeMaximoCupo = 30m, SalarioMinimoEmpleado = 0m,
                SalarioMaximoEmpleado = 100_000_000m, Estado = "ACTIVO", IvaPorcentaje = 19m, MomentoCobroComision = "VENCIDO", MontoMinimoAnticipo = 100_000m, CreatedAt = DateTime.UtcNow });
            db.LibranzaEmpleadoCortesPago.Add(new() { IdEmpleado = c.Empleado, NumeroCorte = 1, DiaPago = 30, ValorPagoProgramado = 1_000_000m, Estado = "ACTIVO", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        public async ValueTask DisposeAsync()
        {
            await using var db = Contexto(cs);
            await db.LibranzaAutorizacionesAnticipo.Where(a => _usuarios.Contains(a.IdUsuario)).ExecuteDeleteAsync();
            await db.WalletIdempotencias.Where(a => _usuarios.Contains(a.IdUsuario)).ExecuteDeleteAsync();
            await db.LibranzaEmpleadoUsuarios.Where(v => _empleados.Contains(v.IdEmpleado)).ExecuteDeleteAsync();
            await db.LibranzaEmpleadoCortesPago.Where(c => _empleados.Contains(c.IdEmpleado)).ExecuteDeleteAsync();
            await db.LibranzaEmpleados.Where(e => _empleados.Contains(e.IdEmpleado)).ExecuteDeleteAsync();
            await db.LibranzaParametrosEmpresas.Where(p => p.IdConvenio == Convenio).ExecuteDeleteAsync();
            await db.LibranzaUsuariosEmpresa.Where(p => p.IdConvenio == Convenio).ExecuteDeleteAsync();
            await db.LibranzaEmpresasConvenio.Where(c => c.IdConvenio == Convenio).ExecuteDeleteAsync();
            await _cuentas.DisposeAsync();
        }
    }
}
