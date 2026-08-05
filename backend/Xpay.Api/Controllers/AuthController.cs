using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xpay.Api.DTOs;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService      _authService;
    private readonly AuditLogService  _audit;

    public AuthController(AuthService authService, AuditLogService audit)
    {
        _authService = authService;
        _audit       = audit;
    }

    [HttpPost("login")]
    [EnableRateLimiting("LoginPolicy")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var usuario = request?.Usuario ?? "-";
        try
        {
            var data = await _authService.LoginAsync(request!);
            _audit.LogLoginSuccess(HttpContext, usuario);
            return Ok(new { success = true, data });
        }
        catch (InvalidOperationException ex)
        {
            _audit.LogLoginFailure(HttpContext, usuario, "credentials_invalid");
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch
        {
            _audit.LogLoginFailure(HttpContext, usuario, "internal_error");
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
}
