using System.Data;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// P0-B — TokenUsuarioService contra SQL Server real (migración 048). Mismo
// patrón que ComercioPermisosP0ATests: se salta en local sin cadena de
// conexión y es obligatorio en CI. Reloj fijo (FijoTimeProvider), datos
// sintéticos únicos y limpieza best-effort en finally (usuario_tokens antes
// que usuario/persona por fk_ut_usuario). Los asserts sobre token y hash son
// booleanos para que un fallo nunca imprima secretos.
[Collection("SqlIntegration")]
public sealed class TokenUsuarioServiceSqlTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";
    private const string Verificar     = PropositoTokenUsuario.VerificarEmail;
    private const string Recuperar     = PropositoTokenUsuario.RecuperarClave;

    private static readonly DateTimeOffset T0 = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
    // Vigencias por defecto del servicio (configuración vacía).
    private static readonly TimeSpan VigenciaVerificar = TimeSpan.FromMinutes(1440);
    private static readonly TimeSpan VigenciaRecuperar = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan EsperaMaxima      = TimeSpan.FromSeconds(30);

    // ── T1 ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task T1_DosEmisiones_TokensDistintosYCanonicos_LaSegundaInvalidaLaPrimera()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var (idUsuario, email) = await SembrarUsuarioAsync(cs, creados);
            await using var ctx = NuevoContexto(cs);
            var svc = Servicio(ctx, T0);

            var a = await svc.EmitirAsync(idUsuario, Verificar, email, null);
            var b = await svc.EmitirAsync(idUsuario, Verificar, email, null);

            Assert.False(string.Equals(a.Token, b.Token, StringComparison.Ordinal));
            Assert.True(EsCanonico(a.Token));
            Assert.True(EsCanonico(b.Token));

            var filas = await FilasAsync(ctx, idUsuario);
            Assert.Equal(2, filas.Count);
            Assert.Equal(T0.UtcDateTime, filas[0].FechaInvalidacion);
            Assert.Null(filas[1].FechaInvalidacion);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── T2 ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task T2_LaBdGuardaSoloElSha256_NuncaElTokenEnClaro()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var (idUsuario, email) = await SembrarUsuarioAsync(cs, creados);
            await using var ctx = NuevoContexto(cs);
            var emitido = await Servicio(ctx, T0).EmitirAsync(idUsuario, Recuperar, email, "203.0.113.7");

            var fila = Assert.Single(await FilasAsync(ctx, idUsuario));
            Assert.Equal(32, fila.TokenHash.Length);
            Assert.True(fila.TokenHash.AsSpan().SequenceEqual(SHA256.HashData(Decodificar(emitido.Token))));
            Assert.True(NoContiene(fila.Proposito, emitido.Token));
            Assert.True(NoContiene(fila.EmailDestino, emitido.Token));
            Assert.True(NoContiene(fila.IpSolicitud, emitido.Token));
            Assert.Equal(Recuperar, fila.Proposito);
            Assert.Equal(email, fila.EmailDestino);
            Assert.Equal("203.0.113.7", fila.IpSolicitud);
            Assert.Equal(T0.UtcDateTime, fila.FechaCreacion);
            Assert.Equal(T0.Add(VigenciaRecuperar).UtcDateTime, fila.FechaExpiracion);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── T3 ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task T3_TokenValido_SeConsume_YFechaUsoEsElInstanteFijo()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var (idUsuario, email) = await SembrarUsuarioAsync(cs, creados);
            await using var ctx = NuevoContexto(cs);
            var emitido = await Servicio(ctx, T0).EmitirAsync(idUsuario, Verificar, email, null);
            var instante = T0.AddMinutes(5);

            var consumido = await Servicio(ctx, instante).ConsumirAsync(emitido.Token, Verificar);

            Assert.NotNull(consumido);
            Assert.Equal(idUsuario, consumido!.IdUsuario);
            Assert.Equal(email, consumido.EmailDestino);
            Assert.Equal(instante.UtcDateTime, Assert.Single(await FilasAsync(ctx, idUsuario)).FechaUso);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── T4 ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task T4_SegundoConsumo_Null_YFechaUsoNoCambia()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var (idUsuario, email) = await SembrarUsuarioAsync(cs, creados);
            await using var ctx = NuevoContexto(cs);
            var emitido = await Servicio(ctx, T0).EmitirAsync(idUsuario, Verificar, email, null);
            var primero = T0.AddMinutes(5);

            Assert.NotNull(await Servicio(ctx, primero).ConsumirAsync(emitido.Token, Verificar));
            Assert.Null(await Servicio(ctx, T0.AddMinutes(6)).ConsumirAsync(emitido.Token, Verificar));
            Assert.Equal(primero.UtcDateTime, Assert.Single(await FilasAsync(ctx, idUsuario)).FechaUso);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── T5 ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task T5_Expiracion_FronterasExactas()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var (idUsuario, email) = await SembrarUsuarioAsync(cs, creados);
            await using var ctx = NuevoContexto(cs);
            var emitido = await Servicio(ctx, T0).EmitirAsync(idUsuario, Verificar, email, null);
            var expiracion = T0.Add(VigenciaVerificar);
            Assert.Equal(expiracion.UtcDateTime, emitido.FechaExpiracion);

            // Primero los instantes inválidos: no deben quemar el token.
            Assert.Null(await Servicio(ctx, expiracion).ConsumirAsync(emitido.Token, Verificar));
            Assert.Null(await Servicio(ctx, expiracion.AddTicks(1)).ConsumirAsync(emitido.Token, Verificar));
            Assert.Null(Assert.Single(await FilasAsync(ctx, idUsuario)).FechaUso);

            var ultimoValido = expiracion.AddTicks(-1);
            Assert.NotNull(await Servicio(ctx, ultimoValido).ConsumirAsync(emitido.Token, Verificar));
            Assert.Equal(ultimoValido.UtcDateTime, Assert.Single(await FilasAsync(ctx, idUsuario)).FechaUso);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── T6 ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task T6_TokenInvalidado_Null_YFechaUsoSigueNull()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var (idUsuario, email) = await SembrarUsuarioAsync(cs, creados);
            await using var ctx = NuevoContexto(cs);
            var emitido = await Servicio(ctx, T0).EmitirAsync(idUsuario, Verificar, email, null);
            await ctx.UsuarioTokens.Where(t => t.IdUsuario == idUsuario)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.FechaInvalidacion, T0.UtcDateTime));

            Assert.Null(await Servicio(ctx, T0.AddMinutes(1)).ConsumirAsync(emitido.Token, Verificar));
            Assert.Null(Assert.Single(await FilasAsync(ctx, idUsuario)).FechaUso);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── T7 ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task T7_NuevaEmision_InvalidaLaAnteriorDelMismoProposito_NoLaDelOtro()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var (idUsuario, email) = await SembrarUsuarioAsync(cs, creados);
            await using var ctx = NuevoContexto(cs);
            var svc = Servicio(ctx, T0);
            var r1 = await svc.EmitirAsync(idUsuario, Recuperar, email, null);
            var v1 = await svc.EmitirAsync(idUsuario, Verificar, email, null);
            var r2 = await svc.EmitirAsync(idUsuario, Recuperar, email, null);

            var filas = await FilasAsync(ctx, idUsuario);
            Assert.Equal(3, filas.Count);
            Assert.Equal(T0.UtcDateTime, filas[0].FechaInvalidacion); // r1
            Assert.Null(filas[1].FechaInvalidacion);                  // v1
            Assert.Null(filas[2].FechaInvalidacion);                  // r2

            var consumo = Servicio(ctx, T0.AddMinutes(1));
            Assert.Null(await consumo.ConsumirAsync(r1.Token, Recuperar));
            Assert.NotNull(await consumo.ConsumirAsync(v1.Token, Verificar));
            Assert.NotNull(await consumo.ConsumirAsync(r2.Token, Recuperar));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── T8 ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task T8_PropositoIncorrecto_NoConsume_NiQuemaElToken()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var (idUsuario, email) = await SembrarUsuarioAsync(cs, creados);
            await using var ctx = NuevoContexto(cs);
            var v = await Servicio(ctx, T0).EmitirAsync(idUsuario, Verificar, email, null);
            var r = await Servicio(ctx, T0).EmitirAsync(idUsuario, Recuperar, email, null);
            var consumo = Servicio(ctx, T0.AddMinutes(1));

            Assert.Null(await consumo.ConsumirAsync(v.Token, Recuperar));
            Assert.Null(await consumo.ConsumirAsync(r.Token, Verificar));
            Assert.All(await FilasAsync(ctx, idUsuario), f => Assert.Null(f.FechaUso));

            Assert.NotNull(await consumo.ConsumirAsync(v.Token, Verificar));
            Assert.NotNull(await consumo.ConsumirAsync(r.Token, Recuperar));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── T9 A/B — carrera determinista bajo READ COMMITTED ──────────────
    // ctx1 consume dentro de una transacción explícita READ COMMITTED y deja
    // la fila bloqueada sin confirmar. ctx2 (otra conexión, autocommit, READ
    // COMMITTED por defecto) intenta consumir el mismo token. La coordinación
    // no depende de tiempos: se espera hasta OBSERVAR en sys.dm_exec_requests
    // que una sesión está bloqueada por la de ctx1. Luego ctx1 confirma (A: el
    // segundo pierde) o revierte (B: el segundo gana). Nunca ganan ambos.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task T9_ConsumoBloqueadoPorTransaccion_ReadCommitted(bool confirmarPrimero)
    {
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var (idUsuario, email) = await SembrarUsuarioAsync(cs, creados);
            string token;
            await using (var c0 = NuevoContexto(cs))
                token = (await Servicio(c0, T0).EmitirAsync(idUsuario, Verificar, email, null)).Token;
            var instante = T0.AddMinutes(1);

            await using var ctx1    = NuevoContexto(cs);
            await using var ctx2    = NuevoContexto(cs);
            await using var monitor = NuevoContexto(cs);

            await using var tx1 = await ctx1.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var spid1 = await ctx1.Database.SqlQueryRaw<short>("SELECT @@SPID AS Value").SingleAsync();
            Assert.NotNull(await Servicio(ctx1, instante).ConsumirAsync(token, Verificar));

            var segundo = Servicio(ctx2, instante).ConsumirAsync(token, Verificar);
            await EsperarSesionBloqueadaPorAsync(monitor, spid1);
            Assert.False(segundo.IsCompleted);

            if (confirmarPrimero) await tx1.CommitAsync();
            else await tx1.RollbackAsync();
            var resultado = await segundo.WaitAsync(EsperaMaxima);

            if (confirmarPrimero) Assert.Null(resultado);
            else Assert.NotNull(resultado);
            Assert.Equal(instante.UtcDateTime, Assert.Single(await FilasAsync(monitor, idUsuario)).FechaUso);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── T9 C — carrera abierta N=8 ──────────────────────────────────────
    [Fact]
    public async Task T9_CarreraAbierta_OchoConsumidores_UnSoloGanador()
    {
        const int n = 8;
        if (!TryConnString(out var cs)) return;
        var creados = new Sembrados();
        try
        {
            var (idUsuario, email) = await SembrarUsuarioAsync(cs, creados);
            string token;
            await using (var c0 = NuevoContexto(cs))
                token = (await Servicio(c0, T0).EmitirAsync(idUsuario, Verificar, email, null)).Token;
            var instante = T0.AddMinutes(1);

            var contextos = Enumerable.Range(0, n).Select(_ => NuevoContexto(cs)).ToList();
            TokenConsumido?[] resultados;
            try
            {
                var salida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var tareas = contextos.Select(c => Task.Run(async () =>
                {
                    await salida.Task;
                    return await Servicio(c, instante).ConsumirAsync(token, Verificar);
                })).ToList();
                salida.SetResult();
                resultados = await Task.WhenAll(tareas).WaitAsync(EsperaMaxima);
            }
            finally
            {
                foreach (var c in contextos) await c.DisposeAsync();
            }

            Assert.Single(resultados, r => r is not null);
            Assert.Equal(n - 1, resultados.Count(r => r is null));

            await using var v = NuevoContexto(cs);
            Assert.Equal(instante.UtcDateTime, Assert.Single(await FilasAsync(v, idUsuario)).FechaUso);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── Infraestructura ─────────────────────────────────────────────────

    private static TokenUsuarioService Servicio(XpayDbContext ctx, DateTimeOffset ahora)
        => new(ctx, new ConfigurationBuilder().Build(), new FijoTimeProvider(ahora));

    private static Task<List<UsuarioToken>> FilasAsync(XpayDbContext ctx, long idUsuario)
        => ctx.UsuarioTokens.AsNoTracking()
            .Where(t => t.IdUsuario == idUsuario)
            .OrderBy(t => t.IdToken)
            .ToListAsync();

    private static async Task EsperarSesionBloqueadaPorAsync(XpayDbContext monitor, short spidBloqueante)
    {
        using var cts = new CancellationTokenSource(EsperaMaxima);
        while (true)
        {
            var bloqueadas = await monitor.Database
                .SqlQueryRaw<int>(
                    "SELECT COUNT(*) AS Value FROM sys.dm_exec_requests WHERE blocking_session_id = {0}",
                    spidBloqueante)
                .SingleAsync(cts.Token);
            if (bloqueadas > 0) return;
            await Task.Delay(25, cts.Token); // sondeo; la autoridad es el estado observado
        }
    }

    private static bool EsCanonico(string token) =>
        Regex.IsMatch(token, "^[A-Za-z0-9_-]{43}$")
        && string.Equals(Codificar(Decodificar(token)), token, StringComparison.Ordinal);

    private static byte[] Decodificar(string token) =>
        Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/') + "=");

    private static string Codificar(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool NoContiene(string? valor, string token) =>
        valor is null || valor.IndexOf(token, StringComparison.Ordinal) < 0;

    private static async Task<(long IdUsuario, string Email)> SembrarUsuarioAsync(string cs, Sembrados creados)
    {
        await using var ctx = NuevoContexto(cs);
        var idUnidad = await ctx.Database
            .SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL")
            .SingleAsync();
        var sufijo = Guid.NewGuid().ToString("N")[..12];
        var ahora  = DateTime.UtcNow;
        var email  = $"p0b.{sufijo}@example.test";

        var persona = new Persona
        {
            IdUnidadNegocio = idUnidad, TipoDocumento = "CC", NumeroDocumento = $"P0B{sufijo}",
            PrimerNombre = "P0B", PrimerApellido = "Sintetico", Celular = "3000000000", Email = email,
            Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
        };
        ctx.Personas.Add(persona);
        await ctx.SaveChangesAsync();
        creados.Personas.Add(persona.IdPersona);

        var usuario = new Usuario
        {
            IdPersona = persona.IdPersona, NombreUsuario = $"p0b_tok_{sufijo}",
            PasswordHash = "x", Estado = EstadosUsuario.Activo, FechaCreacion = ahora,
        };
        ctx.Usuarios.Add(usuario);
        await ctx.SaveChangesAsync();
        creados.Usuarios.Add(usuario.IdUsuario);

        return (usuario.IdUsuario, email);
    }

    private sealed class Sembrados
    {
        public List<long> Personas { get; } = new();
        public List<long> Usuarios { get; } = new();
    }

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL de P0-B tokens.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    private static async Task LimpiarAsync(string cs, Sembrados creados)
    {
        try
        {
            await using var ctx = NuevoContexto(cs);
            await ctx.UsuarioTokens.Where(t => creados.Usuarios.Contains(t.IdUsuario)).ExecuteDeleteAsync();
            await ctx.Usuarios.Where(u => creados.Usuarios.Contains(u.IdUsuario)).ExecuteDeleteAsync();
            await ctx.Personas.Where(p => creados.Personas.Contains(p.IdPersona)).ExecuteDeleteAsync();
        }
        catch { /* limpieza best-effort, como en el resto de pruebas SQL */ }
    }
}
