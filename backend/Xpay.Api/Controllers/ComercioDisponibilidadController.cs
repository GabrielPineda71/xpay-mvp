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

    public ComercioDisponibilidadController(ComercioDisponibilidadService disp) => _disp = disp;

    [HttpGet("ventas-disponibilidad/resumen")]
    public async Task<IActionResult> GetResumen([FromQuery] long idComercio)
    {
        if (idComercio <= 0) return BadRequest(new { success = false, message = "idComercio inválido." });
        try { return Ok(new { success = true, data = await _disp.GetMiDisponibilidadAsync(idComercio) }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    [HttpGet("ventas-no-disponibles")]
    public async Task<IActionResult> ListarNoDisponibles([FromQuery] long idComercio)
    {
        if (idComercio <= 0) return BadRequest(new { success = false, message = "idComercio inválido." });
        try { return Ok(new { success = true, data = await _disp.ListarVentasNoDisponiblesAsync(idComercio) }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    [HttpPost("ventas-no-disponibles/{idDisponibilidad:long}/liquidar-ahora")]
    public async Task<IActionResult> LiquidarAhora(long idDisponibilidad, [FromQuery] long idComercio)
    {
        if (idComercio <= 0) return BadRequest(new { success = false, message = "idComercio inválido." });
        try { return Ok(new { success = true, data = await _disp.LiquidarAhoraAsync(idDisponibilidad, idComercio) }); }
        catch (KeyNotFoundException ex)      { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno procesando liquidación." }); }
    }
}
