using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Exceptions;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// PIN transaccional contra SQL Server real (tabla pin_transaccional de la
// migración 001). Mismo patrón que VersionSeguridadUsuarioSqlTests: se salta
// en local sin cadena de conexión y es obligatorio en CI. Datos sintéticos
// propios y limpieza best-effort en finally.
[Collection("SqlIntegration")]
public sealed class PinTransaccionalSqlTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";
    private const string Clave = "Clave-Prueba-2026";
    private const string PinOk = "2580147";
    private static readonly TimeSpan EsperaMaxima = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task SinPin_NoConfigurado_YCrearConClaveCorrecta_Verifica()
    {
        if (!TryConnString(out var cs)) return;
        var c = new Sembrados();
        try
        {
            var id = await SembrarUsuarioAsync(cs, c);
            await using var ctx = NuevoContexto(cs);
            var svc = new PinTransaccionalService(ctx);

            Assert.Equal(ResultadoPin.NoConfigurado, (await svc.VerificarAsync(id, PinOk)).Resultado);
            Assert.False((await svc.ObtenerEstadoAsync(id)).Configurado);

            await svc.CrearAsync(id, new CrearPinRequest { ClaveActual = Clave, Pin = PinOk, ConfirmacionPin = PinOk });

            var estado = await svc.ObtenerEstadoAsync(id);
            Assert.True(estado.Configurado);
            Assert.Equal(PinTransaccionalService.MaxIntentos, estado.IntentosRestantes);
            Assert.Equal(ResultadoPin.Correcto, (await svc.VerificarAsync(id, PinOk)).Resultado);

            await using var v = NuevoContexto(cs);
            var fila = await v.PinesTransaccionales.AsNoTracking().SingleAsync(p => p.IdUsuario == id);
            Assert.NotEqual(PinOk, fila.PinHash);
            Assert.True(BCrypt.Net.BCrypt.Verify(PinOk, fila.PinHash));
            Assert.Equal(0, fila.IntentosFallidos);
            Assert.Equal(1, await v.Auditorias.CountAsync(a => a.IdUsuario == id && a.Accion == "PIN_CREADO"));
        }
        finally { await LimpiarAsync(cs, c); }
    }

    [Fact]
    public async Task Crear_ConClaveIncorrecta_Rechaza_YNoCreaFila()
    {
        if (!TryConnString(out var cs)) return;
        var c = new Sembrados();
        try
        {
            var id = await SembrarUsuarioAsync(cs, c);
            await using var ctx = NuevoContexto(cs);
            var svc = new PinTransaccionalService(ctx);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                svc.CrearAsync(id, new CrearPinRequest { ClaveActual = "otra", Pin = PinOk, ConfirmacionPin = PinOk }));

            await using var v = NuevoContexto(cs);
            Assert.False(await v.PinesTransaccionales.AnyAsync(p => p.IdUsuario == id));
        }
        finally { await LimpiarAsync(cs, c); }
    }

    [Fact]
    public async Task Crear_DosVeces_LaSegundaRechaza()
    {
        if (!TryConnString(out var cs)) return;
        var c = new Sembrados();
        try
        {
            var id = await SembrarConPinAsync(cs, c);
            await using var ctx = NuevoContexto(cs);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new PinTransaccionalService(ctx).CrearAsync(id, new CrearPinRequest { ClaveActual = Clave, Pin = "3691470", ConfirmacionPin = "3691470" }));
        }
        finally { await LimpiarAsync(cs, c); }
    }

    [Fact]
    public async Task CincoIncorrectos_Bloquea_YNiElCorrectoPasaDespues()
    {
        if (!TryConnString(out var cs)) return;
        var c = new Sembrados();
        try
        {
            var id = await SembrarConPinAsync(cs, c);
            await using var ctx = NuevoContexto(cs);
            var svc = new PinTransaccionalService(ctx);

            for (var i = 1; i < PinTransaccionalService.MaxIntentos; i++)
            {
                var r = await svc.VerificarAsync(id, "1111112");
                Assert.Equal(ResultadoPin.Incorrecto, r.Resultado);
                Assert.Equal(PinTransaccionalService.MaxIntentos - i, r.IntentosRestantes);
            }
            Assert.Equal(ResultadoPin.Bloqueado, (await svc.VerificarAsync(id, "1111112")).Resultado);
            Assert.Equal(ResultadoPin.Bloqueado, (await svc.VerificarAsync(id, PinOk)).Resultado);

            await using var v = NuevoContexto(cs);
            var fila = await v.PinesTransaccionales.AsNoTracking().SingleAsync(p => p.IdUsuario == id);
            Assert.True(fila.Bloqueado);
            Assert.NotNull(fila.FechaBloqueo);
            Assert.Equal(PinTransaccionalService.MaxIntentos, fila.IntentosFallidos);
        }
        finally { await LimpiarAsync(cs, c); }
    }

    [Fact]
    public async Task Correcto_ReiniciaElContador()
    {
        if (!TryConnString(out var cs)) return;
        var c = new Sembrados();
        try
        {
            var id = await SembrarConPinAsync(cs, c);
            await using var ctx = NuevoContexto(cs);
            var svc = new PinTransaccionalService(ctx);

            await svc.VerificarAsync(id, "1111112");
            await svc.VerificarAsync(id, "1111112");
            Assert.Equal(ResultadoPin.Correcto, (await svc.VerificarAsync(id, PinOk)).Resultado);
            Assert.Equal(PinTransaccionalService.MaxIntentos, (await svc.ObtenerEstadoAsync(id)).IntentosRestantes);
        }
        finally { await LimpiarAsync(cs, c); }
    }

    // Doce intentos incorrectos simultáneos: el UPDATE de reserva no deja
    // probar más de MaxIntentos antes del bloqueo.
    [Fact]
    public async Task Concurrencia_DoceIncorrectos_SoloCuentaCinco_YBloquea()
    {
        const int n = 12;
        if (!TryConnString(out var cs)) return;
        var c = new Sembrados();
        try
        {
            var id = await SembrarConPinAsync(cs, c);
            var salida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tareas = Enumerable.Range(0, n).Select(_ => Task.Run(async () =>
            {
                await using var ctx = NuevoContexto(cs);
                await salida.Task;
                return (await new PinTransaccionalService(ctx).VerificarAsync(id, "1111112")).Resultado;
            })).ToList();
            salida.SetResult();
            var resultados = await Task.WhenAll(tareas).WaitAsync(EsperaMaxima);

            Assert.DoesNotContain(ResultadoPin.Correcto, resultados);
            await using var v = NuevoContexto(cs);
            var fila = await v.PinesTransaccionales.AsNoTracking().SingleAsync(p => p.IdUsuario == id);
            Assert.Equal(PinTransaccionalService.MaxIntentos, fila.IntentosFallidos);
            Assert.True(fila.Bloqueado);
        }
        finally { await LimpiarAsync(cs, c); }
    }

    [Fact]
    public async Task Cambiar_ConPinActualCorrecto_ActualizaHash()
    {
        if (!TryConnString(out var cs)) return;
        var c = new Sembrados();
        try
        {
            var id = await SembrarConPinAsync(cs, c);
            await using var ctx = NuevoContexto(cs);
            var svc = new PinTransaccionalService(ctx);

            var r = await svc.CambiarAsync(id, new CambiarPinRequest { PinActual = PinOk, PinNuevo = "3691470", ConfirmacionPinNuevo = "3691470" });
            Assert.Equal(ResultadoPin.Correcto, r.Resultado);

            await using var v = NuevoContexto(cs);
            var otro = new PinTransaccionalService(v);
            Assert.Equal(ResultadoPin.Correcto, (await otro.VerificarAsync(id, "3691470")).Resultado);
            Assert.Equal(ResultadoPin.Incorrecto, (await otro.VerificarAsync(id, PinOk)).Resultado);
        }
        finally { await LimpiarAsync(cs, c); }
    }

    [Fact]
    public async Task Cambiar_ConPinActualIncorrecto_CuentaIntento_YNoCambia()
    {
        if (!TryConnString(out var cs)) return;
        var c = new Sembrados();
        try
        {
            var id = await SembrarConPinAsync(cs, c);
            await using var ctx = NuevoContexto(cs);
            var r = await new PinTransaccionalService(ctx).CambiarAsync(id,
                new CambiarPinRequest { PinActual = "1111112", PinNuevo = "3691470", ConfirmacionPinNuevo = "3691470" });
            Assert.Equal(ResultadoPin.Incorrecto, r.Resultado);

            await using var v = NuevoContexto(cs);
            var otro = new PinTransaccionalService(v);
            Assert.Equal(ResultadoPin.Correcto, (await otro.VerificarAsync(id, PinOk)).Resultado);
        }
        finally { await LimpiarAsync(cs, c); }
    }

    [Fact]
    public async Task AdminRestablece_PinBloqueado_UsuarioCreaUnoNuevo()
    {
        if (!TryConnString(out var cs)) return;
        var c = new Sembrados();
        try
        {
            var id = await SembrarConPinAsync(cs, c);
            var idSuper = await SembrarAdminAsync(cs, c);
            await using (var ctx = NuevoContexto(cs))
            {
                var svc = new PinTransaccionalService(ctx);
                for (var i = 0; i < PinTransaccionalService.MaxIntentos; i++) await svc.VerificarAsync(id, "1111112");
            }

            await using (var ctx = NuevoContexto(cs))
            {
                var detalle = await new UsuarioAdminService(ctx).ObtenerDetalleAsync(id);
                Assert.True(detalle!.PinConfigurado);
                Assert.True(detalle.PinBloqueado);

                detalle = await new UsuarioAdminService(ctx).RestablecerPinAsync(id, idSuper);
                Assert.False(detalle.PinConfigurado);
                Assert.False(detalle.PinBloqueado);
            }

            await using (var ctx = NuevoContexto(cs))
            {
                var svc = new PinTransaccionalService(ctx);
                Assert.Equal(ResultadoPin.NoConfigurado, (await svc.VerificarAsync(id, PinOk)).Resultado);
                Assert.True((await svc.ObtenerEstadoAsync(id)).Restablecido);
                await svc.CrearAsync(id, new CrearPinRequest { ClaveActual = Clave, Pin = "3691470", ConfirmacionPin = "3691470" });
                Assert.Equal(ResultadoPin.Correcto, (await svc.VerificarAsync(id, "3691470")).Resultado);
            }

            await using var v = NuevoContexto(cs);
            Assert.Equal(1, await v.Auditorias.CountAsync(a => a.IdUsuario == idSuper && a.Accion == "PIN_RESTABLECER"));
            Assert.Equal(1, await v.PinesTransaccionales.CountAsync(p => p.IdUsuario == id));
        }
        finally { await LimpiarAsync(cs, c); }
    }

    [Fact]
    public async Task AdminRestablece_SinPinActivo_Conflicto()
    {
        if (!TryConnString(out var cs)) return;
        var c = new Sembrados();
        try
        {
            var id = await SembrarUsuarioAsync(cs, c);
            var idSuper = await SembrarAdminAsync(cs, c);
            await using var ctx = NuevoContexto(cs);
            await Assert.ThrowsAsync<TransicionUsuarioInvalidaException>(
                () => new UsuarioAdminService(ctx).RestablecerPinAsync(id, idSuper));
        }
        finally { await LimpiarAsync(cs, c); }
    }

    // ── Infraestructura ─────────────────────────────────────────────────

    private static async Task<long> SembrarConPinAsync(string cs, Sembrados c)
    {
        var id = await SembrarUsuarioAsync(cs, c);
        await using var ctx = NuevoContexto(cs);
        await new PinTransaccionalService(ctx).CrearAsync(id, new CrearPinRequest { ClaveActual = Clave, Pin = PinOk, ConfirmacionPin = PinOk });
        return id;
    }

    private static async Task<long> SembrarUsuarioAsync(string cs, Sembrados c, string? rol = null)
    {
        await using var ctx = NuevoContexto(cs);
        var idUnidad = await ctx.Database
            .SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL")
            .SingleAsync();
        var sufijo = Guid.NewGuid().ToString("N")[..12];
        var ahora  = DateTime.UtcNow;

        var persona = new Persona
        {
            IdUnidadNegocio = idUnidad, TipoDocumento = "CC", NumeroDocumento = $"PIN{sufijo}",
            PrimerNombre = "PIN", PrimerApellido = "Sintetico", Celular = "3000000000",
            Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
        };
        ctx.Personas.Add(persona);
        await ctx.SaveChangesAsync();
        c.Personas.Add(persona.IdPersona);

        var usuario = new Usuario
        {
            IdPersona = persona.IdPersona, NombreUsuario = $"pin_{sufijo}",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Clave), Estado = EstadosUsuario.Activo, FechaCreacion = ahora,
        };
        ctx.Usuarios.Add(usuario);
        await ctx.SaveChangesAsync();
        c.Usuarios.Add(usuario.IdUsuario);

        if (rol is not null)
        {
            var idRol = await ctx.Roles.Where(r => r.Codigo == rol).Select(r => r.IdRol).SingleAsync();
            ctx.UsuarioRoles.Add(new UsuarioRol { IdUsuario = usuario.IdUsuario, IdRol = idRol, Estado = "ACTIVO", FechaAsignacion = ahora });
            await ctx.SaveChangesAsync();
        }
        return usuario.IdUsuario;
    }

    private static Task<long> SembrarAdminAsync(string cs, Sembrados c) => SembrarUsuarioAsync(cs, c, "SUPERUSUARIO");

    private sealed class Sembrados
    {
        public List<long> Personas { get; } = new();
        public List<long> Usuarios { get; } = new();
    }

    private static async Task LimpiarAsync(string cs, Sembrados c)
    {
        try
        {
            await using var ctx = NuevoContexto(cs);
            var ids = c.Usuarios.Select(i => i.ToString()).ToList();
            await ctx.Auditorias.Where(a => (a.IdUsuario != null && c.Usuarios.Contains(a.IdUsuario.Value))
                                         || (a.Entidad == "pin_transaccional" && ids.Contains(a.IdEntidad!))).ExecuteDeleteAsync();
            await ctx.PinesTransaccionales.Where(p => c.Usuarios.Contains(p.IdUsuario)).ExecuteDeleteAsync();
            await ctx.UsuarioRoles.Where(r => c.Usuarios.Contains(r.IdUsuario)).ExecuteDeleteAsync();
            await ctx.Usuarios.Where(u => c.Usuarios.Contains(u.IdUsuario)).ExecuteDeleteAsync();
            await ctx.Personas.Where(p => c.Personas.Contains(p.IdPersona)).ExecuteDeleteAsync();
        }
        catch { /* limpieza best-effort, como en el resto de pruebas SQL */ }
    }

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL del PIN transaccional.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);
}
