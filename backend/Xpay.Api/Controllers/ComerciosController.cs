using Microsoft.AspNetCore.Mvc;
using Xpay.Api.DTOs;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Route("api/comercios")]
public class ComerciosController : ControllerBase
{
    private readonly LiquidacionComercioService _liquidacionService;
    public ComerciosController(LiquidacionComercioService liquidacionService) => _liquidacionService = liquidacionService;

    [HttpPost("liquidar-venta-qr")]
    public async Task<IActionResult> LiquidarVentaQr([FromBody] LiquidarVentaQrRequest request)
    {
        try
        {
            var liq = await _liquidacionService.LiquidarVentaQrAsync(request);
            return Ok(new
            {
                success = true,
                message = "Venta QR liquidada exitosamente.",
                data = new
                {
                    idLiquidacion    = liq.IdLiquidacion,
                    idVentaQr        = request.IdVentaQr,
                    idComercio       = liq.IdComercio,
                    idWalletComercio = liq.IdWalletComercio,
                    valorNeto        = liq.ValorNeto,
                    estadoVenta      = "LIQUIDADA"
                }
            });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno procesando la liquidación." }); }
    }
}
