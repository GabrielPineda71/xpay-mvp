using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.Common;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/cartera-ordinaria")]
public sealed class CarteraPilotoController(CarteraPilotoService piloto) : ControllerBase
{
    // Solo informa el siguiente paso del titular. Nunca reserva una plaza,
    // registra autorización de crédito o realiza una consulta al proveedor.
    [HttpGet("disponibilidad")]
    [CuentaLimitadaPermitida]
    public async Task<IActionResult> Disponibilidad()
    {
        if (!long.TryParse(User.FindFirst("idUsuario")?.Value, out var id) || id <= 0) return Unauthorized();
        return Ok(await piloto.ObtenerDisponibilidadAsync(id, HttpContext.RequestAborted));
    }
}
