using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Data;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// Integración exclusivamente local, datos sintéticos y HTTP fake. Nunca
// conecta proveedores ni admite una connectionstring remota de QA/PROD.
[Collection("SqlIntegration")]
public sealed class SincronizacionDocumentoVeriffSqlTests
{
    [Fact]
    public async Task Clic_real_confirma_GET_fuera_de_SQL_conserva_raw_y_no_reabre_KYC_ni_crea_Wallet()
    {
        if (!ConexionLocal(out var cs)) return;
        await using var datos = new Datos(cs);
        var cuenta = await datos.CrearAsync();
        await using var db = Contexto(cs);
        var http = new VeriffFake(db, cuenta);
        var servicio = Servicio(db, http);
        Assert.Equal(0, http.Peticiones);
        await servicio.SincronizarAsync(cuenta.Usuario);
        await servicio.SincronizarAsync(cuenta.Usuario);
        Assert.Equal(1, http.Peticiones);
        await using var ver = Contexto(cs);
        var p = await ver.Personas.SingleAsync(p => p.IdPersona == cuenta.Persona);
        Assert.Equal("CC", p.TipoDocumento); Assert.Equal(cuenta.Canonico, p.NumeroDocumento);
        Assert.Equal(cuenta.Raw, p.NumeroDocumentoVerificado);
        Assert.Equal("ANA PRUEBA", p.NombreVerificadoCompleto); Assert.Equal("DOS APELLIDOS", p.ApellidoVerificadoCompleto);
        Assert.Null(p.PrimerApellido); Assert.True(p.IdentidadVerificada);
        var k = await ver.KycVerificaciones.SingleAsync(k => k.IdUsuario == cuenta.Usuario);
        Assert.Equal(cuenta.Attempt, k.AttemptIdVeriff); Assert.Equal(cuenta.Tiempo, k.DecisionTimeVeriff);
        Assert.Equal("approved", k.Decision); Assert.Equal("APROBADO", k.EstadoKyc); Assert.True(k.EsActual);
        Assert.Equal("APROBADO", (await ver.Usuarios.SingleAsync(u => u.IdUsuario == cuenta.Usuario)).EstadoKycActual);
        Assert.False(await ver.Wallets.AnyAsync(w => w.IdPersona == cuenta.Persona));
        Assert.False(await ver.PinesTransaccionales.AnyAsync(t => t.IdUsuario == cuenta.Usuario));
        var audit = Assert.Single(await ver.Auditorias.Where(a => a.IdUsuario == cuenta.Usuario
            && a.Accion == SincronizacionDocumentoVeriffService.AccionSincronizar).ToListAsync());
        Assert.DoesNotContain(cuenta.Raw, audit.Observacion + audit.ValorNuevo);
        Assert.DoesNotContain(cuenta.Canonico, audit.Observacion + audit.ValorNuevo);
        Assert.DoesNotContain("ANA", audit.Observacion + audit.ValorNuevo);
    }

    [Theory]
    [InlineData("sesion_ajena")]
    [InlineData("intento_cambio_en_BD")]
    [InlineData("tiempo_cambio_en_BD")]
    [InlineData("historica")]
    [InlineData("datos_raw_distintos")]
    [InlineData("pais_ausente")]
    [InlineData("pais_no_mapeado")]
    [InlineData("correo_revocado")]
    [InlineData("consentimiento_revocado")]
    [InlineData("documento_agregado_concurrente")]
    public async Task Prueba_stale_ajena_incompleta_o_revocada_no_copia_documento(string caso)
    {
        if (!ConexionLocal(out var cs)) return;
        await using var datos = new Datos(cs);
        var cuenta = await datos.CrearAsync();
        await using var db = Contexto(cs);
        var http = new VeriffFake(db, cuenta) { Caso = caso };
        http.AntesDeResponder = async () => {
            await using var otro = Contexto(cs);
            if (caso == "intento_cambio_en_BD") await otro.KycVerificaciones.Where(k => k.IdUsuario == cuenta.Usuario)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.AttemptIdVeriff, "intento-nuevo"));
            if (caso == "tiempo_cambio_en_BD") await otro.KycVerificaciones.Where(k => k.IdUsuario == cuenta.Usuario)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.DecisionTimeVeriff, cuenta.Tiempo.AddSeconds(1)));
            if (caso == "historica") await otro.KycVerificaciones.Where(k => k.IdUsuario == cuenta.Usuario)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.EsActual, false));
            if (caso == "correo_revocado") await otro.Usuarios.Where(u => u.IdUsuario == cuenta.Usuario)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.EmailVerificado, false));
            if (caso == "consentimiento_revocado") {
                otro.Auditorias.Add(new() { IdUsuario=cuenta.Usuario, IdPersona=cuenta.Persona, Modulo="USUARIOS",
                    Accion=ConsentimientoBiometricoService.AccionAutorizar, Entidad="usuarios", IdEntidad=cuenta.Usuario.ToString(CultureInfo.InvariantCulture),
                    Resultado="EXITOSO", ValorNuevo="{\"autorizaBiometria\":false}", FechaEvento=DateTime.UtcNow });
                await otro.SaveChangesAsync();
            }
            if (caso == "documento_agregado_concurrente") await otro.Personas.Where(p => p.IdPersona == cuenta.Persona)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.TipoDocumento,"CC").SetProperty(p => p.NumeroDocumento,OtroNumero(cuenta)));
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Servicio(db, http).SincronizarAsync(cuenta.Usuario));
        Assert.Equal(1, http.Peticiones);
        await using var ver = Contexto(cs);
        var p = await ver.Personas.SingleAsync(p => p.IdPersona == cuenta.Persona);
        if (caso == "documento_agregado_concurrente") { Assert.Equal("CC",p.TipoDocumento); Assert.Equal(OtroNumero(cuenta),p.NumeroDocumento); }
        else { Assert.Null(p.TipoDocumento); Assert.Null(p.NumeroDocumento); }
        Assert.Equal(cuenta.Raw,p.NumeroDocumentoVerificado);
        Assert.True(p.IdentidadVerificada);
        Assert.False(await ver.Wallets.AnyAsync(w => w.IdPersona == cuenta.Persona));
        Assert.False(await ver.Auditorias.AnyAsync(a => a.IdUsuario == cuenta.Usuario
            && a.Accion == SincronizacionDocumentoVeriffService.AccionSincronizar));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Duplicado_canonico_legal_o_verificado_bloquea_el_documento(bool rawVerificado)
    {
        if (!ConexionLocal(out var cs)) return;
        await using var datos = new Datos(cs);
        var cuenta = await datos.CrearAsync();
        await datos.OtraPersonaAsync(cuenta, rawVerificado ? null : cuenta.Canonico, rawVerificado ? cuenta.Canonico : null);
        await using var db = Contexto(cs);
        var http = new VeriffFake(db,cuenta);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Servicio(db,http).SincronizarAsync(cuenta.Usuario));
        await using var ver = Contexto(cs);
        var p = await ver.Personas.SingleAsync(p => p.IdPersona == cuenta.Persona);
        Assert.Null(p.NumeroDocumento); Assert.Null(p.TipoDocumento); Assert.Equal(cuenta.Raw,p.NumeroDocumentoVerificado);
        Assert.Single(await ver.Auditorias.Where(a => a.IdUsuario == cuenta.Usuario
            && a.Accion == SincronizacionDocumentoVeriffService.AccionRevision).ToListAsync());
    }

    [Fact]
    public async Task No_borra_puntos_arbitrarios_para_inventar_duplicados()
    {
        if (!ConexionLocal(out var cs)) return;
        await using var datos=new Datos(cs);
        var cuenta=await datos.CrearAsync();
        await datos.OtraPersonaAsync(cuenta,cuenta.Canonico[..3]+".."+cuenta.Canonico[3..],null);
        await using var db=Contexto(cs);
        await Servicio(db,new VeriffFake(db,cuenta)).SincronizarAsync(cuenta.Usuario);
        Assert.Equal(cuenta.Canonico,(await db.Personas.AsNoTracking().SingleAsync(p=>p.IdPersona==cuenta.Persona)).NumeroDocumento);
    }

    [Theory]
    [InlineData("parcial")]
    [InlineData("distinto")]
    public async Task Documento_preexistente_no_se_completa_ni_sobrescribe_y_no_consulta_proveedor(string caso)
    {
        if (!ConexionLocal(out var cs)) return;
        await using var datos=new Datos(cs);
        var cuenta=await datos.CrearAsync();
        await using (var otro=Contexto(cs)) await otro.Personas.Where(p=>p.IdPersona==cuenta.Persona)
            .ExecuteUpdateAsync(s=>s.SetProperty(p=>p.TipoDocumento,"CC").SetProperty(p=>p.NumeroDocumento,caso=="parcial" ? null : OtroNumero(cuenta)));
        await using var db=Contexto(cs);
        var http=new VeriffFake(db,cuenta);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Servicio(db,http).SincronizarAsync(cuenta.Usuario));
        Assert.Equal(0,http.Peticiones);
        await using var ver=Contexto(cs);
        var p=await ver.Personas.SingleAsync(p=>p.IdPersona==cuenta.Persona);
        Assert.Equal("CC",p.TipoDocumento); Assert.Equal(caso=="parcial" ? null : OtroNumero(cuenta),p.NumeroDocumento);
        Assert.Equal(cuenta.Raw,p.NumeroDocumentoVerificado);
    }

    private sealed record Cuenta(long Usuario,long Persona,string Session,string Attempt,DateTime Tiempo,string Raw,string Canonico,long Unidad);
    private static string OtroNumero(Cuenta c)=>"8"+c.Canonico[1..];
    private static XpayDbContext Contexto(string cs)=>new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);
    private static IConfiguration Config()
    {
        // Mantiene C1.2 para comprobar la identidad existente; A/B del
        // registro y el feature guiado ya pertenecen al bundle activo1.3.
        var c = ConsentimientoBiometricoTests.Configuracion();
        c[PreparacionWalletConfiguracion.ClaveHabilitada] = "true";
        foreach (var (prefijo, archivo, sha) in new[] {
            (DocumentosRegistroPublico.PrefijoTerminos, "terminos.pdf", PreparacionWalletConfiguracion.TerminosSha256),
            (DocumentosRegistroPublico.PrefijoPoliticaDatos, "politica.pdf", PreparacionWalletConfiguracion.PoliticaDatosSha256) })
        {
            c[prefijo + "_VERSION"] = PreparacionWalletConfiguracion.Version;
            c[prefijo + "_FECHA"] = PreparacionWalletConfiguracion.Fecha;
            c[prefijo + "_URL"] = "https://app.xpaycol.com/legal/v1.3/" + archivo;
            c[prefijo + "_SHA256"] = sha;
        }
        c["VERIFF_API_KEY"] = "clave-sintetica";
        c["VERIFF_SHARED_SECRET"] = "secreto-sintetico";
        c["VERIFF_BASE_URL"] = "https://veriff.example.test";
        return c;
    }
    private static SincronizacionDocumentoVeriffService Servicio(XpayDbContext db,IHttpClientFactory http) {
        var c=Config();var a=Common.AmbienteXpayTests.Ambiente("Production");var consentimiento=new ConsentimientoBiometricoService(db,c,a,TimeProvider.System);
        var kyc=new KycService(db,c,http,NullLogger<KycService>.Instance,a,consentimiento);
        return new(db,new(c,a),consentimiento,kyc,TimeProvider.System);
    }
    private static bool ConexionLocal(out string cs) {
        cs=Environment.GetEnvironmentVariable("ConnectionStrings__XpayConnection")??"";
        if (string.IsNullOrWhiteSpace(cs)) {
            Assert.False(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))||!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")),"Se requiere SQL local para esta integración.");return false;
        }
        var b=new SqlConnectionStringBuilder(cs);var host=b.DataSource.Replace("tcp:","",StringComparison.OrdinalIgnoreCase).Split(',')[0];
        Assert.True(host is "localhost" or "127.0.0.1" or "::1","Estas pruebas solo admiten SQL local; nunca QA/PROD.");return true;
    }

    private sealed class VeriffFake(XpayDbContext db,Cuenta cuenta):HttpMessageHandler,IHttpClientFactory {
        public int Peticiones {get;private set;} public string? Caso {get;init;} public Func<Task>? AntesDeResponder {get;set;}
        public HttpClient CreateClient(string name)=>new(this,disposeHandler:false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
            ++Peticiones;Assert.Null(db.Database.CurrentTransaction);Assert.Equal(HttpMethod.Get,request.Method);
            Assert.Equal("/v1/sessions/"+cuenta.Session+"/decision",request.RequestUri!.AbsolutePath);
            if(AntesDeResponder is not null) await AntesDeResponder();
            return new(HttpStatusCode.OK) {Content=new StringContent(JsonSerializer.Serialize(new {status="success",verification=new {
                id=Caso=="sesion_ajena"?Guid.NewGuid().ToString():cuenta.Session,attemptId=cuenta.Attempt,status="approved",decisionTime=cuenta.Tiempo.ToString("O"),
                person=new {firstName=Caso=="datos_raw_distintos"?"OTRA PERSONA":"ANA PRUEBA",lastName="DOS APELLIDOS"},
                document=new {type="ID_CARD",number=cuenta.Raw,country=Caso=="pais_ausente"?null:Caso=="pais_no_mapeado"?"VE":"CO"} }}))};
        }
    }

    private sealed class Datos(string cs):IAsyncDisposable {
        private readonly List<long> _personas=[];private readonly List<long> _usuarios=[];
        public async Task<Cuenta> CrearAsync() {
            await using var db=Contexto(cs);var ahora=DateTime.UtcNow;var sufijo=Guid.NewGuid().ToString("N");
            var unidad=await db.Database.SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo={0}","XPAY_COL").SingleAsync();
            var canonico="9"+Random.Shared.NextInt64(100_000_000,1_000_000_000).ToString(CultureInfo.InvariantCulture);
            var raw=$"{canonico[..1]}.{canonico.Substring(1,3)}.{canonico.Substring(4,3)}.{canonico.Substring(7,3)}";
            var p=new Persona {IdUnidadNegocio=unidad,Celular="3000000000",Email="sync_"+sufijo+"@example.test",Estado="ACTIVA",FechaCreacion=ahora,
                IdentidadVerificada=true,IdentidadVerificadaProveedor="VERIFF",IdentidadVerificadaFecha=ahora,NombreVerificadoCompleto="ANA PRUEBA",
                ApellidoVerificadoCompleto="DOS APELLIDOS",TipoDocumentoVeriffRaw="ID_CARD",NumeroDocumentoVerificado=raw};
            db.Personas.Add(p);await db.SaveChangesAsync();_personas.Add(p.IdPersona);
            var u=new Usuario {IdPersona=p.IdPersona,NombreUsuario="sync_"+sufijo,PasswordHash="sintetico",Estado="ACTIVO",FechaCreacion=ahora,
                EmailVerificado=true,EstadoKycActual="APROBADO"};
            db.Usuarios.Add(u);await db.SaveChangesAsync();_usuarios.Add(u.IdUsuario);
            // Migraciones antiguas reservan asociaciones sin FK al usuario12.
            // No son de esta cuenta sintética: se conservan y se usa otro id.
            if (await db.LibranzaUsuariosEmpresa.AnyAsync(a=>a.IdUsuario==u.IdUsuario)
                || await db.LibranzaEmpleadoUsuarios.AnyAsync(a=>a.IdUsuario==u.IdUsuario)) return await CrearAsync();
            var rol=await db.Roles.Where(r=>r.Codigo=="USUARIO_FINAL"&&r.Estado=="ACTIVO").Select(r=>r.IdRol).SingleAsync();
            db.UsuarioRoles.Add(new(){IdUsuario=u.IdUsuario,IdRol=rol,Estado="ACTIVO",FechaAsignacion=ahora});
            db.Auditorias.Add(new(){IdUsuario=u.IdUsuario,IdPersona=p.IdPersona,Modulo="USUARIOS",Accion=RegistroPublicoService.AccionAceptarTerminos,
                Entidad="usuarios",IdEntidad=u.IdUsuario.ToString(CultureInfo.InvariantCulture),Resultado="EXITOSO",FechaEvento=ahora});
            var cuenta=new Cuenta(u.IdUsuario,p.IdPersona,Guid.NewGuid().ToString(),Guid.NewGuid().ToString(),new(2026,11,21,12,0,0,DateTimeKind.Utc),raw,canonico,unidad);
            db.KycVerificaciones.Add(new(){IdUsuario=u.IdUsuario,IdPersona=p.IdPersona,Proveedor="VERIFF",EstadoKyc="APROBADO",Decision="approved",
                SessionId=cuenta.Session,AttemptIdVeriff=cuenta.Attempt,DecisionTimeVeriff=cuenta.Tiempo,EsActual=true,FechaCreacion=ahora,FechaDecision=ahora});
            await db.SaveChangesAsync();db.ChangeTracker.Clear();
            var c=Config();await new ConsentimientoBiometricoService(db,c,Common.AmbienteXpayTests.Ambiente("Production"),TimeProvider.System)
                .AutorizarAsync(u.IdUsuario,new(true,"1.2"),null);
            return cuenta;
        }
        public async Task OtraPersonaAsync(Cuenta cuenta,string? legal,string? raw) {
            await using var db=Contexto(cs);var p=new Persona {IdUnidadNegocio=cuenta.Unidad,Celular="3000000000",Estado="ACTIVA",FechaCreacion=DateTime.UtcNow,
                TipoDocumento=legal is null?null:"CC",NumeroDocumento=legal,NumeroDocumentoVerificado=raw,TipoDocumentoVeriffRaw=raw is null?null:"ID_CARD"};
            db.Personas.Add(p);await db.SaveChangesAsync();_personas.Add(p.IdPersona);
        }
        public async ValueTask DisposeAsync() {
            await using var db=Contexto(cs);
            await db.Auditorias.Where(a=>a.IdUsuario!=null&&_usuarios.Contains(a.IdUsuario.Value)).ExecuteDeleteAsync();
            await db.KycVerificaciones.Where(k=>_usuarios.Contains(k.IdUsuario)).ExecuteDeleteAsync();
            await db.UsuarioRoles.Where(r=>_usuarios.Contains(r.IdUsuario)).ExecuteDeleteAsync();
            await db.Usuarios.Where(u=>_usuarios.Contains(u.IdUsuario)).ExecuteDeleteAsync();
            await db.Personas.Where(p=>_personas.Contains(p.IdPersona)).ExecuteDeleteAsync();
        }
    }
}
