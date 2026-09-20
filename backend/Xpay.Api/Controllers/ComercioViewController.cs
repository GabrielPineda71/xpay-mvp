using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.DTOs;
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
        [FromQuery] string? fechaDesde, [FromQuery] string? fechaHasta,
        // XPAY-438 — modo notificación operacional commerce-wide: presente
        // → ignora filtroSede/filtroCajero/fechaDesde/fechaHasta (sección 3
        // del ticket); ausente → comportamiento histórico sin cambios.
        [FromQuery] long? desdeIdVentaQr = null)
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var s = await _scope.RequireScopeAsync(uid);
            if (!s.PuedeVerTodoComercio && filtroSede.HasValue && filtroSede != s.IdEstablecimiento)
                return Forbid();
            return Ok(new { success = true, data = await _scope.ListarVentasAsync(s, filtroSede, filtroCajero, fechaDesde, fechaHasta, desdeIdVentaQr) });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    // XPAY-438A §2 — baseline de primer uso de la notificación operacional
    // QR, sin descargar historial (corrige el riesgo real de que un
    // comercio con >2.000 VentaQr dejara el baseline en una venta antigua
    // al drenar por páginas — ver CommerceNotificationsContext.tsx).
    [HttpGet("ventas/ultimo-id")]
    public async Task<IActionResult> GetUltimoIdVentaQr()
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var s = await _scope.RequireScopeAsync(uid);
            var idVentaQr = await _scope.ObtenerUltimoIdVentaQrAsync(s);
            return Ok(new { success = true, data = new UltimoIdVentaQrResponse(idVentaQr) });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    // ── QR del comercio ──────────────────────────────────────────────────────

    // XPAY-447 — fix del bug confirmado en XPAY-446: hasta ahora no existía
    // ningún endpoint para que un comercio consultara su(s) propio(s) QR;
    // MiComercioPage.tsx mostraba un código hardcodeado ajeno al comercio
    // autenticado. IdComercio se resuelve exclusivamente desde el scope
    // server-side (RequireScopeAsync) — nunca aceptado del cliente. Devuelve
    // TODOS los QR activos (lista, no "el primero" — ver ComercioScopeService.
    // ObtenerQrComercioAsync).
    [HttpGet("mi-qr")]
    public async Task<IActionResult> GetMiQr()
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var s = await _scope.RequireScopeAsync(uid);
            var qrs = await _scope.ObtenerQrComercioAsync(s);
            return Ok(new { success = true, data = qrs });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }
}
