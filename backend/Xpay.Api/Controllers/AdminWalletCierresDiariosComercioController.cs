using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.DTOs;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
[Route("api/admin/wallet-cierres-comercio")]
public class AdminWalletCierresDiariosComercioController(WalletCierreDiarioComercioService svc) : ControllerBase
{
    private bool TryGetUsuarioId(out long id) =>
        long.TryParse(User.FindFirst("idUsuario")?.Value, out id) && id > 0;

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] DateOnly? fechaDesde = null,
        [FromQuery] DateOnly? fechaHasta = null,
        [FromQuery] long?     idComercio = null,
        [FromQuery] string?   estado     = null)
    {
        var data = await svc.ListarAdminAsync(fechaDesde, fechaHasta, idComercio, estado);
        return Ok(new { success = true, data });
    }

    [HttpGet("{idCierre:long}")]
    public async Task<IActionResult> Detalle(long idCierre)
    {
        try
        {
            var data = await svc.GetDetalleAdminAsync(idCierre);
            return Ok(new { success = true, data });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
    }

    [HttpPost("{idCierre:long}/revisar")]
    public async Task<IActionResult> Revisar(long idCierre, [FromBody] RevisarCierreDiarioRequest req)
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var data = await svc.RevisarAsync(uid, idCierre, req);
            return Ok(new { success = true, data });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
    }

    [HttpPost("{idCierre:long}/cerrar")]
    public async Task<IActionResult> Cerrar(long idCierre, [FromBody] CerrarCierreDiarioRequest req)
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var data = await svc.CerrarAsync(uid, idCierre, req);
            return Ok(new { success = true, data });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
    }
}
