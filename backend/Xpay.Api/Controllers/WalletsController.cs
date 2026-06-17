using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.DTOs;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/wallets")]
public class WalletsController : ControllerBase
{
    private readonly WalletService _walletService;
    private readonly WalletOperacionService _walletOperacionService;

    public WalletsController(WalletService walletService, WalletOperacionService walletOperacionService)
    {
        _walletService = walletService;
        _walletOperacionService = walletOperacionService;
    }

    [HttpGet("persona/{idPersona:long}")]
    public async Task<IActionResult> ObtenerWalletPersona(long idPersona)
    {
        var wallet = await _walletService.ObtenerWalletPersonaAsync(idPersona);
        return wallet == null ? NotFound(new { success = false, message = "No se encontró wallet activa para esta persona." }) : Ok(new { success = true, data = wallet });
    }

    [HttpGet("{idWallet:long}/saldo")]
    public async Task<IActionResult> ObtenerSaldo(long idWallet)
    {
        var saldo = await _walletService.ObtenerSaldoAsync(idWallet);
        return saldo == null ? NotFound(new { success = false, message = "No se encontró saldo para esta wallet." }) : Ok(new { success = true, data = saldo });
    }

    [HttpGet("{idWallet:long}/movimientos")]
    public async Task<IActionResult> ObtenerMovimientos(long idWallet) => Ok(new { success = true, data = await _walletService.ObtenerMovimientosAsync(idWallet) });

    [HttpPost("{idWallet:long}/recarga-manual")]
    public async Task<IActionResult> RecargarManual(long idWallet, [FromBody] RecargaWalletRequest request)
    {
        try
        {
            var idMovimiento = await _walletOperacionService.RecargarWalletManualAsync(idWallet, request);
            return Ok(new { success = true, message = "Recarga manual aplicada correctamente.", idMovimientoWallet = idMovimiento });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno aplicando recarga manual." }); }
    }

    [HttpPost("transferencia")]
    public async Task<IActionResult> Transferir([FromBody] TransferenciaWalletRequest request)
    {
        try
        {
            var idTransaccion = await _walletOperacionService.TransferirWalletAsync(request);
            return Ok(new
            {
                success = true,
                message = "Transferencia realizada exitosamente.",
                data = new
                {
                    idTransaccion,
                    idWalletOrigen  = request.IdWalletOrigen,
                    idWalletDestino = request.IdWalletDestino,
                    valor           = request.Valor
                }
            });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno procesando la transferencia." }); }
    }
}
