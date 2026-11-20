using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.Common;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/usuarios/alta-publica")]
public sealed class AltaPublicaEstadoController(HabilitacionCuentaPublicaService habilitacion) : ControllerBase
{
    [HttpGet("estado")]
    [CuentaLimitadaPermitida]
    public async Task<IActionResult> Estado(CancellationToken ct)
    {
        var ids = User.FindAll("idUsuario").ToArray();
        if (ids.Length != 1 || !long.TryParse(ids[0].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            return Unauthorized(new { success = false, message = "Token inválido." });
        try { return Ok(new { success = true, data = await habilitacion.ObtenerEstadoAsync(id, ct) }); }
        catch (AltaPublicaNoDisponibleException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Unauthorized(new { success = false, message = "Token inválido." }); }
    }
}
