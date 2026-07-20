using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.DTOs;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
[Route("api/admin/wallet-recaudos-comercio")]
public class AdminWalletRecaudosComercioController(WalletLiquidacionRecaudoComercioService svc) : ControllerBase
{
    private bool TryGetUsuarioId(out long id) =>
        long.TryParse(User.FindFirst("idUsuario")?.Value, out id) && id > 0;

    [HttpGet("pendientes")]
    public async Task<IActionResult> Pendientes(
        [FromQuery] long?    idComercio      = null,
        [FromQuery] long?    idTienda        = null,
        [FromQuery] long?    idUsuarioCajero = null,
        [FromQuery] DateTime? fechaDesde     = null,
        [FromQuery] DateTime? fechaHasta     = null)
    {
        var data = await svc.ListarPendientesAsync(idComercio, idTienda, idUsuarioCajero, fechaDesde, fechaHasta);
        return Ok(new { success = true, data });
    }

    [HttpGet("resumen-pendientes")]
    public async Task<IActionResult> ResumenPendientes(
        [FromQuery] long?    idComercio      = null,
        [FromQuery] long?    idTienda        = null,
        [FromQuery] long?    idUsuarioCajero = null,
        [FromQuery] DateTime? fechaDesde     = null,
        [FromQuery] DateTime? fechaHasta     = null)
    {
        var data = await svc.ResumenPendientesAsync(idComercio, idTienda, idUsuarioCajero, fechaDesde, fechaHasta);
        return Ok(new { success = true, data });
    }

    [HttpPost("liquidar")]
    public async Task<IActionResult> Liquidar([FromBody] LiquidarRecaudosComercioRequest req)
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var data = await svc.LiquidarAsync(req, uid);
            return Ok(new { success = true, data });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
    }
}
