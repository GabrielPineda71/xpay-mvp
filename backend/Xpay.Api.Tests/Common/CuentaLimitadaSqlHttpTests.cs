using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Common;

// HTTP real en loopback, JWT emitido por AuthService y SQL Server sintético.
// Se usan las políticas de autorización y revocación del servidor. Ningún
// transporte externo se registra: si una acción bloqueada intenta activarse,
// el request no puede terminar como el 403 esperado de CuentaLimitadaFilter.
[Collection("SqlIntegration")]
public sealed class CuentaLimitadaSqlHttpTests
{
    private const string Clave = "Limitada#2026x";
    private const string EnvConnString = "ConnectionStrings__XpayConnection";

    [Fact]
    public async Task Http_cuenta_limitada_bloquea_operaciones_antes_del_body_sin_interrumpir_piloto()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Alta>();
        try
        {
            // También con evidencia KYC y contactos aprobados: ni esos estados
            // ni un JWT válido liberan una cuenta marcada como limitada.
            var limitada = await SembrarAsync(cs, creados, limitada: true);
            var piloto = await SembrarAsync(cs, creados, limitada: false);
            var config = Configuracion();
            var loginLimitada = await LoginAsync(cs, config, limitada);
            var loginPiloto = await LoginAsync(cs, config, piloto);
            Assert.True(loginLimitada.RequierePerfil);
            Assert.False(loginPiloto.RequierePerfil);

            var sonda = new SondaCuentaLimitada();
            await using var app = CrearHost(cs, config, sonda);
            await app.StartAsync();
            using var cliente = new HttpClient
            {
                BaseAddress = new Uri(Assert.Single(app.Services.GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()!.Addresses)),
            };
            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginLimitada.Token);

            foreach (var ruta in new[] { "/api/usuarios/mi-perfil", "/api/contactos/estado", "/api/kyc/mi-estado" })
            {
                using var respuesta = await cliente.GetAsync(ruta);
                Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            }
            using (var cambio = await cliente.PatchAsync("/api/usuarios/mi-perfil",
                JsonContent.Create(new { ciudad = "Bogotá" })))
                Assert.Equal(HttpStatusCode.OK, cambio.StatusCode);

            // El cuerpo es deliberadamente inválido: un rechazo por model
            // binding sería 400 y demostraría que el filtro llegó demasiado tarde.
            foreach (var ruta in new[]
            {
                "/api/wallets/transferencia", "/api/qr/pagar",
                "/api/breb/mi-llave", "/api/breb/mi-llave/resolver",
                "/api/breb/retiros/real", "/api/cartera-ordinaria/solicitar-cupo",
                "/api/wallets/mi-wallet/recarga-breb",
                "/api/prueba-cuenta-limitada/operacion",
            })
            {
                using var respuesta = await cliente.PostAsync(ruta, new StringContent("{invalido", Encoding.UTF8, "application/json"));
                Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
                var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(CuentaLimitadaFilter.CodigoCuentaLimitada, cuerpo.GetProperty("codigo").GetString());
            }
            // KYC es una excepción deliberada al filtro financiero: permite
            // verificar identidad conservando la cuenta limitada. Su servicio
            // exige la nueva autorización C y no puede abrirse por esa excepción.
            using (var kycSinConsentimiento = await cliente.PostAsync("/api/kyc/veriff/session", null))
            {
                Assert.Equal(HttpStatusCode.Forbidden, kycSinConsentimiento.StatusCode);
                var cuerpo = await kycSinConsentimiento.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(KycService.MensajeConsentimientoKycPendiente, cuerpo.GetProperty("message").GetString());
            }
            using (var noPermitida = await cliente.GetAsync("/api/prueba-cuenta-limitada/sin-excepcion"))
                Assert.Equal(HttpStatusCode.Forbidden, noPermitida.StatusCode);
            Assert.Equal(0, sonda.Ejecuciones);

            // Login/callbacks son anónimos, incluso si el navegador manda un
            // bearer limitado. Se conserva la validación propia de cada callback.
            using (var callback = await cliente.PostAsync("/api/prueba-cuenta-limitada/callback", null))
                Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
            Assert.Equal(1, sonda.Callbacks);

            // Las vías legacy nunca se abren en PROD, tampoco como alternativa
            // anónima a las verificaciones del registro nuevo.
            cliente.DefaultRequestHeaders.Authorization = null;
            foreach (var ruta in new[] { "/api/usuarios/registro-inicial", "/api/usuarios/registro-final" })
            {
                using var respuesta = await cliente.PostAsync(ruta, new StringContent("{invalido", Encoding.UTF8, "application/json"));
                Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
            }

            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginPiloto.Token);
            using (var operacion = await cliente.PostAsync("/api/prueba-cuenta-limitada/operacion", JsonContent.Create(new { valor = 1 })))
                Assert.Equal(HttpStatusCode.OK, operacion.StatusCode);
            Assert.Equal(1, sonda.Ejecuciones); // interruptor global sigue true.

            // El marcador se relee en vivo: un JWT emitido como piloto tampoco
            // evade la restricción si la cuenta queda limitada posteriormente.
            await using (var db = Contexto(cs))
            {
                db.Auditorias.Add(Marcador(piloto));
                await db.SaveChangesAsync();
            }
            using (var bloqueada = await cliente.PostAsync("/api/prueba-cuenta-limitada/operacion", JsonContent.Create(new { valor = 1 })))
                Assert.Equal(HttpStatusCode.Forbidden, bloqueada.StatusCode);
            Assert.Equal(1, sonda.Ejecuciones);

            await using var consulta = Contexto(cs);
            var idsPersona = creados.Select(a => a.IdPersona).ToArray();
            var idsUsuario = creados.Select(a => a.IdUsuario).ToArray();
            Assert.False(await consulta.Wallets.AnyAsync(w => w.IdPersona != null && idsPersona.Contains(w.IdPersona.Value)));
            Assert.False(await consulta.LedgerTransacciones.AnyAsync(t => t.CreadoPor != null && idsUsuario.Contains(t.CreadoPor.Value)));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Theory]
    [InlineData("QA")]
    [InlineData("Production")]
    public async Task Servicio_kyc_bloquea_cuenta_limitada_sin_crear_sesion_ni_transporte(string nombreAmbiente)
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Alta>();
        try
        {
            var alta = await SembrarAsync(cs, creados, limitada: true);
            await using var db = Contexto(cs);
            var http = new SinHttp();
            var kyc = new KycService(db, Configuracion(), http,
                NullLogger<KycService>.Instance, AmbienteXpayTests.Ambiente(nombreAmbiente));
            var sesionesAntes = await db.KycVerificaciones.CountAsync(k => k.IdUsuario == alta.IdUsuario);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => kyc.CreateVeriffSessionAsync(alta.IdUsuario));

            Assert.Equal(KycService.MensajeConsentimientoKycPendiente, error.Message);
            Assert.Equal(0, http.Intentos);
            Assert.Equal(sesionesAntes, await db.KycVerificaciones.CountAsync(k => k.IdUsuario == alta.IdUsuario));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Cambio_obligatorio_de_clave_en_prod_conserva_requiere_perfil_y_marcador()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Alta>();
        try
        {
            var alta = await SembrarAsync(cs, creados, limitada: true);
            await using var db = Contexto(cs);
            await db.Usuarios.Where(u => u.IdUsuario == alta.IdUsuario)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.RequiereCambioClave, true));
            var auth = new AuthService(db, Configuracion(), AmbienteXpayTests.Ambiente("Production"));
            var respuesta = await auth.CambiarClaveObligatoriaAsync(alta.IdUsuario, new CambiarClaveObligatoriaRequest
            {
                ClaveActual = Clave,
                ClaveNueva = "NuevaLimitada#2026x",
                ConfirmacionClaveNueva = "NuevaLimitada#2026x",
            });

            Assert.True(respuesta.RequierePerfil);
            Assert.False(respuesta.RequiereCambioClave);
            Assert.True(await RegistroPublicoService.EsCuentaLimitadaAsync(db, alta.IdUsuario));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    private static WebApplication CrearHost(string cs, IConfiguration config, SondaCuentaLimitada sonda)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddConfiguration(config);
        builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
        builder.Services.AddDbContext<XpayDbContext>(o => o.UseSqlServer(cs));
        builder.Services.AddSingleton(AmbienteXpayTests.Ambiente("Production"));
        builder.Services.AddSingleton<ControlOperacionesFinancieras>();
        builder.Services.AddSingleton(sonda);
        builder.Services.AddSingleton<IHttpClientFactory, SinHttp>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<PerfilService>();
        builder.Services.AddScoped<RegistroUsuarioFinalService>();
        builder.Services.AddScoped<RegistroInicialService>();
        builder.Services.AddScoped<ContactoVerificacionService>();
        builder.Services.AddSingleton<RequisitoContactosVerificados>();
        builder.Services.AddScoped<AuditLogService>();
        builder.Services.AddScoped<KycService>();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!)),
                ValidateIssuer = true, ValidIssuer = config["Jwt:Issuer"],
                ValidateAudience = true, ValidAudience = config["Jwt:Audience"],
                ValidateLifetime = true, ClockSkew = TimeSpan.Zero,
            };
            ValidadorVersionSeguridad.Registrar(o);
        });
        builder.Services.AddScoped<IAuthorizationHandler, ClaveVigenteAuthorizationHandler>();
        builder.Services.AddScoped<IAuthorizationHandler, KycAprobadoAuthorizationHandler>();
        builder.Services.AddAuthorization(AutorizacionPoliticas.Configurar);
        builder.Services.AddRateLimiter(o =>
        {
            foreach (var nombre in new[] { "LoginPolicy", "CambiarClavePolicy", "ContactosCodigoPolicy", "IdentidadPublicaPolicy", "DestinatarioWalletPolicy" })
                o.AddFixedWindowLimiter(nombre, limite =>
                {
                    limite.PermitLimit = 1000;
                    limite.Window = TimeSpan.FromMinutes(1);
                });
        });
        builder.Services.AddControllers(o => o.Filters.Add<CuentaLimitadaFilter>(CuentaLimitadaFilter.Orden))
            .AddApplicationPart(typeof(UsuariosController).Assembly)
            .AddApplicationPart(typeof(CuentaLimitadaProbeController).Assembly);
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapControllers();
        return app;
    }

    private sealed record Alta(long IdUsuario, long IdPersona, string Nombre);

    private static async Task<Alta> SembrarAsync(string cs, List<Alta> creados, bool limitada)
    {
        await using var db = Contexto(cs);
        var unidad = await db.Database.SqlQueryRaw<long>(
            "SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL").SingleAsync();
        var nombre = "limited_" + Guid.NewGuid().ToString("N")[..12];
        var ahora = DateTime.UtcNow;
        var persona = new Persona
        {
            IdUnidadNegocio = unidad, Email = nombre + "@example.test", Celular = "3000000000",
            PrimerNombre = "Prueba", PrimerApellido = "Sintetica", Estado = "ACTIVA", FechaCreacion = ahora,
            IdentidadVerificada = true, IdentidadVerificadaProveedor = "VERIFF",
        };
        db.Personas.Add(persona);
        await db.SaveChangesAsync();
        var usuario = new Usuario
        {
            IdPersona = persona.IdPersona, NombreUsuario = nombre, PasswordHash = BCrypt.Net.BCrypt.HashPassword(Clave, workFactor: 4),
            Estado = "ACTIVO", FechaCreacion = ahora, EmailVerificado = true, CelularVerificado = true, EstadoKycActual = "APROBADO",
        };
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        var alta = new Alta(usuario.IdUsuario, persona.IdPersona, nombre);
        creados.Add(alta);
        db.UsuarioRoles.Add(new UsuarioRol
        {
            IdUsuario = usuario.IdUsuario,
            IdRol = await db.Roles.Where(r => r.Codigo == "USUARIO_FINAL" && r.Estado == "ACTIVO").Select(r => r.IdRol).SingleAsync(),
            Estado = "ACTIVO", FechaAsignacion = ahora,
        });
        db.KycVerificaciones.Add(new KycVerificacion
        {
            IdUsuario = usuario.IdUsuario, IdPersona = persona.IdPersona, Proveedor = "VERIFF",
            EstadoKyc = "APROBADO", EsActual = true, FechaCreacion = ahora,
        });
        if (limitada) db.Auditorias.Add(Marcador(alta));
        await db.SaveChangesAsync();
        return alta;
    }

    private static Auditoria Marcador(Alta alta) => new()
    {
        IdUsuario = alta.IdUsuario, IdPersona = alta.IdPersona, Modulo = "USUARIOS",
        Accion = RegistroPublicoService.AccionAceptarTerminos, Resultado = "EXITOSO", FechaEvento = DateTime.UtcNow,
        Entidad = "usuarios", IdEntidad = alta.IdUsuario.ToString(),
    };

    private static async Task<LoginResponse> LoginAsync(string cs, IConfiguration config, Alta alta)
    {
        await using var db = Contexto(cs);
        return await new AuthService(db, config, AmbienteXpayTests.Ambiente("Production"))
            .LoginAsync(new LoginRequest { Usuario = alta.Nombre, Password = Clave });
    }

    private static IConfiguration Configuracion() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Jwt:Key"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
        ["Jwt:Issuer"] = "cuenta-limitada-pruebas", ["Jwt:Audience"] = "cuenta-limitada-pruebas",
        [ControlOperacionesFinancieras.ClaveInterruptor] = "true",
        ["VERIFF_API_KEY"] = "sintetica", ["VERIFF_SHARED_SECRET"] = "sintetica",
        ["VERIFF_BASE_URL"] = "https://veriff.example.test", ["VERIFF_CALLBACK_URL"] = "https://app.example.test/perfil",
    }).Build();

    private static XpayDbContext Contexto(string cs) => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        Assert.False(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")),
            EnvConnString + " es obligatoria en CI para las pruebas de aislamiento de cuentas.");
        return false;
    }

    private static async Task LimpiarAsync(string cs, List<Alta> creados)
    {
        await using var db = Contexto(cs);
        var ids = creados.Select(a => a.IdUsuario).ToArray();
        var personas = creados.Select(a => a.IdPersona).ToArray();
        await db.Auditorias.Where(a => a.IdUsuario != null && ids.Contains(a.IdUsuario.Value)).ExecuteDeleteAsync();
        await db.KycVerificaciones.Where(k => ids.Contains(k.IdUsuario)).ExecuteDeleteAsync();
        await db.UsuarioRoles.Where(r => ids.Contains(r.IdUsuario)).ExecuteDeleteAsync();
        await db.Usuarios.Where(u => ids.Contains(u.IdUsuario)).ExecuteDeleteAsync();
        await db.Personas.Where(p => personas.Contains(p.IdPersona)).ExecuteDeleteAsync();
    }

    private sealed class SinHttp : IHttpClientFactory
    {
        public int Intentos { get; private set; }
        public HttpClient CreateClient(string name)
        {
            Intentos++;
            throw new InvalidOperationException("No se debe invocar un proveedor externo en estas pruebas.");
        }
    }
}

public sealed class SondaCuentaLimitada
{
    public int Ejecuciones;
    public int Callbacks;
}

[ApiController]
[Authorize]
[Route("api/prueba-cuenta-limitada")]
public sealed class CuentaLimitadaProbeController(SondaCuentaLimitada sonda) : ControllerBase
{
    [HttpPost("operacion")]
    [OperacionFinanciera]
    public IActionResult Operacion([FromBody] OperacionPrueba body)
    {
        Interlocked.Increment(ref sonda.Ejecuciones);
        return Ok(new { success = true });
    }

    [HttpGet("sin-excepcion")]
    public IActionResult SinExcepcion()
    {
        Interlocked.Increment(ref sonda.Ejecuciones);
        return Ok();
    }

    [HttpPost("callback")]
    [AllowAnonymous]
    public IActionResult Callback()
    {
        Interlocked.Increment(ref sonda.Callbacks);
        return Ok();
    }

    public sealed record OperacionPrueba(decimal Valor);
}
