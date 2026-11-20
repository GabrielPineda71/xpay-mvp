using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xpay.Api.Tests.Common;
using Xunit;

namespace Xpay.Api.Tests.Services;

[Collection("SqlIntegration")]
public sealed class LibranzaVinculacionSqlTests
{
    [Fact]
    public async Task Listado_no_muta_y_aprobacion_repetida_no_duplica_vinculo_Wallet_ni_ledger()
    {
        if (!Conexion(out var cs)) return;
        var s = new Semilla();
        try
        {
            await SembrarAsync(cs, s);
            await using var db = Contexto(cs);
            var servicio = Servicio(db);
            var c = Assert.Single((await servicio.ListarAsync(s.Admin, s.Convenio)).Items);
            Assert.True(c.Elegible); Assert.DoesNotContain(s.Documento, c.DocumentoMasked);
            Assert.False(await db.LibranzaEmpleadoUsuarios.AnyAsync(x => x.IdEmpleado == s.Empleado));
            var asientos = await db.LedgerTransacciones.CountAsync();
            var primero = await servicio.AprobarAsync(s.Admin, Solicitud(c));
            Assert.Equal(1, primero.Aprobados);
            var segundo = await servicio.AprobarAsync(s.Admin, Solicitud(c));
            Assert.Equal(1, segundo.Aprobados);
            var link = await db.LibranzaEmpleadoUsuarios.AsNoTracking().SingleAsync(x => x.IdEmpleado == s.Empleado);
            Assert.Equal((s.Usuario, s.Wallet, "MANUAL", s.Admin), (link.IdUsuario, link.IdWallet!.Value, link.RevisionMetodo, link.RevisionPorUsuario!.Value));
            Assert.Equal(c.Huella, link.RevisionHuella); Assert.NotNull(link.RevisionFecha);
            await LibranzaVinculacionService.ValidarRevisionVigenteAsync(db, link);
            Assert.Equal(1, await db.Wallets.CountAsync(x => x.IdPersona == s.Persona));
            Assert.Equal(0m, (await db.WalletSaldos.SingleAsync(x => x.IdWallet == s.Wallet)).SaldoDisponible);
            Assert.Equal(asientos, await db.LedgerTransacciones.CountAsync());
            Assert.Equal(1, await db.Auditorias.CountAsync(x => x.IdUsuario == s.Admin && x.Accion == "PERTENENCIA_LABORAL_APROBADA"));
            Assert.Equal("APROBADO", Assert.Single((await servicio.ListarAsync(s.Admin, s.Convenio)).Items).Estado);
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Fact]
    public async Task Cambio_salarial_posterior_a_revision_rechaza_la_huella_y_no_activa()
    {
        if (!Conexion(out var cs)) return;
        var s = new Semilla();
        try
        {
            await SembrarAsync(cs, s);
            LibranzaCoincidenciaResponse candidato;
            await using (var inicial = Contexto(cs)) candidato = Assert.Single((await Servicio(inicial).ListarAsync(s.Admin, s.Convenio)).Items);
            await using (var cambio = Contexto(cs))
                await cambio.LibranzaEmpleados.Where(x => x.IdEmpleado == s.Empleado).ExecuteUpdateAsync(x => x.SetProperty(e => e.SalarioMensual, 2_000_001m));
            await using var db = Contexto(cs);
            var r = await Servicio(db).AprobarAsync(s.Admin, Solicitud(candidato));
            Assert.Equal(0, r.Aprobados); Assert.Contains("cambiaron", r.Resultados[0].Mensaje);
            Assert.False(await db.LibranzaEmpleadoUsuarios.AnyAsync(x => x.IdEmpleado == s.Empleado));
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Fact]
    public async Task Aprobacion_masiva_informa_por_fila_y_no_activa_un_elemento_con_verificacion_pendiente()
    {
        if (!Conexion(out var cs)) return;
        var a = new Semilla(); var b = new Semilla();
        try
        {
            await SembrarAsync(cs, a); await SembrarAsync(cs, b);
            await using var db = Contexto(cs);
            await db.Usuarios.Where(x => x.IdUsuario == b.Usuario).ExecuteUpdateAsync(x => x.SetProperty(y => y.CelularVerificado, false));
            var primero = Assert.Single((await Servicio(db).ListarAsync(a.Admin, a.Convenio)).Items);
            var segundo = Assert.Single((await Servicio(db).ListarAsync(a.Admin, b.Convenio)).Items);
            var asientos = await db.LedgerTransacciones.CountAsync();
            var r = await Servicio(db).AprobarAsync(a.Admin, Solicitud(primero, segundo));
            Assert.Equal((1, 1), (r.Aprobados, r.Rechazados));
            Assert.True(r.Resultados[0].Aprobado); Assert.False(r.Resultados[1].Aprobado);
            Assert.Contains("WhatsApp", r.Resultados[1].Mensaje);
            Assert.False(await db.LibranzaEmpleadoUsuarios.AnyAsync(x => x.IdEmpleado == b.Empleado));
            Assert.Equal(asientos, await db.LedgerTransacciones.CountAsync());
        }
        finally { await LimpiarAsync(cs, a); await LimpiarAsync(cs, b); }
    }

    [Fact]
    public async Task Segundo_empleado_normalizado_en_otro_convenio_bloquea_ambas_coincidencias()
    {
        if (!Conexion(out var cs)) return;
        var s = new Semilla();
        try
        {
            await SembrarAsync(cs, s);
            await using var db = Contexto(cs);
            var otro = new LibranzaEmpresaConvenio
            {
                NombreEmpresa = "Otra empresa ficticia", Nit = "T" + Guid.NewGuid().ToString("N")[..12],
                Estado = "ACTIVO", PeriodicidadPago = "MENSUAL", PorcentajeMaximoCupo = 30,
                FechaInicio = DateTime.UtcNow.AddDays(-1), CreatedAt = DateTime.UtcNow,
            };
            db.LibranzaEmpresasConvenio.Add(otro); await db.SaveChangesAsync(); s.OtroConvenio = otro.IdConvenio;
            var empleado = new LibranzaEmpleado
            {
                IdConvenio = otro.IdConvenio, TipoDocumento = "CC", NumeroDocumento = s.Documento[..3] + "." + s.Documento[3..],
                Nombres = "Otro registro ficticio", SalarioMensual = 2_000_000m, PeriodicidadPago = "MENSUAL",
                DiaPago1 = 30, CupoPreliminar = 600_000m, Estado = "ACTIVO", CreatedAt = DateTime.UtcNow,
            };
            db.LibranzaEmpleados.Add(empleado); await db.SaveChangesAsync(); s.OtroEmpleado = empleado.IdEmpleado;
            var c = Assert.Single((await Servicio(db).ListarAsync(s.Admin, s.Convenio)).Items);
            Assert.False(c.Elegible); Assert.Contains(c.Razones, x => x.Contains("varios empleados"));
            var r = await Servicio(db).AprobarAsync(s.Admin, Solicitud(c));
            Assert.Equal(0, r.Aprobados);
            Assert.False(await db.LibranzaEmpleadoUsuarios.AnyAsync(x => x.IdEmpleado == s.Empleado || x.IdEmpleado == s.OtroEmpleado));
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Fact]
    public async Task Condiciones_modificadas_despues_de_aprobar_bloquean_revision_runtime()
    {
        if (!Conexion(out var cs)) return;
        var s = new Semilla();
        try
        {
            await SembrarAsync(cs, s);
            await using var db = Contexto(cs);
            var c = Assert.Single((await Servicio(db).ListarAsync(s.Admin, s.Convenio)).Items);
            Assert.Equal(1, (await Servicio(db).AprobarAsync(s.Admin, Solicitud(c))).Aprobados);
            var link = await db.LibranzaEmpleadoUsuarios.AsNoTracking().SingleAsync(x => x.IdEmpleado == s.Empleado);
            await db.LibranzaEmpleadoCortesPago.Where(x => x.IdEmpleado == s.Empleado)
                .ExecuteUpdateAsync(x => x.SetProperty(y => y.ValorPagoProgramado, 1_900_000m));
            await Assert.ThrowsAsync<InvalidOperationException>(() => LibranzaVinculacionService.ValidarRevisionVigenteAsync(db, link));
            var nuevo = Assert.Single((await Servicio(db).ListarAsync(s.Admin, s.Convenio)).Items);
            Assert.Equal("REVISION_REQUERIDA", nuevo.Estado); Assert.True(nuevo.Elegible);
            Assert.Equal(1, (await Servicio(db).AprobarAsync(s.Admin, Solicitud(nuevo))).Aprobados);
            var revisado = await db.LibranzaEmpleadoUsuarios.AsNoTracking().SingleAsync(x => x.IdEmpleado == s.Empleado);
            await LibranzaVinculacionService.ValidarRevisionVigenteAsync(db, revisado);
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Fact]
    public async Task Dos_aprobaciones_concurrentes_se_serializan_y_crean_una_sola_revision()
    {
        if (!Conexion(out var cs)) return;
        var s = new Semilla();
        try
        {
            await SembrarAsync(cs, s);
            LibranzaCoincidenciaResponse c;
            await using (var inicial = Contexto(cs)) c = Assert.Single((await Servicio(inicial).ListarAsync(s.Admin, s.Convenio)).Items);
            async Task<AprobarVinculacionesResponse> Ejecutar()
            { await using var db = Contexto(cs); return await Servicio(db).AprobarAsync(s.Admin, Solicitud(c)); }
            var resultados = await Task.WhenAll(Ejecutar(), Ejecutar());
            Assert.All(resultados, r => Assert.Equal(1, r.Aprobados));
            await using var v = Contexto(cs);
            Assert.Equal(1, await v.LibranzaEmpleadoUsuarios.CountAsync(x => x.IdEmpleado == s.Empleado));
            Assert.Equal(1, await v.Auditorias.CountAsync(x => x.IdUsuario == s.Admin && x.Accion == "PERTENENCIA_LABORAL_APROBADA"));
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Fact]
    public async Task Revision_runtime_retiene_cortes_hasta_finalizar_transaccion()
    {
        if (!Conexion(out var cs)) return;
        var s = new Semilla();
        try
        {
            await SembrarAsync(cs, s);
            await using var db = Contexto(cs);
            var c = Assert.Single((await Servicio(db).ListarAsync(s.Admin, s.Convenio)).Items);
            Assert.Equal(1, (await Servicio(db).AprobarAsync(s.Admin, Solicitud(c))).Aprobados);
            var link = await db.LibranzaEmpleadoUsuarios.AsNoTracking().SingleAsync(x => x.IdEmpleado == s.Empleado);
            await using var tx = await db.Database.BeginTransactionAsync();
            await LibranzaVinculacionService.ValidarRevisionVigenteAsync(db, link);
            await using var escritor = Contexto(cs);
            // SET LOCK_TIMEOUT pertenece a la conexión; mantenerla abierta
            // evita que el pool la restablezca antes del UPDATE de la prueba.
            await escritor.Database.OpenConnectionAsync();
            await escritor.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 250;");
            await using var comando = escritor.Database.GetDbConnection().CreateCommand();
            comando.CommandText = "UPDATE dbo.libranza_empleado_cortes_pago SET valor_pago_programado=1900000 WHERE id_empleado=@empleado";
            comando.Parameters.Add(new SqlParameter("@empleado", System.Data.SqlDbType.BigInt) { Value = s.Empleado });
            var error = await Assert.ThrowsAsync<SqlException>(() => comando.ExecuteNonQueryAsync());
            Assert.Equal(1222, error.Number);
            await tx.RollbackAsync();
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Fact]
    public async Task Objeto_anterior_a_revocacion_no_autoriza_una_vinculacion_inactiva()
    {
        if (!Conexion(out var cs)) return;
        var s = new Semilla();
        try
        {
            await SembrarAsync(cs, s);
            await using var db = Contexto(cs);
            var c = Assert.Single((await Servicio(db).ListarAsync(s.Admin, s.Convenio)).Items);
            Assert.Equal(1, (await Servicio(db).AprobarAsync(s.Admin, Solicitud(c))).Aprobados);
            var objetoAnterior = await db.LibranzaEmpleadoUsuarios.AsNoTracking().SingleAsync(x => x.IdEmpleado == s.Empleado);
            await using (var cambio = Contexto(cs))
                await cambio.LibranzaEmpleadoUsuarios.Where(x => x.IdEmpleado == s.Empleado)
                    .ExecuteUpdateAsync(x => x.SetProperty(y => y.Estado, "INACTIVO"));
            Assert.Equal("ACTIVO", objetoAnterior.Estado);
            await Assert.ThrowsAsync<InvalidOperationException>(() => LibranzaVinculacionService.ValidarRevisionVigenteAsync(db, objetoAnterior));
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Fact]
    public async Task Perfil_concurrente_espera_commit_del_guard_y_luego_exige_nueva_verificacion()
    {
        if (!Conexion(out var cs)) return;
        var s = new Semilla();
        try
        {
            await SembrarAsync(cs, s);
            await using var db = Contexto(cs);
            var c = Assert.Single((await Servicio(db).ListarAsync(s.Admin, s.Convenio)).Items);
            Assert.Equal(1, (await Servicio(db).AprobarAsync(s.Admin, Solicitud(c))).Aprobados);
            var link = await db.LibranzaEmpleadoUsuarios.AsNoTracking().SingleAsync(x => x.IdEmpleado == s.Empleado);
            await using var tx = await db.Database.BeginTransactionAsync();
            await LibranzaVinculacionService.BloquearParaOperacionAsync(db, link);
            await LibranzaVinculacionService.ValidarRevisionVigenteAsync(db, link);
            var iniciado = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var editar = Task.Run(async () =>
            {
                await using var perfilDb = Contexto(cs);
                iniciado.SetResult();
                return await new PerfilService(perfilDb).ActualizarAsync(s.Usuario, s.Persona,
                    new(null, "nuevo." + Guid.NewGuid().ToString("N") + "@example.test", null, null, null));
            });
            await iniciado.Task;
            Assert.NotSame(editar, await Task.WhenAny(editar, Task.Delay(250)));
            await tx.CommitAsync();
            await editar.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(await db.Usuarios.AsNoTracking().Where(u => u.IdUsuario == s.Usuario).Select(u => u.EmailVerificado).SingleAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => LibranzaVinculacionService.ValidarRevisionVigenteAsync(db, link));
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Fact]
    public async Task Bandera_apagada_y_admin_revocado_no_pueden_ni_listar_ni_aprobar()
    {
        if (!Conexion(out var cs)) return;
        var s = new Semilla();
        try
        {
            await SembrarAsync(cs, s);
            await using var db = Contexto(cs);
            await Assert.ThrowsAsync<VinculacionNominaNoDisponibleException>(() => Servicio(db, false).ListarAsync(s.Admin, s.Convenio));
            await db.UsuarioRoles.Where(x => x.IdUsuario == s.Admin).ExecuteUpdateAsync(x => x.SetProperty(y => y.Estado, "INACTIVO"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Servicio(db).ListarAsync(s.Admin, s.Convenio));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Servicio(db).AprobarAsync(s.Admin, new()));
        }
        finally { await LimpiarAsync(cs, s); }
    }

    private static AprobarVinculacionesRequest Solicitud(params LibranzaCoincidenciaResponse[] candidatos) => new()
    {
        ConfirmaPertenenciaLaboral = true, ReferenciaRevision = "RRHH prueba local ficticia",
        Seleccion = candidatos.Select(c => new SeleccionVinculacionRequest { IdEmpleado = c.IdEmpleado, IdUsuario = c.IdUsuario, Huella = c.Huella }).ToList(),
    };

    private static LibranzaVinculacionService Servicio(XpayDbContext db, bool habilitado = true)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { [LibranzaVinculacionService.ClaveHabilitado] = habilitado ? "true" : "false" }).Build();
        var consentimiento = new ConsentimientoBiometricoService(db, config, AmbienteXpayTests.Ambiente("Production"), TimeProvider.System);
        return new(db, config, new HabilitacionCuentaPublicaService(db, config, consentimiento), NullLogger<LibranzaVinculacionService>.Instance);
    }

    private sealed class Semilla
    {
        public long Admin, Usuario, PersonaAdmin, Persona, Wallet, Convenio, Empleado, OtroConvenio, OtroEmpleado;
        public string Documento = string.Empty;
    }

    private static async Task SembrarAsync(string cs, Semilla s)
    {
        await using var db = Contexto(cs);
        var unidad = await db.Database.SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo='XPAY_COL'").SingleAsync();
        var sufijo = Guid.NewGuid().ToString("N")[..12]; var ahora = DateTime.UtcNow;
        var adminPersona = new Persona { IdUnidadNegocio = unidad, Estado = "ACTIVA", Celular = "3000000000", Email = $"admin.{sufijo}@example.test", FechaCreacion = ahora };
        db.Personas.Add(adminPersona); await db.SaveChangesAsync(); s.PersonaAdmin = adminPersona.IdPersona;
        var admin = new Usuario { IdPersona = s.PersonaAdmin, NombreUsuario = "vinc_admin_" + sufijo, Estado = "ACTIVO", PasswordHash = "solo-fixture", FechaCreacion = ahora };
        db.Usuarios.Add(admin); await db.SaveChangesAsync(); s.Admin = admin.IdUsuario;
        var rolAdmin = await db.Roles.SingleAsync(x => x.Codigo == "SUPERUSUARIO");
        db.UsuarioRoles.Add(new() { IdUsuario = s.Admin, IdRol = rolAdmin.IdRol, Estado = "ACTIVO", FechaAsignacion = ahora });
        s.Documento = "97" + Random.Shared.NextInt64(10_000_000, 99_999_999).ToString();
        var persona = new Persona
        {
            IdUnidadNegocio = unidad, Estado = "ACTIVA", TipoDocumento = "CC", NumeroDocumento = s.Documento,
            IdentidadVerificada = true, IdentidadVerificadaProveedor = "VERIFF", IdentidadVerificadaFecha = ahora,
            TipoDocumentoVeriffRaw = "ID_CARD", NumeroDocumentoVerificado = s.Documento,
            NombreVerificadoCompleto = "Persona ficticia", ApellidoVerificadoCompleto = "Prueba local",
            Celular = "3000000000", Email = $"usuario.{sufijo}@example.test", FechaCreacion = ahora,
        };
        db.Personas.Add(persona); await db.SaveChangesAsync(); s.Persona = persona.IdPersona;
        var usuario = new Usuario { IdPersona = s.Persona, NombreUsuario = "vinc_usuario_" + sufijo, Estado = "ACTIVO", PasswordHash = "solo-fixture",
            EmailVerificado = true, CelularVerificado = true, EstadoKycActual = "APROBADO", FechaCreacion = ahora };
        db.Usuarios.Add(usuario); await db.SaveChangesAsync(); s.Usuario = usuario.IdUsuario;
        var rol = await db.Roles.SingleAsync(x => x.Codigo == "USUARIO_FINAL");
        db.UsuarioRoles.Add(new() { IdUsuario = s.Usuario, IdRol = rol.IdRol, Estado = "ACTIVO", FechaAsignacion = ahora });
        db.KycVerificaciones.Add(new() { IdUsuario = s.Usuario, IdPersona = s.Persona, Proveedor = "VERIFF", EstadoKyc = "APROBADO", EsActual = true,
            Decision = "approved", FechaCreacion = ahora, FechaDecision = ahora });
        var wallet = new Wallet { IdPersona = s.Persona, IdUnidadNegocio = unidad, TipoWallet = "PERSONA", Estado = "ACTIVA", NombreWallet = "Wallet ficticia", FechaCreacion = ahora };
        db.Wallets.Add(wallet); await db.SaveChangesAsync(); s.Wallet = wallet.IdWallet;
        db.WalletSaldos.Add(new() { IdWallet = s.Wallet, FechaActualizacion = ahora });
        var convenio = new LibranzaEmpresaConvenio { NombreEmpresa = "Empresa ficticia " + sufijo, Nit = "T" + sufijo, Estado = "ACTIVO", PeriodicidadPago = "MENSUAL",
            DiaPago1 = 30, PorcentajeMaximoCupo = 30, FechaInicio = ahora.AddDays(-1), CreatedAt = ahora };
        db.LibranzaEmpresasConvenio.Add(convenio); await db.SaveChangesAsync(); s.Convenio = convenio.IdConvenio;
        db.LibranzaParametrosEmpresas.Add(new() { IdConvenio = s.Convenio, Estado = "ACTIVO", PorcentajeMaximoCupo = 30, MomentoCobroComision = "VENCIDO", MontoMinimoAnticipo = 100_000m, CreatedAt = ahora });
        var empleado = new LibranzaEmpleado { IdConvenio = s.Convenio, TipoDocumento = "CC", NumeroDocumento = s.Documento, Nombres = "Persona ficticia", Apellidos = "Prueba local",
            SalarioMensual = 2_000_000m, PeriodicidadPago = "MENSUAL", DiaPago1 = 30, CupoPreliminar = 600_000m, Estado = "ACTIVO", CreatedAt = ahora };
        db.LibranzaEmpleados.Add(empleado); await db.SaveChangesAsync(); s.Empleado = empleado.IdEmpleado;
        db.LibranzaEmpleadoCortesPago.Add(new() { IdEmpleado = s.Empleado, NumeroCorte = 1, DiaPago = 30, ValorPagoProgramado = 2_000_000m, Estado = "ACTIVO", CreatedAt = ahora });
        await db.SaveChangesAsync();
    }

    private static bool Conexion(out string cs)
    {
        cs = Environment.GetEnvironmentVariable("ConnectionStrings__XpayConnection") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(cs))
        { Assert.False(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")), "Estas pruebas requieren SQL de CI aislado."); return false; }
        var servidor = new SqlConnectionStringBuilder(cs).DataSource.Replace("tcp:", "", StringComparison.OrdinalIgnoreCase);
        Assert.True(servidor.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase) || servidor.StartsWith("localhost", StringComparison.OrdinalIgnoreCase),
            "Las pruebas de vinculaciones solo se ejecutan contra SQL local aislado.");
        return true;
    }
    private static XpayDbContext Contexto(string cs) => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    private static async Task LimpiarAsync(string cs, Semilla s)
    {
        await using var db = Contexto(cs);
        var usuarios = new[] { s.Admin, s.Usuario }.Where(x => x > 0).ToArray();
        var empleados = new[] { s.Empleado, s.OtroEmpleado }.Where(x => x > 0).ToArray();
        var convenios = new[] { s.Convenio, s.OtroConvenio }.Where(x => x > 0).ToArray();
        await db.Auditorias.Where(x => x.IdUsuario != null && usuarios.Contains(x.IdUsuario.Value)).ExecuteDeleteAsync();
        await db.LibranzaEmpleadoUsuarios.Where(x => empleados.Contains(x.IdEmpleado)).ExecuteDeleteAsync();
        await db.LibranzaEmpleadoCortesPago.Where(x => empleados.Contains(x.IdEmpleado)).ExecuteDeleteAsync();
        await db.LibranzaEmpleados.Where(x => empleados.Contains(x.IdEmpleado)).ExecuteDeleteAsync();
        await db.LibranzaParametrosEmpresas.Where(x => convenios.Contains(x.IdConvenio)).ExecuteDeleteAsync();
        await db.LibranzaEmpresasConvenio.Where(x => convenios.Contains(x.IdConvenio)).ExecuteDeleteAsync();
        await db.WalletSaldos.Where(x => x.IdWallet == s.Wallet).ExecuteDeleteAsync();
        await db.Wallets.Where(x => x.IdWallet == s.Wallet).ExecuteDeleteAsync();
        await db.KycVerificaciones.Where(x => usuarios.Contains(x.IdUsuario)).ExecuteDeleteAsync();
        await db.UsuarioRoles.Where(x => usuarios.Contains(x.IdUsuario)).ExecuteDeleteAsync();
        await db.Usuarios.Where(x => usuarios.Contains(x.IdUsuario)).ExecuteDeleteAsync();
        await db.Personas.Where(x => x.IdPersona == s.Persona || x.IdPersona == s.PersonaAdmin).ExecuteDeleteAsync();
    }
}
