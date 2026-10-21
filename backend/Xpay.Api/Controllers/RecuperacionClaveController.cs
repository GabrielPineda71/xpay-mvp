using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xpay.Api.Common;
using Xpay.Api.DTOs;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

// P0-B2 — recuperación de contraseña para cualquier usuario. Misma frontera
// que P0-B1: [SoloAmbienteNoProductivo] (404 en productivo) e
// IdentidadPublicaPolicy por IP. La solicitud responde SIEMPRE 202 con el
// mismo cuerpo, exista o no el usuario y sea o no elegible. Ninguna
// respuesta incluye token, email ni IDs internos.
[ApiController]
[Route("api/auth")]
public class RecuperacionClaveController(RecuperacionClaveService servicio) : ControllerBase
{
    internal const string MensajeSolicitudRecibida = "Si la cuenta puede recuperarse, enviaremos un enlace a su correo registrado.";
    internal const string MensajeClaveRestablecida = "Contraseña restablecida. Ya puedes iniciar sesión.";
    internal const string MensajeEnlaceNoValido    = "El enlace de recuperación no es válido o ya fue usado.";

    [HttpPost("recuperar-clave")]
    [AllowAnonymous]
    [SoloAmbienteNoProductivo]
    [EnableRateLimiting("IdentidadPublicaPolicy")]
    public async Task<IActionResult> SolicitarRecuperacion([FromBody] SolicitarRecuperacionClaveRequest? request, CancellationToken ct)
    {
        try
        {
            await servicio.SolicitarAsync(request?.Usuario, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
            return StatusCode(202, new { success = true, message = MensajeSolicitudRecibida });
        }
        catch { return StatusCode(500, new { success = false, message = "Error interno en la recuperación de contraseña." }); }
    }

    [HttpPost("restablecer-clave")]
    [AllowAnonymous]
    [SoloAmbienteNoProductivo]
    [EnableRateLimiting("IdentidadPublicaPolicy")]
    public async Task<IActionResult> RestablecerClave([FromBody] RestablecerClaveRecuperacionRequest? request, CancellationToken ct)
    {
        try
        {
            return await servicio.RestablecerAsync(request, ct)
                ? Ok(new { success = true, message = MensajeClaveRestablecida })
                : BadRequest(new { success = false, message = MensajeEnlaceNoValido });
        }
        catch (ClaveNoValidaException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno restableciendo la contraseña." }); }
    }
}
