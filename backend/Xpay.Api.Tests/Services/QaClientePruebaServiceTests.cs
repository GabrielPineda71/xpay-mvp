using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Controllers;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;
using Xpay.Api.Tests.Common;
using Xpay.Api.Tests.Integrations.MiDecisor;

namespace Xpay.Api.Tests.Services;

// XPAY-702 - alta administrativa de cliente QA + marcas que lee el gate.
// Parte sin SQL (configuracion y roles) y parte SQL (mismo guard que M2.4b).
[Collection("SqlIntegration")]
public sealed class QaClientePruebaServiceTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";

    private static Dictionary<string, string?> ConfigOn(string max = "50") => new()
    {
        [QaIdentidadPruebaConfig.EnvEnabled]    = "true",
        [QaClientePruebaConfig.EnvCreateEnabled] = "true",
        [QaClientePruebaConfig.EnvCreateMax]     = max,
    };

    [Theory]
    [InlineData(QaIdentidadPruebaConfig.EnvEnabled, null)]
    [InlineData(QaIdentidadPruebaConfig.EnvEnabled, "True")]
    [InlineData(QaClientePruebaConfig.EnvCreateEnabled, null)]
    [InlineData(QaClientePruebaConfig.EnvCreateEnabled, "1")]
    [InlineData(QaClientePruebaConfig.EnvCreateMax, null)]
    [InlineData(QaClientePruebaConfig.EnvCreateMax, "0")]
    [InlineData(QaClientePruebaConfig.EnvCreateMax, "-1")]
    [InlineData(QaClientePruebaConfig.EnvCreateMax, "x")]
    public void Config_Incompleta_FailClosed(string clave, string? valor)
    {
        var cfg = ConfigOn();
        cfg[clave] = valor;
        Assert.False(QaClientePruebaConfig.TryHabilitado(new FakeConfiguration(cfg), out _));
    }

    [Fact]
    public void Config_Completa_Habilitada()
        => Assert.True(QaClientePruebaConfig.TryHabilitado(new FakeConfiguration(ConfigOn("3")), out var max) && max == 3);

    [Fact]
    public async Task Servicio_Deshabilitado_NoDisponible_SinTocarBase()
    {
        var svc = new QaClientePruebaService(null!, null!, null!,
            new FakeConfiguration(new Dictionary<string, string?>()), TimeProvider.System,
            AmbienteXpayTests.Ambiente("QA"));
        var r = await svc.CrearAsync(new RegistroUsuarioFinalRequest { TipoDocumento = "CC" }, 5);
        Assert.Equal(ResultadoClientePruebaQa.NoDisponible, r.Resultado);
        Assert.False(svc.Habilitado);
    }

    // ── XPAY-717 — frontera QA/PROD: el servicio consume IAmbienteXpay ──
    // db/registro/store = null!: cualquier acceso lanzaría NullReferenceException.
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("qa")]
    public async Task Ambiente_productivo_con_flags_qa_no_disponible_sin_tocar_base(string ambiente)
    {
        var svc = new QaClientePruebaService(null!, null!, null!,
            new FakeConfiguration(ConfigOn()), TimeProvider.System, AmbienteXpayTests.Ambiente(ambiente));
        Assert.False(svc.Habilitado);
        var r = await svc.CrearAsync(new RegistroUsuarioFinalRequest { TipoDocumento = "CC" }, 5);
        Assert.Equal(ResultadoClientePruebaQa.NoDisponible, r.Resultado);
        Assert.Null(r.IdUsuario);
    }

    [Fact]
    public async Task Ambiente_productivo_sin_flags_no_disponible_sin_tocar_base()
    {
        var svc = new QaClientePruebaService(null!, null!, null!,
            new FakeConfiguration(new Dictionary<string, string?>()), TimeProvider.System, AmbienteXpayTests.Ambiente("Production"));
        Assert.False(svc.Habilitado);
        var r = await svc.CrearAsync(new RegistroUsuarioFinalRequest { TipoDocumento = "CC" }, 5);
        Assert.Equal(ResultadoClientePruebaQa.NoDisponible, r.Resultado);
    }

    [Fact]
    public void Ambiente_QA_con_flags_habilitado()
    {
        var svc = new QaClientePruebaService(null!, null!, null!,
            new FakeConfiguration(ConfigOn()), TimeProvider.System, AmbienteXpayTests.Ambiente("QA"));
        Assert.True(svc.Habilitado);
    }

    [Theory]
    [InlineData(typeof(QaClientePruebaController))]
    [InlineData(typeof(CarteraRevisionManualController))]
    public void Controladores_SoloAdminYSuperusuario(Type controlador)
    {
        var attr = Assert.Single(controlador.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal("ADMIN_XPAY,SUPERUSUARIO", attr.Roles);
        Assert.Null(attr.Policy);
        Assert.Empty(controlador.GetCustomAttributes<AllowAnonymousAttribute>());
    }

    // ═══════════════════════════ parte SQL ═══════════════════════════════

    private static QaClientePruebaService Servicio(XpayDbContext c, string max = "50")
        => new(c, new RegistroUsuarioFinalService(c, AmbienteXpayTests.Ambiente("QA")), new QaIdentidadPruebaStore(c),
               new FakeConfiguration(ConfigOn(max)), TimeProvider.System,
               AmbienteXpayTests.Ambiente("QA"));

    private static async Task<RegistroUsuarioFinalRequest> RequestAsync(XpayDbContext c, string documento)
    {
        var idUnidad = await c.Database.SqlQueryRaw<long>(
            "SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL").SingleAsync();
        return new RegistroUsuarioFinalRequest
        {
            IdUnidadNegocio = idUnidad, TipoDocumento = "CC", NumeroDocumento = documento,
            PrimerNombre = "QaTest", PrimerApellido = "Sintetico", Celular = "3000000000",
            Usuario = $"qa_cli_{Guid.NewGuid().ToString("N")[..12]}", Password = "Qa-Test-702!",
        };
    }

    private static string NuevoDocumento()
        => $"78{(uint)Guid.NewGuid().GetHashCode() % 100_000_000:D8}";

    [Fact]
    public async Task Crear_ClienteQa_DobleMarca_GateLoReconoce_SinConsultas()
    {
        if (!TryConnString(out var cs)) return;
        var doc = NuevoDocumento();
        try
        {
            ClientePruebaQaResultado r;
            await using (var c = NuevoContexto(cs))
                r = await Servicio(c).CrearAsync(await RequestAsync(c, doc), await AdminAsync(c));

            Assert.Equal(ResultadoClientePruebaQa.Creado, r.Resultado);
            var id = r.IdUsuario!.Value;

            await using var v = NuevoContexto(cs);
            var u = await v.Usuarios.AsNoTracking().SingleAsync(x => x.IdUsuario == id);
            var p = await v.Personas.AsNoTracking().SingleAsync(x => x.IdPersona == u.IdPersona);
            Assert.Equal("APROBADO", u.EstadoKycActual);
            Assert.True(p.IdentidadVerificada);
            Assert.Equal("SIMULACION_QA", p.IdentidadVerificadaProveedor);
            Assert.True(await v.Wallets.AnyAsync(w => w.IdPersona == p.IdPersona && w.Estado == "ACTIVA"));
            Assert.True(await v.KycVerificaciones.AnyAsync(k => k.IdUsuario == id && k.EsActual && k.Proveedor == "SIMULACION_QA"));

            var gate = new QaClienteRiesgoStore(v);
            Assert.True(await gate.EsClienteQaPreparadoAsync(id));
            Assert.Equal(0, await gate.ContarConsultasRealesAsync(id));
        }
        finally { await LimpiarAsync(cs, doc); }
    }

    [Fact]
    public async Task Crear_DocumentoDuplicado_Conflicto_SinSegundaPersona()
    {
        if (!TryConnString(out var cs)) return;
        var doc = NuevoDocumento();
        try
        {
            await using (var c = NuevoContexto(cs))
                Assert.Equal(ResultadoClientePruebaQa.Creado,
                    (await Servicio(c).CrearAsync(await RequestAsync(c, doc), await AdminAsync(c))).Resultado);
            await using (var c = NuevoContexto(cs))
                Assert.Equal(ResultadoClientePruebaQa.Conflicto,
                    (await Servicio(c).CrearAsync(await RequestAsync(c, doc), await AdminAsync(c))).Resultado);

            await using var v = NuevoContexto(cs);
            Assert.Equal(1, await v.Personas.CountAsync(p => p.NumeroDocumento == doc));
        }
        finally { await LimpiarAsync(cs, doc); }
    }

    // Documento no numerico: el registro lo acepta pero la preparacion QA no.
    // Debe revertirse TODO: ni persona, ni usuario, ni wallet.
    [Fact]
    public async Task Crear_PreparacionFalla_RollbackTotal()
    {
        if (!TryConnString(out var cs)) return;
        var doc = "78AB" + Guid.NewGuid().ToString("N")[..6];
        try
        {
            await using (var c = NuevoContexto(cs))
                Assert.Equal(ResultadoClientePruebaQa.NoPreparado,
                    (await Servicio(c).CrearAsync(await RequestAsync(c, doc), await AdminAsync(c))).Resultado);

            await using var v = NuevoContexto(cs);
            Assert.False(await v.Personas.AnyAsync(p => p.NumeroDocumento == doc));
        }
        finally { await LimpiarAsync(cs, doc); }
    }

    [Fact]
    public async Task Crear_TopeAlcanzado_LimiteAlcanzado_SinEscrituras()
    {
        if (!TryConnString(out var cs)) return;
        var doc = NuevoDocumento();
        try
        {
            await using var c = NuevoContexto(cs);
            var existentes = await c.Personas.CountAsync(p => p.IdentidadVerificadaProveedor == "SIMULACION_QA");
            var tope = Math.Max(existentes, 1).ToString();
            if (existentes == 0)
            {
                await using var c0 = NuevoContexto(cs);
                await Servicio(c0).CrearAsync(await RequestAsync(c0, doc), await AdminAsync(c0));
            }
            var doc2 = NuevoDocumento();
            var r = await Servicio(c, tope).CrearAsync(await RequestAsync(c, doc2), await AdminAsync(c));
            Assert.Equal(ResultadoClientePruebaQa.LimiteAlcanzado, r.Resultado);
            Assert.False(await c.Personas.AnyAsync(p => p.NumeroDocumento == doc2));
        }
        finally { await LimpiarAsync(cs, doc); }
    }

    [Fact]
    public async Task Gate_UsuarioConRastroVeriff_NoEsClienteQa()
    {
        if (!TryConnString(out var cs)) return;
        var doc = NuevoDocumento();
        try
        {
            long id;
            await using (var c = NuevoContexto(cs))
                id = (await Servicio(c).CrearAsync(await RequestAsync(c, doc), await AdminAsync(c))).IdUsuario!.Value;

            await using var v = NuevoContexto(cs);
            var idPersona = await v.Usuarios.Where(u => u.IdUsuario == id).Select(u => u.IdPersona).SingleAsync();
            v.KycVerificaciones.Add(new KycVerificacion
            {
                IdUsuario = id, IdPersona = idPersona, Proveedor = "VERIFF", EsActual = false,
                EstadoKyc = "APROBADO", FechaCreacion = DateTime.UtcNow,
            });
            await v.SaveChangesAsync();

            Assert.False(await new QaClienteRiesgoStore(v).EsClienteQaPreparadoAsync(id));
        }
        finally { await LimpiarAsync(cs, doc); }
    }

    [Fact]
    public async Task Gate_ConteoConsultas_PreCallNoCuenta_FinalizadoSi()
    {
        if (!TryConnString(out var cs)) return;
        var doc = NuevoDocumento();
        try
        {
            long id;
            await using (var c = NuevoContexto(cs))
                id = (await Servicio(c).CrearAsync(await RequestAsync(c, doc), await AdminAsync(c))).IdUsuario!.Value;

            await using var v = NuevoContexto(cs);
            var ahora = DateTime.UtcNow;
            var idPersona = await v.Usuarios.Where(u => u.IdUsuario == id).Select(u => u.IdPersona).SingleAsync();
            var idPolitica = await v.CarteraPoliticasCredito.AsNoTracking()
                .Where(p => p.Estado == "ACTIVO").OrderBy(p => p.IdPolitica).Select(p => p.IdPolitica).FirstAsync();
            var sol = new CarteraSolicitudCupo
            {
                IdUsuario = id, IdPersona = idPersona,
                EstadoSolicitud = CarteraSolicitudCupoEstados.Recibida,
                DecisionCrediticia = CarteraDecisionCrediticia.Pendiente,
                IdPoliticaAplicada = idPolitica, CupoMinimoAplicado = 0m, CupoMaximoAplicado = 1_000_000m,
                EdadMinimaAplicada = 18, EdadMaximaAplicada = 99, NumeroIntento = 1,
                CorrelationId = $"qa702-{Guid.NewGuid().ToString("N")[..12]}",
                FechaSolicitud = ahora, FechaActualizacion = ahora,
            };
            v.CarteraSolicitudesCupo.Add(sol);
            await v.SaveChangesAsync();
            var intento = new CarteraSolicitudCupoIntento
            {
                IdSolicitud = sol.IdSolicitud, NumeroIntento = 1, IdempotencyKey = Guid.NewGuid(),
                FechaInicio = ahora, CorrelationId = $"qa702i-{Guid.NewGuid().ToString("N")[..12]}",
                FaseIntento = CarteraIntentoFases.PreCall,
            };
            v.CarteraSolicitudCupoIntentos.Add(intento);
            await v.SaveChangesAsync();

            var gate = new QaClienteRiesgoStore(v);
            Assert.Equal(0, await gate.ContarConsultasRealesAsync(id));

            intento.FaseIntento = CarteraIntentoFases.Finalizado;
            await v.SaveChangesAsync();
            Assert.Equal(1, await gate.ContarConsultasRealesAsync(id));
        }
        finally { await LimpiarAsync(cs, doc); }
    }

    [Fact]
    public async Task Crear_SinUnidad_UsaLaUnidadDelAdministrador()
    {
        if (!TryConnString(out var cs)) return;
        var doc = NuevoDocumento();
        try
        {
            ClientePruebaQaResultado r;
            long idAdmin;
            await using (var c = NuevoContexto(cs))
            {
                idAdmin = await AdminAsync(c);
                var req = await RequestAsync(c, doc);
                req.IdUnidadNegocio = 0;
                r = await Servicio(c).CrearAsync(req, idAdmin);
            }
            Assert.Equal(ResultadoClientePruebaQa.Creado, r.Resultado);

            await using var v = NuevoContexto(cs);
            var unidadAdmin = await v.Usuarios.Where(u => u.IdUsuario == idAdmin)
                .Join(v.Personas, u => u.IdPersona, p => p.IdPersona, (u, p) => p.IdUnidadNegocio).SingleAsync();
            var unidadCliente = await v.Personas.Where(p => p.NumeroDocumento == doc)
                .Select(p => p.IdUnidadNegocio).SingleAsync();
            Assert.Equal(unidadAdmin, unidadCliente);
        }
        finally { await LimpiarAsync(cs, doc); }
    }

    // ════════════════════ infraestructura de test ════════════════════════

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL de XPAY-702.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    // Cualquier usuario preexistente del fixture sirve como ejecutor (FK).
    private static Task<long> AdminAsync(XpayDbContext c)
        => c.Usuarios.AsNoTracking().OrderBy(u => u.IdUsuario).Select(u => u.IdUsuario).FirstAsync();

    // Limpieza por numero de documento sintetico (parametrizada, orden de FKs).
    private static async Task LimpiarAsync(string cs, string doc)
    {
        await using var ctx = NuevoContexto(cs);
        var db = ctx.Database;
        await db.ExecuteSqlAsync($"DELETE FROM dbo.cartera_solicitud_cupo_intentos WHERE id_solicitud IN (SELECT s.id_solicitud FROM dbo.cartera_solicitudes_cupo s JOIN dbo.personas p ON p.id_persona = s.id_persona WHERE p.numero_documento = {doc})");
        await db.ExecuteSqlAsync($"DELETE s FROM dbo.cartera_solicitudes_cupo s JOIN dbo.personas p ON p.id_persona = s.id_persona WHERE p.numero_documento = {doc}");
        await db.ExecuteSqlAsync($"DELETE k FROM dbo.kyc_verificaciones k JOIN dbo.usuarios u ON u.id_usuario = k.id_usuario JOIN dbo.personas p ON p.id_persona = u.id_persona WHERE p.numero_documento = {doc}");
        await db.ExecuteSqlAsync($"DELETE a FROM dbo.auditoria a JOIN dbo.usuarios u ON u.id_usuario = a.id_usuario JOIN dbo.personas p ON p.id_persona = u.id_persona WHERE p.numero_documento = {doc}");
        await db.ExecuteSqlAsync($"DELETE a FROM dbo.auditoria a JOIN dbo.personas p ON p.id_persona = a.id_persona WHERE p.numero_documento = {doc}");
        await db.ExecuteSqlAsync($"DELETE r FROM dbo.usuario_roles r JOIN dbo.usuarios u ON u.id_usuario = r.id_usuario JOIN dbo.personas p ON p.id_persona = u.id_persona WHERE p.numero_documento = {doc}");
        await db.ExecuteSqlAsync($"DELETE ws FROM dbo.wallet_saldos ws JOIN dbo.wallets w ON w.id_wallet = ws.id_wallet JOIN dbo.personas p ON p.id_persona = w.id_persona WHERE p.numero_documento = {doc}");
        await db.ExecuteSqlAsync($"DELETE w FROM dbo.wallets w JOIN dbo.personas p ON p.id_persona = w.id_persona WHERE p.numero_documento = {doc}");
        await db.ExecuteSqlAsync($"DELETE u FROM dbo.usuarios u JOIN dbo.personas p ON p.id_persona = u.id_persona WHERE p.numero_documento = {doc}");
        await db.ExecuteSqlAsync($"DELETE FROM dbo.personas WHERE numero_documento = {doc}");
    }
}
