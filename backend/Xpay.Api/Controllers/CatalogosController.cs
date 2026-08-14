using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

// Commit 1 — Catálogo geográfico (Fase 0, Motor de Evaluación de Crédito
// Datacrédito / onboarding móvil). Solo lectura, solo registros ACTIVO.
[ApiController]
[Authorize]
[Route("api/catalogos")]
public class CatalogosController(CatalogoGeograficoService svc) : ControllerBase
{
    [HttpGet("paises")]
    public async Task<IActionResult> Paises()
    {
        var data = await svc.GetPaisesAsync();
        return Ok(new { success = true, data });
    }

    [HttpGet("departamentos")]
    public async Task<IActionResult> Departamentos([FromQuery] long idPais)
    {
        if (idPais <= 0)
            return BadRequest(new { success = false, message = "idPais es requerido." });

        var data = await svc.GetDepartamentosAsync(idPais);
        return Ok(new { success = true, data });
    }

    [HttpGet("ciudades")]
    public async Task<IActionResult> Ciudades([FromQuery] long idDepartamento)
    {
        if (idDepartamento <= 0)
            return BadRequest(new { success = false, message = "idDepartamento es requerido." });

        var data = await svc.GetCiudadesAsync(idDepartamento);
        return Ok(new { success = true, data });
    }
}
