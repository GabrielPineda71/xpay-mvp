using System.Data.Common;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Common;
using Xpay.Api.Controllers;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Services;
using Xpay.Api.Tests.Common;
using Xunit;

namespace Xpay.Api.Tests.Services;

// P0-B1 — RegistroPublicoService contra SQL Server local: alta en
// PENDIENTE_VERIFICACION con unidad XPAY_COL, rol, wallet, saldo, auditoría y
// token en una transacción; email solo tras el commit; verificación de un
// solo uso. Ambiente QA (sender espía, sin envío real). Datos sintéticos
// propios y limpieza best-effort en finally.
[Collection("SqlIntegration")]
public sealed class RegistroPublicoServiceSqlTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";
    private const string Clave         = "Registro#2026x";

    // ── Alta ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Alta_QuedaPendiente_ConUnidadRolWalletSaldoAuditoriaYToken()
    {
        if (!TryConnString(out var cs)) return;
        var a = NuevaAlta();
        try
        {
            await using var e = new Entorno(cs);
            await e.Servicio.RegistrarAsync(Request(a), "203.0.113.9");

            await using var v = NuevoContexto(cs);
            var u = await v.Usuarios.AsNoTracking().SingleAsync(x => x.NombreUsuario == a.Usuario);
            Assert.Equal(EstadosUsuario.PendienteVerificacion, u.Estado);
            Assert.False(u.EmailVerificado);
            Assert.True(BCrypt.Net.BCrypt.Verify(Clave, u.PasswordHash));

            var p = await v.Personas.AsNoTracking().SingleAsync(x => x.IdPersona == u.IdPersona);
            Assert.Equal(a.Email, p.Email);
            Assert.Equal("3000000000", p.Celular);
            Assert.Equal(await IdXpayColAsync(v), p.IdUnidadNegocio);

            var roles = await (
                from ur in v.UsuarioRoles.AsNoTracking()
                join r in v.Roles.AsNoTracking() on ur.IdRol equals r.IdRol
                where ur.IdUsuario == u.IdUsuario && ur.Estado == "ACTIVO"
                select r.Codigo).ToListAsync();
            Assert.Equal("USUARIO_FINAL", Assert.Single(roles));

            var w = await v.Wallets.AsNoTracking().SingleAsync(x => x.IdPersona == p.IdPersona);
            Assert.Equal("PERSONA", w.TipoWallet);
            Assert.Equal("ACTIVA", w.Estado);
            Assert.Equal(p.IdUnidadNegocio, w.IdUnidadNegocio);
            Assert.Equal(0m, (await v.WalletSaldos.AsNoTracking().SingleAsync(x => x.IdWallet == w.IdWallet)).SaldoDisponible);

            Assert.Equal(1, await v.Auditorias.AsNoTracking().CountAsync(x => x.IdUsuario == u.IdUsuario && x.Accion == "REGISTRO_PUBLICO"));

            var t = await v.UsuarioTokens.AsNoTracking().SingleAsync(x => x.IdUsuario == u.IdUsuario);
            Assert.Equal(PropositoTokenUsuario.VerificarEmail, t.Proposito);
            Assert.Equal(a.Email, t.EmailDestino);
            Assert.Null(t.FechaUso);
            Assert.Equal("203.0.113.9", t.IpSolicitud);

            Assert.Equal(a.Email, Assert.Single(e.Sender.Enviados).Destinatario);
        }
        finally { await LimpiarAsync(cs, a); }
    }

    [Fact]
    public async Task Email_SeEnviaSoloDespuesDelCommit()
    {
        if (!TryConnString(out var cs)) return;
        var a = NuevaAlta();
        try
        {
            await using var e = new Entorno(cs);
            e.Sender.UsuarioEsperado = a.Usuario;
            await e.Servicio.RegistrarAsync(Request(a), null);

            var (transaccionAbierta, usuarioConfirmado) = Assert.Single(e.Sender.Observaciones);
            Assert.False(transaccionAbierta);
            Assert.True(usuarioConfirmado);
        }
        finally { await LimpiarAsync(cs, a); }
    }

    [Fact]
    public async Task Login_RechazadoMientrasPendiente()
    {
        if (!TryConnString(out var cs)) return;
        var a = NuevaAlta();
        try
        {
            await using (var e = new Entorno(cs))
                await e.Servicio.RegistrarAsync(Request(a), null);

            await using var ctx = NuevoContexto(cs);
            // P0-B3 — misma respuesta pública que cualquier credencial inválida.
            var ex = await Assert.ThrowsAsync<CredencialesInvalidasException>(
                () => new AuthService(ctx, ConfigJwt()).LoginAsync(new LoginRequest { Usuario = a.Usuario, Password = Clave }));
            Assert.Equal(CredencialesInvalidasException.MensajePublico, ex.Message);
        }
        finally { await LimpiarAsync(cs, a); }
    }

    [Fact]
    public async Task UsuarioDuplicado_Rechaza_SinSegundaCuenta()
    {
        if (!TryConnString(out var cs)) return;
        var a = NuevaAlta();
        var b = a with { Email = "otro." + a.Email };
        try
        {
            await using (var e = new Entorno(cs))
                await e.Servicio.RegistrarAsync(Request(a), null);

            await using var e2 = new Entorno(cs);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => e2.Servicio.RegistrarAsync(Request(b), null));
            Assert.Equal(RegistroPublicoService.MensajeUsuarioExiste, ex.Message);
            Assert.Empty(e2.Sender.Enviados);

            await using var v = NuevoContexto(cs);
            Assert.Equal(1, await v.Usuarios.CountAsync(x => x.NombreUsuario == a.Usuario));
            Assert.Equal(0, await v.Personas.CountAsync(x => x.Email == b.Email));
        }
        finally { await LimpiarAsync(cs, a, b); }
    }

    // ── Verificación ────────────────────────────────────────────────────

    [Fact]
    public async Task Verificacion_Valida_ActivaCuenta_YPermiteLogin()
    {
        if (!TryConnString(out var cs)) return;
        var a = NuevaAlta();
        try
        {
            var token = await RegistrarYObtenerTokenAsync(cs, a);
            await using (var e = new Entorno(cs))
                Assert.True(await e.Servicio.VerificarEmailAsync(token));

            await using var v = NuevoContexto(cs);
            var u = await v.Usuarios.AsNoTracking().SingleAsync(x => x.NombreUsuario == a.Usuario);
            Assert.Equal(EstadosUsuario.Activo, u.Estado);
            Assert.True(u.EmailVerificado);
            Assert.Equal(1, await v.Auditorias.AsNoTracking().CountAsync(x => x.IdUsuario == u.IdUsuario && x.Accion == "VERIFICAR_EMAIL"));
            Assert.NotNull((await v.UsuarioTokens.AsNoTracking().SingleAsync(x => x.IdUsuario == u.IdUsuario)).FechaUso);

            var login = await new AuthService(v, ConfigJwt()).LoginAsync(new LoginRequest { Usuario = a.Usuario, Password = Clave });
            Assert.False(string.IsNullOrEmpty(login.Token));
        }
        finally { await LimpiarAsync(cs, a); }
    }

    [Fact]
    public async Task TokenUsado_NoVerificaOtraVez()
    {
        if (!TryConnString(out var cs)) return;
        var a = NuevaAlta();
        try
        {
            var token = await RegistrarYObtenerTokenAsync(cs, a);
            await using (var e = new Entorno(cs))
                Assert.True(await e.Servicio.VerificarEmailAsync(token));
            await using (var e = new Entorno(cs))
                Assert.False(await e.Servicio.VerificarEmailAsync(token));

            await using var v = NuevoContexto(cs);
            Assert.Equal(EstadosUsuario.Activo, (await v.Usuarios.AsNoTracking().SingleAsync(x => x.NombreUsuario == a.Usuario)).Estado);
        }
        finally { await LimpiarAsync(cs, a); }
    }

    [Fact]
    public async Task TokenDePropositoIncorrecto_NoVerifica()
    {
        if (!TryConnString(out var cs)) return;
        var a = NuevaAlta();
        try
        {
            var tokenVerificacion = await RegistrarYObtenerTokenAsync(cs, a);
            long idUsuario;
            string tokenRecuperacion;
            await using (var ctx = NuevoContexto(cs))
            {
                idUsuario = (await ctx.Usuarios.AsNoTracking().SingleAsync(x => x.NombreUsuario == a.Usuario)).IdUsuario;
                tokenRecuperacion = (await new TokenUsuarioService(ctx, new ConfigurationBuilder().Build(), TimeProvider.System)
                    .EmitirAsync(idUsuario, PropositoTokenUsuario.RecuperarClave, a.Email, null)).Token;
            }

            await using (var e = new Entorno(cs))
                Assert.False(await e.Servicio.VerificarEmailAsync(tokenRecuperacion));
            Assert.Equal(EstadosUsuario.PendienteVerificacion, await EstadoAsync(cs, idUsuario));

            // El token correcto sigue siendo válido: el intento con otro propósito no lo quemó.
            await using (var e = new Entorno(cs))
                Assert.True(await e.Servicio.VerificarEmailAsync(tokenVerificacion));
        }
        finally { await LimpiarAsync(cs, a); }
    }

    [Fact]
    public async Task CambioDeEmail_InvalidaLaVerificacion_YQuemaElToken()
    {
        if (!TryConnString(out var cs)) return;
        var a = NuevaAlta();
        try
        {
            var token = await RegistrarYObtenerTokenAsync(cs, a);
            long idUsuario;
            await using (var ctx = NuevoContexto(cs))
            {
                var u = await ctx.Usuarios.AsNoTracking().SingleAsync(x => x.NombreUsuario == a.Usuario);
                idUsuario = u.IdUsuario;
                await ctx.Personas.Where(p => p.IdPersona == u.IdPersona)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Email, "cambiado." + a.Email));
            }

            await using (var e = new Entorno(cs))
                Assert.False(await e.Servicio.VerificarEmailAsync(token));
            Assert.Equal(EstadosUsuario.PendienteVerificacion, await EstadoAsync(cs, idUsuario));

            // Aunque se restaure el email, el token ya quedó consumido.
            await using (var ctx = NuevoContexto(cs))
                await ctx.Personas.Where(p => p.Email == "cambiado." + a.Email)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Email, a.Email));
            await using (var e = new Entorno(cs))
                Assert.False(await e.Servicio.VerificarEmailAsync(token));
        }
        finally { await LimpiarAsync(cs, a, a with { Email = "cambiado." + a.Email }); }
    }

    // ── P0-B3: fallo del envío tras el commit ───────────────────────────

    [Fact]
    public async Task FalloDelEnvioTrasCommit_NoLanza_CuentaPendiente_YReenvioPermiteVerificar()
    {
        if (!TryConnString(out var cs)) return;
        var a = NuevaAlta();
        try
        {
            await using (var e = new Entorno(cs))
            {
                e.Sender.Fallar = true;
                await e.Servicio.RegistrarAsync(Request(a), null);
            }

            await using (var v = NuevoContexto(cs))
            {
                var u = await v.Usuarios.AsNoTracking().SingleAsync(x => x.NombreUsuario == a.Usuario);
                Assert.Equal(EstadosUsuario.PendienteVerificacion, u.Estado);
                Assert.Equal(1, await v.UsuarioTokens.CountAsync(t => t.IdUsuario == u.IdUsuario));
            }

            string token;
            await using (var e = new Entorno(cs))
            {
                await e.Servicio.ReenviarVerificacionAsync(a.Usuario, null);
                token = TokenDe(Assert.Single(e.Sender.Enviados));
            }
            await using (var e = new Entorno(cs))
                Assert.True(await e.Servicio.VerificarEmailAsync(token));
        }
        finally { await LimpiarAsync(cs, a); }
    }

    [Fact]
    public async Task FalloDelEnvioTrasCommit_ControllerResponde202()
    {
        if (!TryConnString(out var cs)) return;
        var a = NuevaAlta();
        try
        {
            await using var e = new Entorno(cs);
            e.Sender.Fallar = true;
            var controller = new RegistroPublicoController(e.Servicio)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };
            var r = Assert.IsType<ObjectResult>(await controller.Registrar(Request(a), default));
            Assert.Equal(202, r.StatusCode);
        }
        finally { await LimpiarAsync(cs, a); }
    }

    // ── Fallo cerrado y rollback ────────────────────────────────────────

    [Fact]
    public async Task SinXpayCol_FallaCerrado_SinEfectos()
    {
        if (!TryConnString(out var cs)) return;
        var a = NuevaAlta();
        try
        {
            var interceptor = new SinUnidadXpayCol();
            await using var e = new Entorno(cs, interceptor);
            await Assert.ThrowsAsync<IdentidadConfiguracionException>(() => e.Servicio.RegistrarAsync(Request(a), null));
            Assert.True(interceptor.Aplicado);
            Assert.Empty(e.Sender.Enviados);
            await AssertSinRastroAsync(cs, a);
        }
        finally { await LimpiarAsync(cs, a); }
    }

    [Fact]
    public async Task FalloAlEmitirElToken_RevierteTodo()
    {
        if (!TryConnString(out var cs)) return;
        var a = NuevaAlta();
        try
        {
            await using var e = new Entorno(cs, new FallaAlInsertarToken());
            var ex = await Assert.ThrowsAnyAsync<Exception>(() => e.Servicio.RegistrarAsync(Request(a), null));
            Assert.True(ContieneFalloInyectado(ex));
            Assert.Empty(e.Sender.Enviados);
            await AssertSinRastroAsync(cs, a);
        }
        finally { await LimpiarAsync(cs, a); }
    }

    // ── Infraestructura ─────────────────────────────────────────────────

    private sealed record Alta(string Usuario, string Email);

    private static Alta NuevaAlta()
    {
        var s = Guid.NewGuid().ToString("N")[..12];
        return new Alta($"p0p_reg_{s}", $"p0p.{s}@example.test");
    }

    // Email en mayúsculas y celular con espacios: el servicio debe normalizarlos.
    private static RegistroPublicoRequest Request(Alta a)
        => new(a.Email.ToUpperInvariant(), a.Usuario, Clave, " 3000000000 ");

    private sealed class Entorno : IAsyncDisposable
    {
        public XpayDbContext Ctx { get; }
        public SenderEspia Sender { get; }
        public RegistroPublicoService Servicio { get; }

        public Entorno(string cs, params IInterceptor[] interceptores)
        {
            var opciones = new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs);
            if (interceptores.Length > 0) opciones.AddInterceptors(interceptores);
            Ctx = new XpayDbContext(opciones.Options);
            var ambiente = AmbienteXpayTests.Ambiente("QA");
            var config   = new ConfigurationBuilder().Build();
            Sender   = new SenderEspia(Ctx, cs);
            Servicio = new RegistroPublicoService(Ctx, ambiente,
                new TokenUsuarioService(Ctx, config, TimeProvider.System), Sender,
                new PlantillasEmailIdentidad(config, ambiente), TimeProvider.System,
                NullLogger<RegistroPublicoService>.Instance);
        }

        public ValueTask DisposeAsync() => Ctx.DisposeAsync();
    }

    // Registra cada envío y, en ese instante, si la transacción del servicio
    // sigue abierta y si el usuario ya es visible (confirmado) desde otra conexión.
    private sealed class SenderEspia(XpayDbContext ctxServicio, string cs) : IEmailSender
    {
        public List<MensajeEmail> Enviados { get; } = new();
        public List<(bool TransaccionAbierta, bool UsuarioConfirmado)> Observaciones { get; } = new();
        public string? UsuarioEsperado { get; set; }
        public bool Fallar { get; set; } // P0-B3 — simula la caída del proveedor

        public async Task EnviarAsync(MensajeEmail mensaje, CancellationToken ct = default)
        {
            if (Fallar) throw new InvalidOperationException("Proveedor de email caído.");
            var abierta = ctxServicio.Database.CurrentTransaction is not null;
            var confirmado = false;
            if (UsuarioEsperado is not null && !abierta)
            {
                await using var otro = NuevoContexto(cs);
                confirmado = await otro.Usuarios.AsNoTracking().AnyAsync(u => u.NombreUsuario == UsuarioEsperado, ct);
            }
            Observaciones.Add((abierta, confirmado));
            Enviados.Add(mensaje);
        }
    }

    private static async Task<string> RegistrarYObtenerTokenAsync(string cs, Alta a)
    {
        await using var e = new Entorno(cs);
        await e.Servicio.RegistrarAsync(Request(a), null);
        var cuerpo = Assert.Single(e.Sender.Enviados).CuerpoTexto;
        var i = cuerpo.IndexOf("#token=", StringComparison.Ordinal);
        Assert.True(i >= 0);
        return cuerpo[(i + "#token=".Length)..].Split('\n')[0].Trim();
    }

    private static string TokenDe(MensajeEmail mensaje)
    {
        var i = mensaje.CuerpoTexto.IndexOf("#token=", StringComparison.Ordinal);
        Assert.True(i >= 0);
        return mensaje.CuerpoTexto[(i + "#token=".Length)..].Split('\n')[0].Trim();
    }

    private static async Task<string> EstadoAsync(string cs, long idUsuario)
    {
        await using var ctx = NuevoContexto(cs);
        return (await ctx.Usuarios.AsNoTracking().SingleAsync(x => x.IdUsuario == idUsuario)).Estado;
    }

    private static async Task<long> IdXpayColAsync(XpayDbContext ctx)
        => await ctx.Database
            .SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL")
            .SingleAsync();

    private static async Task AssertSinRastroAsync(string cs, Alta a)
    {
        await using var v = NuevoContexto(cs);
        Assert.Equal(0, await v.Usuarios.CountAsync(x => x.NombreUsuario == a.Usuario));
        Assert.Equal(0, await v.Personas.CountAsync(x => x.Email == a.Email));
        Assert.Equal(0, await v.Wallets.CountAsync(x => x.NombreWallet == "Wallet " + a.Usuario));
    }

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

    // Hace que la búsqueda de XPAY_COL no devuelva filas.
    private sealed class SinUnidadXpayCol : DbCommandInterceptor
    {
        public bool Aplicado { get; private set; }

        private void Alterar(DbCommand c)
        {
            if (c.CommandText.Contains("unidades_negocio", StringComparison.Ordinal)
                && c.CommandText.Contains("WHERE codigo =", StringComparison.Ordinal))
            {
                c.CommandText = c.CommandText.Replace("WHERE codigo =", "WHERE 1 = 0 AND codigo =", StringComparison.Ordinal);
                Aplicado = true;
            }
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        { Alterar(command); return base.ReaderExecuting(command, eventData, result); }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        { Alterar(command); return base.ReaderExecutingAsync(command, eventData, result, cancellationToken); }
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

    private static IConfiguration ConfigJwt() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"]      = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
            ["Jwt:Issuer"]   = "xpay-test-issuer",
            ["Jwt:Audience"] = "xpay-test-audience",
        })
        .Build();

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL de P0-B1 registro público.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    // Borra por username y email todo lo que el alta pudo crear (FK primero).
    private static async Task LimpiarAsync(string cs, params Alta[] altas)
    {
        try
        {
            var nombres = altas.Select(x => x.Usuario).ToList();
            var emails  = altas.Select(x => x.Email).ToList();
            await using var ctx = NuevoContexto(cs);
            var ids = await ctx.Usuarios.Where(u => nombres.Contains(u.NombreUsuario)).Select(u => u.IdUsuario).ToListAsync();
            var personas = await ctx.Usuarios.Where(u => ids.Contains(u.IdUsuario)).Select(u => u.IdPersona).ToListAsync();
            personas.AddRange(await ctx.Personas.Where(p => p.Email != null && emails.Contains(p.Email)).Select(p => p.IdPersona).ToListAsync());
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
