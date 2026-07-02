using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.DTOs;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
public class LibranzaController : ControllerBase
{
    private readonly LibranzaService  _libranza;
    private readonly AuditLogService  _audit;

    public LibranzaController(LibranzaService libranza, AuditLogService audit)
    {
        _libranza = libranza;
        _audit    = audit;
    }

    private bool TryGetIdUsuario(out long id) =>
        long.TryParse(User.FindFirst("idUsuario")?.Value, out id) && id > 0;

    // ──────────────────────────────────────────────────────────────────────
    // CONVENIOS
    // ──────────────────────────────────────────────────────────────────────

    [HttpGet("api/libranza/admin/convenios")]
    public async Task<IActionResult> ListarConvenios()
    {
        try
        {
            var lista = await _libranza.ListarConveniosAsync();
            return Ok(new { success = true, data = lista });
        }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    [HttpGet("api/libranza/admin/convenios/{id:long}")]
    public async Task<IActionResult> GetConvenio(long id)
    {
        try
        {
            var c = await _libranza.GetConvenioAsync(id);
            if (c is null) return NotFound(new { success = false, message = $"Convenio {id} no encontrado." });
            return Ok(new { success = true, data = c });
        }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    [HttpPost("api/libranza/admin/convenios")]
    public async Task<IActionResult> CrearConvenio([FromBody] CrearConvenioRequest request)
    {
        if (!TryGetIdUsuario(out var adminId))
            return Unauthorized(new { success = false, message = "Token inválido." });

        _audit.LogSensitiveAction(HttpContext, "LIBRANZA_CONVENIO_CREAR_ATTEMPT",
            new { nit = request.Nit, nombre = request.NombreEmpresa, adminId });
        try
        {
            var c = await _libranza.CrearConvenioAsync(request, adminId);
            _audit.LogSensitiveAction(HttpContext, "LIBRANZA_CONVENIO_CREAR_OK", new { idConvenio = c.IdConvenio });
            return Ok(new { success = true, data = c });
        }
        catch (InvalidOperationException ex)
        { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno creando convenio." }); }
    }

    [HttpPut("api/libranza/admin/convenios/{id:long}")]
    public async Task<IActionResult> ActualizarConvenio(long id, [FromBody] ActualizarConvenioRequest request)
    {
        if (!TryGetIdUsuario(out var adminId))
            return Unauthorized(new { success = false, message = "Token inválido." });

        _audit.LogSensitiveAction(HttpContext, "LIBRANZA_CONVENIO_ACTUALIZAR_ATTEMPT", new { idConvenio = id, adminId });
        try
        {
            var c = await _libranza.ActualizarConvenioAsync(id, request, adminId);
            _audit.LogSensitiveAction(HttpContext, "LIBRANZA_CONVENIO_ACTUALIZAR_OK", new { idConvenio = id });
            return Ok(new { success = true, data = c });
        }
        catch (KeyNotFoundException ex)
        { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex)
        { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno actualizando convenio." }); }
    }

    // ──────────────────────────────────────────────────────────────────────
    // PARÁMETROS
    // ──────────────────────────────────────────────────────────────────────

    [HttpGet("api/libranza/admin/convenios/{id:long}/parametros")]
    public async Task<IActionResult> ListarParametros(long id)
    {
        try
        {
            var lista = await _libranza.ListarParametrosAsync(id);
            return Ok(new { success = true, data = lista });
        }
        catch (KeyNotFoundException ex)
        { return NotFound(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    [HttpPost("api/libranza/admin/convenios/{id:long}/parametros")]
    public async Task<IActionResult> CrearParametros(long id, [FromBody] CrearParametrosRequest request)
    {
        if (!TryGetIdUsuario(out var adminId))
            return Unauthorized(new { success = false, message = "Token inválido." });

        _audit.LogSensitiveAction(HttpContext, "LIBRANZA_PARAMETROS_CREAR_ATTEMPT", new { idConvenio = id, adminId });
        try
        {
            var p = await _libranza.CrearParametrosAsync(id, request, adminId);
            _audit.LogSensitiveAction(HttpContext, "LIBRANZA_PARAMETROS_CREAR_OK", new { idParametro = p.IdParametro });
            return Ok(new { success = true, data = p });
        }
        catch (KeyNotFoundException ex)
        { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex)
        { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno creando parámetros." }); }
    }

    [HttpPut("api/libranza/admin/parametros/{id:long}")]
    public async Task<IActionResult> ActualizarParametros(long id, [FromBody] ActualizarParametrosRequest request)
    {
        if (!TryGetIdUsuario(out var adminId))
            return Unauthorized(new { success = false, message = "Token inválido." });

        _audit.LogSensitiveAction(HttpContext, "LIBRANZA_PARAMETROS_ACTUALIZAR_ATTEMPT", new { idParametro = id, adminId });
        try
        {
            var p = await _libranza.ActualizarParametrosAsync(id, request, adminId);
            _audit.LogSensitiveAction(HttpContext, "LIBRANZA_PARAMETROS_ACTUALIZAR_OK", new { idParametro = id });
            return Ok(new { success = true, data = p });
        }
        catch (KeyNotFoundException ex)
        { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex)
        { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno actualizando parámetros." }); }
    }

    // ──────────────────────────────────────────────────────────────────────
    // RANGOS DE COBRO
    // ──────────────────────────────────────────────────────────────────────

    [HttpGet("api/libranza/admin/convenios/{id:long}/rangos")]
    public async Task<IActionResult> ListarRangos(long id)
    {
        try
        {
            var lista = await _libranza.ListarRangosAsync(id);
            return Ok(new { success = true, data = lista });
        }
        catch (KeyNotFoundException ex)
        { return NotFound(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    [HttpPost("api/libranza/admin/convenios/{id:long}/rangos")]
    public async Task<IActionResult> CrearRango(long id, [FromBody] CrearRangoRequest request)
    {
        if (!TryGetIdUsuario(out var adminId))
            return Unauthorized(new { success = false, message = "Token inválido." });

        _audit.LogSensitiveAction(HttpContext, "LIBRANZA_RANGO_CREAR_ATTEMPT", new { idConvenio = id, adminId });
        try
        {
            var r = await _libranza.CrearRangoAsync(id, request, adminId);
            _audit.LogSensitiveAction(HttpContext, "LIBRANZA_RANGO_CREAR_OK", new { idRango = r.IdRango });
            return Ok(new { success = true, data = r });
        }
        catch (KeyNotFoundException ex)
        { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex)
        { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno creando rango." }); }
    }

    [HttpPut("api/libranza/admin/rangos/{id:long}")]
    public async Task<IActionResult> ActualizarRango(long id, [FromBody] ActualizarRangoRequest request)
    {
        if (!TryGetIdUsuario(out var adminId))
            return Unauthorized(new { success = false, message = "Token inválido." });

        _audit.LogSensitiveAction(HttpContext, "LIBRANZA_RANGO_ACTUALIZAR_ATTEMPT", new { idRango = id, adminId });
        try
        {
            var r = await _libranza.ActualizarRangoAsync(id, request, adminId);
            _audit.LogSensitiveAction(HttpContext, "LIBRANZA_RANGO_ACTUALIZAR_OK", new { idRango = id });
            return Ok(new { success = true, data = r });
        }
        catch (KeyNotFoundException ex)
        { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex)
        { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno actualizando rango." }); }
    }
}
