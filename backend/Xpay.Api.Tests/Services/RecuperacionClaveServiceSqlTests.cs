using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xpay.Api.Authorization;
using Xpay.Api.Common;
using Xpay.Api.Controllers;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xpay.Api.Tests.Common;
using Xunit;

namespace Xpay.Api.Tests.Services;

// P0-B2 — RecuperacionClaveService contra SQL Server local: elegibilidad por
// estado (ACTIVO y bloqueo automático exacto), email solo desde
// personas.email, respuesta sin enumeración, token de un solo uso, política
// de clave, rollback y revocación por version_seguridad. Ambiente QA (sender
// espía, sin envío real). Datos sintéticos propios y limpieza best-effort.
[Collection("SqlIntegration")]
public sealed class RecuperacionClaveServiceSqlTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";
    private const string ClaveActual   = "Actual#2026x";
    private const string ClaveNueva    = "Recupera#2026x";
    private const string MotivoManual  = "Bloqueo administrativo.";

    // ── Solicitud ───────────────────────────────────────────────────────

    [Fact]
    public async Task Solicitud_Activo_EmiteTokenYEnviaAlEmailDePersona_TrasCommit()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Sembrado>();
        try
        {
            var s = await SembrarAsync(cs, creados, EstadosUsuario.Activo);
            await using var e = new Entorno(cs);
            await e.Servicio.SolicitarAsync("  " + s.Usuario.ToUpperInvariant() + " ", "203.0.113.7");

            var enviado = Assert.Single(e.Sender.Enviados);
            Assert.Equal(s.Email, enviado.Destinatario);
            Assert.False(Assert.Single(e.Sender.TransaccionAbierta));
            var token = TokenDe(enviado);

            await using var v = NuevoContexto(cs);
            var t = await v.UsuarioTokens.AsNoTracking().SingleAsync(x => x.IdUsuario == s.IdUsuario);
            Assert.Equal(PropositoTokenUsuario.RecuperarClave, t.Proposito);
            Assert.Equal(s.Email, t.EmailDestino);
            Assert.Equal("203.0.113.7", t.IpSolicitud);
            Assert.Null(t.FechaUso);
            Assert.Equal(SHA256.HashData(DesdeBase64Url(token)), t.TokenHash);
            Assert.Equal(1, await v.Auditorias.CountAsync(x => x.IdUsuario == s.IdUsuario && x.Accion == "SOLICITAR_RECUPERACION_CLAVE"));
            Assert.DoesNotContain(e.Logger.Mensajes, m => m.Contains(token, StringComparison.Ordinal)
                || m.Contains(s.Email, StringComparison.OrdinalIgnoreCase));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Theory]
    [InlineData("ACTIVO_SIN_EMAIL_VERIFICADO")]
    [InlineData("BLOQUEO_AUTOMATICO")]
    public async Task Solicitud_CasoElegible_EmiteToken(string caso)
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Sembrado>();
        try
        {
            var s = await SembrarCasoAsync(cs, creados, caso);
            await using var e = new Entorno(cs);
            await e.Servicio.SolicitarAsync(s.Usuario, null);

            Assert.Equal(s.Email, Assert.Single(e.Sender.Enviados).Destinatario);
            Assert.Equal(1, await TokensAsync(cs, s.IdUsuario));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Theory]
    [InlineData("INACTIVO")]
    [InlineData("PENDIENTE_VERIFICACION")]
    [InlineData("BLOQUEO_MANUAL")]
    [InlineData("BLOQUEO_SIN_FECHA")]
    [InlineData("BLOQUEO_MOTIVO_VARIANTE")]
    [InlineData("SIN_EMAIL")]
    [InlineData("EMAIL_INVALIDO")]
    public async Task Solicitud_CasoNoElegible_NoEmiteNiEnvia(string caso)
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Sembrado>();
        try
        {
            var s = await SembrarCasoAsync(cs, creados, caso);
            await using var e = new Entorno(cs);
            await e.Servicio.SolicitarAsync(s.Usuario, null);

            Assert.Empty(e.Sender.Enviados);
            Assert.Equal(0, await TokensAsync(cs, s.IdUsuario));
            await using var v = NuevoContexto(cs);
            Assert.Equal(0, await v.Auditorias.CountAsync(x => x.IdUsuario == s.IdUsuario));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("p0r_inexistente_sin_fila")]
    public async Task Solicitud_UsuarioInexistenteOVacio_NoHaceNada_NiLanza(string? usuario)
    {
        if (!TryConnString(out var cs)) return;
        await using var e = new Entorno(cs);
        await e.Servicio.SolicitarAsync(usuario, null);
        Assert.Empty(e.Sender.Enviados);
    }

    [Fact]
    public async Task Solicitud_LimitePorUsuario_NoEmiteMasDeTresPorVentana()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Sembrado>();
        try
        {
            var s = await SembrarAsync(cs, creados, EstadosUsuario.Activo);
            var enviados = 0;
            for (var i = 0; i < RecuperacionClaveService.MaxSolicitudesPorVentana + 1; i++)
            {
                await using var e = new Entorno(cs);
                await e.Servicio.SolicitarAsync(s.Usuario, null);
                enviados += e.Sender.Enviados.Count;
            }

            Assert.Equal(RecuperacionClaveService.MaxSolicitudesPorVentana, enviados);
            Assert.Equal(RecuperacionClaveService.MaxSolicitudesPorVentana, await TokensAsync(cs, s.IdUsuario));
            await using var v = NuevoContexto(cs);
            Assert.Equal(1, await v.UsuarioTokens.CountAsync(t => t.IdUsuario == s.IdUsuario
                && t.FechaUso == null && t.FechaInvalidacion == null));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Solicitud_FalloDelEnvio_NoSePropaga_YSoloRegistraElTipoDeError()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Sembrado>();
        try
        {
            var s = await SembrarAsync(cs, creados, EstadosUsuario.Activo);
            await using var e = new Entorno(cs);
            e.Sender.Fallar = true;
            await e.Servicio.SolicitarAsync(s.Usuario, null);

            Assert.Equal(1, await TokensAsync(cs, s.IdUsuario));
            var mensaje = Assert.Single(e.Logger.Mensajes);
            Assert.Contains(nameof(InvalidOperationException), mensaje);
            Assert.DoesNotContain(s.Email, mensaje, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(s.Usuario, mensaje, StringComparison.OrdinalIgnoreCase);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task NoEnumeracion_RespuestaPublicaIdentica_ExistaONoYSeaONoElegible()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Sembrado>();
        try
        {
            var activo   = await SembrarAsync(cs, creados, EstadosUsuario.Activo);
            var inactivo = await SembrarAsync(cs, creados, EstadosUsuario.Inactivo);
            var solicitudes = new SolicitarRecuperacionClaveRequest?[]
            {
                new(activo.Usuario), new(inactivo.Usuario),
                new("p0r_inexistente_" + Guid.NewGuid().ToString("N")[..12]), new(null), null,
            };

            var respuestas = new List<string>();
            foreach (var solicitud in solicitudes)
            {
                await using var e = new Entorno(cs);
                var r = Assert.IsType<ObjectResult>(await e.Controller().SolicitarRecuperacion(solicitud, default));
                respuestas.Add($"{r.StatusCode}|{Json(r.Value)}");
            }

            var unica = Assert.Single(respuestas.Distinct());
            Assert.StartsWith("202|", unica);
            Assert.Contains(RecuperacionClaveController.MensajeSolicitudRecibida, unica);
            Assert.DoesNotContain(activo.Email, unica, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, await TokensAsync(cs, activo.IdUsuario));
            Assert.Equal(0, await TokensAsync(cs, inactivo.IdUsuario));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── Restablecimiento ────────────────────────────────────────────────

    [Fact]
    public async Task Reset_Activo_CambiaClave_RevocaJwt_SinTocarRolesNiUnidad()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Sembrado>();
        try
        {
            var s = await SembrarAsync(cs, creados, EstadosUsuario.Activo, requiereCambioClave: true);
            var token = await SolicitarYObtenerTokenAsync(cs, s);
            var antes = await FotoAsync(cs, s);

            await using (var e = new Entorno(cs))
                Assert.True(await e.Servicio.RestablecerAsync(Req(token)));

            await using var v = NuevoContexto(cs);
            var u = await v.Usuarios.AsNoTracking().SingleAsync(x => x.IdUsuario == s.IdUsuario);
            Assert.True(BCrypt.Net.BCrypt.Verify(ClaveNueva, u.PasswordHash));
            Assert.False(BCrypt.Net.BCrypt.Verify(ClaveActual, u.PasswordHash));
            Assert.False(u.RequiereCambioClave);
            Assert.Equal(EstadosUsuario.Activo, u.Estado);
            Assert.Equal(antes.Version + 1, u.VersionSeguridad);
            Assert.False(await ValidadorVersionSeguridad.EsVigenteAsync(v, s.IdUsuario, antes.Version, default));
            Assert.True(await ValidadorVersionSeguridad.EsVigenteAsync(v, s.IdUsuario, u.VersionSeguridad, default));

            var despues = await FotoAsync(cs, s);
            Assert.Equal(antes.Roles, despues.Roles);
            Assert.Equal(antes.IdUnidad, despues.IdUnidad);
            Assert.Equal(antes.Email, despues.Email);
            Assert.NotNull((await v.UsuarioTokens.AsNoTracking().SingleAsync(x => x.IdUsuario == s.IdUsuario)).FechaUso);
            Assert.Equal(1, await v.Auditorias.CountAsync(x => x.IdUsuario == s.IdUsuario && x.Accion == "RECUPERAR_CLAVE"));

            var login = await new AuthService(v, ConfigJwt()).LoginAsync(new LoginRequest { Usuario = s.Usuario, Password = ClaveNueva });
            Assert.False(string.IsNullOrEmpty(login.Token));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Reset_BloqueoAutomatico_VuelveActivo_LimpiaBloqueo_YPermiteLogin()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Sembrado>();
        try
        {
            var s = await SembrarCasoAsync(cs, creados, "BLOQUEO_AUTOMATICO");
            var token = await SolicitarYObtenerTokenAsync(cs, s);
            var antes = await FotoAsync(cs, s);

            await using (var e = new Entorno(cs))
                Assert.True(await e.Servicio.RestablecerAsync(Req(token)));

            await using var v = NuevoContexto(cs);
            var u = await v.Usuarios.AsNoTracking().SingleAsync(x => x.IdUsuario == s.IdUsuario);
            Assert.Equal(EstadosUsuario.Activo, u.Estado);
            Assert.Equal(0, u.IntentosFallidos);
            Assert.Null(u.FechaBloqueo);
            Assert.Null(u.MotivoBloqueo);
            Assert.Equal(antes.Version + 1, u.VersionSeguridad);
            Assert.Equal(antes.Roles, (await FotoAsync(cs, s)).Roles);

            var login = await new AuthService(v, ConfigJwt()).LoginAsync(new LoginRequest { Usuario = s.Usuario, Password = ClaveNueva });
            Assert.False(string.IsNullOrEmpty(login.Token));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Reset_TokenUsado_SegundoIntentoFalla_SinCambiarLaClave()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Sembrado>();
        try
        {
            var s = await SembrarAsync(cs, creados, EstadosUsuario.Activo);
            var token = await SolicitarYObtenerTokenAsync(cs, s);
            await using (var e = new Entorno(cs))
                Assert.True(await e.Servicio.RestablecerAsync(Req(token)));
            var version = (await FotoAsync(cs, s)).Version;

            await using (var e = new Entorno(cs))
                Assert.False(await e.Servicio.RestablecerAsync(Req(token, "Otra#Clave2026")));

            var despues = await FotoAsync(cs, s);
            Assert.True(BCrypt.Net.BCrypt.Verify(ClaveNueva, despues.Hash));
            Assert.Equal(version, despues.Version);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Reset_TokenDeVerificacionEmail_NoSirve_NiSeConsume()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Sembrado>();
        try
        {
            var s = await SembrarAsync(cs, creados, EstadosUsuario.Activo);
            string tokenVerificacion;
            await using (var ctx = NuevoContexto(cs))
                tokenVerificacion = (await new TokenUsuarioService(ctx, new ConfigurationBuilder().Build(), TimeProvider.System)
                    .EmitirAsync(s.IdUsuario, PropositoTokenUsuario.VerificarEmail, s.Email, null)).Token;

            await using (var e = new Entorno(cs))
                Assert.False(await e.Servicio.RestablecerAsync(Req(tokenVerificacion)));

            await using var v = NuevoContexto(cs);
            Assert.Null((await v.UsuarioTokens.AsNoTracking().SingleAsync(x => x.IdUsuario == s.IdUsuario)).FechaUso);
            Assert.True(BCrypt.Net.BCrypt.Verify(ClaveActual, (await FotoAsync(cs, s)).Hash));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Reset_EmailCambiado_Falla_YQuemaElToken()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Sembrado>();
        try
        {
            var s = await SembrarAsync(cs, creados, EstadosUsuario.Activo);
            var token = await SolicitarYObtenerTokenAsync(cs, s);
            await CambiarEmailAsync(cs, s, "cambiado." + s.Email);

            await using (var e = new Entorno(cs))
                Assert.False(await e.Servicio.RestablecerAsync(Req(token)));

            // Aunque se restaure el email, el token ya quedó consumido.
            await CambiarEmailAsync(cs, s, s.Email);
            await using (var e = new Entorno(cs))
                Assert.False(await e.Servicio.RestablecerAsync(Req(token)));
            Assert.True(BCrypt.Net.BCrypt.Verify(ClaveActual, (await FotoAsync(cs, s)).Hash));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Theory]
    [InlineData("INACTIVO")]
    [InlineData("BLOQUEO_MANUAL")]
    [InlineData("PENDIENTE_VERIFICACION")]
    public async Task Reset_UsuarioYaNoElegible_Falla_YQuemaElToken(string cambio)
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Sembrado>();
        try
        {
            var s = await SembrarAsync(cs, creados, EstadosUsuario.Activo);
            var token = await SolicitarYObtenerTokenAsync(cs, s);
            var (estado, motivo) = cambio switch
            {
                "INACTIVO"       => (EstadosUsuario.Inactivo, (string?)null),
                "BLOQUEO_MANUAL" => (EstadosUsuario.Bloqueado, MotivoManual),
                _                => (EstadosUsuario.PendienteVerificacion, (string?)null),
            };
            DateTime? fechaBloqueo = motivo is null ? null : DateTime.UtcNow;
            await using (var ctx = NuevoContexto(cs))
                await ctx.Usuarios.Where(u => u.IdUsuario == s.IdUsuario).ExecuteUpdateAsync(x => x
                    .SetProperty(u => u.Estado, estado)
                    .SetProperty(u => u.MotivoBloqueo, motivo)
                    .SetProperty(u => u.FechaBloqueo, fechaBloqueo));
            var antes = await FotoAsync(cs, s);

            await using (var e = new Entorno(cs))
                Assert.False(await e.Servicio.RestablecerAsync(Req(token)));

            var despues = await FotoAsync(cs, s);
            Assert.Equal(estado, despues.Estado);
            Assert.Equal(antes.Hash, despues.Hash);
            Assert.Equal(antes.Version, despues.Version);
            await using var v = NuevoContexto(cs);
            Assert.NotNull((await v.UsuarioTokens.AsNoTracking().SingleAsync(x => x.IdUsuario == s.IdUsuario)).FechaUso);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Theory]
    [InlineData("Ab1!")]
    [InlineData("Recupera2026x")]
    [InlineData("recupera#2026x")]
    public async Task Reset_ClaveQueIncumplePolitica_NoConsumeElToken(string claveDebil)
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Sembrado>();
        try
        {
            var s = await SembrarAsync(cs, creados, EstadosUsuario.Activo);
            var token = await SolicitarYObtenerTokenAsync(cs, s);
            var antes = await FotoAsync(cs, s);

            await using (var e = new Entorno(cs))
                await Assert.ThrowsAsync<ClaveNoValidaException>(() => e.Servicio.RestablecerAsync(Req(token, claveDebil)));

            var despues = await FotoAsync(cs, s);
            Assert.Equal(antes.Hash, despues.Hash);
            Assert.Equal(antes.Version, despues.Version);

            // El token sigue vigente: un segundo intento con clave válida funciona.
            await using (var e = new Entorno(cs))
                Assert.True(await e.Servicio.RestablecerAsync(Req(token)));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Reset_ClaveConNombreDeUsuario_Rechaza_SinConsumir()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Sembrado>();
        try
        {
            var s = await SembrarAsync(cs, creados, EstadosUsuario.Activo);
            var token = await SolicitarYObtenerTokenAsync(cs, s);
            await using (var e = new Entorno(cs))
                await Assert.ThrowsAsync<ClaveNoValidaException>(() => e.Servicio.RestablecerAsync(Req(token, "Aa1!" + s.Usuario)));
            await using (var e = new Entorno(cs))
                Assert.True(await e.Servicio.RestablecerAsync(Req(token)));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Reset_ConfirmacionDistinta_NoConsumeElToken()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Sembrado>();
        try
        {
            var s = await SembrarAsync(cs, creados, EstadosUsuario.Activo);
            var token = await SolicitarYObtenerTokenAsync(cs, s);
            await using (var e = new Entorno(cs))
            {
                var ex = await Assert.ThrowsAsync<ClaveNoValidaException>(
                    () => e.Servicio.RestablecerAsync(new RestablecerClaveRecuperacionRequest(token, ClaveNueva, ClaveNueva + "x")));
                Assert.Equal(RecuperacionClaveService.MensajeConfirmacionDistinta, ex.Message);
            }
            await using (var e = new Entorno(cs))
                Assert.True(await e.Servicio.RestablecerAsync(Req(token)));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Reset_ClaveIgualALaActual_Rechaza_SinConsumir()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Sembrado>();
        try
        {
            var s = await SembrarAsync(cs, creados, EstadosUsuario.Activo);
            var token = await SolicitarYObtenerTokenAsync(cs, s);
            await using (var e = new Entorno(cs))
            {
                var ex = await Assert.ThrowsAsync<ClaveNoValidaException>(() => e.Servicio.RestablecerAsync(Req(token, ClaveActual)));
                Assert.Equal(RecuperacionClaveService.MensajeClaveIgualActual, ex.Message);
            }
            await using (var e = new Entorno(cs))
                Assert.True(await e.Servicio.RestablecerAsync(Req(token)));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Reset_FalloAlIncrementarVersion_RevierteTodo()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Sembrado>();
        try
        {
            var s = await SembrarCasoAsync(cs, creados, "BLOQUEO_AUTOMATICO");
            var token = await SolicitarYObtenerTokenAsync(cs, s);
            var antes = await FotoAsync(cs, s);

            var interceptor = new FallaAlIncrementarVersion();
            await using (var e = new Entorno(cs, interceptor))
            {
                var ex = await Assert.ThrowsAnyAsync<Exception>(() => e.Servicio.RestablecerAsync(Req(token)));
                Assert.True(ContieneFalloInyectado(ex));
            }
            Assert.True(interceptor.Aplicado);

            var despues = await FotoAsync(cs, s);
            Assert.Equal(EstadosUsuario.Bloqueado, despues.Estado);
            Assert.Equal(antes.Hash, despues.Hash);
            Assert.Equal(antes.Version, despues.Version);
            await using (var v = NuevoContexto(cs))
            {
                var u = await v.Usuarios.AsNoTracking().SingleAsync(x => x.IdUsuario == s.IdUsuario);
                Assert.Equal(BloqueoAutomaticoUsuario.Motivo, u.MotivoBloqueo);
                Assert.Null((await v.UsuarioTokens.AsNoTracking().SingleAsync(x => x.IdUsuario == s.IdUsuario)).FechaUso);
                Assert.Equal(0, await v.Auditorias.CountAsync(x => x.IdUsuario == s.IdUsuario && x.Accion == "RECUPERAR_CLAVE"));
            }

            // Nada quedó a medias: el mismo token sigue sirviendo.
            await using (var e = new Entorno(cs))
                Assert.True(await e.Servicio.RestablecerAsync(Req(token)));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-es-un-token")]
    public async Task Reset_TokenNuloOMalformado_DevuelveFalse(string? token)
    {
        if (!TryConnString(out var cs)) return;
        await using var e = new Entorno(cs);
        Assert.False(await e.Servicio.RestablecerAsync(Req(token)));
        Assert.False(await e.Servicio.RestablecerAsync(null));
    }

    [Fact]
    public async Task Reset_Controller_RespuestasSinDatosSensibles()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Sembrado>();
        try
        {
            var s = await SembrarAsync(cs, creados, EstadosUsuario.Activo);
            var token = await SolicitarYObtenerTokenAsync(cs, s);

            await using (var e = new Entorno(cs))
            {
                var ok = Assert.IsType<OkObjectResult>(await e.Controller().RestablecerClave(Req(token), default));
                var cuerpo = Json(ok.Value);
                Assert.Contains(RecuperacionClaveController.MensajeClaveRestablecida, cuerpo);
                Assert.DoesNotContain(token, cuerpo);
            }
            await using (var e = new Entorno(cs))
            {
                var rechazo = Assert.IsType<BadRequestObjectResult>(await e.Controller().RestablecerClave(Req(token), default));
                var cuerpo = Json(rechazo.Value);
                Assert.Contains(RecuperacionClaveController.MensajeEnlaceNoValido, cuerpo);
                Assert.DoesNotContain(token, cuerpo);
                Assert.DoesNotContain(s.Email, cuerpo, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    // ── Infraestructura ─────────────────────────────────────────────────

    private enum EmailSemilla { Valido, Ausente, Invalido }

    // Email = forma normalizada esperada (en BD se guarda en mayúsculas).
    private sealed record Sembrado(long IdUsuario, long IdPersona, string Usuario, string Email);

    private sealed record Foto(int Version, string Hash, string Estado, string Roles, long IdUnidad, string? Email);

    private static RestablecerClaveRecuperacionRequest Req(string? token, string clave = ClaveNueva) => new(token, clave, clave);

    // Sin escapar acentos: los mensajes se comparan literalmente.
    private static readonly JsonSerializerOptions JsonLegible = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static string Json(object? valor) => JsonSerializer.Serialize(valor, JsonLegible);

    private static Task<Sembrado> SembrarCasoAsync(string cs, List<Sembrado> creados, string caso) => caso switch
    {
        "ACTIVO_SIN_EMAIL_VERIFICADO" => SembrarAsync(cs, creados, EstadosUsuario.Activo, emailVerificado: false),
        "BLOQUEO_AUTOMATICO"          => SembrarAsync(cs, creados, EstadosUsuario.Bloqueado, motivoBloqueo: BloqueoAutomaticoUsuario.Motivo),
        "INACTIVO"                    => SembrarAsync(cs, creados, EstadosUsuario.Inactivo),
        "PENDIENTE_VERIFICACION"      => SembrarAsync(cs, creados, EstadosUsuario.PendienteVerificacion, emailVerificado: false),
        "BLOQUEO_MANUAL"              => SembrarAsync(cs, creados, EstadosUsuario.Bloqueado, motivoBloqueo: MotivoManual),
        "BLOQUEO_SIN_FECHA"           => SembrarAsync(cs, creados, EstadosUsuario.Bloqueado, motivoBloqueo: BloqueoAutomaticoUsuario.Motivo, conFechaBloqueo: false),
        "BLOQUEO_MOTIVO_VARIANTE"     => SembrarAsync(cs, creados, EstadosUsuario.Bloqueado, motivoBloqueo: BloqueoAutomaticoUsuario.Motivo.ToLowerInvariant()),
        "SIN_EMAIL"                   => SembrarAsync(cs, creados, EstadosUsuario.Activo, email: EmailSemilla.Ausente),
        "EMAIL_INVALIDO"              => SembrarAsync(cs, creados, EstadosUsuario.Activo, email: EmailSemilla.Invalido),
        _ => throw new ArgumentOutOfRangeException(nameof(caso)),
    };

    private static async Task<Sembrado> SembrarAsync(string cs, List<Sembrado> creados, string estado,
        bool emailVerificado = true, string? motivoBloqueo = null, bool conFechaBloqueo = true,
        EmailSemilla email = EmailSemilla.Valido, bool requiereCambioClave = false)
    {
        var sufijo = Guid.NewGuid().ToString("N")[..12];
        var normalizado = $"p0r.{sufijo}@example.test";
        await using var ctx = NuevoContexto(cs);
        var idUnidad = await ctx.Database
            .SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL")
            .SingleAsync();
        var idRol = await ctx.Roles.Where(r => r.Codigo == "USUARIO_FINAL" && r.Estado == "ACTIVO").Select(r => r.IdRol).SingleAsync();
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
        var sembrado = new Sembrado(0, persona.IdPersona, $"p0r_rec_{sufijo}", normalizado);
        creados.Add(sembrado);

        var bloqueado = estado == EstadosUsuario.Bloqueado;
        var usuario = new Usuario
        {
            IdPersona = persona.IdPersona, NombreUsuario = sembrado.Usuario,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(ClaveActual),
            Estado = estado, EmailVerificado = emailVerificado, RequiereCambioClave = requiereCambioClave,
            IntentosFallidos = bloqueado ? 5 : 0,
            FechaBloqueo     = bloqueado && conFechaBloqueo ? ahora : null,
            MotivoBloqueo    = bloqueado ? motivoBloqueo : null,
            FechaCreacion    = ahora,
        };
        ctx.Usuarios.Add(usuario);
        await ctx.SaveChangesAsync();
        sembrado = sembrado with { IdUsuario = usuario.IdUsuario };
        creados[^1] = sembrado;

        ctx.UsuarioRoles.Add(new UsuarioRol { IdUsuario = usuario.IdUsuario, IdRol = idRol, Estado = "ACTIVO", FechaAsignacion = ahora });
        await ctx.SaveChangesAsync();
        return sembrado;
    }

    private sealed class Entorno : IAsyncDisposable
    {
        public XpayDbContext Ctx { get; }
        public SenderEspia Sender { get; }
        public LoggerEspia Logger { get; } = new();
        public RecuperacionClaveService Servicio { get; }

        public Entorno(string cs, params IInterceptor[] interceptores)
        {
            var opciones = new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs);
            if (interceptores.Length > 0) opciones.AddInterceptors(interceptores);
            Ctx = new XpayDbContext(opciones.Options);
            var ambiente = AmbienteXpayTests.Ambiente("QA");
            var config   = new ConfigurationBuilder().Build();
            Sender   = new SenderEspia(Ctx);
            Servicio = new RecuperacionClaveService(Ctx,
                new TokenUsuarioService(Ctx, config, TimeProvider.System), Sender,
                new PlantillasEmailIdentidad(config, ambiente), TimeProvider.System, Logger);
        }

        public RecuperacionClaveController Controller() => new(Servicio)
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
    private sealed class LoggerEspia : ILogger<RecuperacionClaveService>
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

    private static byte[] DesdeBase64Url(string token)
        => Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/') + "=");

    private static async Task<string> SolicitarYObtenerTokenAsync(string cs, Sembrado s)
    {
        await using var e = new Entorno(cs);
        await e.Servicio.SolicitarAsync(s.Usuario, null);
        return TokenDe(Assert.Single(e.Sender.Enviados));
    }

    private static async Task<int> TokensAsync(string cs, long idUsuario)
    {
        await using var ctx = NuevoContexto(cs);
        return await ctx.UsuarioTokens.CountAsync(t => t.IdUsuario == idUsuario && t.Proposito == PropositoTokenUsuario.RecuperarClave);
    }

    private static async Task CambiarEmailAsync(string cs, Sembrado s, string email)
    {
        await using var ctx = NuevoContexto(cs);
        await ctx.Personas.Where(p => p.IdPersona == s.IdPersona).ExecuteUpdateAsync(x => x.SetProperty(p => p.Email, email));
    }

    private static async Task<Foto> FotoAsync(string cs, Sembrado s)
    {
        await using var ctx = NuevoContexto(cs);
        var u = await ctx.Usuarios.AsNoTracking().SingleAsync(x => x.IdUsuario == s.IdUsuario);
        var p = await ctx.Personas.AsNoTracking().SingleAsync(x => x.IdPersona == s.IdPersona);
        var roles = await ctx.UsuarioRoles.AsNoTracking().Where(r => r.IdUsuario == s.IdUsuario)
            .OrderBy(r => r.IdRol).Select(r => r.IdRol + ":" + r.Estado).ToListAsync();
        return new Foto(u.VersionSeguridad, u.PasswordHash, u.Estado, string.Join(",", roles), p.IdUnidadNegocio, p.Email);
    }

    private sealed class FalloInyectadoException : Exception
    {
        public FalloInyectadoException() : base("Fallo inyectado al incrementar version_seguridad.") { }
    }

    private static bool ContieneFalloInyectado(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
            if (e is FalloInyectadoException) return true;
        return false;
    }

    // Falla el UPDATE atómico de version_seguridad: último paso antes del commit.
    private sealed class FallaAlIncrementarVersion : DbCommandInterceptor
    {
        public bool Aplicado { get; private set; }

        private bool EsIncremento(DbCommand c)
        {
            var es = c.CommandText.Contains("UPDATE", StringComparison.Ordinal)
                  && c.CommandText.Contains("[version_seguridad] + 1", StringComparison.Ordinal);
            if (es) Aplicado = true;
            return es;
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
            => EsIncremento(command) ? throw new FalloInyectadoException() : base.NonQueryExecuting(command, eventData, result);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
            => EsIncremento(command) ? throw new FalloInyectadoException()
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
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL de P0-B2 recuperación.");
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
            await ctx.UsuarioTokens.Where(t => ids.Contains(t.IdUsuario)).ExecuteDeleteAsync();
            await ctx.Auditorias.Where(x => x.IdUsuario != null && ids.Contains(x.IdUsuario.Value)).ExecuteDeleteAsync();
            await ctx.UsuarioRoles.Where(r => ids.Contains(r.IdUsuario)).ExecuteDeleteAsync();
            await ctx.Usuarios.Where(u => ids.Contains(u.IdUsuario)).ExecuteDeleteAsync();
            await ctx.Personas.Where(p => personas.Contains(p.IdPersona)).ExecuteDeleteAsync();
        }
        catch { /* limpieza best-effort, como en el resto de pruebas SQL */ }
    }
}
