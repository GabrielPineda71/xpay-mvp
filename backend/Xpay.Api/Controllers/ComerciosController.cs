using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.DTOs;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/comercios")]
public class ComerciosController : ControllerBase
{
    private readonly LiquidacionComercioService _liquidacionService;
    private readonly RetiroComercioService      _retiroService;

    public ComerciosController(LiquidacionComercioService liquidacionService, RetiroComercioService retiroService)
    {
        _liquidacionService = liquidacionService;
        _retiroService      = retiroService;
    }

    [HttpGet("retiros/{idRetiro}")]
    public async Task<IActionResult> GetRetiro(long idRetiro)
    {
        try
        {
            var data = await _retiroService.GetRetiroByIdAsync(idRetiro);
            return Ok(new { success = true, data });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno consultando el retiro." }); }
    }

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

    [HttpPost("solicitar-retiro")]
    public async Task<IActionResult> SolicitarRetiro([FromBody] SolicitarRetiroComercioRequest request)
    {
        try
        {
            var retiro = await _retiroService.SolicitarRetiroAsync(request);
            return Ok(new
            {
                success = true,
                message = "Solicitud de retiro creada exitosamente.",
                data = new
                {
                    idRetiro         = retiro.IdRetiro,
                    idComercio       = retiro.IdComercio,
                    idWalletComercio = retiro.IdWalletComercio,
                    valor            = retiro.Valor,
                    estado           = retiro.Estado
                }
            });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno procesando la solicitud de retiro." }); }
    }

    [HttpPost("retiros/confirmar-pago")]
    public async Task<IActionResult> ConfirmarPago([FromBody] ConfirmarRetiroComercioRequest request)
    {
        try
        {
            var retiro = await _retiroService.ConfirmarRetiroPagadoAsync(request);
            return Ok(new
            {
                success = true,
                message = "Retiro marcado como pagado exitosamente.",
                data = new
                {
                    idRetiro = retiro.IdRetiro,
                    estado   = retiro.Estado,
                    valor    = retiro.Valor
                }
            });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno confirmando el pago del retiro." }); }
    }

    [HttpPost("retiros/rechazar")]
    public async Task<IActionResult> RechazarRetiro([FromBody] RechazarRetiroComercioRequest request)
    {
        try
        {
            var retiro = await _retiroService.RechazarRetiroAsync(request);
            return Ok(new
            {
                success = true,
                message = "Retiro rechazado exitosamente.",
                data = new
                {
                    idRetiro = retiro.IdRetiro,
                    estado   = retiro.Estado,
                    valor    = retiro.Valor
                }
            });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno rechazando el retiro." }); }
    }
}
