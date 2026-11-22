using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xpay.Api.Data;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xpay.Api.Tests.Common;
using Xunit;

namespace Xpay.Api.Tests.Services;

// Solo SQL local y cuentas sintéticas. La compatibilidad no llama Veriff,
// no inicia una captura y nunca modifica evidencia de una aceptación previa.
[Collection("SqlIntegration")]
public sealed class ConsentimientoBiometricoCompatibilidadSqlTests
{
    [Fact]
    public async Task C_12_sigue_vigente_con_bundle_y_AB_13_sin_reetiquetar_ni_pedir_nueva_captura()
    {
        if (!ConexionLocal(out var cs)) return;
        await using var datos = new Datos(cs);
        var u = await datos.CrearAsync();
        await datos.C12RealAsync(u);
        await datos.AB13Async(u);
        await using var db = Contexto(cs);
        var servicio = Servicio(db);
        Assert.True(await servicio.TieneConsentimientoVigenteAsync(u.Usuario));
        Assert.True(await servicio.PuedeIniciarKycAsync(u.Usuario));
        var estado = await servicio.ObtenerEstadoAsync(u.Usuario);
        Assert.True(estado.Aceptado); Assert.Equal("1.3",estado.Documento!.Version);
        await servicio.AutorizarAsync(u.Usuario,new(true,"1.3"),null);
        await using var ver = Contexto(cs);
        var fila = Assert.Single(await ver.Auditorias.Where(a=>a.IdUsuario==u.Usuario&&a.Accion==ConsentimientoBiometricoService.AccionAutorizar).ToListAsync());
        using var evidencia = JsonDocument.Parse(fila.ValorNuevo!);
        Assert.Equal("1.2",evidencia.RootElement.GetProperty("version").GetString());
        Assert.Equal(ConsentimientoBiometricoService.ShaDocumentoAprobado,evidencia.RootElement.GetProperty("sha256").GetString());
        Assert.False(await ver.KycVerificaciones.AnyAsync(k=>k.IdUsuario==u.Usuario));
        Assert.False(await ver.Wallets.AnyAsync(w=>w.IdPersona==u.Persona));
    }

    [Fact]
    public async Task Una_nueva_aceptacion_guarda_13_con_fecha_SHA_texto_y_ruta_correctos_una_sola_vez()
    {
        if (!ConexionLocal(out var cs)) return;
        await using var datos = new Datos(cs); var u = await datos.CrearAsync();
        await using var db = Contexto(cs); var servicio = Servicio(db);
        Assert.False(await servicio.TieneConsentimientoVigenteAsync(u.Usuario));
        await servicio.AutorizarAsync(u.Usuario,new(true,"1.3"),"203.0.113.10");
        await servicio.AutorizarAsync(u.Usuario,new(true,"1.3"),"203.0.113.10");
        var fila = Assert.Single(await db.Auditorias.AsNoTracking().Where(a=>a.IdUsuario==u.Usuario&&a.Accion==ConsentimientoBiometricoService.AccionAutorizar).ToListAsync());
        using var evidencia = JsonDocument.Parse(fila.ValorNuevo!); var e = evidencia.RootElement;
        Assert.Equal("1.3",e.GetProperty("version").GetString());
        Assert.Equal("2026-11-21",e.GetProperty("fechaDocumento").GetString());
        Assert.Equal(PreparacionWalletConfiguracion.AutorizacionSha256,e.GetProperty("sha256").GetString());
        Assert.Equal(ConsentimientoBiometricoService.ShaTextoCasilla,e.GetProperty("textoCasillaSha256").GetString());
        Assert.Equal("https://app.xpaycol.com/legal/v1.3/autorizaciones.pdf",e.GetProperty("urlDocumento").GetString());
        Assert.True(e.GetProperty("autorizaBiometria").GetBoolean());
        Assert.False(await db.KycVerificaciones.AnyAsync(k=>k.IdUsuario==u.Usuario));
        Assert.False(await db.Wallets.AnyAsync(w=>w.IdPersona==u.Persona));
    }

    [Theory]
    [InlineData("revocada")]
    [InlineData("json_invalido")]
    [InlineData("hash_ajeno")]
    [InlineData("sin_acto_expreso")]
    public async Task Ultima_evidencia_invalida_no_rescata_una_C_12_anterior(string caso)
    {
        if (!ConexionLocal(out var cs)) return;
        await using var datos = new Datos(cs); var u = await datos.CrearAsync();
        await datos.C12RealAsync(u);
        var nueva = ConsentimientoBiometricoCompatibilidadTests.Evidencia("1.3");
        if(caso=="hash_ajeno") nueva["sha256"]=new string('f',64);
        if(caso=="sin_acto_expreso") nueva["autorizaBiometria"]=false;
        await datos.CAsync(u,caso=="revocada"?"REVOCADO":"EXITOSO",caso=="json_invalido"?"{":JsonSerializer.Serialize(nueva));
        await using var db = Contexto(cs); var servicio = Servicio(db);
        Assert.False(await servicio.TieneConsentimientoVigenteAsync(u.Usuario));
        Assert.False(await servicio.PuedeIniciarKycAsync(u.Usuario));
        Assert.False((await servicio.ObtenerEstadoAsync(u.Usuario)).Aceptado);
        Assert.Equal(2,await db.Auditorias.CountAsync(a=>a.IdUsuario==u.Usuario&&a.Accion==ConsentimientoBiometricoService.AccionAutorizar));
        Assert.False(await db.KycVerificaciones.AnyAsync(k=>k.IdUsuario==u.Usuario));
        Assert.False(await db.Wallets.AnyAsync(w=>w.IdPersona==u.Persona));
    }

    private sealed record Cuenta(long Usuario,long Persona);
    private static XpayDbContext Contexto(string cs)=>new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);
    private static ConsentimientoBiometricoService Servicio(XpayDbContext db)=>new(db,
        ConsentimientoBiometricoCompatibilidadTests.Configuracion("1.3"),AmbienteXpayTests.Ambiente("Production"),TimeProvider.System);
    private static bool ConexionLocal(out string cs) {
        cs=Environment.GetEnvironmentVariable("ConnectionStrings__XpayConnection")??"";
        if(string.IsNullOrWhiteSpace(cs)) {
            Assert.False(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))||!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")),"Se requiere SQL local para compatibilidad C.");return false;
        }
        var b=new SqlConnectionStringBuilder(cs);var host=b.DataSource.Replace("tcp:","",StringComparison.OrdinalIgnoreCase).Split(',')[0];
        Assert.True(host is "127.0.0.1" or "localhost" or "::1","Estas pruebas solo admiten SQL local.");return true;
    }

    private sealed class Datos(string cs):IAsyncDisposable {
        private readonly List<Cuenta> _cuentas=[];
        public async Task<Cuenta> CrearAsync() {
            await using var db=Contexto(cs);var n=Guid.NewGuid().ToString("N");var ahora=DateTime.UtcNow;
            var unidad=await db.Database.SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo={0}","XPAY_COL").SingleAsync();
            var p=new Persona {IdUnidadNegocio=unidad,Celular="3000000000",Email="ccompat_"+n+"@example.test",Estado="ACTIVA",FechaCreacion=ahora};
            db.Personas.Add(p);await db.SaveChangesAsync();
            var u=new Usuario {IdPersona=p.IdPersona,NombreUsuario="ccompat_"+n,PasswordHash="sintetico",Estado="ACTIVO",EmailVerificado=true,FechaCreacion=ahora};
            db.Usuarios.Add(u);await db.SaveChangesAsync();var c=new Cuenta(u.IdUsuario,p.IdPersona);_cuentas.Add(c);
            db.Auditorias.Add(new(){IdUsuario=u.IdUsuario,IdPersona=p.IdPersona,Modulo="USUARIOS",Accion=RegistroPublicoService.AccionAceptarTerminos,
                Entidad="usuarios",IdEntidad=u.IdUsuario.ToString(CultureInfo.InvariantCulture),Resultado="EXITOSO",FechaEvento=ahora,
                ValorNuevo="{\"tipoDocumento\":\"TERMINOS_Y_CONDICIONES\",\"version\":\"1.2\",\"canal\":\"WEB_AUTORREGISTRO\"}"});
            await db.SaveChangesAsync();return c;
        }
        public async Task C12RealAsync(Cuenta c) {
            await using var db=Contexto(cs);
            await new ConsentimientoBiometricoService(db,ConsentimientoBiometricoTests.Configuracion(),
                AmbienteXpayTests.Ambiente("Production"),TimeProvider.System).AutorizarAsync(c.Usuario,new(true,"1.2"),null);
        }
        public async Task CAsync(Cuenta c,string resultado,string json) {
            await using var db=Contexto(cs);db.Auditorias.Add(new(){IdUsuario=c.Usuario,IdPersona=c.Persona,Modulo="USUARIOS",
                Accion=ConsentimientoBiometricoService.AccionAutorizar,Entidad="usuarios",IdEntidad=c.Usuario.ToString(CultureInfo.InvariantCulture),
                Resultado=resultado,ValorNuevo=json,FechaEvento=DateTime.UtcNow});await db.SaveChangesAsync();
        }
        public async Task AB13Async(Cuenta c) {
            await using var db=Contexto(cs);
            foreach(var (accion,tipo,sha) in new[]{(RegistroPublicoService.AccionAceptarTerminos,"TERMINOS_Y_CONDICIONES",PreparacionWalletConfiguracion.TerminosSha256),
                (RegistroPublicoService.AccionAutorizarDatos,"POLITICA_TRATAMIENTO_DATOS",PreparacionWalletConfiguracion.PoliticaDatosSha256)})
                db.Auditorias.Add(new(){IdUsuario=c.Usuario,IdPersona=c.Persona,Modulo="USUARIOS",Accion=accion,Entidad="usuarios",IdEntidad=c.Usuario.ToString(CultureInfo.InvariantCulture),
                    Resultado="EXITOSO",ValorNuevo=JsonSerializer.Serialize(new{tipoDocumento=tipo,version="1.3",sha256=sha,canal="WEB_AUTORREGISTRO"}),FechaEvento=DateTime.UtcNow});
            await db.SaveChangesAsync();
        }
        public async ValueTask DisposeAsync() {
            await using var db=Contexto(cs);var ids=_cuentas.Select(c=>c.Usuario).ToArray();var ps=_cuentas.Select(c=>c.Persona).ToArray();
            await db.Auditorias.Where(a=>a.IdUsuario!=null&&ids.Contains(a.IdUsuario.Value)).ExecuteDeleteAsync();
            await db.Usuarios.Where(u=>ids.Contains(u.IdUsuario)).ExecuteDeleteAsync();await db.Personas.Where(p=>ps.Contains(p.IdPersona)).ExecuteDeleteAsync();
        }
    }
}
