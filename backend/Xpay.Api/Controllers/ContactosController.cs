using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

// Verificación de correo y celular del propio usuario (idUsuario siempre del
// token) y sus preferencias de comunicación. No mueve dinero ni depende del
// interruptor financiero ni de KYC: debe seguir disponible para que el usuario
// pueda cumplir el requisito de contactos verificados.
[ApiController]
[Authorize]
[Route("api/contactos")]
public class ContactosController(
    ContactoVerificacionService verificacion,
    RequisitoContactosVerificados requisito,
    XpayDbContext db,
    AuditLogService audit) : ControllerBase
{
    [HttpGet("estado")]
    public async Task<IActionResult> Estado(CancellationToken ct)
    {
        if (!TryGetIdUsuario(out var idUsuario)) return Unauthorized(new { success = false, message = "Token inválido." });
        var u = await db.Usuarios.AsNoTracking().Where(x => x.IdUsuario == idUsuario)
            .Select(x => new { x.EmailVerificado, x.CelularVerificado }).SingleOrDefaultAsync(ct);
        if (u is null) return Unauthorized(new { success = false, message = "Token inválido." });

        return Ok(new { success = true, data = new EstadoContactosDto
        {
            EmailVerificado         = u.EmailVerificado,
            CelularVerificado       = u.CelularVerificado,
            CanalEmailDisponible    = verificacion.CanalDisponible(CanalContacto.Email),
            CanalWhatsAppDisponible = verificacion.CanalDisponible(CanalContacto.WhatsApp),
            RequeridoParaOperar     = requisito.Efectivo,
            ServicioWhatsApp   = await verificacion.ConsentimientoVigenteAsync(idUsuario, CanalContacto.WhatsApp, FinalidadComunicacion.Servicio, ct),
            PublicidadEmail    = await verificacion.ConsentimientoVigenteAsync(idUsuario, CanalContacto.Email, FinalidadComunicacion.Publicidad, ct),
            PublicidadWhatsApp = await verificacion.ConsentimientoVigenteAsync(idUsuario, CanalContacto.WhatsApp, FinalidadComunicacion.Publicidad, ct),
        } });
    }

    [HttpPost("{canal}/codigo")]
    [EnableRateLimiting("ContactosCodigoPolicy")]
    public async Task<IActionResult> SolicitarCodigo(string canal, [FromBody] SolicitarCodigoContactoRequest request, CancellationToken ct)
    {
        if (!TryGetIdUsuario(out var idUsuario)) return Unauthorized(new { success = false, message = "Token inválido." });
        if (CanalDesdeRuta(canal) is not { } c) return NotFound(new { success = false, message = "Canal no válido." });

        var r = await verificacion.SolicitarAsync(idUsuario, c, request.AceptaServicioWhatsApp,
            HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        audit.LogSensitiveAction(HttpContext, "CONTACTO_CODIGO_SOLICITADO", new { idUsuario, canal = c, resultado = r.Resultado.ToString() });

        return r.Resultado switch
        {
            ResultadoSolicitudCodigo.Enviado => Ok(new
            {
                success = true,
                message = c == CanalContacto.Email
                    ? $"Te enviamos un código a {r.DestinoEnmascarado}. Vence en {ContactoVerificacionService.VigenciaMinutos} minutos."
                    : $"Te enviamos un código por WhatsApp a {r.DestinoEnmascarado}. Vence en {ContactoVerificacionService.VigenciaMinutos} minutos.",
                data = new { destino = r.DestinoEnmascarado, fechaExpiracion = r.FechaExpiracion },
            }),
            ResultadoSolicitudCodigo.YaVerificado            => Error(409, "YA_VERIFICADO", "Este dato ya está verificado."),
            ResultadoSolicitudCodigo.SinContacto             => Error(400, "SIN_CONTACTO", c == CanalContacto.Email ? "Primero registra tu correo en Mi información." : "Primero registra tu celular en Mi información."),
            ResultadoSolicitudCodigo.ContactoInvalido        => Error(400, "CONTACTO_INVALIDO", c == CanalContacto.Email ? "El correo registrado no es válido. Corrígelo en Mi información." : "El celular debe ser un móvil colombiano de 10 dígitos que empiece por 3. Corrígelo en Mi información."),
            ResultadoSolicitudCodigo.ContactoEnUsoPorOtro    => Error(409, "CONTACTO_EN_USO", "Este dato ya está verificado en otra cuenta. Usa otro o comunícate con XPAY."),
            ResultadoSolicitudCodigo.ConsentimientoRequerido => Error(428, "CONSENTIMIENTO_WHATSAPP", "Para enviarte el código por WhatsApp necesitamos tu autorización para mensajes de servicio."),
            ResultadoSolicitudCodigo.Espera                  => Error(429, "ESPERA_REENVIO", $"Espera {r.SegundosEspera} segundos para pedir otro código.", r.SegundosEspera),
            ResultadoSolicitudCodigo.LimiteDiario            => Error(429, "LIMITE_DIARIO", "Alcanzaste el máximo de códigos por hoy. Intenta de nuevo mañana."),
            ResultadoSolicitudCodigo.CanalNoDisponible       => Error(503, "CANAL_NO_DISPONIBLE", c == CanalContacto.Email ? "La verificación por correo aún no está disponible." : "La verificación por WhatsApp aún no está disponible."),
            _                                                => Error(502, "ERROR_ENVIO", "No pudimos enviar el código. Intenta de nuevo en unos minutos."),
        };
    }

    [HttpPost("{canal}/verificar")]
    [EnableRateLimiting("ContactosCodigoPolicy")]
    public async Task<IActionResult> Verificar(string canal, [FromBody] ConfirmarCodigoContactoRequest request, CancellationToken ct)
    {
        if (!TryGetIdUsuario(out var idUsuario)) return Unauthorized(new { success = false, message = "Token inválido." });
        if (CanalDesdeRuta(canal) is not { } c) return NotFound(new { success = false, message = "Canal no válido." });

        var codigo = request.Codigo;
        request.Codigo = null;
        var r = await verificacion.ConfirmarAsync(idUsuario, c, codigo, ct);
        audit.LogSensitiveAction(HttpContext, "CONTACTO_CODIGO_CONFIRMACION", new { idUsuario, canal = c, resultado = r.Resultado.ToString() });

        return r.Resultado switch
        {
            ResultadoConfirmacionCodigo.Verificado           => Ok(new { success = true, message = c == CanalContacto.Email ? "Correo verificado." : "Celular verificado." }),
            ResultadoConfirmacionCodigo.YaVerificado         => Error(409, "YA_VERIFICADO", "Este dato ya está verificado."),
            ResultadoConfirmacionCodigo.FormatoInvalido      => Error(400, "CODIGO_FORMATO", $"Escribe el código de {ContactoVerificacionService.Digitos} dígitos."),
            ResultadoConfirmacionCodigo.SinCodigoVigente     => Error(400, "SIN_CODIGO_VIGENTE", "No tienes un código vigente. Solicita uno nuevo."),
            ResultadoConfirmacionCodigo.Incorrecto           => Error(400, "CODIGO_INCORRECTO", $"Código incorrecto. Te quedan {r.IntentosRestantes} intento(s).", intentosRestantes: r.IntentosRestantes),
            ResultadoConfirmacionCodigo.Agotado              => Error(400, "CODIGO_AGOTADO", "Superaste los intentos para este código. Solicita uno nuevo."),
            ResultadoConfirmacionCodigo.ContactoCambio       => Error(409, "CONTACTO_CAMBIO", "Tu dato de contacto cambió. Solicita un código nuevo."),
            _                                                => Error(409, "CONTACTO_EN_USO", "Este dato ya está verificado en otra cuenta. Usa otro o comunícate con XPAY."),
        };
    }

    [HttpPut("preferencias")]
    public async Task<IActionResult> Preferencias([FromBody] PreferenciasComunicacionRequest request, CancellationToken ct)
    {
        if (!TryGetIdUsuario(out var idUsuario)) return Unauthorized(new { success = false, message = "Token inválido." });
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (request.ServicioWhatsApp is { } s)
            await verificacion.RegistrarConsentimientoAsync(idUsuario, CanalContacto.WhatsApp, FinalidadComunicacion.Servicio, s, "PERFIL", ip, ct);
        if (request.PublicidadEmail is { } pe)
            await verificacion.RegistrarConsentimientoAsync(idUsuario, CanalContacto.Email, FinalidadComunicacion.Publicidad, pe, "PERFIL", ip, ct);
        if (request.PublicidadWhatsApp is { } pw)
            await verificacion.RegistrarConsentimientoAsync(idUsuario, CanalContacto.WhatsApp, FinalidadComunicacion.Publicidad, pw, "PERFIL", ip, ct);
        audit.LogSensitiveAction(HttpContext, "PREFERENCIAS_COMUNICACION", new { idUsuario });
        return await Estado(ct);
    }

    internal static string? CanalDesdeRuta(string? canal) => canal switch
    {
        "email"   => CanalContacto.Email,
        "celular" => CanalContacto.WhatsApp,
        _         => null,
    };

    private static ObjectResult Error(int status, string codigo, string mensaje, int? segundosEspera = null, int? intentosRestantes = null) =>
        new(new { success = false, codigo, message = mensaje, segundosEspera, intentosRestantes }) { StatusCode = status };

    private bool TryGetIdUsuario(out long id) =>
        long.TryParse(User.FindFirst("idUsuario")?.Value, out id) && id > 0;
}
