using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xpay.Api.Common;
using Xpay.Api.DTOs;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

// P0-B1 — registro público y verificación de email. [SoloAmbienteNoProductivo]
// mientras el consentimiento/términos siga pendiente: en productivo responden
// 404 y no existen. Rate limiting dedicado por IP (IdentidadPublicaPolicy).
// Respuestas sin datos sensibles: nunca devuelven token ni IDs internos.
[ApiController]
[Route("api/usuarios")]
public class RegistroPublicoController(RegistroPublicoService servicio) : ControllerBase
{
    internal const string MensajeRegistroRecibido     = "Registro recibido. Revisa tu correo para activar la cuenta.";
    internal const string MensajeVerificado           = "Correo verificado. Ya puedes iniciar sesión.";
    internal const string MensajeVerificacionNoValida = "El enlace de verificación no es válido o ya fue usado.";
    internal const string MensajeReenvioRecibido      = "Si la cuenta está pendiente de verificación, enviaremos un nuevo enlace a su correo registrado.";

    [HttpPost("registro")]
    [AllowAnonymous]
    [SoloAmbienteNoProductivo]
    [EnableRateLimiting("IdentidadPublicaPolicy")]
    public async Task<IActionResult> Registrar([FromBody] RegistroPublicoRequest? request, CancellationToken ct)
    {
        if (request is null) return BadRequest(new { success = false, message = "Solicitud inválida." });
        try
        {
            await servicio.RegistrarAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
            return StatusCode(202, new { success = true, message = MensajeRegistroRecibido });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno en el registro." }); }
    }

    [HttpPost("verificar-email")]
    [AllowAnonymous]
    [SoloAmbienteNoProductivo]
    [EnableRateLimiting("IdentidadPublicaPolicy")]
    public async Task<IActionResult> VerificarEmail([FromBody] VerificarEmailRequest? request, CancellationToken ct)
    {
        try
        {
            return await servicio.VerificarEmailAsync(request?.Token, ct)
                ? Ok(new { success = true, message = MensajeVerificado })
                : BadRequest(new { success = false, message = MensajeVerificacionNoValida });
        }
        catch { return StatusCode(500, new { success = false, message = "Error interno verificando el correo." }); }
    }

    // P0-B3 — reenvío del enlace de verificación: SIEMPRE 202 con el mismo
    // cuerpo, exista o no el usuario y sea o no elegible.
    [HttpPost("reenviar-verificacion")]
    [AllowAnonymous]
    [SoloAmbienteNoProductivo]
    [EnableRateLimiting("IdentidadPublicaPolicy")]
    public async Task<IActionResult> ReenviarVerificacion([FromBody] ReenviarVerificacionRequest? request, CancellationToken ct)
    {
        try
        {
            await servicio.ReenviarVerificacionAsync(request?.Usuario, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
            return StatusCode(202, new { success = true, message = MensajeReenvioRecibido });
        }
        catch { return StatusCode(500, new { success = false, message = "Error interno en el reenvío de verificación." }); }
    }
}
