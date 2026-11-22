using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xpay.Api.Common;
using Xpay.Api.DTOs;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

// PIN transaccional del propio usuario (idUsuario siempre del token). No mueve
// dinero: funciona con el interruptor financiero cerrado, para que el usuario
// pueda crear su PIN antes de operar. Ver PinTransaccionalService.
[ApiController]
[Authorize]
[Route("api/pin")]
public class PinController(PinTransaccionalService pin, AuditLogService audit, PreparacionWalletService preparacion) : ControllerBase
{
    [HttpGet("estado")]
    [CuentaLimitadaPermitida]
    public async Task<IActionResult> Estado()
    {
        if (!TryGetIdUsuario(out var idUsuario)) return Unauthorized(new { success = false, message = "Token inválido." });
        try { await preparacion.ExigirAccesoPinAsync(idUsuario, HttpContext.RequestAborted); }
        catch (PreparacionWalletNoDisponibleException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        return Ok(new { success = true, data = await pin.ObtenerEstadoAsync(idUsuario) });
    }

    [HttpPost]
    [CuentaLimitadaPermitida]
    [EnableRateLimiting("CambiarClavePolicy")]
    public async Task<IActionResult> Crear([FromBody] CrearPinRequest request)
    {
        if (!TryGetIdUsuario(out var idUsuario)) return Unauthorized(new { success = false, message = "Token inválido." });
        audit.LogSensitiveAction(HttpContext, "PIN_CREAR_ATTEMPT", new { idUsuario });
        try
        {
            await preparacion.ExigirAccesoPinAsync(idUsuario, HttpContext.RequestAborted);
            await pin.CrearAsync(idUsuario, request);
            audit.LogSensitiveAction(HttpContext, "PIN_CREAR_SUCCESS", new { idUsuario });
            return Ok(new { success = true, message = "PIN transaccional creado.", data = await pin.ObtenerEstadoAsync(idUsuario) });
        }
        catch (PreparacionWalletNoDisponibleException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException)          { return Unauthorized(new { success = false, message = "Token inválido." }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno creando el PIN." }); }
    }

    [HttpPost("cambiar")]
    [CuentaLimitadaPermitida]
    [EnableRateLimiting("CambiarClavePolicy")]
    public async Task<IActionResult> Cambiar([FromBody] CambiarPinRequest request)
    {
        if (!TryGetIdUsuario(out var idUsuario)) return Unauthorized(new { success = false, message = "Token inválido." });
        audit.LogSensitiveAction(HttpContext, "PIN_CAMBIAR_ATTEMPT", new { idUsuario });
        try
        {
            await preparacion.ExigirAccesoPinAsync(idUsuario, HttpContext.RequestAborted);
            var v = await pin.CambiarAsync(idUsuario, request);
            audit.LogSensitiveAction(HttpContext, v.Resultado == ResultadoPin.Correcto ? "PIN_CAMBIAR_SUCCESS" : "PIN_CAMBIAR_RECHAZADO",
                new { idUsuario, resultado = v.Resultado.ToString() });
            return v.Resultado switch
            {
                ResultadoPin.Correcto      => Ok(new { success = true, message = "PIN transaccional actualizado.", data = await pin.ObtenerEstadoAsync(idUsuario) }),
                ResultadoPin.NoConfigurado => StatusCode(428, new { success = false, codigo = "PIN_NO_CONFIGURADO", message = "Aún no tienes PIN transaccional." }),
                ResultadoPin.Bloqueado     => StatusCode(423, new { success = false, codigo = "PIN_BLOQUEADO", message = "Tu PIN transaccional está bloqueado por intentos fallidos. Solicita a XPAY su restablecimiento." }),
                ResultadoPin.Incorrecto    => BadRequest(new { success = false, codigo = "PIN_INCORRECTO", intentosRestantes = v.IntentosRestantes, message = $"El PIN actual no coincide. Te quedan {v.IntentosRestantes} intento(s)." }),
                ResultadoPin.CambiadoConcurrentemente => BadRequest(new { success = false, codigo = "PIN_CAMBIADO", message = RequierePinTransaccionalAttribute.MensajeCambiado }),
                _                          => BadRequest(new { success = false, codigo = "PIN_REQUERIDO", message = "Ingresa tu PIN actual de 6 dígitos o el anterior de 5 o 7 dígitos." }),
            };
        }
        catch (PreparacionWalletNoDisponibleException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno cambiando el PIN." }); }
    }

    private bool TryGetIdUsuario(out long id) =>
        long.TryParse(User.FindFirst("idUsuario")?.Value, out id) && id > 0;
}
