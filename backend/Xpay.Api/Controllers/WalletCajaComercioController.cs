using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.DTOs;
using Xpay.Api.Exceptions;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Authorize(Roles = "COMERCIO")]
[Route("api/comercio/cajas")]
public class WalletCajaComercioController(WalletCajaComercioService svc) : ControllerBase
{
    private bool TryGetUsuarioId(out long id) =>
        long.TryParse(User.FindFirst("idUsuario")?.Value, out id) && id > 0;

    [HttpGet("mi-caja-actual")]
    public async Task<IActionResult> MiCajaActual()
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var data = await svc.GetCajaActualAsync(uid);
            return Ok(new { success = true, data });
        }
        catch (ScopeComercioAmbiguoException ex) { return Conflict(new { success = false, message = ex.Message }); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpPost("abrir")]
    public async Task<IActionResult> Abrir([FromBody] AbrirCajaRequest req)
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var data = await svc.AbrirAsync(uid, req);
            return Ok(new { success = true, data });
        }
        catch (CajaDuplicadaException ex) { return Conflict(new { success = false, message = ex.Message }); }
        catch (ScopeComercioAmbiguoException ex) { return Conflict(new { success = false, message = ex.Message }); }
        catch (TransicionCajaInvalidaException ex) { return Conflict(new { success = false, message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
    }

    [HttpPatch("{idCaja:long}/fondo-inicial")]
    public async Task<IActionResult> CorregirFondoInicial(long idCaja, [FromBody] CorregirFondoInicialRequest req)
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var data = await svc.CorregirFondoInicialAsync(uid, idCaja, req);
            return Ok(new { success = true, data });
        }
        catch (CajaVencidaException ex) { return Conflict(new { success = false, message = ex.Message, data = ex.CajaActual }); }
        catch (ScopeComercioAmbiguoException ex) { return Conflict(new { success = false, message = ex.Message }); }
        catch (TransicionCajaInvalidaException ex) { return Conflict(new { success = false, message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
    }

    [HttpPost("{idCaja:long}/iniciar-cuadre")]
    public async Task<IActionResult> IniciarCuadre(long idCaja)
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var data = await svc.IniciarCuadreAsync(uid, idCaja);
            return Ok(new { success = true, data });
        }
        catch (CajaVencidaException ex) { return Conflict(new { success = false, message = ex.Message, data = ex.CajaActual }); }
        catch (ScopeComercioAmbiguoException ex) { return Conflict(new { success = false, message = ex.Message }); }
        catch (TransicionCajaInvalidaException ex) { return Conflict(new { success = false, message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
    }

    [HttpPost("{idCaja:long}/cerrar")]
    public async Task<IActionResult> Cerrar(long idCaja, [FromBody] CerrarCajaRequest req)
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var data = await svc.CerrarAsync(uid, idCaja, req);
            return Ok(new { success = true, data });
        }
        catch (CajaVencidaException ex) { return Conflict(new { success = false, message = ex.Message, data = ex.CajaActual }); }
        catch (ScopeComercioAmbiguoException ex) { return Conflict(new { success = false, message = ex.Message }); }
        catch (TransicionCajaInvalidaException ex) { return Conflict(new { success = false, message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
    }

    [HttpPost("{idCaja:long}/revisar")]
    public async Task<IActionResult> Revisar(long idCaja, [FromBody] RevisarCajaRequest req)
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var data = await svc.RevisarAsync(uid, idCaja, req);
            return Ok(new { success = true, data });
        }
        catch (ScopeComercioAmbiguoException ex) { return Conflict(new { success = false, message = ex.Message }); }
        catch (TransicionCajaInvalidaException ex) { return Conflict(new { success = false, message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] long? idEstablecimiento = null,
        [FromQuery] string? estado = null,
        [FromQuery] DateOnly? desde = null,
        [FromQuery] DateOnly? hasta = null)
    {
        if (!TryGetUsuarioId(out var uid)) return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var data = await svc.ListarAsync(uid, page, pageSize, idEstablecimiento, estado, desde, hasta);
            return Ok(new { success = true, data });
        }
        catch (ScopeComercioAmbiguoException ex) { return Conflict(new { success = false, message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }
}
