using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xpay.Api.Common;
using Xpay.Api.DTOs;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService      _authService;
    private readonly AuditLogService  _audit;
    private readonly ILogger<AuthController> _logger;

    public AuthController(AuthService authService, AuditLogService audit, ILogger<AuthController> logger)
    {
        _authService = authService;
        _audit       = audit;
        _logger      = logger;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("LoginPolicy")]
    public async Task<IActionResult> Login([FromBody] LoginRequest? request)
    {
        var usuario = request?.Usuario ?? "-";
        // P0-B3 — request nulo: el mismo 400 fijo que cualquier credencial inválida.
        if (request is null)
        {
            _audit.LogLoginFailure(HttpContext, usuario, "credentials_invalid");
            return BadRequest(new { success = false, message = CredencialesInvalidasException.MensajePublico });
        }
        try
        {
            var data = await _authService.LoginAsync(request);
            _audit.LogLoginSuccess(HttpContext, usuario);
            return Ok(new { success = true, data });
        }
        catch (CredencialesInvalidasException)
        {
            // P0-B3 — mensaje público fijo, nunca el texto de la excepción.
            _audit.LogLoginFailure(HttpContext, usuario, "credentials_invalid");
            return BadRequest(new { success = false, message = CredencialesInvalidasException.MensajePublico });
        }
        catch (Exception ex)
        {
            // Error técnico (incluida cualquier InvalidOperationException de EF):
            // 500 genérico; solo se registra el tipo de excepción.
            _audit.LogLoginFailure(HttpContext, usuario, "internal_error");
            _logger.LogError("Error técnico en login ({TipoError}).", ex.GetType().Name);
            return StatusCode(500, new { success = false, message = "Error interno iniciando sesión." });
        }
    }

    // Fase USUARIOS-ADMIN-5: [Authorize(Policy="SoloAutenticado")] en vez de
    // [Authorize] simple — esta política explícita NO se combina con la
    // DefaultPolicy (que incluye ClaveVigenteRequirement), así que este es el
    // único endpoint accesible mientras requiere_cambio_clave = true.
    [HttpPost("cambiar-clave-obligatoria")]
    [Authorize(Policy = "SoloAutenticado")]
    public async Task<IActionResult> CambiarClaveObligatoria([FromBody] CambiarClaveObligatoriaRequest request)
    {
        if (!long.TryParse(User.FindFirst("idUsuario")?.Value, out var idUsuario) || idUsuario <= 0)
            return Unauthorized(new { success = false, message = "Token inválido." });

        _audit.LogSensitiveAction(HttpContext, "USUARIO_CAMBIAR_CLAVE_OBLIGATORIA_ATTEMPT");
        try
        {
            var data = await _authService.CambiarClaveObligatoriaAsync(idUsuario, request);
            _audit.LogSensitiveAction(HttpContext, "USUARIO_CAMBIAR_CLAVE_OBLIGATORIA_SUCCESS");
            return Ok(new { success = true, message = "Contraseña actualizada correctamente.", data });
        }
        catch (KeyNotFoundException ex)      { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch
        {
            return StatusCode(500, new { success = false, message = "Error interno cambiando la contraseña." });
        }
    }

    // XPAY-400 — cambio VOLUNTARIO de contraseña. [Authorize] SIMPLE
    // (no "SoloAutenticado"): hereda la DefaultPolicy de Program.cs, que
    // incluye ClaveVigenteRequirement — si el usuario tiene
    // RequiereCambioClave=true, la petición ya es rechazada con 403 por el
    // pipeline de autorización ANTES de llegar a este método (mismo
    // comportamiento que el resto de la aplicación). Por eso este endpoint
    // NUNCA permite saltarse /cambiar-clave-obligatoria — no hace falta (ni
    // se duplica) ninguna comprobación adicional aquí.
    [HttpPost("cambiar-clave")]
    [Authorize]
    [EnableRateLimiting("CambiarClavePolicy")]
    public async Task<IActionResult> CambiarClave([FromBody] CambiarClaveRequest request)
    {
        if (!long.TryParse(User.FindFirst("idUsuario")?.Value, out var idUsuario) || idUsuario <= 0)
            return Unauthorized(new { success = false, message = "Token inválido." });

        _audit.LogSensitiveAction(HttpContext, "USUARIO_CAMBIAR_CLAVE_ATTEMPT");
        try
        {
            await _authService.CambiarClaveVoluntariaAsync(idUsuario, request);
            _audit.LogSensitiveAction(HttpContext, "USUARIO_CAMBIAR_CLAVE_SUCCESS");
            return Ok(new { success = true, message = "Contraseña actualizada correctamente." });
        }
        catch (KeyNotFoundException ex)      { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch
        {
            return StatusCode(500, new { success = false, message = "Error interno cambiando la contraseña." });
        }
    }
}
