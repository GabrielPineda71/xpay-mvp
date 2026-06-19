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
}
