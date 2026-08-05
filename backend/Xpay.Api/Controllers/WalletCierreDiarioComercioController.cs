using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.DTOs;
using Xpay.Api.Exceptions;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Authorize(Roles = "COMERCIO")]
[Route("api/comercio/wallet-cierres")]
public class WalletCierreDiarioComercioController(WalletCierreDiarioComercioService svc) : ControllerBase
{
    private bool TryGetUsuarioId(out long id) =>
        long.TryParse(User.FindFirst("idUsuario")?.Value, out id) && id > 0;

    // Solo ADMIN_COMERCIO puede previsualizar/generar — validado dentro del
    // servicio (RequireAdminComercioAsync), nunca solo por visibilidad de UI.
    [HttpGet("preview")]
    public async Task<IActionResult> Preview([FromQuery] DateOnly fecha)
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var data = await svc.PreviewAsync(uid, fecha);
            return Ok(new { success = true, data });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
    }

    [HttpPost("generar")]
    public async Task<IActionResult> Generar([FromBody] GenerarCierreDiarioRequest req)
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var data = await svc.GenerarCierreAsync(uid, req);
            return Ok(new { success = true, data });
        }
        catch (CierreDuplicadoException ex) { return Conflict(new { success = false, message = ex.Message }); }
        catch (CajasOperativasPendientesException ex)
        {
            return Conflict(new { success = false, message = ex.Message, cantidadCajasOperativas = ex.CantidadCajasOperativas });
        }
        catch (OperacionCajaCierreConcurrenteException ex) { return Conflict(new { success = false, message = ex.Message }); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
    }

    [HttpGet("mis-cierres")]
    public async Task<IActionResult> MisCierres(
        [FromQuery] DateOnly? desde = null, [FromQuery] DateOnly? hasta = null, [FromQuery] string? estado = null)
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var data = await svc.GetMisCierresAsync(uid, desde, hasta, estado);
            return Ok(new { success = true, data });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
    }

    [HttpGet("{idCierre:long}")]
    public async Task<IActionResult> Detalle(long idCierre)
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var data = await svc.GetDetalleComercioAsync(uid, idCierre);
            return Ok(new { success = true, data });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
    }
}
