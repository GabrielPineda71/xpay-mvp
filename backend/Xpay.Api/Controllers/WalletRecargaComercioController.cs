using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.DTOs;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Authorize(Roles = "COMERCIO")]
[Route("api/comercio/wallet-recargas")]
public class WalletRecargaComercioController(WalletRecargaComercioService svc) : ControllerBase
{
    private bool TryGetUsuarioId(out long id) =>
        long.TryParse(User.FindFirst("idUsuario")?.Value, out id) && id > 0;

    [HttpGet("buscar-usuario")]
    public async Task<IActionResult> BuscarUsuario([FromQuery] string? query)
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        var data = await svc.BuscarUsuariosAsync(query);
        return Ok(new { success = true, data });
    }

    [HttpPost]
    public async Task<IActionResult> Recargar([FromBody] RecargarWalletComercioRequest req)
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var data = await svc.RecargarWalletAsync(req, uid);
            return Ok(new { success = true, data });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
    }

    [HttpGet("mis-recargas")]
    public async Task<IActionResult> MisRecargas([FromQuery] DateTime? fechaDesde, [FromQuery] DateTime? fechaHasta)
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var data = await svc.GetMisRecargasAsync(uid, fechaDesde, fechaHasta);
            return Ok(new { success = true, data });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }
}
