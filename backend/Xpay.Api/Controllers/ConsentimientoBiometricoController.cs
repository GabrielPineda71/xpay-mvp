using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.Common;
using Xpay.Api.DTOs;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/usuarios/alta-publica/consentimiento-kyc")]
public sealed class ConsentimientoBiometricoController(ConsentimientoBiometricoService consentimiento) : ControllerBase
{
    [HttpGet]
    [CuentaLimitadaPermitida]
    public async Task<IActionResult> Estado(CancellationToken ct)
    {
        if (!TryIdUsuario(out var id)) return Unauthorized(new { success = false, message = "Token inválido." });
        return Ok(new { success = true, data = await consentimiento.ObtenerEstadoAsync(id, ct) });
    }

    [HttpPost]
    [CuentaLimitadaPermitida]
    public async Task<IActionResult> Autorizar([FromBody] AutorizarConsentimientoBiometricoRequest request, CancellationToken ct)
    {
        if (!TryIdUsuario(out var id)) return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var data = await consentimiento.AutorizarAsync(id, request, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
            return Ok(new { success = true, data });
        }
        catch (ConsentimientoBiometricoNoDisponibleException)
        { return NotFound(new { success = false, message = "La autorización de verificación de identidad no está disponible." }); }
        catch (UnauthorizedAccessException)
        { return Unauthorized(new { success = false, message = "Token inválido." }); }
        catch (InvalidOperationException ex)
        { return BadRequest(new { success = false, message = ex.Message }); }
    }

    private bool TryIdUsuario(out long id) => long.TryParse(User.FindFirst("idUsuario")?.Value, out id) && id > 0;
}
