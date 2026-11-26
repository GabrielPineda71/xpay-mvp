using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Xpay.Api.Authorization;
using Xpay.Api.Common;
using Xpay.Api.Controllers;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Integrations.MiDecisor;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xpay.Api.Tests.Common;
using Xunit;

namespace Xpay.Api.Tests.Services;

// Production real, SQL efímero y HTTP loopback. Sólo los transportes externos
// son ficticios; stores, autorizaciones, normalización, motor y cupo son reales.
[Collection("SqlIntegration")]
public sealed class CarteraPilotoProduccionFullFlowSqlTests
{
    private const string Clave = "FullFlow#2026Prueba";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegistroPublicoHastaCartera_ConsentimientoExplicitoYReplaySinNuevaConsulta(bool aceptaV1)
    {
        if (!Conexion(out var cs)) return;
        await using var f = new Flujo(cs);
        await f.IniciarAsync();
        await f.RegistrarYPrepararWalletAsync();
        using var client = f.Cliente();
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var solicitar = await client.PostAsync("/api/cartera-ordinaria/solicitar-cupo", null);
        Assert.Equal(HttpStatusCode.OK, solicitar.StatusCode); // Supera el antiguo 403 del alta pública.
        var solicitud = (await solicitar.Content.ReadFromJsonAsync<SolicitudCupoResponse>())!;
        Assert.Equal(CarteraSolicitudCupoEstados.Recibida, solicitud.EstadoSolicitud);
        Assert.Null(solicitud.IdCupoOrdinario);
        using (var scope = f.App!.Services.CreateScope())
        {
            var gate = Assert.IsType<AutorizacionConsultaRiesgoPiloto>(scope.ServiceProvider.GetRequiredService<IConsultaRiesgoAutorizacion>());
            Assert.IsType<AutorizacionConsultaRiesgoProduccion>(gate.AutorizacionInterna);
            Assert.IsType<MiDecisorClient>(scope.ServiceProvider.GetRequiredService<IMiDecisorClient>());
            Assert.IsType<MiDecisorIdentidadResolver>(scope.ServiceProvider.GetRequiredService<IMiDecisorIdentidadResolver>());
        }
        // Ni la membresía ni las aceptaciones de registro/KYC son el V1 de esta solicitud.
        using (var previa = await client.PostAsync($"/api/cartera-ordinaria/solicitudes/{solicitud.IdSolicitud}/iniciar-evaluacion", null))
        {
            Assert.Equal(HttpStatusCode.OK, previa.StatusCode);
            Assert.Equal(CarteraSolicitudCupoEstados.Recibida, (await previa.Content.ReadFromJsonAsync<IniciarEvaluacionResponse>())!.EstadoSolicitud);
        }
        Assert.Equal(0, f.Proveedor.AuthPosts); Assert.Equal(0, f.Proveedor.QueryPosts);
        if (aceptaV1)
        {
            using var textoResponse = await client.GetAsync("/api/cartera-ordinaria/autorizacion-consulta-riesgo/texto-vigente");
            Assert.Equal(HttpStatusCode.OK, textoResponse.StatusCode);
            var texto = (await textoResponse.Content.ReadFromJsonAsync<AutorizacionConsultaRiesgoTextoResponse>())!;
            Assert.Equal(CarteraAutorizacionConsultaRiesgoTextos.V1_HashSha256, texto.Hash);
            using var aceptar = await client.PostAsJsonAsync($"/api/cartera-ordinaria/solicitudes/{solicitud.IdSolicitud}/autorizar-consulta-riesgo",
                new RegistrarAutorizacionConsultaRiesgoRequest(texto.Version));
            Assert.Equal(HttpStatusCode.OK, aceptar.StatusCode);
            var getsAntes = f.Proveedor.VeriffGets;
            using var evaluar = await client.PostAsync($"/api/cartera-ordinaria/solicitudes/{solicitud.IdSolicitud}/iniciar-evaluacion", null);
            Assert.Equal(HttpStatusCode.OK, evaluar.StatusCode);
            var aprobada = (await evaluar.Content.ReadFromJsonAsync<IniciarEvaluacionResponse>())!;
            Assert.Equal(CarteraSolicitudCupoEstados.Aprobada, aprobada.EstadoSolicitud);
            Assert.Equal(CarteraDecisionCrediticia.Aprobada, aprobada.DecisionCrediticia);
            Assert.Equal(700_000m, aprobada.MontoAprobado); // Score853/ALTA/A/6N, política vigente; montoSugerido=1 no manda.
            Assert.Equal(1, f.Proveedor.AuthPosts); Assert.Equal(1, f.Proveedor.QueryPosts);
            Assert.Equal(getsAntes+1, f.Proveedor.VeriffGets); // Resolver scoped compartido con recencia/store.
            using var replay = await client.PostAsync($"/api/cartera-ordinaria/solicitudes/{solicitud.IdSolicitud}/iniciar-evaluacion", null);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal(CarteraSolicitudCupoEstados.Aprobada, (await replay.Content.ReadFromJsonAsync<IniciarEvaluacionResponse>())!.EstadoSolicitud);
            Assert.Equal(1, f.Proveedor.AuthPosts); Assert.Equal(1, f.Proveedor.QueryPosts);
            Assert.Equal(getsAntes+1, f.Proveedor.VeriffGets);
        }
        await using var db = Contexto(cs);
        var durable = await db.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(s=>s.IdSolicitud==solicitud.IdSolicitud);
        var intento = await db.CarteraSolicitudCupoIntentos.AsNoTracking().SingleAsync(i=>i.IdSolicitud==solicitud.IdSolicitud);
        Assert.Single(await db.CarteraSolicitudesCupo.Where(s=>s.IdUsuario==f.Usuario).ToListAsync());
        if (aceptaV1)
        {
            var cupo = await db.CarteraCuposOrdinarios.AsNoTracking().SingleAsync(c=>c.IdUsuario==f.Usuario);
            Assert.Equal(700_000m,cupo.CupoAprobado); Assert.Equal(0m,cupo.CupoUsado); Assert.Equal("ACTIVO",cupo.Estado);
            Assert.Equal(cupo.IdCupo,durable.IdCupoOrdinario); Assert.NotNull(durable.FechaMaterializacionCupo);
            Assert.Equal(CarteraIntentoFases.Finalizado,intento.FaseIntento); Assert.NotNull(intento.ResultadoConsumidoUtc);
            Assert.True(await db.CarteraAutorizacionesConsultaRiesgo.AnyAsync(a=>a.IdSolicitudOrigen==solicitud.IdSolicitud));
        }
        else
        {
            Assert.Equal(CarteraSolicitudCupoEstados.Recibida,durable.EstadoSolicitud);
            Assert.Equal(CarteraIntentoFases.PreCall,intento.FaseIntento); Assert.Null(intento.FechaFin);
            Assert.False(await db.CarteraCuposOrdinarios.AnyAsync(c=>c.IdUsuario==f.Usuario));
            Assert.False(await db.CarteraAutorizacionesConsultaRiesgo.AnyAsync(a=>a.IdSolicitudOrigen==solicitud.IdSolicitud));
        }
        Assert.False(await db.WalletMovimientos.AnyAsync(m=>m.IdWallet==f.Wallet));
        Assert.False(await db.LedgerTransacciones.AnyAsync(t=>t.CreadoPor==f.Usuario));
        Assert.Equal(0m,(await db.WalletSaldos.SingleAsync(s=>s.IdWallet==f.Wallet)).SaldoDisponible);
        Assert.Equal(0,f.Proveedor.DestinosNoPermitidos);
    }

    private sealed class Flujo(string cs) : IAsyncDisposable
    {
        private readonly string nombre="flowprod_"+Guid.NewGuid().ToString("N")[..12];
        internal readonly Proveedor Proveedor=new();
        private readonly IConfigurationRoot config=Configuracion();
        internal WebApplication? App;
        internal long Usuario, Persona, Wallet;
        private string? bearer;

        internal async Task IniciarAsync()
        {
            App=CrearHost(cs,config,Proveedor);
            await App.StartAsync();
        }

        internal async Task RegistrarYPrepararWalletAsync()
        {
            var email=new EmailSenderAcsTests.Transporte(HttpStatusCode.Accepted);
            await using (var db=Contexto(cs))
            {
                var ambiente=AmbienteXpayTests.Ambiente("Production");
                var registro=new RegistroPublicoService(db,ambiente,new TokenUsuarioService(db,config,TimeProvider.System),
                    EmailSenderAcsTests.Sender(email),new PlantillasEmailIdentidad(config,ambiente),TimeProvider.System,
                    NullLogger<RegistroPublicoService>.Instance,config);
                await registro.RegistrarAsync(new RegistroPublicoRequest(nombre+"@example.test",nombre,Clave,"3000000000",
                    new(true,"1.3",true,"1.3"),"PRUEBA"),"203.0.113.59");
                var user=await db.Usuarios.AsNoTracking().SingleAsync(u=>u.NombreUsuario==nombre);
                Usuario=user.IdUsuario; Persona=user.IdPersona;
                Assert.False(user.EmailVerificado); Assert.False(await db.Wallets.AnyAsync(w=>w.IdPersona==Persona));
                using var enviado=JsonDocument.Parse(email.Cuerpo!);
                var match=Regex.Match(enviado.RootElement.GetProperty("content").GetProperty("plainText").GetString()!,"#token=([A-Za-z0-9_-]+)");
                Assert.True(match.Success); Assert.True(await registro.VerificarEmailAsync(match.Groups[1].Value));
            }
            using (var scope=App!.Services.CreateScope())
            {
                var consent=scope.ServiceProvider.GetRequiredService<ConsentimientoBiometricoService>();
                Assert.True((await consent.AutorizarAsync(Usuario,new(true,config[ConsentimientoBiometricoService.PrefijoDocumento+"_VERSION"]),null)).Aceptado);
                var kyc=scope.ServiceProvider.GetRequiredService<KycService>();
                await kyc.CreateVeriffSessionAsync(Usuario);
                Assert.True((await kyc.ProcessVeriffWebhookAsync(Proveedor.Decision())).Processed);
            }
            using (var scope=App!.Services.CreateScope())
            {
                var db=scope.ServiceProvider.GetRequiredService<XpayDbContext>();
                var actual=await db.Personas.AsNoTracking().SingleAsync(p=>p.IdPersona==Persona);
                Assert.True(actual.IdentidadVerificada); Assert.Equal("CC",actual.TipoDocumento);
                var lugar=await (from c in db.CatalogoCiudades join d in db.CatalogoDepartamentos on c.IdDepartamento equals d.IdDepartamento
                    join p in db.CatalogoPaises on d.IdPais equals p.IdPais
                    where c.Estado=="ACTIVO"&&d.Estado=="ACTIVO"&&p.Estado=="ACTIVO"&&p.Codigo=="COL"
                    orderby c.IdCiudad select new { Departamento=d.IdDepartamento,Ciudad=c.IdCiudad }).FirstAsync();
                var preparacion=scope.ServiceProvider.GetRequiredService<PreparacionWalletService>();
                await preparacion.GuardarDatosAsync(Usuario,new("Calle sintética 12",lugar.Departamento,lugar.Ciudad));
                await new PinTransaccionalService(db).CrearAsync(Usuario,new(){ClaveActual=Clave,Pin="648291",ConfirmacionPin="648291"});
                var sesion=await preparacion.ActivarAsync(Usuario,new(),null);
                bearer=sesion.Token; Assert.False(sesion.RequierePerfil);
                Wallet=await db.Wallets.Where(w=>w.IdPersona==Persona&&w.TipoWallet=="PERSONA"&&w.Estado=="ACTIVA").Select(w=>w.IdWallet).SingleAsync();
            }
        }

        internal HttpClient Cliente()
        {
            var uri=Assert.Single(App!.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses);
            var client=new HttpClient(new SocketsHttpHandler{UseProxy=false,AllowAutoRedirect=false}){BaseAddress=new Uri(uri)};
            client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",bearer);
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            if(App is not null) await App.DisposeAsync();
            await using var db=Contexto(cs);
            var user=await db.Usuarios.AsNoTracking().SingleOrDefaultAsync(u=>u.NombreUsuario==nombre);
            if(user is null)return;
            var uid=user.IdUsuario; var pid=user.IdPersona;
            var solicitudes=db.CarteraSolicitudesCupo.Where(s=>s.IdUsuario==uid).Select(s=>s.IdSolicitud);
            await db.CarteraSolicitudCupoMotivosDecision.Where(m=>solicitudes.Contains(m.IdSolicitud)).ExecuteDeleteAsync();
            await db.CarteraSolicitudCupoRevisiones.Where(m=>solicitudes.Contains(m.IdSolicitud)).ExecuteDeleteAsync();
            await db.CarteraAutorizacionesConsultaRiesgo.Where(a=>a.IdUsuario==uid).ExecuteDeleteAsync();
            await db.CarteraSolicitudCupoIntentos.Where(i=>solicitudes.Contains(i.IdSolicitud)).ExecuteDeleteAsync();
            await db.CarteraSolicitudesCupo.Where(s=>s.IdUsuario==uid).ExecuteDeleteAsync();
            await db.CarteraCuposOrdinarios.Where(c=>c.IdUsuario==uid).ExecuteDeleteAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE dbo.cartera_piloto_habilitaciones WHERE id_usuario={uid}");
            var wallets=db.Wallets.Where(w=>w.IdPersona==pid).Select(w=>w.IdWallet);
            await db.WalletPreparaciones.Where(p=>p.IdUsuario==uid).ExecuteDeleteAsync();
            await db.WalletSaldos.Where(s=>wallets.Contains(s.IdWallet)).ExecuteDeleteAsync();
            await db.Wallets.Where(w=>w.IdPersona==pid).ExecuteDeleteAsync();
            await db.PinesTransaccionales.Where(p=>p.IdUsuario==uid).ExecuteDeleteAsync();
            await db.KycVerificaciones.Where(k=>k.IdUsuario==uid).ExecuteDeleteAsync();
            await db.UsuarioTokens.Where(t=>t.IdUsuario==uid).ExecuteDeleteAsync();
            await db.Auditorias.Where(a=>a.IdUsuario==uid||a.IdPersona==pid).ExecuteDeleteAsync();
            await db.UsuarioRoles.Where(r=>r.IdUsuario==uid).ExecuteDeleteAsync();
            await db.Usuarios.Where(u=>u.IdUsuario==uid).ExecuteDeleteAsync();
            await db.Personas.Where(p=>p.IdPersona==pid).ExecuteDeleteAsync();
        }
    }

    // A monotonic step per sample exercises the real limiter without real waiting.
    private sealed class RelojSinEspera : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>Interlocked.Add(ref timestamp,TimeSpan.TicksPerSecond*2);
    }

    private sealed class Proveedor : HttpMessageHandler
    {
        internal int AuthPosts,QueryPosts,VeriffGets,DestinosNoPermitidos;
        private readonly string session=Guid.NewGuid().ToString(),attempt=Guid.NewGuid().ToString();
        private readonly string numero="8"+RandomNumberGenerator.GetInt32(100_000_000,999_999_999).ToString();
        private string? vendor;
        internal string Decision()=>JsonSerializer.Serialize(new{status="success",verification=new{
            id=session,attemptId=attempt,status="approved",decisionTime="2026-11-25T00:00:00Z",vendorData=vendor,
            person=new{firstName="ANA",lastName="PRUEBA SINTETICA",dateOfBirth="1990-01-01"},
            document=new{type="ID_CARD",number=numero,country="CO"}}});
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var url=request.RequestUri!.AbsoluteUri;
            if(request.Method==HttpMethod.Post&&url=="https://api-saas.veriff.com/v1/sessions")
            {
                var body=await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken:ct);
                vendor=body.GetProperty("verification").GetProperty("vendorData").GetString();
                return Json(new{status="success",verification=new{id=session,url="https://saas.veriff.com/v/sintetica"}});
            }
            if(request.Method==HttpMethod.Get&&url=="https://api-saas.veriff.com/v1/sessions/"+session+"/decision")
            {
                Interlocked.Increment(ref VeriffGets);
                return new(HttpStatusCode.OK){Content=new StringContent(Decision(),Encoding.UTF8,"application/json")};
            }
            if(request.Method==HttpMethod.Post&&(url=="https://api.datacredito.com.co/spla/oauth2/v1/token"||
                url=="https://api.datacredito.com.co/co/cs/midecisor/v1/client"))
            {
                Assert.Equal("application/json",request.Content!.Headers.ContentType!.ToString());
                Assert.True(request.Content.Headers.ContentLength>0); Assert.False(request.Headers.TransferEncodingChunked is true);
                if(url.EndsWith("/token",StringComparison.Ordinal))
                { Interlocked.Increment(ref AuthPosts); return Json(new{access_token="synthetic-token-only",expires_in="3600"}); }
                Interlocked.Increment(ref QueryPosts);
                Assert.Equal("synthetic-token-only",request.Headers.Authorization!.Parameter);
                var query=await request.Content.ReadFromJsonAsync<JsonElement>(cancellationToken:ct);
                Assert.Equal("1",query.GetProperty("tipoIdentificacion").GetString());
                Assert.Equal(numero,query.GetProperty("numeroIdentificacion").GetString());
                Assert.Equal("PRUEBA",query.GetProperty("apellidoRazonSocial").GetString());
                return Json(new{status="ACCEPTED",content=new{status="202 ACCEPTED",
                    infoTransaccion=new{anioConsulta="2026",mesConsulta="11",diaConsulta="25"},respuesta=new{
                        informacionRiesgo=new{conInformacion=true,score="853",viabilidad="ALTA",ratingRecaudos="A",montoSugerido="1",alertas=Array.Empty<object>()},
                        validacion=new{datosBasicos=new{tipoDocumento="CC",estadoDocumento="VIGENTE",rangoEdad="36-45"}},
                        comportamientoCrediticio=new{comportamientoPago=new{vectorComportamiento=Enumerable.Range(6,6)
                            .Select(m=>new{anioMes=$"2026-{m:D2}",comportamiento="N"}).ToArray()}}}}});
            }
            Interlocked.Increment(ref DestinosNoPermitidos);
            throw new InvalidOperationException("Transporte externo no permitido en la prueba.");
        }
        private static HttpResponseMessage Json(object value)=>new(HttpStatusCode.OK){Content=JsonContent.Create(value)};
    }

    private static IConfigurationRoot Configuracion()
    {
        var c=PreparacionWalletSqlTests.Configuracion();
        c[DocumentosRegistroPublico.ClaveHabilitado]="true";c[DocumentosRegistroPublico.ClaveModalidad]="CUENTA_LIMITADA";
        c[ControlOperacionesFinancieras.ClaveInterruptor]="true";c[MiDecisorProduccionConfig.EnvProductionEnabled]="true";
        c["VERIFF_BASE_URL"]="https://api-saas.veriff.com";c["VERIFF_API_KEY"]="synthetic-api-key";
        c["VERIFF_SHARED_SECRET"]="synthetic-shared-secret";c["VERIFF_CALLBACK_URL"]="https://app.xpaycol.com/mi-wallet/perfil";
        c[MiDecisorOptions.EnvBaseUrl]="https://api.datacredito.com.co";
        c[MiDecisorOptions.EnvClientId]="synthetic-client";c[MiDecisorOptions.EnvClientSecret]="synthetic-secret";
        c[MiDecisorOptions.EnvUsername]="synthetic-user";c[MiDecisorOptions.EnvPassword]="synthetic-password";
        return c;
    }

    private static WebApplication CrearHost(string cs,IConfiguration config,Proveedor proveedor)
    {
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Production"});
        builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddConfiguration(config);builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
        builder.Services.AddSingleton(AmbienteXpayTests.Ambiente("Production"));
        builder.Services.AddSingleton<ControlOperacionesFinancieras>();builder.Services.AddSingleton<BloqueoFinancieroInterceptor>();
        builder.Services.AddDbContext<XpayDbContext>((sp,o)=>o.UseSqlServer(cs).AddInterceptors(sp.GetRequiredService<BloqueoFinancieroInterceptor>()));
        builder.Services.AddSingleton<TimeProvider>(new RelojSinEspera());
        builder.Services.ConfigureHttpClientDefaults(h=>h.ConfigurePrimaryHttpMessageHandler(()=>proveedor));
        builder.Services.AddMiDecisorTransport();builder.Services.AddMiDecisorIdentidadProduccion();builder.Services.AddCarteraRiesgoRuntime();
        foreach(var name in new[]{string.Empty,MiDecisorJsonTransport.ClientName,MiDecisorIdentidadResolver.VeriffClientName})
            builder.Services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(()=>proveedor);
        builder.Services.AddSingleton<IMiDecisorTokenProvider,MiDecisorTokenProvider>();builder.Services.AddSingleton<IMiDecisorClient,MiDecisorClient>();
        builder.Services.AddScoped<PerfilService>();builder.Services.AddScoped<AuditLogService>();builder.Services.AddScoped<KycService>();
        builder.Services.AddScoped<ConsentimientoBiometricoService>();builder.Services.AddScoped<CarteraPilotoService>();
        builder.Services.AddScoped<PreparacionWalletConfiguracion>();builder.Services.AddScoped<PreparacionWalletService>();
        builder.Services.AddScoped<AuthService>();builder.Services.AddScoped<PagoQrService>();builder.Services.AddScoped<CarteraOrdinariaService>();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o=>
        {
            o.TokenValidationParameters=new(){ValidateIssuerSigningKey=true,IssuerSigningKey=new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!)),
                ValidateIssuer=true,ValidIssuer=config["Jwt:Issuer"],ValidateAudience=true,ValidAudience=config["Jwt:Audience"],ValidateLifetime=true,ClockSkew=TimeSpan.Zero};
            ValidadorVersionSeguridad.Registrar(o);
        });
        builder.Services.AddScoped<IAuthorizationHandler,ClaveVigenteAuthorizationHandler>();builder.Services.AddScoped<IAuthorizationHandler,KycAprobadoAuthorizationHandler>();
        builder.Services.AddAuthorization(AutorizacionPoliticas.Configurar);
        builder.Services.AddControllers(o=>o.Filters.Add<CuentaLimitadaFilter>(CuentaLimitadaFilter.Orden)).AddApplicationPart(typeof(CarteraOrdinariaController).Assembly);
        var app=builder.Build();app.UseAuthentication();app.UseAuthorization();app.MapControllers();return app;
    }

    private static XpayDbContext Contexto(string cs)=>new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);
    private static bool Conexion(out string cs)
    {
        cs=Environment.GetEnvironmentVariable("ConnectionStrings__XpayConnection")??"";
        if(!string.IsNullOrWhiteSpace(cs))return true;
        Assert.False(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))||!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")),
            "CI exige SQL efímero para el flujo completo del piloto Production.");
        return false;
    }
}
