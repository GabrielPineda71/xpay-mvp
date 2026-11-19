using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Exceptions;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// PIN transaccional contra SQL Server real (tabla pin_transaccional de la
// migraciones 001 y 053). Mismo patrón que VersionSeguridadUsuarioSqlTests: se salta
// en local sin cadena de conexión y es obligatorio en CI. Datos sintéticos
// propios y limpieza best-effort en finally.
[Collection("SqlIntegration")]
public sealed class PinTransaccionalSqlTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";
    private const string Clave = "Clave-Prueba-2026";
    private const string PinOk = "258013";
    private const string PinLegado = "2580147";
    private const string PinNuevo = "369148";
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
            Assert.Equal(6, (await svc.ObtenerEstadoAsync(id)).LongitudRequerida);
            Assert.False((await svc.ObtenerEstadoAsync(id)).RequiereActualizacion);

            await svc.CrearAsync(id, new CrearPinRequest { ClaveActual = Clave, Pin = PinOk, ConfirmacionPin = PinOk });

            var estado = await svc.ObtenerEstadoAsync(id);
            Assert.True(estado.Configurado);
            Assert.Equal(6, estado.LongitudRequerida);
            Assert.False(estado.RequiereActualizacion);
            Assert.Equal(PinTransaccionalService.MaxIntentos, estado.IntentosRestantes);
            Assert.Equal(ResultadoPin.Correcto, (await svc.VerificarAsync(id, PinOk)).Resultado);

            await using var v = NuevoContexto(cs);
            var fila = await v.PinesTransaccionales.AsNoTracking().SingleAsync(p => p.IdUsuario == id);
            Assert.NotEqual(PinOk, fila.PinHash);
            Assert.True(BCrypt.Net.BCrypt.Verify(PinOk, fila.PinHash));
            Assert.Equal((byte?)6, fila.LongitudPin);
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
                new PinTransaccionalService(ctx).CrearAsync(id, new CrearPinRequest { ClaveActual = Clave, Pin = PinNuevo, ConfirmacionPin = PinNuevo }));
        }
        finally { await LimpiarAsync(cs, c); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(5)]
    [InlineData(7)]
    public async Task PinLegado_OperarDevuelve428_SinProbarHashNiConsumirIntentos(int? longitud)
    {
        if (!TryConnString(out var cs)) return;
        var c = new Sembrados();
        try
        {
            var id = await SembrarUsuarioAsync(cs, c);
            var fecha = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
            await using var ctx = NuevoContexto(cs);
            // Un hash ilegible haría fallar BCrypt.Verify: el gate de metadata
            // tiene que rechazar antes de ejecutarlo y antes del UPDATE de reserva.
            ctx.PinesTransaccionales.Add(new PinTransaccional
            {
                IdUsuario = id, PinHash = "hash-no-bcrypt-no-debe-compararse",
                LongitudPin = longitud.HasValue ? (byte)longitud.Value : null,
                Estado = EstadosPin.Activo, IntentosFallidos = 2,
                FechaCreacion = fecha, FechaActualizacion = fecha,
            });
            await ctx.SaveChangesAsync();
            var servicio = new PinTransaccionalService(ctx);
            foreach (var entrada in new[] { "25801", PinOk, PinLegado })
                Assert.Equal(ResultadoPin.ActualizacionRequerida, (await servicio.VerificarAsync(id, entrada)).Resultado);

            using var provider = new ServiceCollection().AddSingleton(ctx).AddScoped<PinTransaccionalService>().BuildServiceProvider();
            var http = new DefaultHttpContext { RequestServices = provider,
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("idUsuario", id.ToString()) }, "test")) };
            var cuerpo = new CuerpoPin { Pin = PinOk };
            var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
            var contexto = new ActionExecutingContext(action, new List<IFilterMetadata>(),
                new Dictionary<string, object?> { ["request"] = cuerpo }, new object());
            var ejecutada = false;
            await new RequierePinTransaccionalAttribute().OnActionExecutionAsync(contexto, () =>
            {
                ejecutada = true;
                return Task.FromResult(new ActionExecutedContext(contexto, new List<IFilterMetadata>(), new object()));
            });
            Assert.False(ejecutada);
            var respuesta = Assert.IsType<ObjectResult>(contexto.Result);
            Assert.Equal(428, respuesta.StatusCode);
            Assert.Contains("PIN_ACTUALIZACION_REQUERIDA", JsonSerializer.Serialize(respuesta.Value));
            Assert.Null(cuerpo.Pin);

            var fila = await ctx.PinesTransaccionales.AsNoTracking().SingleAsync(p => p.IdUsuario == id);
            Assert.Equal(2, fila.IntentosFallidos);
            Assert.False(fila.Bloqueado);
            Assert.Null(fila.FechaBloqueo);
            Assert.Equal(fecha, fila.FechaActualizacion);
            Assert.Equal("hash-no-bcrypt-no-debe-compararse", fila.PinHash);
            Assert.Equal(longitud.HasValue ? (byte?)longitud.Value : null, fila.LongitudPin);
            Assert.True((await servicio.ObtenerEstadoAsync(id)).RequiereActualizacion);
        }
        finally { await LimpiarAsync(cs, c); }
    }

    [Theory]
    [InlineData("25801")]
    [InlineData("2580147")]
    public async Task PinVigenteDeSeis_EntradaLegadaRechazaFormato_SinContarIntentos(string entrada)
    {
        if (!TryConnString(out var cs)) return;
        var c = new Sembrados();
        try
        {
            var id = await SembrarConPinAsync(cs, c);
            await using var ctx = NuevoContexto(cs);
            var servicio = new PinTransaccionalService(ctx);
            Assert.Equal(ResultadoPin.FormatoInvalido, (await servicio.VerificarAsync(id, entrada)).Resultado);
            Assert.Equal(PinTransaccionalService.MaxIntentos, (await servicio.ObtenerEstadoAsync(id)).IntentosRestantes);
        }
        finally { await LimpiarAsync(cs, c); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CrearConcurrentemente_SoloUnPinYMetadataPuedeGuardarse(bool trasRestablecimiento)
    {
        if (!TryConnString(out var cs)) return;
        var c = new Sembrados();
        try
        {
            var id = await SembrarUsuarioAsync(cs, c);
            if (trasRestablecimiento)
            {
                await using var seed = NuevoContexto(cs);
                seed.PinesTransaccionales.Add(new PinTransaccional
                {
                    IdUsuario = id, PinHash = BCrypt.Net.BCrypt.HashPassword(PinLegado),
                    Estado = EstadosPin.Restablecido, FechaCreacion = DateTime.UtcNow,
                });
                await seed.SaveChangesAsync();
            }
            var opciones = new[] { PinOk, PinNuevo };
            var listos = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tareas = opciones.Select(async candidato =>
            {
                await using var ctx = NuevoContexto(cs);
                await listos.Task;
                try
                {
                    await new PinTransaccionalService(ctx).CrearAsync(id,
                        new CrearPinRequest { ClaveActual = Clave, Pin = candidato, ConfirmacionPin = candidato });
                    return true;
                }
                catch (InvalidOperationException) { return false; }
            }).ToArray();
            listos.SetResult();
            var resultados = await Task.WhenAll(tareas).WaitAsync(EsperaMaxima);
            var ganador = Assert.Single(resultados.Select((guardado, i) => (guardado, i)), r => r.guardado);
            await using var lectura = NuevoContexto(cs);
            var fila = await lectura.PinesTransaccionales.AsNoTracking().SingleAsync(p => p.IdUsuario == id);
            Assert.Equal((byte?)6, fila.LongitudPin);
            Assert.True(BCrypt.Net.BCrypt.Verify(opciones[ganador.i], fila.PinHash));
            Assert.False(BCrypt.Net.BCrypt.Verify(opciones[1 - ganador.i], fila.PinHash));
            Assert.Equal(1, await lectura.Auditorias.CountAsync(a => a.IdUsuario == id
                && a.Accion == (trasRestablecimiento ? "PIN_CREADO_TRAS_RESTABLECIMIENTO" : "PIN_CREADO")));
        }
        finally { await LimpiarAsync(cs, c); }
    }

    private sealed class CuerpoPin : IConPinTransaccional { public string? Pin { get; set; } }

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
                var r = await svc.VerificarAsync(id, "111112");
                Assert.Equal(ResultadoPin.Incorrecto, r.Resultado);
                Assert.Equal(PinTransaccionalService.MaxIntentos - i, r.IntentosRestantes);
            }
            Assert.Equal(ResultadoPin.Bloqueado, (await svc.VerificarAsync(id, "111112")).Resultado);
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

            await svc.VerificarAsync(id, "111112");
            await svc.VerificarAsync(id, "111112");
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
                return (await new PinTransaccionalService(ctx).VerificarAsync(id, "111112")).Resultado;
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

            var r = await svc.CambiarAsync(id, new CambiarPinRequest { PinActual = PinOk, PinNuevo = PinNuevo, ConfirmacionPinNuevo = PinNuevo });
            Assert.Equal(ResultadoPin.Correcto, r.Resultado);

            await using var v = NuevoContexto(cs);
            var otro = new PinTransaccionalService(v);
            Assert.Equal(ResultadoPin.Correcto, (await otro.VerificarAsync(id, PinNuevo)).Resultado);
            Assert.Equal(ResultadoPin.Incorrecto, (await otro.VerificarAsync(id, PinOk)).Resultado);
        }
        finally { await LimpiarAsync(cs, c); }
    }

    [Theory]
    [InlineData("25801", null)]
    [InlineData("2580147", null)]
    [InlineData("25801", 5)]
    [InlineData("2580147", 7)]
    public async Task PinLegado_SoloSirveParaCambiarAUnPinDeSeisDigitos(string pinLegado, int? longitud)
    {
        if (!TryConnString(out var cs)) return;
        var c = new Sembrados();
        try
        {
            var id = await SembrarConPinLegadoAsync(cs, c, pinLegado, longitud);
            await using var ctx = NuevoContexto(cs);
            var servicio = new PinTransaccionalService(ctx);

            Assert.Equal(ResultadoPin.ActualizacionRequerida, (await servicio.VerificarAsync(id, pinLegado)).Resultado);
            Assert.True((await servicio.ObtenerEstadoAsync(id)).RequiereActualizacion);
            Assert.Equal(PinTransaccionalService.MaxIntentos, (await servicio.ObtenerEstadoAsync(id)).IntentosRestantes);

            var incorrecto = await servicio.CambiarAsync(id,
                new CambiarPinRequest { PinActual = new string('8', pinLegado.Length), PinNuevo = PinNuevo, ConfirmacionPinNuevo = PinNuevo });
            Assert.Equal(ResultadoPin.Incorrecto, incorrecto.Resultado);
            Assert.Equal(PinTransaccionalService.MaxIntentos - 1, incorrecto.IntentosRestantes);

            var solicitud = new CambiarPinRequest { PinActual = pinLegado, PinNuevo = PinNuevo, ConfirmacionPinNuevo = PinNuevo };
            Assert.Equal(ResultadoPin.Correcto, (await servicio.CambiarAsync(id, solicitud)).Resultado);
            Assert.Equal(PinTransaccionalService.MaxIntentos, (await servicio.ObtenerEstadoAsync(id)).IntentosRestantes);
            Assert.False((await servicio.ObtenerEstadoAsync(id)).RequiereActualizacion);
            Assert.Equal(ResultadoPin.FormatoInvalido, (await servicio.VerificarAsync(id, pinLegado)).Resultado);
            Assert.Equal(ResultadoPin.Correcto, (await servicio.VerificarAsync(id, PinNuevo)).Resultado);

            var anterior = await servicio.CambiarAsync(id,
                new CambiarPinRequest { PinActual = pinLegado, PinNuevo = PinOk, ConfirmacionPinNuevo = PinOk });
            Assert.Equal(ResultadoPin.Incorrecto, anterior.Resultado);
            Assert.Equal(1, await ctx.Auditorias.CountAsync(a => a.IdUsuario == id && a.Accion == "PIN_CAMBIADO"));
            var actualizado = await ctx.PinesTransaccionales.AsNoTracking().SingleAsync(p => p.IdUsuario == id);
            Assert.Equal((byte?)6, actualizado.LongitudPin);
            Assert.True(BCrypt.Net.BCrypt.Verify(PinNuevo, actualizado.PinHash));
        }
        finally { await LimpiarAsync(cs, c); }
    }

    [Theory]
    [InlineData("25801", null)]
    [InlineData("2580147", null)]
    [InlineData(PinOk, 6)]
    public async Task DosCambiosConcurrentes_ConElMismoPinActual_SoloUnoPuedeGuardarse(string pinActual, int? longitud)
    {
        if (!TryConnString(out var cs)) return;
        var c = new Sembrados();
        try
        {
            var id = await SembrarConPinLegadoAsync(cs, c, pinActual, longitud);
            var opciones = new[] { "582061", PinNuevo };
            var listos = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var intentos = opciones.Select(async candidato =>
            {
                await using var contexto = NuevoContexto(cs);
                await listos.Task;
                var solicitud = new CambiarPinRequest { PinActual = pinActual, PinNuevo = candidato, ConfirmacionPinNuevo = candidato };
                return await new PinTransaccionalService(contexto).CambiarAsync(id, solicitud);
            }).ToArray();

            listos.SetResult();
            var resultados = await Task.WhenAll(intentos).WaitAsync(EsperaMaxima);
            var ganador = Assert.Single(resultados.Select((r, i) => (r.Resultado, Indice: i)),
                r => r.Resultado == ResultadoPin.Correcto);

            await using var lectura = NuevoContexto(cs);
            var servicio = new PinTransaccionalService(lectura);
            Assert.Equal(ResultadoPin.Correcto, (await servicio.VerificarAsync(id, opciones[ganador.Indice])).Resultado);
            Assert.Equal(ResultadoPin.Incorrecto, (await servicio.VerificarAsync(id, opciones[1 - ganador.Indice])).Resultado);
            Assert.Equal(1, await lectura.Auditorias.CountAsync(a => a.IdUsuario == id && a.Accion == "PIN_CAMBIADO"));
            Assert.Equal((byte?)6, (await lectura.PinesTransaccionales.AsNoTracking().SingleAsync(p => p.IdUsuario == id)).LongitudPin);
        }
        finally { await LimpiarAsync(cs, c); }
    }

    [Theory]
    [InlineData("25801")]
    [InlineData("2580147")]
    public async Task CambioLegado_CincoIncorrectosBloquean_YNoPermitenMigrarConElCorrecto(string pinActual)
    {
        if (!TryConnString(out var cs)) return;
        var c = new Sembrados();
        try
        {
            var id = await SembrarConPinLegadoAsync(cs, c, pinActual);
            await using var ctx = NuevoContexto(cs);
            var servicio = new PinTransaccionalService(ctx);
            var incorrecto = new CambiarPinRequest
            {
                PinActual = new string('8', pinActual.Length),
                PinNuevo = PinNuevo, ConfirmacionPinNuevo = PinNuevo,
            };
            for (var i = 1; i <= PinTransaccionalService.MaxIntentos; i++)
            {
                var resultado = await servicio.CambiarAsync(id, incorrecto);
                Assert.Equal(i == PinTransaccionalService.MaxIntentos ? ResultadoPin.Bloqueado : ResultadoPin.Incorrecto,
                    resultado.Resultado);
            }
            Assert.Equal(ResultadoPin.Bloqueado, (await servicio.CambiarAsync(id,
                new CambiarPinRequest { PinActual = pinActual, PinNuevo = PinNuevo, ConfirmacionPinNuevo = PinNuevo })).Resultado);
            Assert.Equal(ResultadoPin.Bloqueado, (await servicio.VerificarAsync(id, PinOk)).Resultado);
            var fila = await ctx.PinesTransaccionales.AsNoTracking().SingleAsync(p => p.IdUsuario == id);
            Assert.Null(fila.LongitudPin);
            Assert.Equal(PinTransaccionalService.MaxIntentos, fila.IntentosFallidos);
            Assert.True(fila.Bloqueado);
            Assert.True(BCrypt.Net.BCrypt.Verify(pinActual, fila.PinHash));
            Assert.False(await ctx.Auditorias.AnyAsync(a => a.IdUsuario == id && a.Accion == "PIN_CAMBIADO"));
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
                new CambiarPinRequest { PinActual = "111112", PinNuevo = PinNuevo, ConfirmacionPinNuevo = PinNuevo });
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
                for (var i = 0; i < PinTransaccionalService.MaxIntentos; i++) await svc.VerificarAsync(id, "111112");
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
                Assert.False((await svc.ObtenerEstadoAsync(id)).RequiereActualizacion);
                Assert.Null((await ctx.PinesTransaccionales.AsNoTracking().SingleAsync(p => p.IdUsuario == id)).LongitudPin);
                await svc.CrearAsync(id, new CrearPinRequest { ClaveActual = Clave, Pin = PinNuevo, ConfirmacionPin = PinNuevo });
                Assert.Equal(ResultadoPin.Correcto, (await svc.VerificarAsync(id, PinNuevo)).Resultado);
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

    private static async Task<long> SembrarConPinLegadoAsync(string cs, Sembrados c,
        string pinLegado = PinLegado, int? longitud = null)
    {
        var id = await SembrarUsuarioAsync(cs, c);
        await using var ctx = NuevoContexto(cs);
        ctx.PinesTransaccionales.Add(new PinTransaccional
        {
            IdUsuario = id,
            PinHash = BCrypt.Net.BCrypt.HashPassword(pinLegado),
            LongitudPin = longitud.HasValue ? (byte)longitud.Value : null,
            FechaCreacion = DateTime.UtcNow,
            Estado = EstadosPin.Activo,
        });
        await ctx.SaveChangesAsync();
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
