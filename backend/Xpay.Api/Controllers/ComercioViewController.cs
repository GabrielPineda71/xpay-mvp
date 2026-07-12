using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Authorize(Roles = "COMERCIO")]
[Route("api/comercio")]
public class ComercioViewController : ControllerBase
{
    private readonly ComercioScopeService _scope;

    public ComercioViewController(ComercioScopeService scope)
    {
        _scope = scope;
    }

    private bool TryGetUsuarioId(out long id) =>
        long.TryParse(User.FindFirst("idUsuario")?.Value, out id) && id > 0;

    // ── Scope ──────────────────────────────────────────────────────────────────

    [HttpGet("mi-scope")]
    public async Task<IActionResult> GetMiScope()
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var s = await _scope.GetScopeAsync(uid);
            if (s == null) return Ok(new { success = true, data = (object?)null, message = "Sin acceso operativo activo." });
            return Ok(new { success = true, data = s });
        }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    // ── Dashboard ──────────────────────────────────────────────────────────────

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var s = await _scope.RequireScopeAsync(uid);
            return Ok(new { success = true, data = await _scope.GetDashboardAsync(s) });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    // ── Totales ────────────────────────────────────────────────────────────────

    [HttpGet("totales")]
    public async Task<IActionResult> GetTotales([FromQuery] string? fechaDesde, [FromQuery] string? fechaHasta)
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var s = await _scope.RequireScopeAsync(uid);
            if (!s.PuedeVerTodoComercio && !s.IdEstablecimiento.HasValue)
                return BadRequest(new { success = false, message = "CAJERO sin sede asignada no puede ver totales." });
            return Ok(new { success = true, data = await _scope.GetTotalesAsync(s, fechaDesde, fechaHasta) });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    // ── Ventas ─────────────────────────────────────────────────────────────────

    [HttpGet("ventas")]
    public async Task<IActionResult> GetVentas(
        [FromQuery] long? filtroSede, [FromQuery] long? filtroCajero,
        [FromQuery] string? fechaDesde, [FromQuery] string? fechaHasta)
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var s = await _scope.RequireScopeAsync(uid);
            if (!s.PuedeVerTodoComercio && filtroSede.HasValue && filtroSede != s.IdEstablecimiento)
                return Forbid();
            return Ok(new { success = true, data = await _scope.ListarVentasAsync(s, filtroSede, filtroCajero, fechaDesde, fechaHasta) });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }
}
