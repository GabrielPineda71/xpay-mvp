using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Common;
using Xpay.Api.Controllers;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// P0-B3 — login con respuesta pública única contra SQL Server local:
// inexistente, clave incorrecta y cualquier estado no ACTIVO son
// indistinguibles; los estados no aptos no acumulan intentos ni revocan; el
// controlador nunca expone ex.Message. El bloqueo al 5.º fallo y su
// revocación siguen cubiertos por AuthServiceRevocacionSqlTests.
[Collection("SqlIntegration")]
public sealed class AuthLoginGenericoSqlTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";
    private const string Clave         = "Login#2026gen";
    private const string ClaveErronea  = "Erronea#2026gen";
    private const string MotivoManual  = "Bloqueo administrativo.";

    [Theory]
    [InlineData("INEXISTENTE", Clave)]
    [InlineData("ACTIVO", ClaveErronea)]
    [InlineData("PENDIENTE_VERIFICACION", Clave)]
    [InlineData("BLOQUEO_AUTOMATICO", Clave)]
    [InlineData("BLOQUEO_MANUAL", Clave)]
    [InlineData("INACTIVO", Clave)]
    [InlineData("ESTADO_DESCONOCIDO", Clave)]
    public async Task Login_CualquierFallo_MismoTipoYMensaje(string caso, string clave)
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Sembrado>();
        try
        {
            var nombre = caso == "INEXISTENTE"
                ? "p0l_inexistente_" + Guid.NewGuid().ToString("N")[..12]
                : (await SembrarCasoAsync(cs, creados, caso)).Usuario;

            await using var ctx = NuevoContexto(cs);
            var ex = await Assert.ThrowsAsync<CredencialesInvalidasException>(
                () => Servicio(ctx).LoginAsync(new LoginRequest { Usuario = nombre, Password = clave }));
            Assert.Equal("Usuario o contraseña inválidos.", ex.Message);
            Assert.Equal(CredencialesInvalidasException.MensajePublico, ex.Message);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Theory]
    [InlineData("PENDIENTE_VERIFICACION", ClaveErronea)]
    [InlineData("BLOQUEO_AUTOMATICO", ClaveErronea)]
    [InlineData("BLOQUEO_MANUAL", Clave)]
    [InlineData("INACTIVO", ClaveErronea)]
    public async Task Login_EstadoNoApto_NoAcumulaIntentos_NiRevoca(string caso, string clave)
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Sembrado>();
        try
        {
            var s = await SembrarCasoAsync(cs, creados, caso);
            var antes = await LeerAsync(cs, s.IdUsuario);

            for (var intento = 0; intento < 3; intento++)
            {
                await using var ctx = NuevoContexto(cs);
                await Assert.ThrowsAsync<CredencialesInvalidasException>(
                    () => Servicio(ctx).LoginAsync(new LoginRequest { Usuario = s.Usuario, Password = clave }));
            }

            var despues = await LeerAsync(cs, s.IdUsuario);
            Assert.Equal(antes.IntentosFallidos, despues.IntentosFallidos);
            Assert.Equal(antes.Estado, despues.Estado);
            Assert.Equal(antes.FechaBloqueo, despues.FechaBloqueo);
            Assert.Equal(antes.MotivoBloqueo, despues.MotivoBloqueo);
            Assert.Equal(antes.VersionSeguridad, despues.VersionSeguridad);
            Assert.Equal(antes.UltimoIngreso, despues.UltimoIngreso);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Login_HashMalFormadoEnEstadoNoApto_RespuestaGenerica()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Sembrado>();
        try
        {
            var s = await SembrarAsync(cs, creados, EstadosUsuario.Inactivo, passwordHash: "no-es-un-hash-bcrypt");
            await using var ctx = NuevoContexto(cs);
            var ex = await Assert.ThrowsAsync<CredencialesInvalidasException>(
                () => Servicio(ctx).LoginAsync(new LoginRequest { Usuario = s.Usuario, Password = Clave }));
            Assert.Equal(CredencialesInvalidasException.MensajePublico, ex.Message);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Theory]
    [InlineData(null, Clave)]
    [InlineData("", Clave)]
    [InlineData("   ", Clave)]
    [InlineData("p0l_cualquiera", null)]
    public async Task Login_CamposNulosOVacios_RespuestaGenerica(string? usuario, string? clave)
    {
        if (!TryConnString(out var cs)) return;
        await using var ctx = NuevoContexto(cs);
        var ex = await Assert.ThrowsAsync<CredencialesInvalidasException>(
            () => Servicio(ctx).LoginAsync(new LoginRequest { Usuario = usuario!, Password = clave! }));
        Assert.Equal(CredencialesInvalidasException.MensajePublico, ex.Message);
        await Assert.ThrowsAsync<CredencialesInvalidasException>(() => Servicio(ctx).LoginAsync(null!));
    }

    [Fact]
    public async Task Controller_FalloDeCredenciales_400IdenticoParaTodos()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Sembrado>();
        try
        {
            var activo    = await SembrarAsync(cs, creados, EstadosUsuario.Activo);
            var pendiente = await SembrarAsync(cs, creados, EstadosUsuario.PendienteVerificacion);
            var solicitudes = new LoginRequest?[]
            {
                new() { Usuario = "p0l_inexistente_" + Guid.NewGuid().ToString("N")[..12], Password = Clave },
                new() { Usuario = activo.Usuario, Password = ClaveErronea },
                new() { Usuario = pendiente.Usuario, Password = Clave },
                null,
            };

            var respuestas = new List<string>();
            foreach (var solicitud in solicitudes)
            {
                await using var ctx = NuevoContexto(cs);
                var r = Assert.IsType<BadRequestObjectResult>(await Controller(ctx).Login(solicitud));
                respuestas.Add($"{r.StatusCode}|{Json(r.Value)}");
            }

            var unica = Assert.Single(respuestas.Distinct());
            Assert.StartsWith("400|", unica);
            Assert.Contains(CredencialesInvalidasException.MensajePublico, unica);
            Assert.DoesNotContain("activo", unica, StringComparison.OrdinalIgnoreCase);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Controller_ErrorTecnico_500SinDetalleInterno()
    {
        if (!TryConnString(out var cs)) return;
        var interceptor = new FallaAlLeerUsuarios();
        await using var ctx = new XpayDbContext(new DbContextOptionsBuilder<XpayDbContext>()
            .UseSqlServer(cs).AddInterceptors(interceptor).Options);

        var r = Assert.IsType<ObjectResult>(await Controller(ctx)
            .Login(new LoginRequest { Usuario = "p0l_cualquiera", Password = Clave }));

        Assert.True(interceptor.Aplicado);
        Assert.Equal(500, r.StatusCode);
        Assert.DoesNotContain(FallaAlLeerUsuarios.Detalle, Json(r.Value));
    }

    // ── Infraestructura ─────────────────────────────────────────────────

    private sealed record Sembrado(long IdUsuario, long IdPersona, string Usuario);

    private static Task<Sembrado> SembrarCasoAsync(string cs, List<Sembrado> creados, string caso) => caso switch
    {
        "ACTIVO"                 => SembrarAsync(cs, creados, EstadosUsuario.Activo),
        "PENDIENTE_VERIFICACION" => SembrarAsync(cs, creados, EstadosUsuario.PendienteVerificacion),
        "BLOQUEO_AUTOMATICO"     => SembrarAsync(cs, creados, EstadosUsuario.Bloqueado, BloqueoAutomaticoUsuario.Motivo),
        "BLOQUEO_MANUAL"         => SembrarAsync(cs, creados, EstadosUsuario.Bloqueado, MotivoManual),
        "INACTIVO"               => SembrarAsync(cs, creados, EstadosUsuario.Inactivo),
        "ESTADO_DESCONOCIDO"     => SembrarAsync(cs, creados, "SUSPENDIDO"),
        _ => throw new ArgumentOutOfRangeException(nameof(caso)),
    };

    private static async Task<Sembrado> SembrarAsync(string cs, List<Sembrado> creados, string estado,
        string? motivoBloqueo = null, string? passwordHash = null)
    {
        var sufijo = Guid.NewGuid().ToString("N")[..12];
        await using var ctx = NuevoContexto(cs);
        var idUnidad = await ctx.Database
            .SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL")
            .SingleAsync();
        var ahora = DateTime.UtcNow;

        var persona = new Persona
        {
            IdUnidadNegocio = idUnidad, Celular = "3000000000", Email = $"p0l.{sufijo}@example.test",
            Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
        };
        ctx.Personas.Add(persona);
        await ctx.SaveChangesAsync();
        creados.Add(new Sembrado(0, persona.IdPersona, $"p0l_log_{sufijo}"));

        var bloqueado = estado == EstadosUsuario.Bloqueado;
        var usuario = new Usuario
        {
            IdPersona = persona.IdPersona, NombreUsuario = creados[^1].Usuario,
            PasswordHash = passwordHash ?? BCrypt.Net.BCrypt.HashPassword(Clave),
            Estado = estado, FechaCreacion = ahora,
            IntentosFallidos = bloqueado ? 5 : 0,
            FechaBloqueo     = bloqueado ? ahora : null,
            MotivoBloqueo    = bloqueado ? motivoBloqueo : null,
        };
        ctx.Usuarios.Add(usuario);
        await ctx.SaveChangesAsync();
        creados[^1] = creados[^1] with { IdUsuario = usuario.IdUsuario };
        return creados[^1];
    }

    private static AuthService Servicio(XpayDbContext ctx) => new(ctx, new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"]      = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
            ["Jwt:Issuer"]   = "xpay-test-issuer",
            ["Jwt:Audience"] = "xpay-test-audience",
        })
        .Build());

    private static AuthController Controller(XpayDbContext ctx) =>
        new(Servicio(ctx),
            new AuditLogService(NullLogger<AuditLogService>.Instance, new ConfigurationBuilder().Build()),
            NullLogger<AuthController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    // Sin escapar acentos: los mensajes se comparan literalmente.
    private static readonly JsonSerializerOptions JsonLegible = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static string Json(object? valor) => JsonSerializer.Serialize(valor, JsonLegible);

    private static async Task<Usuario> LeerAsync(string cs, long idUsuario)
    {
        await using var ctx = NuevoContexto(cs);
        return await ctx.Usuarios.AsNoTracking().SingleAsync(u => u.IdUsuario == idUsuario);
    }

    // Simula un fallo técnico de EF (InvalidOperationException con detalle
    // interno) al leer usuarios: el controlador no debe exponerlo.
    private sealed class FallaAlLeerUsuarios : DbCommandInterceptor
    {
        public const string Detalle = "detalle-interno-ef-p0b3";
        public bool Aplicado { get; private set; }

        private bool EsLecturaUsuarios(DbCommand c)
        {
            var es = c.CommandText.Contains("FROM [usuarios]", StringComparison.Ordinal);
            if (es) Aplicado = true;
            return es;
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
            => EsLecturaUsuarios(command) ? throw new InvalidOperationException(Detalle)
                                          : base.ReaderExecuting(command, eventData, result);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
            => EsLecturaUsuarios(command) ? throw new InvalidOperationException(Detalle)
                                          : base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL de P0-B3 login.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    // Borra lo que el test pudo crear (FK primero). Best-effort.
    private static async Task LimpiarAsync(string cs, List<Sembrado> creados)
    {
        try
        {
            var ids      = creados.Select(x => x.IdUsuario).Where(id => id > 0).ToList();
            var personas = creados.Select(x => x.IdPersona).ToList();
            await using var ctx = NuevoContexto(cs);
            await ctx.Auditorias.Where(x => x.IdUsuario != null && ids.Contains(x.IdUsuario.Value)).ExecuteDeleteAsync();
            await ctx.Usuarios.Where(u => ids.Contains(u.IdUsuario)).ExecuteDeleteAsync();
            await ctx.Personas.Where(p => personas.Contains(p.IdPersona)).ExecuteDeleteAsync();
        }
        catch { /* limpieza best-effort, como en el resto de pruebas SQL */ }
    }
}
