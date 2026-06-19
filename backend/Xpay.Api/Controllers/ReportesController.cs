using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/reportes")]
public class ReportesController : ControllerBase
{
    private readonly ReportesService _reportes;
    private readonly AuditLogService _audit;

    public ReportesController(ReportesService reportes, AuditLogService audit)
    {
        _reportes = reportes;
        _audit    = audit;
    }

    [HttpGet("wallet/{idWallet}/estado-cuenta")]
    public async Task<IActionResult> EstadoCuentaWallet(long idWallet)
    {
        try
        {
            var data = await _reportes.GetEstadoCuentaWalletAsync(idWallet);
            return Ok(new { success = true, data });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno consultando estado de cuenta." }); }
    }

    [HttpGet("comercios/{idComercio}/resumen")]
    public async Task<IActionResult> ResumenComercio(long idComercio)
    {
        try
        {
            var data = await _reportes.GetResumenComercioAsync(idComercio);
            return Ok(new { success = true, data });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno consultando resumen de comercio." }); }
    }

    [HttpGet("ledger/transaccion/{idTransaccion}")]
    public async Task<IActionResult> LedgerTransaccion(long idTransaccion)
    {
        try
        {
            var data = await _reportes.GetLedgerTransaccionAsync(idTransaccion);
            return Ok(new { success = true, data });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno consultando transacción ledger." }); }
    }

    [HttpGet("operaciones/resumen-general")]
    public async Task<IActionResult> ResumenGeneral()
    {
        _audit.LogSensitiveAction(HttpContext, "ADMIN_REPORT_ACCESS");
        try
        {
            var data = await _reportes.GetResumenGeneralAsync();
            return Ok(new { success = true, data });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno consultando resumen general." }); }
    }
}
