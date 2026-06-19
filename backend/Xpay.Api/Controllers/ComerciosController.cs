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
    private readonly AuditLogService            _audit;

    public ComerciosController(LiquidacionComercioService liquidacionService, RetiroComercioService retiroService, AuditLogService audit)
    {
        _liquidacionService = liquidacionService;
        _retiroService      = retiroService;
        _audit              = audit;
    }

    [HttpGet("retiros")]
    public async Task<IActionResult> ListarRetiros(
        [FromQuery] string?   estado     = null,
        [FromQuery] long?     idComercio = null,
        [FromQuery] DateTime? desde      = null,
        [FromQuery] DateTime? hasta      = null,
        [FromQuery] int       page       = 1,
        [FromQuery] int       pageSize   = 20)
    {
        try
        {
            var data = await _retiroService.ListarRetirosAsync(estado, idComercio, desde, hasta, page, pageSize);
            return Ok(new { success = true, data });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno listando los retiros." }); }
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
        _audit.LogSensitiveAction(HttpContext, "QR_SETTLEMENT_ATTEMPT",
            new { idVentaQr = request.IdVentaQr });
        try
        {
            var liq = await _liquidacionService.LiquidarVentaQrAsync(request);
            _audit.LogSensitiveAction(HttpContext, "QR_SETTLEMENT_SUCCESS",
                new { idLiquidacion = liq.IdLiquidacion, idVentaQr = request.IdVentaQr, idComercio = liq.IdComercio, valorNeto = liq.ValorNeto });
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
        // metadata segura: sin NumeroCuenta, TitularCuenta ni DocumentoTitular
        _audit.LogSensitiveAction(HttpContext, "MERCHANT_WITHDRAWAL_REQUEST_ATTEMPT",
            new { idComercio = request.IdComercio, valor = request.Valor, medioRetiro = request.MedioRetiro });
        try
        {
            var retiro = await _retiroService.SolicitarRetiroAsync(request);
            _audit.LogSensitiveAction(HttpContext, "MERCHANT_WITHDRAWAL_REQUEST_SUCCESS",
                new { idRetiro = retiro.IdRetiro, idComercio = retiro.IdComercio, valor = retiro.Valor });
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
        _audit.LogSensitiveAction(HttpContext, "MERCHANT_WITHDRAWAL_PAID_ATTEMPT",
            new { idRetiro = request.IdRetiro });
        try
        {
            var retiro = await _retiroService.ConfirmarRetiroPagadoAsync(request);
            _audit.LogSensitiveAction(HttpContext, "MERCHANT_WITHDRAWAL_PAID_SUCCESS",
                new { idRetiro = retiro.IdRetiro, valor = retiro.Valor });
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
        _audit.LogSensitiveAction(HttpContext, "MERCHANT_WITHDRAWAL_REJECTED_ATTEMPT",
            new { idRetiro = request.IdRetiro });
        try
        {
            var retiro = await _retiroService.RechazarRetiroAsync(request);
            _audit.LogSensitiveAction(HttpContext, "MERCHANT_WITHDRAWAL_REJECTED_SUCCESS",
                new { idRetiro = retiro.IdRetiro, valor = retiro.Valor });
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
