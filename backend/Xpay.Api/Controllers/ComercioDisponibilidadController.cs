using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.DTOs;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Authorize(Roles = "COMERCIO")]
[Route("api/comercio")]
public class ComercioDisponibilidadController : ControllerBase
{
    private readonly ComercioDisponibilidadService _disp;
    private readonly ComercioScopeService          _scope;

    public ComercioDisponibilidadController(ComercioDisponibilidadService disp, ComercioScopeService scope)
    {
        _disp  = disp;
        _scope = scope;
    }

    private bool TryGetUsuarioId(out long id) =>
        long.TryParse(User.FindFirst("idUsuario")?.Value, out id) && id > 0;

    [HttpGet("ventas-disponibilidad/resumen")]
    public async Task<IActionResult> GetResumen(
        [FromQuery] long idComercio,
        [FromQuery] DateTime? desde = null,
        [FromQuery] DateTime? hasta = null)
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        if (idComercio <= 0) return BadRequest(new { success = false, message = "idComercio inválido." });
        try
        {
            var s = await _scope.RequireScopeAsync(uid);
            if (!s.PuedeLiquidarAnticipado)
                return Forbid();
            return Ok(new { success = true, data = await _disp.GetMiDisponibilidadAsync(idComercio, desde, hasta) });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    [HttpGet("ventas-no-disponibles")]
    public async Task<IActionResult> ListarNoDisponibles(
        [FromQuery] long idComercio,
        [FromQuery] DateTime? desde = null,
        [FromQuery] DateTime? hasta = null)
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        if (idComercio <= 0) return BadRequest(new { success = false, message = "idComercio inválido." });
        try
        {
            var s = await _scope.RequireScopeAsync(uid);
            if (!s.PuedeLiquidarAnticipado)
                return Forbid();
            return Ok(new { success = true, data = await _disp.ListarVentasNoDisponiblesAsync(idComercio, desde, hasta) });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    [HttpPost("ventas-no-disponibles/{idDisponibilidad:long}/liquidar-ahora")]
    public async Task<IActionResult> LiquidarAhora(long idDisponibilidad, [FromQuery] long idComercio)
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        if (idComercio <= 0) return BadRequest(new { success = false, message = "idComercio inválido." });
        try
        {
            var s = await _scope.RequireScopeAsync(uid);
            if (!s.PuedeLiquidarAnticipado)
                return Forbid();
            return Ok(new { success = true, data = await _disp.LiquidarAhoraAsync(idDisponibilidad, idComercio) });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException ex)      { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno procesando liquidación." }); }
    }
}
