using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xpay.Api.Data;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xpay.Api.Tests.Common;
using Xunit;

namespace Xpay.Api.Tests.Services;

// Exclusivamente fixtures sintéticas en la conexión SQL de integración de CI.
[Collection("SqlIntegration")]
public sealed class CarteraPilotoSqlTests
{
    [Fact]
    public async Task UnionDeduplicaTresFuentesYUsaTitularDeAltaAdmin()
    {
        if (!Conexion(out var cs)) return;
        await using var datos = new PreparacionWalletSqlTests.Semillero(cs);
        var actor = await datos.CuentaAsync(publica:false);
        var titular = await datos.CuentaAsync(publica:false);
        await using var db = Contexto(cs);
        var antes = await PreparacionWalletService.ContarPlazasAsync(db);
        var wallet = new Wallet { IdPersona=titular.Persona,
            IdUnidadNegocio=await db.Personas.Where(p=>p.IdPersona==titular.Persona).Select(p=>p.IdUnidadNegocio).SingleAsync(),
            TipoWallet="PERSONA", Estado="ACTIVA", NombreWallet="Wallet sintética", FechaCreacion=DateTime.UtcNow };
        db.Wallets.Add(wallet); await db.SaveChangesAsync();
        db.Auditorias.Add(new Auditoria { IdUsuario=actor.Usuario, IdPersona=titular.Persona, Modulo="USUARIOS",
            Accion=AltaUsuarioPilotoService.AccionAuditoria, Entidad="usuarios", IdEntidad=titular.Usuario.ToString(),
            Resultado="EXITOSO", FechaEvento=DateTime.UtcNow });
        await db.SaveChangesAsync();
        Assert.Equal(antes+1, await PreparacionWalletService.ContarPlazasAsync(db));
        db.Auditorias.Add(new Auditoria { IdUsuario=titular.Usuario, IdPersona=titular.Persona, Modulo="USUARIOS",
            Accion=PreparacionWalletService.AccionActivar, Entidad="usuarios", IdEntidad=titular.Usuario.ToString(),
            Resultado="EXITOSO", FechaEvento=DateTime.UtcNow });
        await db.SaveChangesAsync();
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT dbo.cartera_piloto_habilitaciones(id_usuario,id_persona,id_wallet,fecha_habilitacion,fuente)
                VALUES({titular.Usuario},{titular.Persona},{wallet.IdWallet},{DateTime.UtcNow},'LEGACY')
                """);
            Assert.Equal(antes+1, await PreparacionWalletService.ContarPlazasAsync(db));
            var config=PreparacionWalletSqlTests.Configuracion();
            config["MIDECISOR_PRODUCTION_ENABLED"]="false";
            Assert.True(await Servicio(db,config).EsHabilitadoAsync(titular.Usuario));
            var pausa=await Servicio(db,config).ObtenerDisponibilidadAsync(titular.Usuario);
            Assert.False(pausa.Disponible); Assert.True(pausa.Habilitado);
            Assert.False(await Servicio(db,config).EsHabilitadoAsync(actor.Usuario));
        }
        finally { await BorrarMembershipAsync(db,titular.Usuario); }
    }

    [Fact]
    public async Task DosAltasDeMismoPublicoSonIdempotentesInclusoConCapacidadCompleta()
    {
        if (!Conexion(out var cs)) return;
        await using var datos=new PreparacionWalletSqlTests.Semillero(cs);
        var config=PreparacionWalletSqlTests.Configuracion(); config["MIDECISOR_PRODUCTION_ENABLED"]="true";
        var cuenta=await ActivadaAsync(cs,datos,config);
        await using var verificar=Contexto(cs);
        var antes=await PreparacionWalletService.ContarPlazasAsync(verificar);
        config[AltaUsuarioPilotoService.ClaveMaxUsuarios]=antes.ToString();
        try
        {
            async Task<CarteraPilotoDisponibilidad> Alta()
            {
                await using var db=Contexto(cs);
                return await Servicio(db,config).AsegurarHabilitacionAsync(cuenta.Usuario);
            }
            var resultados=await Task.WhenAll(Alta(),Alta());
            Assert.All(resultados,r=>Assert.True(r.Disponible&&r.Habilitado));
            Assert.Equal(1,await verificar.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value FROM dbo.cartera_piloto_habilitaciones WHERE id_usuario={0}",cuenta.Usuario).SingleAsync());
            Assert.Equal(antes,await PreparacionWalletService.ContarPlazasAsync(verificar));
            Assert.False(await verificar.CarteraCuposOrdinarios.AnyAsync(c=>c.IdUsuario==cuenta.Usuario));
            Assert.False(await verificar.CarteraSolicitudesCupo.AnyAsync(c=>c.IdUsuario==cuenta.Usuario));
        }
        finally { await BorrarMembershipAsync(verificar,cuenta.Usuario); }
    }

    [Fact]
    public async Task DosLegacyCompitenPorUltimaPlazaSoloUnoSeHabilita()
    {
        if (!Conexion(out var cs)) return;
        await using var datos=new PreparacionWalletSqlTests.Semillero(cs);
        var config=PreparacionWalletSqlTests.Configuracion(); config["MIDECISOR_PRODUCTION_ENABLED"]="true";
        var uno=await ActivadaAsync(cs,datos,config); var dos=await ActivadaAsync(cs,datos,config);
        await using var verificar=Contexto(cs);
        // Convertir exclusivamente estas dos fixtures en perfiles legacy: la
        // evidencia biométrica VERIFF permanece; no se crea consentimiento.
        foreach (var cuenta in new[]{uno,dos})
            await verificar.Auditorias.Where(a=>a.IdUsuario==cuenta.Usuario &&
                (a.Accion==RegistroPublicoService.AccionAceptarTerminos || a.Accion==RegistroPublicoService.AccionAutorizarDatos ||
                 a.Accion==PreparacionWalletService.AccionActivar)).ExecuteDeleteAsync();
        var antes=await PreparacionWalletService.ContarPlazasAsync(verificar);
        config[AltaUsuarioPilotoService.ClaveMaxUsuarios]=(antes+1).ToString();
        try
        {
            async Task<CarteraPilotoDisponibilidad> Alta(long id)
            {
                await using var db=Contexto(cs);
                return await Servicio(db,config).AsegurarHabilitacionAsync(id);
            }
            var resultados=await Task.WhenAll(Alta(uno.Usuario),Alta(dos.Usuario));
            Assert.Single(resultados,r=>r.Disponible&&r.Habilitado);
            Assert.Single(resultados,r=>!r.Disponible&&r.Estado=="PILOTO_LLENO");
            Assert.Equal(antes+1,await PreparacionWalletService.ContarPlazasAsync(verificar));
        }
        finally { await BorrarMembershipAsync(verificar,uno.Usuario); await BorrarMembershipAsync(verificar,dos.Usuario); }
    }

    [Fact]
    public async Task SegundoApellidoDeclaradoDiscordanteNoHabilitaNiConsumePlaza()
    {
        if (!Conexion(out var cs)) return;
        await using var datos=new PreparacionWalletSqlTests.Semillero(cs);
        var config=PreparacionWalletSqlTests.Configuracion(); config["MIDECISOR_PRODUCTION_ENABLED"]="true";
        var cuenta=await ActivadaAsync(cs,datos,config);
        await using var db=Contexto(cs);
        // Esta fixture pasa a ser legacy sin plaza histórica. La identidad y
        // biometría sintéticas se conservan; no se fabrica aceptación de crédito.
        await db.Auditorias.Where(a=>a.IdUsuario==cuenta.Usuario &&
            (a.Accion==RegistroPublicoService.AccionAceptarTerminos || a.Accion==RegistroPublicoService.AccionAutorizarDatos ||
             a.Accion==PreparacionWalletService.AccionActivar)).ExecuteDeleteAsync();
        await db.Personas.Where(p=>p.IdPersona==cuenta.Persona)
            .ExecuteUpdateAsync(s=>s.SetProperty(p=>p.SegundoApellido,"OTRO"));
        var antes=await PreparacionWalletService.ContarPlazasAsync(db);
        config[AltaUsuarioPilotoService.ClaveMaxUsuarios]=(antes+1).ToString();
        try
        {
            var servicio=Servicio(db,config);
            var disponible=await servicio.ObtenerDisponibilidadAsync(cuenta.Usuario);
            Assert.False(disponible.Disponible); Assert.False(disponible.Habilitado);
            Assert.Equal("IDENTIDAD_REQUERIDA",disponible.Estado);
            var resultado=await servicio.AsegurarHabilitacionAsync(cuenta.Usuario);
            Assert.False(resultado.Disponible); Assert.False(resultado.Habilitado);
            Assert.Equal("IDENTIDAD_REQUERIDA",resultado.Estado);
            Assert.False(await servicio.EsHabilitadoAsync(cuenta.Usuario));
            Assert.Equal(0,await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value FROM dbo.cartera_piloto_habilitaciones WHERE id_usuario={0}",cuenta.Usuario).SingleAsync());
            Assert.Equal(antes,await PreparacionWalletService.ContarPlazasAsync(db));
            Assert.False(await db.CarteraSolicitudesCupo.AnyAsync(s=>s.IdUsuario==cuenta.Usuario));
            Assert.False(await db.CarteraCuposOrdinarios.AnyAsync(c=>c.IdUsuario==cuenta.Usuario));
            Assert.False(await db.CarteraAutorizacionesConsultaRiesgo.AnyAsync(a=>a.IdUsuario==cuenta.Usuario));
        }
        finally { await BorrarMembershipAsync(db,cuenta.Usuario); }
    }

    private static async Task<PreparacionWalletSqlTests.Cuenta> ActivadaAsync(string cs,
        PreparacionWalletSqlTests.Semillero datos,IConfiguration config)
    {
        var cuenta=await datos.ListaAsync(config);
        await using var db=Contexto(cs);
        await PreparacionWalletSqlTests.Servicio(db,config).ActivarAsync(cuenta.Usuario,new(),null);
        await db.Personas.Where(p=>p.IdPersona==cuenta.Persona).ExecuteUpdateAsync(s=>s
            .SetProperty(p=>p.PrimerApellido,"PRUEBA").SetProperty(p=>p.ApellidoVerificadoCompleto,"PRUEBA SINTETICA"));
        return cuenta;
    }

    private static Task<int> BorrarMembershipAsync(XpayDbContext db,long id) => db.Database.ExecuteSqlInterpolatedAsync(
        $"DELETE FROM dbo.cartera_piloto_habilitaciones WHERE id_usuario={id}");
    private static CarteraPilotoService Servicio(XpayDbContext db,IConfiguration config) =>
        new(db,config,new(db,config,AmbienteXpayTests.Ambiente("Production"),TimeProvider.System),new(db),TimeProvider.System);
    private static XpayDbContext Contexto(string cs) => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);
    private static bool Conexion(out string cs)
    {
        cs=Environment.GetEnvironmentVariable("ConnectionStrings__XpayConnection")??"";
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        Assert.False(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")),"Se requiere SQL sintético de CI para CarteraPilotoSqlTests.");
        return false;
    }
}
