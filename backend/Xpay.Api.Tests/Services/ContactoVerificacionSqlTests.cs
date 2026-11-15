using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// Verificación de contactos contra SQL Server real (migración 050). Mismo
// patrón que PinTransaccionalSqlTests: se salta en local sin cadena de
// conexión y es obligatorio en CI. Los canales son falsos: capturan el código
// para que la prueba lo escriba, sin red.
[Collection("SqlIntegration")]
public sealed class ContactoVerificacionSqlTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";

    [Fact]
    public async Task Email_CodigoCorrecto_Verifica_YEsDeUnSoloUso()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var id = await SembrarUsuarioAsync(cs, s);
            var (svc, canales, _) = Servicio(cs);

            var r = await svc.SolicitarAsync(id, CanalContacto.Email, false, "10.0.0.1");
            Assert.Equal(ResultadoSolicitudCodigo.Enviado, r.Resultado);
            var codigo = canales.Email.UltimoCodigo!;

            Assert.Equal(ResultadoConfirmacionCodigo.Verificado, (await Servicio(cs).svc.ConfirmarAsync(id, CanalContacto.Email, codigo)).Resultado);
            Assert.Equal(ResultadoConfirmacionCodigo.YaVerificado, (await Servicio(cs).svc.ConfirmarAsync(id, CanalContacto.Email, codigo)).Resultado);

            await using var v = NuevoContexto(cs);
            var u = await v.Usuarios.AsNoTracking().SingleAsync(x => x.IdUsuario == id);
            Assert.True(u.EmailVerificado);
            Assert.False(u.CelularVerificado);
            var fila = await v.ContactoCodigosVerificacion.AsNoTracking().SingleAsync(c => c.IdUsuario == id);
            Assert.NotNull(fila.FechaUso);
            Assert.NotEqual(codigo, Convert.ToHexString(fila.CodigoHash));
            Assert.Equal(1, await v.Auditorias.CountAsync(a => a.IdUsuario == id && a.Accion == "EMAIL_VERIFICADO"));
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Fact]
    public async Task CincoIntentosFallidos_AgotanElCodigo_YNiElCorrectoPasa()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var id = await SembrarUsuarioAsync(cs, s);
            var (svc, canales, _) = Servicio(cs);
            await svc.SolicitarAsync(id, CanalContacto.Email, false, null);
            var correcto = canales.Email.UltimoCodigo!;
            var errado = correcto == "000000" ? "111111" : "000000";

            for (var i = 1; i < ContactoVerificacionService.MaxIntentos; i++)
            {
                var r = await Servicio(cs).svc.ConfirmarAsync(id, CanalContacto.Email, errado);
                Assert.Equal(ResultadoConfirmacionCodigo.Incorrecto, r.Resultado);
                Assert.Equal(ContactoVerificacionService.MaxIntentos - i, r.IntentosRestantes);
            }
            Assert.Equal(ResultadoConfirmacionCodigo.Agotado, (await Servicio(cs).svc.ConfirmarAsync(id, CanalContacto.Email, errado)).Resultado);
            Assert.Equal(ResultadoConfirmacionCodigo.SinCodigoVigente, (await Servicio(cs).svc.ConfirmarAsync(id, CanalContacto.Email, correcto)).Resultado);

            await using var v = NuevoContexto(cs);
            Assert.False((await v.Usuarios.AsNoTracking().SingleAsync(x => x.IdUsuario == id)).EmailVerificado);
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Fact]
    public async Task CodigoVencido_NoVerifica()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var id = await SembrarUsuarioAsync(cs, s);
            var reloj = new RelojMovil(DateTimeOffset.UtcNow);
            var (svc, canales, _) = Servicio(cs, reloj);
            await svc.SolicitarAsync(id, CanalContacto.Email, false, null);

            reloj.Avanzar(TimeSpan.FromMinutes(ContactoVerificacionService.VigenciaMinutos + 1));
            var r = await Servicio(cs, reloj).svc.ConfirmarAsync(id, CanalContacto.Email, canales.Email.UltimoCodigo);
            Assert.Equal(ResultadoConfirmacionCodigo.SinCodigoVigente, r.Resultado);
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Fact]
    public async Task Reenvio_RespetaEnfriamiento_YCupoDiario_YSoloSirveElUltimo()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var id = await SembrarUsuarioAsync(cs, s);
            var reloj = new RelojMovil(DateTimeOffset.UtcNow);
            var (svc, canales, _) = Servicio(cs, reloj);

            Assert.Equal(ResultadoSolicitudCodigo.Enviado, (await svc.SolicitarAsync(id, CanalContacto.Email, false, null)).Resultado);
            var primero = canales.Email.UltimoCodigo!;
            var espera = await Servicio(cs, reloj).svc.SolicitarAsync(id, CanalContacto.Email, false, null);
            Assert.Equal(ResultadoSolicitudCodigo.Espera, espera.Resultado);
            Assert.InRange(espera.SegundosEspera!.Value, 1, ContactoVerificacionService.EnfriamientoSegundos);

            for (var i = 1; i < ContactoVerificacionService.MaxEnviosUsuarioDia; i++)
            {
                reloj.Avanzar(TimeSpan.FromSeconds(ContactoVerificacionService.EnfriamientoSegundos + 1));
                Assert.Equal(ResultadoSolicitudCodigo.Enviado, (await svc.SolicitarAsync(id, CanalContacto.Email, false, null)).Resultado);
            }
            reloj.Avanzar(TimeSpan.FromSeconds(ContactoVerificacionService.EnfriamientoSegundos + 1));
            Assert.Equal(ResultadoSolicitudCodigo.LimiteDiario, (await svc.SolicitarAsync(id, CanalContacto.Email, false, null)).Resultado);

            // El primer código quedó reemplazado; solo el último es vigente.
            var ultimo = canales.Email.UltimoCodigo!;
            if (primero != ultimo)
                Assert.Equal(ResultadoConfirmacionCodigo.Incorrecto, (await Servicio(cs, reloj).svc.ConfirmarAsync(id, CanalContacto.Email, primero)).Resultado);
            Assert.Equal(ResultadoConfirmacionCodigo.Verificado, (await Servicio(cs, reloj).svc.ConfirmarAsync(id, CanalContacto.Email, ultimo)).Resultado);
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Fact]
    public async Task CambiarElCorreo_RevocaLaVerificacion_EInvalidaElCodigoPendiente()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var id = await SembrarUsuarioAsync(cs, s);
            var (svc, canales, _) = Servicio(cs);
            await svc.SolicitarAsync(id, CanalContacto.Email, false, null);
            await Servicio(cs).svc.ConfirmarAsync(id, CanalContacto.Email, canales.Email.UltimoCodigo);

            // Nuevo código pendiente y, antes de usarlo, cambio de correo en Perfil.
            await using (var ctx = NuevoContexto(cs))
            {
                var u = await ctx.Usuarios.SingleAsync(x => x.IdUsuario == id);
                u.EmailVerificado = false; // simula la verificación pendiente del nuevo dato
                await ctx.SaveChangesAsync();
            }
            var reloj = new RelojMovil(DateTimeOffset.UtcNow.AddMinutes(2));
            await Servicio(cs, reloj).svc.SolicitarAsync(id, CanalContacto.Email, false, null);
            var pendiente = canales.Email.UltimoCodigo;

            await using (var ctx = NuevoContexto(cs))
            {
                var u = await ctx.Usuarios.SingleAsync(x => x.IdUsuario == id);
                u.EmailVerificado = true;
                await ctx.SaveChangesAsync();
                await new PerfilService(ctx).ActualizarAsync(id, u.IdPersona, new ActualizarMiPerfilRequest(null, $"otro.{id}@correo.com", null, null, null));
            }

            await using var v = NuevoContexto(cs);
            Assert.False((await v.Usuarios.AsNoTracking().SingleAsync(x => x.IdUsuario == id)).EmailVerificado);
            Assert.False(await v.ContactoCodigosVerificacion.AnyAsync(c => c.IdUsuario == id && c.FechaUso == null && c.FechaInvalidacion == null));
            Assert.Equal(ResultadoConfirmacionCodigo.SinCodigoVigente, (await Servicio(cs, reloj).svc.ConfirmarAsync(id, CanalContacto.Email, pendiente)).Resultado);
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Fact]
    public async Task CodigoAtadoAlDestino_SiElDatoCambiaPorOtraVia_NoVerifica()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var id = await SembrarUsuarioAsync(cs, s);
            var (svc, canales, _) = Servicio(cs);
            await svc.SolicitarAsync(id, CanalContacto.Email, false, null);

            await using (var ctx = NuevoContexto(cs))
            {
                var idPersona = await ctx.Usuarios.Where(x => x.IdUsuario == id).Select(x => x.IdPersona).SingleAsync();
                await ctx.Personas.Where(p => p.IdPersona == idPersona)
                    .ExecuteUpdateAsync(x => x.SetProperty(p => p.Email, $"cambio.{id}@correo.com"));
            }

            var r = await Servicio(cs).svc.ConfirmarAsync(id, CanalContacto.Email, canales.Email.UltimoCodigo);
            Assert.Equal(ResultadoConfirmacionCodigo.ContactoCambio, r.Resultado);
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Fact]
    public async Task ConfirmarCodigo_YEditarPerfilConcurrentemente_NoVerificaElDatoNuevo()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var id = await SembrarUsuarioAsync(cs, s);
            await using var solicitud = NuevoContexto(cs);
            var email = new CanalFalso(CanalContacto.Email);
            var servicio = new ContactoVerificacionService(solicitud, [email], TimeProvider.System);
            Assert.Equal(ResultadoSolicitudCodigo.Enviado,
                (await servicio.SolicitarAsync(id, CanalContacto.Email, false, null)).Resultado);

            var pausa = new PausaConsultaSql(sql =>
                sql.Contains("JOIN [personas]", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("email_verificado", StringComparison.OrdinalIgnoreCase));
            await using var confirmacion = NuevoContexto(cs, pausa);
            await using var edicion = NuevoContexto(cs);
            var nuevaDireccion = $"edicion.{id}@correo.com";
            var confirmar = new ContactoVerificacionService(confirmacion, [email], TimeProvider.System)
                .ConfirmarAsync(id, CanalContacto.Email, email.UltimoCodigo);
            Task<MiPerfilResponseDto>? editar = null;
            try
            {
                await pausa.Iniciada.Task.WaitAsync(TimeSpan.FromSeconds(10));
                editar = new PerfilService(edicion).ActualizarAsync(id, s.Personas.Single(),
                    new ActualizarMiPerfilRequest(null, nuevaDireccion, null, null, null));
                await ComprobarEnEsperaAsync(editar);
            }
            finally
            {
                pausa.Continuar.TrySetResult();
                await confirmar;
                if (editar is not null) await editar;
            }

            Assert.Equal(ResultadoConfirmacionCodigo.Verificado, (await confirmar).Resultado);
            await using var verificacion = NuevoContexto(cs);
            Assert.Equal(nuevaDireccion,
                (await verificacion.Personas.AsNoTracking().SingleAsync(p => p.IdPersona == s.Personas.Single())).Email);
            Assert.False((await verificacion.Usuarios.AsNoTracking().SingleAsync(u => u.IdUsuario == id)).EmailVerificado);
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Fact]
    public async Task CuentasConcurrentes_SoloUnaPuedeConfirmarElMismoDestino()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var destino = $"compartido.{Guid.NewGuid():N}@correo.com";
            var id1 = await SembrarUsuarioAsync(cs, s, destino);
            var id2 = await SembrarUsuarioAsync(cs, s, destino.ToUpperInvariant());
            var email1 = new CanalFalso(CanalContacto.Email);
            var email2 = new CanalFalso(CanalContacto.Email);
            await using (var solicitudes = NuevoContexto(cs))
            {
                Assert.Equal(ResultadoSolicitudCodigo.Enviado, (await new ContactoVerificacionService(solicitudes, [email1], TimeProvider.System)
                    .SolicitarAsync(id1, CanalContacto.Email, false, null)).Resultado);
                Assert.Equal(ResultadoSolicitudCodigo.Enviado, (await new ContactoVerificacionService(solicitudes, [email2], TimeProvider.System)
                    .SolicitarAsync(id2, CanalContacto.Email, false, null)).Resultado);
            }

            var pausa = new PausaConsultaSql(sql =>
                sql.Contains("JOIN [personas]", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("email_verificado", StringComparison.OrdinalIgnoreCase));
            await using var ctx1 = NuevoContexto(cs, pausa);
            await using var ctx2 = NuevoContexto(cs);
            var confirmar1 = new ContactoVerificacionService(ctx1, [email1], TimeProvider.System)
                .ConfirmarAsync(id1, CanalContacto.Email, email1.UltimoCodigo);
            Task<ConfirmacionCodigoResultado>? confirmar2 = null;
            try
            {
                await pausa.Iniciada.Task.WaitAsync(TimeSpan.FromSeconds(10));
                confirmar2 = new ContactoVerificacionService(ctx2, [email2], TimeProvider.System)
                    .ConfirmarAsync(id2, CanalContacto.Email, email2.UltimoCodigo);
                await ComprobarEnEsperaAsync(confirmar2);
            }
            finally
            {
                pausa.Continuar.TrySetResult();
                await confirmar1;
                if (confirmar2 is not null) await confirmar2;
            }

            Assert.Equal(ResultadoConfirmacionCodigo.Verificado, (await confirmar1).Resultado);
            Assert.Equal(ResultadoConfirmacionCodigo.ContactoEnUsoPorOtro, (await confirmar2!).Resultado);
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Fact]
    public async Task EnviosConcurrentes_RespetanCupoDiarioPorDestino()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var destino = $"compartido.{Guid.NewGuid():N}@correo.com";
            var id1 = await SembrarUsuarioAsync(cs, s, destino);
            var id2 = await SembrarUsuarioAsync(cs, s, destino.ToUpperInvariant());
            var reloj = new RelojMovil(DateTimeOffset.UtcNow);
            await using (var semillas = NuevoContexto(cs))
            {
                for (var i = 0; i < ContactoVerificacionService.MaxEnviosDestinoDia - 1; i++)
                {
                    semillas.ContactoCodigosVerificacion.Add(new ContactoCodigoVerificacion
                    {
                        IdUsuario = i % 2 == 0 ? id1 : id2,
                        Canal = CanalContacto.Email,
                        Destino = destino,
                        CodigoSal = new byte[16],
                        CodigoHash = new byte[32],
                        FechaCreacion = reloj.GetUtcNow().UtcDateTime.AddMinutes(-30),
                        FechaExpiracion = reloj.GetUtcNow().UtcDateTime.AddMinutes(-20),
                    });
                }
                await semillas.SaveChangesAsync();
            }

            var pausa = new PausaConsultaSql(sql =>
                sql.Contains("COUNT(*)", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("[destino]", StringComparison.OrdinalIgnoreCase));
            await using var ctx1 = NuevoContexto(cs, pausa);
            await using var ctx2 = NuevoContexto(cs);
            var email1 = new CanalFalso(CanalContacto.Email);
            var email2 = new CanalFalso(CanalContacto.Email);
            var enviar1 = new ContactoVerificacionService(ctx1, [email1], reloj)
                .SolicitarAsync(id1, CanalContacto.Email, false, null);
            Task<SolicitudCodigoResultado>? enviar2 = null;
            try
            {
                await pausa.Iniciada.Task.WaitAsync(TimeSpan.FromSeconds(10));
                enviar2 = new ContactoVerificacionService(ctx2, [email2], reloj)
                    .SolicitarAsync(id2, CanalContacto.Email, false, null);
                await ComprobarEnEsperaAsync(enviar2);
            }
            finally
            {
                pausa.Continuar.TrySetResult();
                await enviar1;
                if (enviar2 is not null) await enviar2;
            }

            Assert.Equal(ResultadoSolicitudCodigo.Enviado, (await enviar1).Resultado);
            Assert.Equal(ResultadoSolicitudCodigo.LimiteDiario, (await enviar2!).Resultado);
            Assert.Null(email2.UltimoCodigo);
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Fact]
    public async Task WhatsApp_SinConsentimiento_NoEnvia_YConConsentimientoLoRegistraYVerifica()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var id = await SembrarUsuarioAsync(cs, s);
            var (svc, canales, _) = Servicio(cs);

            Assert.Equal(ResultadoSolicitudCodigo.ConsentimientoRequerido, (await svc.SolicitarAsync(id, CanalContacto.WhatsApp, false, null)).Resultado);
            Assert.Null(canales.WhatsApp.UltimoCodigo);

            var r = await Servicio(cs).svc.SolicitarAsync(id, CanalContacto.WhatsApp, true, null);
            Assert.Equal(ResultadoSolicitudCodigo.Enviado, r.Resultado);
            Assert.StartsWith("57", canales.WhatsApp.UltimoDestino);
            Assert.Equal(ResultadoConfirmacionCodigo.Verificado,
                (await Servicio(cs).svc.ConfirmarAsync(id, CanalContacto.WhatsApp, canales.WhatsApp.UltimoCodigo)).Resultado);

            await using var v = NuevoContexto(cs);
            var consentimientos = await v.ConsentimientosComunicacion.AsNoTracking().Where(c => c.IdUsuario == id).ToListAsync();
            var unico = Assert.Single(consentimientos);
            Assert.Equal((CanalContacto.WhatsApp, FinalidadComunicacion.Servicio, true), (unico.Canal, unico.Finalidad, unico.Otorgado));
            // Verificar no otorga publicidad.
            Assert.False(await Servicio(cs).svc.ConsentimientoVigenteAsync(id, CanalContacto.WhatsApp, FinalidadComunicacion.Publicidad));
            Assert.True((await v.Usuarios.AsNoTracking().SingleAsync(x => x.IdUsuario == id)).CelularVerificado);
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Fact]
    public async Task WhatsApp_ConservaNuevoConsentimiento_SiElReenvioDebeEsperar()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var id = await SembrarUsuarioAsync(cs, s);
            var reloj = new RelojMovil(DateTimeOffset.UtcNow);
            var (servicio, _, _) = Servicio(cs, reloj);
            Assert.Equal(ResultadoSolicitudCodigo.Enviado,
                (await servicio.SolicitarAsync(id, CanalContacto.WhatsApp, true, null)).Resultado);
            await Servicio(cs).svc.RegistrarConsentimientoAsync(id, CanalContacto.WhatsApp,
                FinalidadComunicacion.Servicio, false, "PERFIL", null);

            Assert.Equal(ResultadoSolicitudCodigo.Espera,
                (await Servicio(cs).svc.SolicitarAsync(id, CanalContacto.WhatsApp, true, null)).Resultado);
            Assert.True(await Servicio(cs).svc.ConsentimientoVigenteAsync(id, CanalContacto.WhatsApp,
                FinalidadComunicacion.Servicio));
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Fact]
    public async Task EnvioFallido_InvalidaElCodigo_YNoVerifica()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var id = await SembrarUsuarioAsync(cs, s);
            var (svc, canales, _) = Servicio(cs);
            canales.Email.Falla = true;

            Assert.Equal(ResultadoSolicitudCodigo.ErrorEnvio, (await svc.SolicitarAsync(id, CanalContacto.Email, false, null)).Resultado);
            await using var v = NuevoContexto(cs);
            var fila = await v.ContactoCodigosVerificacion.AsNoTracking().SingleAsync(c => c.IdUsuario == id);
            Assert.Equal(ContactoVerificacionService.MotivoErrorEnvio, fila.MotivoInvalidacion);
            Assert.Equal(ResultadoConfirmacionCodigo.SinCodigoVigente,
                (await Servicio(cs).svc.ConfirmarAsync(id, CanalContacto.Email, canales.Email.UltimoCodigo)).Resultado);
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Fact]
    public async Task CanalNoConfigurado_NoCreaCodigo()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var id = await SembrarUsuarioAsync(cs, s);
            await using var ctx = NuevoContexto(cs);
            var svc = new ContactoVerificacionService(ctx,
                new ICanalCodigoContacto[] { new CanalCodigoNoConfigurado(CanalContacto.Email), new CanalCodigoNoConfigurado(CanalContacto.WhatsApp) },
                TimeProvider.System);
            Assert.Equal(ResultadoSolicitudCodigo.CanalNoDisponible, (await svc.SolicitarAsync(id, CanalContacto.Email, false, null)).Resultado);
            Assert.False(await ctx.ContactoCodigosVerificacion.AnyAsync(c => c.IdUsuario == id));
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Fact]
    public async Task ContactoYaVerificadoEnOtraCuenta_NoSeEnvia()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var correo = $"compartido.{Guid.NewGuid():N}@correo.com";
            var otro = await SembrarUsuarioAsync(cs, s, correo);
            await using (var ctx = NuevoContexto(cs))
                await ctx.Usuarios.Where(u => u.IdUsuario == otro).ExecuteUpdateAsync(x => x.SetProperty(u => u.EmailVerificado, true));
            var id = await SembrarUsuarioAsync(cs, s, correo);

            var (svc, canales, _) = Servicio(cs);
            Assert.Equal(ResultadoSolicitudCodigo.ContactoEnUsoPorOtro, (await svc.SolicitarAsync(id, CanalContacto.Email, false, null)).Resultado);
            Assert.Null(canales.Email.UltimoCodigo);
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Fact]
    public async Task Preferencias_HistorialSoloCuandoCambia()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var id = await SembrarUsuarioAsync(cs, s);
            var svc = Servicio(cs).svc;
            await svc.RegistrarConsentimientoAsync(id, CanalContacto.Email, FinalidadComunicacion.Publicidad, true, "PERFIL", null);
            await svc.RegistrarConsentimientoAsync(id, CanalContacto.Email, FinalidadComunicacion.Publicidad, true, "PERFIL", null);
            await svc.RegistrarConsentimientoAsync(id, CanalContacto.Email, FinalidadComunicacion.Publicidad, false, "PERFIL", null);

            await using var v = NuevoContexto(cs);
            Assert.Equal(2, await v.ConsentimientosComunicacion.CountAsync(c => c.IdUsuario == id));
            Assert.False(await Servicio(cs).svc.ConsentimientoVigenteAsync(id, CanalContacto.Email, FinalidadComunicacion.Publicidad));
        }
        finally { await LimpiarAsync(cs, s); }
    }

    // ── Infraestructura ─────────────────────────────────────────────────

    private static async Task ComprobarEnEsperaAsync(Task operacion) =>
        Assert.NotSame(operacion, await Task.WhenAny(operacion, Task.Delay(TimeSpan.FromMilliseconds(200))));

    private sealed class PausaConsultaSql(Func<string, bool> coincide) : DbCommandInterceptor
    {
        public TaskCompletionSource Iniciada { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continuar { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (coincide(command.CommandText) && Iniciada.TrySetResult())
                await Continuar.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

    private sealed class CanalFalso(string canal) : ICanalCodigoContacto
    {
        public string Canal => canal;
        public bool Disponible => true;
        public bool Falla { get; set; }
        public string? UltimoCodigo { get; private set; }
        public string? UltimoDestino { get; private set; }

        public Task EnviarCodigoAsync(string destino, string codigo, int minutosVigencia, CancellationToken ct = default)
        {
            UltimoCodigo = codigo;
            UltimoDestino = destino;
            return Falla ? Task.FromException(new CanalContactoException("FALLA_PRUEBA")) : Task.CompletedTask;
        }
    }

    private sealed class Canales
    {
        public CanalFalso Email { get; } = new(CanalContacto.Email);
        public CanalFalso WhatsApp { get; } = new(CanalContacto.WhatsApp);
    }

    private sealed class RelojMovil(DateTimeOffset inicio) : TimeProvider
    {
        private DateTimeOffset _ahora = inicio;
        public void Avanzar(TimeSpan t) => _ahora = _ahora.Add(t);
        public override DateTimeOffset GetUtcNow() => _ahora;
    }

    // Canales y reloj compartidos entre llamadas de la misma prueba; contexto
    // nuevo en cada llamada (como requests distintos).
    private Canales _canales = new();
    private RelojMovil? _reloj;

    private (ContactoVerificacionService svc, Canales canales, XpayDbContext ctx) Servicio(string cs, RelojMovil? reloj = null)
    {
        _reloj = reloj ?? _reloj;
        var ctx = NuevoContexto(cs);
        var svc = new ContactoVerificacionService(ctx,
            new ICanalCodigoContacto[] { _canales.Email, _canales.WhatsApp },
            (TimeProvider?)_reloj ?? TimeProvider.System);
        return (svc, _canales, ctx);
    }

    private static async Task<long> SembrarUsuarioAsync(string cs, Sembrados s, string? email = null)
    {
        await using var ctx = NuevoContexto(cs);
        var idUnidad = await ctx.Database
            .SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL")
            .SingleAsync();
        var sufijo = Guid.NewGuid().ToString("N")[..12];
        var ahora  = DateTime.UtcNow;
        // Celular único por prueba (3 + 9 dígitos) para no chocar con otras cuentas verificadas.
        var celular = "3" + (Math.Abs(BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 0)) % 1_000_000_000).ToString("D9");

        var persona = new Persona
        {
            IdUnidadNegocio = idUnidad, TipoDocumento = "CC", NumeroDocumento = $"CTV{sufijo}",
            PrimerNombre = "Contacto", PrimerApellido = "Sintetico", Celular = celular,
            Email = email ?? $"ctv.{sufijo}@correo.com",
            Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
        };
        ctx.Personas.Add(persona);
        await ctx.SaveChangesAsync();
        s.Personas.Add(persona.IdPersona);

        var usuario = new Usuario
        {
            IdPersona = persona.IdPersona, NombreUsuario = $"ctv_{sufijo}",
            PasswordHash = "no-usado", Estado = EstadosUsuario.Activo, FechaCreacion = ahora,
        };
        ctx.Usuarios.Add(usuario);
        await ctx.SaveChangesAsync();
        s.Usuarios.Add(usuario.IdUsuario);
        return usuario.IdUsuario;
    }

    private sealed class Sembrados
    {
        public List<long> Personas { get; } = new();
        public List<long> Usuarios { get; } = new();
    }

    private static async Task LimpiarAsync(string cs, Sembrados s)
    {
        try
        {
            await using var ctx = NuevoContexto(cs);
            await ctx.Auditorias.Where(a => a.IdUsuario != null && s.Usuarios.Contains(a.IdUsuario.Value)).ExecuteDeleteAsync();
            await ctx.ContactoCodigosVerificacion.Where(c => s.Usuarios.Contains(c.IdUsuario)).ExecuteDeleteAsync();
            await ctx.ConsentimientosComunicacion.Where(c => s.Usuarios.Contains(c.IdUsuario)).ExecuteDeleteAsync();
            await ctx.Usuarios.Where(u => s.Usuarios.Contains(u.IdUsuario)).ExecuteDeleteAsync();
            await ctx.Personas.Where(p => s.Personas.Contains(p.IdPersona)).ExecuteDeleteAsync();
        }
        catch { /* limpieza best-effort, como en el resto de pruebas SQL */ }
    }

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL de verificación de contactos.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs, params IInterceptor[] interceptores)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).AddInterceptors(interceptores).Options);
}
