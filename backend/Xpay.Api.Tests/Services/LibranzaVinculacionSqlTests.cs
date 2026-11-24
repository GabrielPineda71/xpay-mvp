using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// Solo fixtures sintéticos contra SQL Server. Ni clientes ni proveedores reales.
[Collection("SqlIntegration")]
public sealed class LibranzaVinculacionSqlTests
{
    [Fact]
    public async Task Lista_por_documento_aprobacion_auditada_idempotente_y_sin_dinero()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new Semillero(cs); await s.IniciarAsync();
        var cuenta = await s.EmpleadoAsync(cedulaConMiles: true);
        await using var db = Contexto(cs); var servicio = Servicio(db);
        var foto = await FotoAsync(db);
        var lista = await servicio.ListarAsync(s.Admin, s.Convenio);
        var fila = Assert.Single(lista.Items); Assert.True(fila.Elegible); Assert.Equal(cuenta.Usuario, fila.IdUsuario);
        Assert.Equal(foto, await FotoAsync(db));
        var peticion = Peticion(fila);
        Assert.Equal(1, (await servicio.AprobarAsync(s.Admin, peticion)).Aprobados);
        Assert.Equal(1, (await servicio.AprobarAsync(s.Admin, peticion)).Aprobados);
        var propia = await db.LibranzaEmpleadoUsuarios.AsNoTracking().SingleAsync(v => v.IdEmpleado == cuenta.Empleado);
        Assert.Equal(cuenta.Usuario, propia.IdUsuario); Assert.Equal(cuenta.Wallet, propia.IdWallet);
        var auditoria = await db.Auditorias.AsNoTracking().SingleAsync(a => a.IdUsuario == s.Admin && a.Accion == LibranzaVinculacionService.AccionAuditoria);
        Assert.Contains(fila.Huella, auditoria.ValorNuevo); Assert.DoesNotContain(cuenta.Documento, auditoria.ValorNuevo);
        Assert.Equal(s.Admin, propia.CreatedByUsuario); Assert.Equal(foto, await FotoAsync(db));
        var aplicada = Assert.Single((await servicio.ListarAsync(s.Admin, s.Convenio)).Items);
        Assert.Equal("APROBADO", aplicada.Estado); Assert.False(aplicada.Elegible);
    }

    [Fact]
    public async Task Cuenta_historica_sin_legales_lee_Veriff_y_solo_vincula_por_accion_expresa()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new Semillero(cs); await s.IniciarAsync();
        var cuenta = await s.EmpleadoAsync(cedulaConMiles: true);
        await using var db = Contexto(cs); var servicio = Servicio(db);
        await db.Personas.Where(p => p.IdPersona == cuenta.Persona).ExecuteUpdateAsync(set => set
            .SetProperty(p => p.TipoDocumento, (string?)null).SetProperty(p => p.NumeroDocumento, (string?)null));
        var foto = await FotoAsync(db);
        var fila = Assert.Single((await servicio.ListarAsync(s.Admin, s.Convenio)).Items);
        Assert.True(fila.IdentidadVerificada); Assert.True(fila.Elegible); Assert.Empty(fila.Razones);
        Assert.False(await db.LibranzaEmpleadoUsuarios.AnyAsync(v => v.IdUsuario == cuenta.Usuario));
        Assert.Equal(foto, await FotoAsync(db));
        var peticion = Peticion(fila); peticion.ConfirmaPertenenciaLaboral = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.AprobarAsync(s.Admin, peticion));
        Assert.False(await db.LibranzaEmpleadoUsuarios.AnyAsync(v => v.IdUsuario == cuenta.Usuario));
        peticion.ConfirmaPertenenciaLaboral = true;
        Assert.Equal(1, (await servicio.AprobarAsync(s.Admin, peticion)).Aprobados);
        Assert.Equal(1, (await servicio.AprobarAsync(s.Admin, peticion)).Aprobados);
        var propia = await db.LibranzaEmpleadoUsuarios.AsNoTracking().SingleAsync(v => v.IdUsuario == cuenta.Usuario);
        Assert.Equal(cuenta.Empleado, propia.IdEmpleado); Assert.Equal(cuenta.Wallet, propia.IdWallet);
        Assert.Equal(1, await db.Auditorias.CountAsync(a => a.IdUsuario == s.Admin && a.Accion == LibranzaVinculacionService.AccionAuditoria));
        var persona = await db.Personas.AsNoTracking().SingleAsync(p => p.IdPersona == cuenta.Persona);
        Assert.Null(persona.TipoDocumento); Assert.Null(persona.NumeroDocumento);
        Assert.Contains('.', persona.NumeroDocumentoVerificado!);
        Assert.Equal(foto, await FotoAsync(db));
    }

    [Fact]
    public async Task Vinculo_historico_activo_con_legales_vacios_no_se_presenta_como_revocado()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new Semillero(cs); await s.IniciarAsync();
        var cuenta = await s.EmpleadoAsync(cedulaConMiles: true);
        await using var db = Contexto(cs); var servicio = Servicio(db);
        await db.Personas.Where(p => p.IdPersona == cuenta.Persona).ExecuteUpdateAsync(set => set
            .SetProperty(p => p.TipoDocumento, (string?)null).SetProperty(p => p.NumeroDocumento, (string?)null));
        db.LibranzaEmpleadoUsuarios.Add(new() { IdEmpleado = cuenta.Empleado, IdUsuario = cuenta.Usuario,
            IdWallet = cuenta.Wallet, Estado = "ACTIVO", CreatedByUsuario = s.Admin, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var fila = Assert.Single((await servicio.ListarAsync(s.Admin, s.Convenio)).Items);
        Assert.Equal("APROBADO", fila.Estado); Assert.False(fila.Elegible); Assert.Empty(fila.Razones);
        Assert.Equal(1, (await servicio.AprobarAsync(s.Admin, Peticion(fila))).Aprobados);
        Assert.Equal(1, await db.LibranzaEmpleadoUsuarios.CountAsync(v => v.IdUsuario == cuenta.Usuario));
        Assert.False(await db.Auditorias.AnyAsync(a => a.IdUsuario == s.Admin && a.Accion == LibranzaVinculacionService.AccionAuditoria));
    }

    [Theory]
    [InlineData("solo-tipo")]
    [InlineData("solo-numero")]
    [InlineData("numero-distinto")]
    public async Task Documento_legal_parcial_o_distinto_no_se_sustituye_por_Veriff(string caso)
    {
        if (!Conexion(out var cs)) return;
        await using var s = new Semillero(cs); await s.IniciarAsync();
        var cuenta = await s.EmpleadoAsync(cedulaConMiles: true);
        await using var db = Contexto(cs); var servicio = Servicio(db);
        string? tipo = caso == "solo-numero" ? null : "CC";
        string? numero = caso == "solo-tipo" ? null : caso == "numero-distinto" ? "9990001111" : cuenta.Documento;
        await db.Personas.Where(p => p.IdPersona == cuenta.Persona).ExecuteUpdateAsync(set => set
            .SetProperty(p => p.TipoDocumento, tipo).SetProperty(p => p.NumeroDocumento, numero));
        var fila = Assert.Single((await servicio.ListarAsync(s.Admin, s.Convenio)).Items);
        Assert.True(fila.IdentidadVerificada); Assert.False(fila.Elegible);
        Assert.Contains(fila.Razones, r => r.Contains(caso == "numero-distinto" ? "no coinciden" : "incompleto"));
        var foto = await FotoAsync(db);
        Assert.Equal(0, (await servicio.AprobarAsync(s.Admin, Peticion(fila))).Aprobados);
        Assert.False(await db.LibranzaEmpleadoUsuarios.AnyAsync(v => v.IdUsuario == cuenta.Usuario));
        var persona = await db.Personas.AsNoTracking().SingleAsync(p => p.IdPersona == cuenta.Persona);
        Assert.Equal(tipo, persona.TipoDocumento); Assert.Equal(numero, persona.NumeroDocumento);
        Assert.Equal(foto, await FotoAsync(db));
    }

    [Fact]
    public async Task Lote_parcial_conserva_pendientes_correo_Wallet_sin_crearlas()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new Semillero(cs); await s.IniciarAsync();
        var lista = await s.EmpleadoAsync(); var pendiente = await s.EmpleadoAsync(wallet: false, correo: false);
        await using var db = Contexto(cs); var servicio = Servicio(db);
        var filas = (await servicio.ListarAsync(s.Admin, s.Convenio)).Items;
        var sin = filas.Single(f => f.IdUsuario == pendiente.Usuario);
        Assert.False(sin.Elegible); Assert.False(sin.WalletActiva); Assert.False(sin.EmailVerificado);
        var r = await servicio.AprobarAsync(s.Admin, Peticion(filas.ToArray()));
        Assert.Equal(1, r.Aprobados); Assert.Equal(1, r.Rechazados);
        Assert.True(await db.LibranzaEmpleadoUsuarios.AnyAsync(v => v.IdUsuario == lista.Usuario));
        Assert.False(await db.LibranzaEmpleadoUsuarios.AnyAsync(v => v.IdUsuario == pendiente.Usuario));
        Assert.False(await db.Wallets.AnyAsync(w => w.IdPersona == pendiente.Persona));
    }

    [Fact]
    public async Task Cambios_posteriores_a_la_vista_no_aprueban_huella_anterior()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new Semillero(cs); await s.IniciarAsync(); var cuenta = await s.EmpleadoAsync();
        await using var db = Contexto(cs); var servicio = Servicio(db);
        var fila = Assert.Single((await servicio.ListarAsync(s.Admin, s.Convenio)).Items);
        await db.LibranzaEmpleados.Where(e => e.IdEmpleado == cuenta.Empleado).ExecuteUpdateAsync(set => set.SetProperty(e => e.Nombres, "Nombre corregido ficticio"));
        var r = Assert.Single((await servicio.AprobarAsync(s.Admin, Peticion(fila))).Resultados);
        Assert.False(r.Aprobado); Assert.Contains("cambiaron", r.Mensaje);
        Assert.False(await db.LibranzaEmpleadoUsuarios.AnyAsync(v => v.IdUsuario == cuenta.Usuario));
    }

    [Fact]
    public async Task Documento_duplicado_en_otro_convenio_o_persona_bloquea_sin_elegir_primero()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new Semillero(cs); await s.IniciarAsync(); var cuenta = await s.EmpleadoAsync();
        await s.DuplicarEmpleadoAsync(cuenta.Documento);
        await using var db = Contexto(cs); var servicio = Servicio(db);
        var filas = (await servicio.ListarAsync(s.Admin, s.Convenio)).Items;
        Assert.All(filas, f => Assert.Contains(f.Razones, r => r.Contains("varios empleados")));
        Assert.All(filas, f => Assert.False(f.Elegible));
        Assert.Equal(0, (await servicio.AprobarAsync(s.Admin, Peticion(filas.ToArray()))).Aprobados);
        await s.DuplicarPersonaAsync(cuenta.Documento);
        Assert.All((await servicio.ListarAsync(s.Admin, s.Convenio)).Items, f => Assert.Contains(f.Razones, r => r.Contains("ambigua")));
    }

    [Fact]
    public async Task Usuario_o_empleado_ya_cruzado_no_se_reemplaza()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new Semillero(cs); await s.IniciarAsync();
        var a = await s.EmpleadoAsync(); var b = await s.EmpleadoAsync();
        await using var db = Contexto(cs); var servicio = Servicio(db);
        db.LibranzaEmpleadoUsuarios.Add(new() { IdEmpleado = b.Empleado, IdUsuario = a.Usuario, IdWallet = a.Wallet, Estado = "ACTIVO", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var fila = (await servicio.ListarAsync(s.Admin, s.Convenio)).Items.Single(f => f.IdEmpleado == a.Empleado);
        Assert.False(fila.Elegible); Assert.Contains(fila.Razones, r => r.Contains("otro vínculo"));
        Assert.Equal(0, (await servicio.AprobarAsync(s.Admin, Peticion(fila))).Aprobados);
        Assert.Equal(b.Empleado, (await db.LibranzaEmpleadoUsuarios.AsNoTracking().SingleAsync(v => v.IdUsuario == a.Usuario)).IdEmpleado);
    }

    [Fact]
    public async Task Admin_revocado_no_lista_ni_aprueba_y_paginacion_es_acotada()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new Semillero(cs); await s.IniciarAsync(); await s.EmpleadoAsync(); await s.EmpleadoAsync();
        await using var db = Contexto(cs); var servicio = Servicio(db);
        var pagina = await servicio.ListarAsync(s.Admin, s.Convenio, 1, 1);
        Assert.Single(pagina.Items); Assert.Equal(2, pagina.Total); Assert.Equal(2, pagina.TotalPaginas);
        Assert.Single((await servicio.ListarAsync(s.Admin, s.Convenio, 2, 1)).Items);
        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.ListarAsync(s.Admin, s.Convenio, 1, 201));
        await db.UsuarioRoles.Where(ur => ur.IdUsuario == s.Admin).ExecuteUpdateAsync(set => set.SetProperty(ur => ur.Estado, "INACTIVO"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => servicio.ListarAsync(s.Admin, s.Convenio));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => servicio.AprobarAsync(s.Admin, Peticion(pagina.Items[0])));
    }

    [Fact]
    public async Task Dos_aprobaciones_concurrentes_crean_un_vinculo_y_una_auditoria()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new Semillero(cs); await s.IniciarAsync(); var cuenta = await s.EmpleadoAsync();
        await using var vista = Contexto(cs); var fila = Assert.Single((await Servicio(vista).ListarAsync(s.Admin, s.Convenio)).Items);
        var juntas = new AbrirJuntos(2);
        async Task<AprobarVinculacionesResponse> Aprobar()
        { await using var db = Contexto(cs, juntas); return await Servicio(db).AprobarAsync(s.Admin, Peticion(fila)); }
        var resultados = await Task.WhenAll(Aprobar(), Aprobar());
        Assert.All(resultados, r => Assert.Equal(1, r.Aprobados));
        Assert.Equal(1, await vista.LibranzaEmpleadoUsuarios.CountAsync(v => v.IdUsuario == cuenta.Usuario));
        Assert.Equal(1, await vista.Auditorias.CountAsync(a => a.IdUsuario == s.Admin && a.Accion == LibranzaVinculacionService.AccionAuditoria));
    }

    [Fact]
    public async Task Error_al_persistir_auditoria_revierte_tambien_el_vinculo()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new Semillero(cs); await s.IniciarAsync(); var cuenta = await s.EmpleadoAsync();
        await using var vista = Contexto(cs); var fila = Assert.Single((await Servicio(vista).ListarAsync(s.Admin, s.Convenio)).Items);
        await using var db = Contexto(cs, new FallarAuditoria());
        var r = await Servicio(db).AprobarAsync(s.Admin, Peticion(fila)); Assert.Equal(0, r.Aprobados);
        Assert.False(await vista.LibranzaEmpleadoUsuarios.AnyAsync(v => v.IdUsuario == cuenta.Usuario));
        Assert.False(await vista.Auditorias.AnyAsync(a => a.IdUsuario == s.Admin && a.Accion == LibranzaVinculacionService.AccionAuditoria));
    }

    [Fact]
    public async Task Usuario_sin_KYC_aprobado_no_aparece_ni_se_aprueba_por_ids_fabricados()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new Semillero(cs); await s.IniciarAsync(); var cuenta = await s.EmpleadoAsync();
        await using var db = Contexto(cs); var servicio = Servicio(db);
        var fila = Assert.Single((await servicio.ListarAsync(s.Admin, s.Convenio)).Items);
        await db.Usuarios.Where(u => u.IdUsuario == cuenta.Usuario).ExecuteUpdateAsync(set => set.SetProperty(u => u.EstadoKycActual, "RECHAZADO"));
        Assert.Empty((await servicio.ListarAsync(s.Admin, s.Convenio)).Items);
        Assert.Equal(0, (await servicio.AprobarAsync(s.Admin, Peticion(fila))).Aprobados);
        Assert.False(await db.LibranzaEmpleadoUsuarios.AnyAsync(v => v.IdUsuario == cuenta.Usuario));
    }

    [Theory]
    [InlineData("rol")]
    [InlineData("empresa")]
    public async Task Cuenta_empresarial_o_perfil_mixto_no_pasa_como_empleado(string caso)
    {
        if (!Conexion(out var cs)) return;
        await using var s = new Semillero(cs); await s.IniciarAsync(); var cuenta = await s.EmpleadoAsync();
        await using var db = Contexto(cs);
        if (caso == "rol")
        {
            var rol = await db.Roles.Where(r => r.Codigo == "ADMIN_XPAY").Select(r => r.IdRol).SingleAsync();
            db.UsuarioRoles.Add(new() { IdUsuario = cuenta.Usuario, IdRol = rol, Estado = "ACTIVO", FechaAsignacion = DateTime.UtcNow });
        }
        else db.LibranzaUsuariosEmpresa.Add(new() { IdUsuario = cuenta.Usuario, IdConvenio = s.Convenio,
            RolEmpresa = "ADMIN_EMPRESA", Estado = "ACTIVO", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var fila = Assert.Single((await Servicio(db).ListarAsync(s.Admin, s.Convenio)).Items);
        Assert.False(fila.Elegible); Assert.Contains(fila.Razones, r => r.Contains("perfil"));
        Assert.Equal(0, (await Servicio(db).AprobarAsync(s.Admin, Peticion(fila))).Aprobados);
    }

    [Fact]
    public async Task Replay_anterior_a_revocacion_no_resucita_el_vinculo()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new Semillero(cs); await s.IniciarAsync(); var cuenta = await s.EmpleadoAsync();
        await using var db = Contexto(cs); var servicio = Servicio(db);
        var fila = Assert.Single((await servicio.ListarAsync(s.Admin, s.Convenio)).Items);
        Assert.Equal(1, (await servicio.AprobarAsync(s.Admin, Peticion(fila))).Aprobados);
        await db.LibranzaEmpleadoUsuarios.Where(v => v.IdUsuario == cuenta.Usuario).ExecuteUpdateAsync(set => set.SetProperty(v => v.Estado, "INACTIVO"));
        Assert.Equal(0, (await servicio.AprobarAsync(s.Admin, Peticion(fila))).Aprobados);
        Assert.False(await db.LibranzaEmpleadoUsuarios.AnyAsync(v => v.IdUsuario == cuenta.Usuario && v.Estado == "ACTIVO"));
        var nueva = Assert.Single((await servicio.ListarAsync(s.Admin, s.Convenio)).Items); Assert.NotEqual(fila.Huella, nueva.Huella);
        Assert.True(nueva.Elegible); Assert.Equal(1, (await servicio.AprobarAsync(s.Admin, Peticion(nueva))).Aprobados);
        Assert.Equal(2, await db.Auditorias.CountAsync(a => a.IdUsuario == s.Admin && a.Accion == LibranzaVinculacionService.AccionAuditoria));
    }

    [Fact]
    public async Task Error_de_commit_no_anuncia_exito_y_revierte_el_vinculo()
    {
        if (!Conexion(out var cs)) return;
        await using var s = new Semillero(cs); await s.IniciarAsync(); var cuenta = await s.EmpleadoAsync();
        await using var vista = Contexto(cs); var fila = Assert.Single((await Servicio(vista).ListarAsync(s.Admin, s.Convenio)).Items);
        await using var db = Contexto(cs, new FallarCommit());
        Assert.Equal(0, (await Servicio(db).AprobarAsync(s.Admin, Peticion(fila))).Aprobados);
        Assert.False(await vista.LibranzaEmpleadoUsuarios.AnyAsync(v => v.IdUsuario == cuenta.Usuario));
        Assert.False(await vista.Auditorias.AnyAsync(a => a.IdUsuario == s.Admin && a.Accion == LibranzaVinculacionService.AccionAuditoria));
    }

    [Theory]
    [InlineData("legacy")]
    [InlineData("importacion")]
    public async Task Aprobacion_retiene_lecturas_hasta_commit_frente_a_legacy_e_importacion(string competidor)
    {
        if (!Conexion(out var cs)) return;
        await using var s = new Semillero(cs); await s.IniciarAsync(); var cuenta = await s.EmpleadoAsync();
        await using var vista = Contexto(cs); var fila = Assert.Single((await Servicio(vista).ListarAsync(s.Admin, s.Convenio)).Items);
        var pausa = new PausarAntesDeGuardar(); await using var aprobador = Contexto(cs, pausa);
        var aprobacion = Servicio(aprobador).AprobarAsync(s.Admin, Peticion(fila));
        var spidAprobador = await pausa.Detenida.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await using var otra = Contexto(cs); await otra.Database.OpenConnectionAsync();
        var spidOtro = ((SqlConnection)otra.Database.GetDbConnection()).ServerProcessId;
        Task operacion = competidor == "legacy"
            ? new LibranzaAnticipoService(otra, NullLogger<LibranzaAnticipoService>.Instance, null!)
                .AsociarEmpleadoUsuarioAsync(cuenta.Empleado, new() { IdConvenio = s.Convenio, IdUsuario = cuenta.Usuario }, s.Admin)
            // Mismo UPDATE de la importación real: ningún applock del aprobador.
            : otra.LibranzaEmpleados.Where(e => e.IdEmpleado == cuenta.Empleado)
                .ExecuteUpdateAsync(set => set.SetProperty(e => e.Nombres, "Nombre actualizado por carga ficticia"));
        try
        {
            var reloj = Stopwatch.StartNew(); var bloqueada = false;
            while (reloj.Elapsed < TimeSpan.FromSeconds(8))
            {
                var numero = await vista.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*) AS Value FROM sys.dm_exec_requests WHERE session_id={0} AND blocking_session_id={1} AND wait_type LIKE 'LCK_M_%'",
                    spidOtro, spidAprobador).SingleAsync();
                if (numero == 1) { bloqueada = true; break; }
                if (operacion.IsCompleted) break;
                await Task.Delay(40);
            }
            Assert.True(bloqueada, "La escritura competidora debe esperar al commit del aprobador.");
        }
        finally
        {
            pausa.Continuar.TrySetResult();
            await aprobacion.WaitAsync(TimeSpan.FromSeconds(20)); await operacion.WaitAsync(TimeSpan.FromSeconds(20));
        }
        Assert.Equal(1, (await aprobacion).Aprobados);
        Assert.Equal(1, await vista.LibranzaEmpleadoUsuarios.CountAsync(v => v.IdUsuario == cuenta.Usuario && v.Estado == "ACTIVO"));
        Assert.Equal(1, await vista.Auditorias.CountAsync(a => a.IdUsuario == s.Admin && a.Accion == LibranzaVinculacionService.AccionAuditoria));
    }

    private static AprobarVinculacionesRequest Peticion(params LibranzaCoincidenciaResponse[] filas) => new()
    { ConfirmaPertenenciaLaboral = true, ReferenciaRevision = "RRHH sintético de prueba",
        Seleccion = filas.Select(f => new SeleccionVinculacionRequest { IdEmpleado = f.IdEmpleado, IdUsuario = f.IdUsuario, Huella = f.Huella }).ToList() };
    private static LibranzaVinculacionService Servicio(XpayDbContext db) => new(db, NullLogger<LibranzaVinculacionService>.Instance);
    private static XpayDbContext Contexto(string cs, IInterceptor? interceptor = null)
    { var b = new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs); if (interceptor is not null) b.AddInterceptors(interceptor); return new(b.Options); }
    private static bool Conexion(out string cs)
    { cs = Environment.GetEnvironmentVariable("ConnectionStrings__XpayConnection") ?? "";
        if (cs.Length > 0) return true;
        Assert.False(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")), "Se requiere SQL de pruebas para coincidencias."); return false; }
    private static async Task<string> FotoAsync(XpayDbContext db) => string.Join('|',
        await db.Wallets.CountAsync(), await db.WalletSaldos.SumAsync(s => s.SaldoDisponible),
        await db.LibranzaAnticipos.CountAsync(), await db.WalletMovimientos.CountAsync(), await db.LedgerTransacciones.CountAsync(),
        await db.WalletPreparaciones.CountAsync(), await db.PinesTransaccionales.CountAsync());

    private sealed class AbrirJuntos(int participantes) : DbConnectionInterceptor
    {
        private int _abiertos; private readonly TaskCompletionSource _todos = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData data, CancellationToken ct = default)
        { if (Interlocked.Increment(ref _abiertos) >= participantes) _todos.TrySetResult(); await _todos.Task.WaitAsync(TimeSpan.FromSeconds(15), ct); }
    }
    private sealed class FallarCommit : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken ct = default) =>
            throw new InvalidOperationException("Fallo sintético de commit");
    }
    private sealed class PausarAntesDeGuardar : DbCommandInterceptor
    {
        public TaskCompletionSource<int> Detenida { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continuar { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("INSERT INTO [libranza_empleado_usuario]", StringComparison.OrdinalIgnoreCase))
            { Detenida.TrySetResult(((SqlConnection)command.Connection!).ServerProcessId); await Continuar.Task.WaitAsync(TimeSpan.FromSeconds(20), ct); }
            return result;
        }
    }
    private sealed class FallarAuditoria : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { if (command.CommandText.Contains("INSERT INTO [auditoria]", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Fallo sintético de auditoría"); return new(result); }
    }

    private sealed record Cuenta(long Usuario, long Persona, long Empleado, long? Wallet, string Documento);
    private sealed class Semillero(string cs) : IAsyncDisposable
    {
        private readonly PreparacionWalletSqlTests.Semillero _cuentas = new(cs);
        private readonly List<long> _empleados = []; private readonly List<long> _convenios = [];
        public long Admin { get; private set; } public long Convenio { get; private set; }
        public async Task IniciarAsync()
        { Admin = (await _cuentas.CuentaAsync(publica: false, rol: "ADMIN_XPAY")).Usuario; Convenio = await ConvenioAsync(); }
        private async Task<long> ConvenioAsync()
        { await using var db = Contexto(cs); var c = new LibranzaEmpresaConvenio { NombreEmpresa = "Empresa sintética " + Guid.NewGuid().ToString("N")[..10],
            Nit = "9" + Guid.NewGuid().ToString("N")[..10], Estado = "ACTIVO", PeriodicidadPago = "MENSUAL", PorcentajeMaximoCupo = 30m,
            FechaInicio = DateTime.UtcNow.AddDays(-1), CreatedAt = DateTime.UtcNow };
            db.LibranzaEmpresasConvenio.Add(c); await db.SaveChangesAsync(); _convenios.Add(c.IdConvenio); return c.IdConvenio; }
        public async Task<Cuenta> EmpleadoAsync(bool wallet = true, bool correo = true, bool cedulaConMiles = false)
        {
            var cuenta = await _cuentas.CuentaAsync(publica: false); await _cuentas.AprobarIdentidadAsync(cuenta);
            if (correo) await _cuentas.ConfirmarCorreoAsync(cuenta);
            await using var db = Contexto(cs); var documento = cuenta.Documento;
            var tipo = "PASAPORTE";
            if (cedulaConMiles)
            { documento = (9_000_000_000L + cuenta.Persona).ToString(); tipo = "CC";
                var crudo = long.Parse(documento).ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("es-CO"));
                await db.Personas.Where(p => p.IdPersona == cuenta.Persona).ExecuteUpdateAsync(set => set
                    .SetProperty(p => p.TipoDocumento, "CC").SetProperty(p => p.NumeroDocumento, documento)
                    .SetProperty(p => p.NumeroDocumentoVerificado, crudo).SetProperty(p => p.TipoDocumentoVeriffRaw, "ID_CARD")); }
            var e = new LibranzaEmpleado { IdConvenio = Convenio, TipoDocumento = tipo, NumeroDocumento = documento,
                Nombres = "Empleado ficticio", SalarioMensual = 2_000_000m, PeriodicidadPago = "MENSUAL", Estado = "ACTIVO",
                CupoPreliminar = 600_000m, OrigenCarga = "MANUAL", CreatedAt = DateTime.UtcNow };
            db.LibranzaEmpleados.Add(e); await db.SaveChangesAsync(); _empleados.Add(e.IdEmpleado);
            long? idWallet = null;
            if (wallet)
            { var unidad = await db.Personas.Where(p => p.IdPersona == cuenta.Persona).Select(p => p.IdUnidadNegocio).SingleAsync();
                var w = new Wallet { IdUnidadNegocio = unidad, IdPersona = cuenta.Persona, TipoWallet = "PERSONA", Estado = "ACTIVA", FechaCreacion = DateTime.UtcNow };
                db.Wallets.Add(w); await db.SaveChangesAsync(); idWallet = w.IdWallet;
                db.WalletSaldos.Add(new() { IdWallet = w.IdWallet, SaldoDisponible = 250_000m, FechaActualizacion = DateTime.UtcNow }); await db.SaveChangesAsync(); }
            return new(cuenta.Usuario, cuenta.Persona, e.IdEmpleado, idWallet, documento);
        }
        public async Task DuplicarEmpleadoAsync(string documento)
        { var convenio = await ConvenioAsync(); await using var db = Contexto(cs);
            var e = new LibranzaEmpleado { IdConvenio = convenio, TipoDocumento = "PASAPORTE", NumeroDocumento = documento,
                Nombres = "Duplicado ficticio", SalarioMensual = 2_000_000m, PeriodicidadPago = "MENSUAL", Estado = "ACTIVO", OrigenCarga = "MANUAL", CreatedAt = DateTime.UtcNow };
            db.LibranzaEmpleados.Add(e); await db.SaveChangesAsync(); _empleados.Add(e.IdEmpleado); }
        public async Task DuplicarPersonaAsync(string documento)
        { var c = await _cuentas.CuentaAsync(publica: false); await using var db = Contexto(cs);
            await db.Personas.Where(p => p.IdPersona == c.Persona).ExecuteUpdateAsync(set => set.SetProperty(p => p.TipoDocumentoVeriffRaw, "PASSPORT")
                .SetProperty(p => p.NumeroDocumentoVerificado, documento)); }
        public async ValueTask DisposeAsync()
        { await using var db = Contexto(cs); await db.LibranzaEmpleadoUsuarios.Where(v => _empleados.Contains(v.IdEmpleado)).ExecuteDeleteAsync();
            await db.LibranzaEmpleados.Where(e => _empleados.Contains(e.IdEmpleado)).ExecuteDeleteAsync();
            await db.LibranzaUsuariosEmpresa.Where(v => _convenios.Contains(v.IdConvenio)).ExecuteDeleteAsync();
            await db.LibranzaEmpresasConvenio.Where(c => _convenios.Contains(c.IdConvenio)).ExecuteDeleteAsync(); await _cuentas.DisposeAsync(); }
    }
}
