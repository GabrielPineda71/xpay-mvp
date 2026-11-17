using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.Common;
using Xpay.Api.Integrations.Passport;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Authorize(Policy = "KycAprobado")]
[Route("api/wallets/mi-wallet/recarga-breb")]
public sealed class WalletRecargaBrebController(WalletRecargaBrebQrService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Obtener(CancellationToken ct)
    {
        if (!Identificar(out var idPersona, out var idUsuario))
            return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            return Ok(new { success = true, data = await service.ObtenerAsync(idPersona, idUsuario, ct) });
        }
        catch (KeyNotFoundException)
        { return NotFound(new { success = false, message = "No se encontró una Wallet activa." }); }
        catch (InvalidOperationException ex)
        { return Conflict(new { success = false, message = ex.Message }); }
        catch (PassportConfigurationException)
        { return StatusCode(503, new { success = false, message = "La recarga por Bre-B aún no está configurada." }); }
    }

    [HttpPost]
    [OperacionFinanciera]
    public async Task<IActionResult> Generar(CancellationToken ct)
    {
        if (!Identificar(out var idPersona, out var idUsuario))
            return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            return Ok(new { success = true, data = await service.GenerarAsync(idPersona, idUsuario, ct) });
        }
        catch (KeyNotFoundException)
        { return NotFound(new { success = false, message = "No se encontró una Wallet activa." }); }
        catch (InvalidOperationException ex)
        { return Conflict(new { success = false, message = ex.Message }); }
        catch (PassportConfigurationException)
        { return StatusCode(503, new { success = false, message = "La recarga por Bre-B aún no está configurada." }); }
        catch (PassportException)
        { return StatusCode(502, new { success = false, message = "No pudimos confirmar el QR con Passport. Intenta de nuevo en un momento." }); }
    }

    private bool Identificar(out long idPersona, out long idUsuario)
    {
        idUsuario = 0;
        return long.TryParse(User.FindFirst("idPersona")?.Value, out idPersona) && idPersona > 0
            && long.TryParse(User.FindFirst("idUsuario")?.Value, out idUsuario) && idUsuario > 0;
    }
}
