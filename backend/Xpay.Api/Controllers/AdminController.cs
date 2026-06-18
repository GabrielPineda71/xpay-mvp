using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly AdminService _adminService;

    public AdminController(AdminService adminService)
    {
        _adminService = adminService;
    }

    [HttpGet("wallets")]
    public async Task<IActionResult> ListarWallets(
        [FromQuery] string? tipoWallet = null,
        [FromQuery] string? estado     = null,
        [FromQuery] long?   idPersona  = null,
        [FromQuery] int     page       = 1,
        [FromQuery] int     pageSize   = 20)
    {
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
}
