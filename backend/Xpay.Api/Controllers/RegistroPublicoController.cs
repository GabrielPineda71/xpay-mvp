using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xpay.Api.Common;
using Xpay.Api.DTOs;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

// El alta productiva requiere configuración explícita para cuentas limitadas.
// Las respuestas públicas no incluyen tokens ni identificadores internos.
[ApiController]
[Route("api/usuarios")]
[RegistroPublicoDisponible]
public class RegistroPublicoController(RegistroPublicoService servicio) : ControllerBase
{
    internal const string MensajeRegistroRecibido     = "Registro recibido. Revisa tu correo para activar la cuenta.";
    internal const string MensajeVerificado           = "Correo verificado. Ya puedes iniciar sesión.";
    internal const string MensajeVerificacionNoValida = "El enlace de verificación no es válido o ya fue usado.";
    internal const string MensajeReenvioRecibido      = "Si la cuenta está pendiente de verificación, enviaremos un nuevo enlace a su correo registrado.";

    [HttpGet("registro/configuracion")]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult ObtenerConfiguracion()
    {
        var datos = servicio.ObtenerConfiguracion();
        return datos is null
            ? RegistroNoDisponible()
            : Ok(new { success = true, data = datos });
    }

    [HttpPost("registro")]
    [AllowAnonymous]
    [EnableRateLimiting("IdentidadPublicaPolicy")]
    public async Task<IActionResult> Registrar([FromBody] RegistroPublicoRequest? request, CancellationToken ct)
    {
        if (request is null) return BadRequest(new { success = false, message = "Solicitud inválida." });
        try
        {
            await servicio.RegistrarAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
            return StatusCode(202, new { success = true, message = MensajeRegistroRecibido });
        }
        catch (RegistroPublicoNoDisponibleException) { return RegistroNoDisponible(); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno en el registro." }); }
    }

    private NotFoundObjectResult RegistroNoDisponible() =>
        NotFound(new { success = false, message = "Registro no disponible." });

    [HttpPost("verificar-email")]
    [AllowAnonymous]
    [EnableRateLimiting("IdentidadPublicaPolicy")]
    public async Task<IActionResult> VerificarEmail([FromBody] VerificarEmailRequest? request, CancellationToken ct)
    {
        try
        {
            return await servicio.VerificarEmailAsync(request?.Token, ct)
                ? Ok(new { success = true, message = MensajeVerificado })
                : BadRequest(new { success = false, message = MensajeVerificacionNoValida });
        }
        catch (RegistroPublicoNoDisponibleException) { return RegistroNoDisponible(); }
        catch { return StatusCode(500, new { success = false, message = "Error interno verificando el correo." }); }
    }

    // P0-B3 — reenvío del enlace de verificación: SIEMPRE 202 con el mismo
    // cuerpo, exista o no el usuario y sea o no elegible.
    [HttpPost("reenviar-verificacion")]
    [AllowAnonymous]
    [EnableRateLimiting("IdentidadPublicaPolicy")]
    public async Task<IActionResult> ReenviarVerificacion([FromBody] ReenviarVerificacionRequest? request, CancellationToken ct)
    {
        try
        {
            await servicio.ReenviarVerificacionAsync(request?.Usuario, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
            return StatusCode(202, new { success = true, message = MensajeReenvioRecibido });
        }
        catch (RegistroPublicoNoDisponibleException) { return RegistroNoDisponible(); }
        catch { return StatusCode(500, new { success = false, message = "Error interno en el reenvío de verificación." }); }
    }
}
