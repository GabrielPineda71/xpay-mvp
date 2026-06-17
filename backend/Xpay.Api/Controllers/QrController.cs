using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.DTOs;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/qr")]
public class QrController : ControllerBase
{
    private readonly PagoQrService _pagoQrService;
    public QrController(PagoQrService pagoQrService) => _pagoQrService = pagoQrService;

    [HttpPost("pagar")]
    public async Task<IActionResult> Pagar([FromBody] PagoQrRequest request)
    {
        try
        {
            var venta = await _pagoQrService.PagarQrAsync(request);
            return Ok(new
            {
                success = true,
                message = "Pago QR realizado exitosamente.",
                data = new
                {
                    idVentaQr       = venta.IdVentaQr,
                    idTransaccion   = venta.IdTransaccionLedger,
                    idComercio      = venta.IdComercio,
                    idTienda        = venta.IdTienda,
                    idWalletUsuario = venta.IdWalletUsuario,
                    valor           = venta.ValorBruto,
                    estado          = venta.Estado
                }
            });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno procesando el pago QR." }); }
    }
}
