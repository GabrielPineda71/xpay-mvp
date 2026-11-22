using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xpay.Api.Common;
using Xpay.Api.DTOs;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/usuarios/preparacion-wallet")]
public sealed class PreparacionWalletController(PreparacionWalletService servicio) : ControllerBase
{
    [HttpGet]
    [CuentaLimitadaPermitida]
    public Task<IActionResult> Estado(CancellationToken ct) => Ejecutar(id => servicio.ObtenerAsync(id, ct));

    [HttpPatch("datos")]
    [CuentaLimitadaPermitida]
    public Task<IActionResult> Datos(PreparacionDatosRequest request, CancellationToken ct) =>
        Ejecutar(id => servicio.GuardarDatosAsync(id, request, ct));

    [HttpPost("identidad/actualizar")]
    [CuentaLimitadaPermitida]
    [EnableRateLimiting("CambiarClavePolicy")]
    public Task<IActionResult> ActualizarIdentidad([FromServices] SincronizacionDocumentoVeriffService sincronizacion, CancellationToken ct) =>
        Ejecutar(async id => { await sincronizacion.SincronizarAsync(id, ct); return await servicio.ObtenerAsync(id, ct); });

    [HttpPost("activar")]
    [CuentaLimitadaPermitida]
    [EnableRateLimiting("CambiarClavePolicy")]
    public Task<IActionResult> Activar(ActivarWalletRequest request, CancellationToken ct) =>
        Ejecutar(id => servicio.ActivarAsync(id, request, HttpContext.Connection.RemoteIpAddress?.ToString(), ct));

    private async Task<IActionResult> Ejecutar<T>(Func<long, Task<T>> accion)
    {
        if (!long.TryParse(User.FindFirst("idUsuario")?.Value, out var id) || id <= 0)
            return Unauthorized(new { success = false, message = "Token inválido." });
        try { return Ok(new { success = true, data = await accion(id) }); }
        catch (PreparacionWalletNoDisponibleException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return StatusCode(403, new { success = false, message = "La cuenta no está disponible para este proceso." }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
    }
}
