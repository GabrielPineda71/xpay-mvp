using Microsoft.AspNetCore.Mvc;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Route("api/reportes")]
public class ReportesController : ControllerBase
{
    private readonly ReportesService _reportes;

    public ReportesController(ReportesService reportes) => _reportes = reportes;

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
        try
        {
            var data = await _reportes.GetResumenGeneralAsync();
            return Ok(new { success = true, data });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno consultando resumen general." }); }
    }
}
