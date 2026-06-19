using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly AdminService    _adminService;
    private readonly AuditLogService _audit;

    public AdminController(AdminService adminService, AuditLogService audit)
    {
        _adminService = adminService;
        _audit        = audit;
    }

    [HttpGet("wallets")]
    public async Task<IActionResult> ListarWallets(
        [FromQuery] string? tipoWallet = null,
        [FromQuery] string? estado     = null,
        [FromQuery] long?   idPersona  = null,
        [FromQuery] int     page       = 1,
        [FromQuery] int     pageSize   = 20)
    {
        _audit.LogSensitiveAction(HttpContext, "ADMIN_WALLETS_ACCESS",
            new { tipoWallet, estado, page, pageSize });
        try
        {
            var data = await _adminService.ListarWalletsAsync(tipoWallet, estado, idPersona, page, pageSize);
            return Ok(new { success = true, data });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno listando las wallets." }); }
    }

    [HttpGet("comercios")]
    public async Task<IActionResult> ListarComercios(
        [FromQuery] string? estado   = null,
        [FromQuery] string? texto    = null,
        [FromQuery] int     page     = 1,
        [FromQuery] int     pageSize = 20)
    {
        try
        {
            var data = await _adminService.ListarComerciosAsync(estado, texto, page, pageSize);
            return Ok(new { success = true, data });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno listando los comercios." }); }
    }

    [HttpGet("ventas-qr")]
    public async Task<IActionResult> ListarVentasQr(
        [FromQuery] string?   estado     = null,
        [FromQuery] long?     idComercio = null,
        [FromQuery] long?     idTienda   = null,
        [FromQuery] DateTime? desde      = null,
        [FromQuery] DateTime? hasta      = null,
        [FromQuery] int       page       = 1,
        [FromQuery] int       pageSize   = 20)
    {
        _audit.LogSensitiveAction(HttpContext, "ADMIN_VENTAS_QR_ACCESS",
            new { estado, idComercio, page, pageSize });
        try
        {
            var data = await _adminService.ListarVentasQrAsync(estado, idComercio, idTienda, desde, hasta, page, pageSize);
            return Ok(new { success = true, data });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno listando las ventas QR." }); }
    }

    [HttpGet("ledger-transacciones")]
    public async Task<IActionResult> ListarLedgerTransacciones(
        [FromQuery] string?   tipoTransaccion = null,
        [FromQuery] DateTime? desde           = null,
        [FromQuery] DateTime? hasta           = null,
        [FromQuery] int       page            = 1,
        [FromQuery] int       pageSize        = 20)
    {
        _audit.LogSensitiveAction(HttpContext, "ADMIN_LEDGER_ACCESS",
            new { tipoTransaccion, page, pageSize });
        try
        {
            var data = await _adminService.ListarLedgerTransaccionesAsync(tipoTransaccion, desde, hasta, page, pageSize);
            return Ok(new { success = true, data });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno listando las transacciones ledger." }); }
    }
}
