using System.Data.Common;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xpay.Api.Common;
using Xpay.Api.Controllers;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xpay.Api.Tests.Common;
using Xunit;

namespace Xpay.Api.Tests.Services;

// P0-B3 — reenvío de verificación contra SQL Server local: elegible solo
// PENDIENTE_VERIFICACION con personas.email normalizable; token nuevo que
// invalida el anterior; tope de 3 tokens VERIFICAR_EMAIL por hora contando el
// del alta; email solo tras el commit; fallo del envío no propagado;
// respuesta pública idéntica. Ambiente QA (sender espía, sin envío real).
[Collection("SqlIntegration")]
public sealed class ReenvioVerificacionSqlTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";
    private const string Clave         = "Reenvio#2026x";

    private enum EmailSemilla { Valido, Ausente, Invalido }

    [Fact]
    public async Task Reenvio_PendienteValido_EmiteNuevoToken_InvalidaElAnterior_YVerifica()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Sembrado>();
        try
        {
            var s = await SembrarAsync(cs, creados, EstadosUsuario.PendienteVerificacion);
            string token1, token2;
            await using (var e = new Entorno(cs))
            {
                await e.Servicio.ReenviarVerificacionAsync("  " + s.Usuario.ToUpperInvariant() + " ", "203.0.113.8");
                var enviado = Assert.Single(e.Sender.Enviados);
                Assert.Equal(s.Email, enviado.Destinatario);
                Assert.False(Assert.Single(e.Sender.TransaccionAbierta));
                token1 = TokenDe(enviado);
            }
            await using (var e = new Entorno(cs))
            {
                await e.Servicio.ReenviarVerificacionAsync(s.Usuario, null);
                token2 = TokenDe(Assert.Single(e.Sender.Enviados));
                Assert.DoesNotContain(e.Logger.Mensajes, m => m.Contains(token2, StringComparison.Ordinal)
                    || m.Contains(s.Email, StringComparison.OrdinalIgnoreCase));
            }

            await using (var v = NuevoContexto(cs))
            {
                var tokens = await v.UsuarioTokens.AsNoTracking().Where(t => t.IdUsuario == s.IdUsuario).ToListAsync();
                Assert.Equal(2, tokens.Count);
                Assert.All(tokens, t => Assert.Equal(PropositoTokenUsuario.VerificarEmail, t.Proposito));
                Assert.Single(tokens, t => t.FechaInvalidacion != null);
                Assert.Equal(2, await v.Auditorias.CountAsync(x => x.IdUsuario == s.IdUsuario && x.Accion == "REENVIAR_VERIFICACION_EMAIL"));
            }

            await using (var e = new Entorno(cs))
                Assert.False(await e.Servicio.VerificarEmailAsync(token1));
            await using (var e = new Entorno(cs))
                Assert.True(await e.Servicio.VerificarEmailAsync(token2));

            await using var f = NuevoContexto(cs);
            Assert.Equal(EstadosUsuario.Activo, (await f.Usuarios.AsNoTracking().SingleAsync(x => x.IdUsuario == s.IdUsuario)).Estado);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Theory]
    [InlineData("ACTIVO")]
    [InlineData("INACTIVO")]
    [InlineData("BLOQUEO_AUTOMATICO")]
    [InlineData("BLOQUEO_MANUAL")]
    [InlineData("PENDIENTE_SIN_EMAIL")]
    [InlineData("PENDIENTE_EMAIL_INVALIDO")]
    public async Task Reenvio_CasoNoElegible_NoEmiteNiEnvia(string caso)
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Sembrado>();
        try
        {
            var s = caso switch
            {
                "ACTIVO"                   => await SembrarAsync(cs, creados, EstadosUsuario.Activo),
                "INACTIVO"                 => await SembrarAsync(cs, creados, EstadosUsuario.Inactivo),
                "BLOQUEO_AUTOMATICO"       => await SembrarAsync(cs, creados, EstadosUsuario.Bloqueado, motivoBloqueo: BloqueoAutomaticoUsuario.Motivo),
                "BLOQUEO_MANUAL"           => await SembrarAsync(cs, creados, EstadosUsuario.Bloqueado, motivoBloqueo: "Bloqueo administrativo."),
                "PENDIENTE_SIN_EMAIL"      => await SembrarAsync(cs, creados, EstadosUsuario.PendienteVerificacion, EmailSemilla.Ausente),
                "PENDIENTE_EMAIL_INVALIDO" => await SembrarAsync(cs, creados, EstadosUsuario.PendienteVerificacion, EmailSemilla.Invalido),
                _ => throw new ArgumentOutOfRangeException(nameof(caso)),
            };

            await using var e = new Entorno(cs);
            await e.Servicio.ReenviarVerificacionAsync(s.Usuario, null);

            Assert.Empty(e.Sender.Enviados);
            await using var v = NuevoContexto(cs);
            Assert.Equal(0, await v.UsuarioTokens.CountAsync(t => t.IdUsuario == s.IdUsuario));
            Assert.Equal(0, await v.Auditorias.CountAsync(x => x.IdUsuario == s.IdUsuario));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("p0v_inexistente_sin_fila")]
    public async Task Reenvio_UsuarioInexistenteOVacio_NoHaceNada_NiLanza(string? usuario)
    {
        if (!TryConnString(out var cs)) return;
        await using var e = new Entorno(cs);
        await e.Servicio.ReenviarVerificacionAsync(usuario, null);
        Assert.Empty(e.Sender.Enviados);
    }

    [Fact]
    public async Task Reenvio_TopeDeTresPorHora_IncluyeElTokenDelAlta()
    {
        if (!TryConnString(out var cs)) return;
        var sufijo  = Guid.NewGuid().ToString("N")[..12];
        var usuario = $"p0v_reg_{sufijo}";
        var email   = $"p0v.{sufijo}@example.test";
        try
        {
            await using (var e = new Entorno(cs))
                await e.Servicio.RegistrarAsync(new RegistroPublicoRequest(email, usuario, Clave, "3000000000"), null);

            var reenviados = 0;
            for (var i = 0; i < RegistroPublicoService.MaxTokensVerificacionPorVentana; i++)
            {
                await using var e = new Entorno(cs);
                await e.Servicio.ReenviarVerificacionAsync(usuario, null);
                reenviados += e.Sender.Enviados.Count;
            }

            Assert.Equal(RegistroPublicoService.MaxTokensVerificacionPorVentana - 1, reenviados);
            await using var v = NuevoContexto(cs);
            var idUsuario = (await v.Usuarios.AsNoTracking().SingleAsync(x => x.NombreUsuario == usuario)).IdUsuario;
            Assert.Equal(RegistroPublicoService.MaxTokensVerificacionPorVentana,
                await v.UsuarioTokens.CountAsync(t => t.IdUsuario == idUsuario && t.Proposito == PropositoTokenUsuario.VerificarEmail));
            Assert.Equal(1, await v.UsuarioTokens.CountAsync(t => t.IdUsuario == idUsuario
                && t.FechaUso == null && t.FechaInvalidacion == null));
        }
        finally { await LimpiarRegistroAsync(cs, usuario, email); }
    }

    [Fact]
    public async Task Reenvio_FalloDelEnvio_NoSePropaga_YSoloRegistraElTipoDeError()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Sembrado>();
        try
        {
            var s = await SembrarAsync(cs, creados, EstadosUsuario.PendienteVerificacion);
            await using var e = new Entorno(cs);
            e.Sender.Fallar = true;
            await e.Servicio.ReenviarVerificacionAsync(s.Usuario, null);

            await using var v = NuevoContexto(cs);
            Assert.Equal(1, await v.UsuarioTokens.CountAsync(t => t.IdUsuario == s.IdUsuario));
            var mensaje = Assert.Single(e.Logger.Mensajes);
            Assert.Contains(nameof(InvalidOperationException), mensaje);
            Assert.DoesNotContain(s.Email, mensaje, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(s.Usuario, mensaje, StringComparison.OrdinalIgnoreCase);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Reenvio_Controller_RespuestaPublicaIdentica_ExistaONoYSeaONoElegible()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Sembrado>();
        try
        {
            var pendiente = await SembrarAsync(cs, creados, EstadosUsuario.PendienteVerificacion);
            var activo    = await SembrarAsync(cs, creados, EstadosUsuario.Activo);
            var solicitudes = new ReenviarVerificacionRequest?[]
            {
                new(pendiente.Usuario), new(activo.Usuario),
                new("p0v_inexistente_" + Guid.NewGuid().ToString("N")[..12]), new(null), null,
            };

            var respuestas = new List<string>();
            foreach (var solicitud in solicitudes)
            {
                await using var e = new Entorno(cs);
                var r = Assert.IsType<ObjectResult>(await e.Controller().ReenviarVerificacion(solicitud, default));
                respuestas.Add($"{r.StatusCode}|{Json(r.Value)}");
            }

            var unica = Assert.Single(respuestas.Distinct());
            Assert.StartsWith("202|", unica);
            Assert.Contains(RegistroPublicoController.MensajeReenvioRecibido, unica);
            Assert.DoesNotContain(pendiente.Email, unica, StringComparison.OrdinalIgnoreCase);
            await using var v = NuevoContexto(cs);
            Assert.Equal(1, await v.UsuarioTokens.CountAsync(t => t.IdUsuario == pendiente.IdUsuario));
            Assert.Equal(0, await v.UsuarioTokens.CountAsync(t => t.IdUsuario == activo.IdUsuario));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Reenvio_FalloAlInsertarToken_RevierteLaAuditoria_YNoEnvia()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Sembrado>();
        try
        {
            var s = await SembrarAsync(cs, creados, EstadosUsuario.PendienteVerificacion);
            await using (var e = new Entorno(cs, new FallaAlInsertarToken()))
            {
                var ex = await Assert.ThrowsAnyAsync<Exception>(() => e.Servicio.ReenviarVerificacionAsync(s.Usuario, null));
                Assert.True(ContieneFalloInyectado(ex));
                Assert.Empty(e.Sender.Enviados);
            }

            await using var v = NuevoContexto(cs);
            Assert.Equal(0, await v.UsuarioTokens.CountAsync(t => t.IdUsuario == s.IdUsuario));
            Assert.Equal(0, await v.Auditorias.CountAsync(x => x.IdUsuario == s.IdUsuario));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── Infraestructura ─────────────────────────────────────────────────

    // Email = forma normalizada esperada (en BD se guarda en mayúsculas).
    private sealed record Sembrado(long IdUsuario, long IdPersona, string Usuario, string Email);

    private static async Task<Sembrado> SembrarAsync(string cs, List<Sembrado> creados, string estado,
        EmailSemilla email = EmailSemilla.Valido, string? motivoBloqueo = null)
    {
        var sufijo = Guid.NewGuid().ToString("N")[..12];
        var normalizado = $"p0v.{sufijo}@example.test";
        await using var ctx = NuevoContexto(cs);
        var idUnidad = await ctx.Database
            .SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL")
            .SingleAsync();
        var ahora = DateTime.UtcNow;

        var persona = new Persona
        {
            IdUnidadNegocio = idUnidad, Celular = "3000000000", Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
            Email = email switch
            {
                EmailSemilla.Valido   => normalizado.ToUpperInvariant(),
                EmailSemilla.Invalido => "no-es-un-email",
                _                     => null,
            },
        };
        ctx.Personas.Add(persona);
        await ctx.SaveChangesAsync();
        creados.Add(new Sembrado(0, persona.IdPersona, $"p0v_ree_{sufijo}", normalizado));

        var bloqueado = estado == EstadosUsuario.Bloqueado;
        var usuario = new Usuario
        {
            IdPersona = persona.IdPersona, NombreUsuario = creados[^1].Usuario,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Clave),
            Estado = estado, EmailVerificado = false, FechaCreacion = ahora,
            IntentosFallidos = bloqueado ? 5 : 0,
            FechaBloqueo     = bloqueado ? ahora : null,
            MotivoBloqueo    = bloqueado ? motivoBloqueo : null,
        };
        ctx.Usuarios.Add(usuario);
        await ctx.SaveChangesAsync();
        creados[^1] = creados[^1] with { IdUsuario = usuario.IdUsuario };
        return creados[^1];
    }

    private sealed class Entorno : IAsyncDisposable
    {
        public XpayDbContext Ctx { get; }
        public SenderEspia Sender { get; }
        public LoggerEspia Logger { get; } = new();
        public RegistroPublicoService Servicio { get; }

        public Entorno(string cs, params IInterceptor[] interceptores)
        {
            var opciones = new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs);
            if (interceptores.Length > 0) opciones.AddInterceptors(interceptores);
            Ctx = new XpayDbContext(opciones.Options);
            var ambiente = AmbienteXpayTests.Ambiente("QA");
            var config   = new ConfigurationBuilder().Build();
            Sender   = new SenderEspia(Ctx);
            Servicio = new RegistroPublicoService(Ctx, ambiente,
                new TokenUsuarioService(Ctx, config, TimeProvider.System), Sender,
                new PlantillasEmailIdentidad(config, ambiente), TimeProvider.System, Logger);
        }

        public RegistroPublicoController Controller() => new(Servicio)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        public ValueTask DisposeAsync() => Ctx.DisposeAsync();
    }

    // Registra cada envío y si la transacción del servicio seguía abierta. Con
    // Fallar lanza un error cuyo mensaje SÍ lleva el destinatario: el servicio
    // no debe registrarlo.
    private sealed class SenderEspia(XpayDbContext ctxServicio) : IEmailSender
    {
        public List<MensajeEmail> Enviados { get; } = new();
        public List<bool> TransaccionAbierta { get; } = new();
        public bool Fallar { get; set; }

        public Task EnviarAsync(MensajeEmail mensaje, CancellationToken ct = default)
        {
            if (Fallar) throw new InvalidOperationException("Proveedor caído para " + mensaje.Destinatario);
            TransaccionAbierta.Add(ctxServicio.Database.CurrentTransaction is not null);
            Enviados.Add(mensaje);
            return Task.CompletedTask;
        }
    }

    // Captura el texto formateado y la excepción adjunta (si la hubiera).
    private sealed class LoggerEspia : ILogger<RegistroPublicoService>
    {
        public List<string> Mensajes { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Mensajes.Add(formatter(state, exception) + (exception is null ? string.Empty : " | " + exception));
    }

    private static string TokenDe(MensajeEmail mensaje)
    {
        var i = mensaje.CuerpoTexto.IndexOf("#token=", StringComparison.Ordinal);
        Assert.True(i >= 0);
        return Uri.UnescapeDataString(mensaje.CuerpoTexto[(i + "#token=".Length)..].Split('\n')[0].Trim());
    }

    // Sin escapar acentos: los mensajes se comparan literalmente.
    private static readonly JsonSerializerOptions JsonLegible = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static string Json(object? valor) => JsonSerializer.Serialize(valor, JsonLegible);

    private sealed class FalloInyectadoException : Exception
    {
        public FalloInyectadoException() : base("Fallo inyectado al insertar el token de verificación.") { }
    }

    private static bool ContieneFalloInyectado(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
            if (e is FalloInyectadoException) return true;
        return false;
    }

    // Falla el INSERT del token: último paso antes del commit.
    private sealed class FallaAlInsertarToken : DbCommandInterceptor
    {
        private static bool EsInsertDeToken(DbCommand c)
            => c.CommandText.Contains("INSERT INTO [usuario_tokens]", StringComparison.Ordinal);

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
            => EsInsertDeToken(command) ? throw new FalloInyectadoException() : base.ReaderExecuting(command, eventData, result);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
            => EsInsertDeToken(command) ? throw new FalloInyectadoException()
                                        : base.ReaderExecutingAsync(command, eventData, result, cancellationToken);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
            => EsInsertDeToken(command) ? throw new FalloInyectadoException()
                                        : base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL de P0-B3 reenvío.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    // Borra lo sembrado directamente (FK primero). Best-effort.
    private static async Task LimpiarAsync(string cs, List<Sembrado> creados)
    {
        try
        {
            var ids      = creados.Select(x => x.IdUsuario).Where(id => id > 0).ToList();
            var personas = creados.Select(x => x.IdPersona).ToList();
            await using var ctx = NuevoContexto(cs);
            await ctx.UsuarioTokens.Where(t => ids.Contains(t.IdUsuario)).ExecuteDeleteAsync();
            await ctx.Auditorias.Where(x => x.IdUsuario != null && ids.Contains(x.IdUsuario.Value)).ExecuteDeleteAsync();
            await ctx.Usuarios.Where(u => ids.Contains(u.IdUsuario)).ExecuteDeleteAsync();
            await ctx.Personas.Where(p => personas.Contains(p.IdPersona)).ExecuteDeleteAsync();
        }
        catch { /* limpieza best-effort, como en el resto de pruebas SQL */ }
    }

    // Borra todo lo que creó un alta pública (rol, wallet y saldo incluidos).
    private static async Task LimpiarRegistroAsync(string cs, string usuario, string email)
    {
        try
        {
            await using var ctx = NuevoContexto(cs);
            var ids = await ctx.Usuarios.Where(u => u.NombreUsuario == usuario).Select(u => u.IdUsuario).ToListAsync();
            var personas = await ctx.Personas.Where(p => p.Email == email).Select(p => p.IdPersona).ToListAsync();
            var wallets = await ctx.Wallets.Where(w => w.IdPersona != null && personas.Contains(w.IdPersona.Value)).Select(w => w.IdWallet).ToListAsync();

            await ctx.UsuarioTokens.Where(t => ids.Contains(t.IdUsuario)).ExecuteDeleteAsync();
            await ctx.Auditorias.Where(x => x.IdUsuario != null && ids.Contains(x.IdUsuario.Value)).ExecuteDeleteAsync();
            await ctx.UsuarioRoles.Where(r => ids.Contains(r.IdUsuario)).ExecuteDeleteAsync();
            await ctx.WalletSaldos.Where(s => wallets.Contains(s.IdWallet)).ExecuteDeleteAsync();
            await ctx.Wallets.Where(w => wallets.Contains(w.IdWallet)).ExecuteDeleteAsync();
            await ctx.Usuarios.Where(u => ids.Contains(u.IdUsuario)).ExecuteDeleteAsync();
            await ctx.Personas.Where(p => personas.Contains(p.IdPersona)).ExecuteDeleteAsync();
        }
        catch { /* limpieza best-effort, como en el resto de pruebas SQL */ }
    }
}
