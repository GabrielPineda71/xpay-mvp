using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.DTOs;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
[Route("api/comercios-aliados/admin")]
public class ComerciosAliadosController : ControllerBase
{
    private readonly ComercioAliadoService _svc;

    public ComerciosAliadosController(ComercioAliadoService svc) => _svc = svc;

    private bool TryGetAdminId(out long id) =>
        long.TryParse(User.FindFirst("idUsuario")?.Value, out id) && id > 0;

    // ── Comercios ──────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        try { return Ok(new { success = true, data = await _svc.ListarAsync() }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno listando comercios aliados." }); }
    }

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] CrearComercioAliadoRequest req)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized(new { success = false, message = "Token inválido." });
        try { return Ok(new { success = true, data = await _svc.CrearAsync(req, adminId) }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno creando comercio aliado." }); }
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        try { return Ok(new { success = true, data = await _svc.GetByIdAsync(id) }); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Actualizar(long id, [FromBody] ActualizarComercioAliadoRequest req)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized(new { success = false, message = "Token inválido." });
        try { return Ok(new { success = true, data = await _svc.ActualizarAsync(id, req, adminId) }); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno actualizando comercio aliado." }); }
    }

    [HttpPost("{id:long}/activar")]
    public async Task<IActionResult> Activar(long id)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized(new { success = false, message = "Token inválido." });
        try { return Ok(new { success = true, data = await _svc.ActivarAsync(id, adminId) }); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    [HttpPost("{id:long}/inactivar")]
    public async Task<IActionResult> Inactivar(long id)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized(new { success = false, message = "Token inválido." });
        try { return Ok(new { success = true, data = await _svc.InactivarAsync(id, adminId) }); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    // ── Representantes legales ────────────────────────────────────────────────

    [HttpGet("{id:long}/representantes")]
    public async Task<IActionResult> ListarRepresentantes(long id)
    {
        try { return Ok(new { success = true, data = await _svc.ListarRepresentantesAsync(id) }); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    [HttpPost("{id:long}/representantes")]
    public async Task<IActionResult> CrearRepresentante(long id, [FromBody] CrearRepresentanteRequest req)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized(new { success = false, message = "Token inválido." });
        try { return Ok(new { success = true, data = await _svc.CrearRepresentanteAsync(id, req, adminId) }); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    [HttpPut("representantes/{idRepresentante:long}")]
    public async Task<IActionResult> ActualizarRepresentante(long idRepresentante, [FromBody] ActualizarRepresentanteRequest req)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized(new { success = false, message = "Token inválido." });
        try { return Ok(new { success = true, data = await _svc.ActualizarRepresentanteAsync(idRepresentante, req, adminId) }); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    // ── Establecimientos ──────────────────────────────────────────────────────

    [HttpGet("{id:long}/establecimientos")]
    public async Task<IActionResult> ListarEstablecimientos(long id)
    {
        try { return Ok(new { success = true, data = await _svc.ListarEstablecimientosAsync(id) }); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    [HttpPost("{id:long}/establecimientos")]
    public async Task<IActionResult> CrearEstablecimiento(long id, [FromBody] CrearEstablecimientoRequest req)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized(new { success = false, message = "Token inválido." });
        try { return Ok(new { success = true, data = await _svc.CrearEstablecimientoAsync(id, req, adminId) }); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    [HttpPut("establecimientos/{idEstablecimiento:long}")]
    public async Task<IActionResult> ActualizarEstablecimiento(long idEstablecimiento, [FromBody] ActualizarEstablecimientoRequest req)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized(new { success = false, message = "Token inválido." });
        try { return Ok(new { success = true, data = await _svc.ActualizarEstablecimientoAsync(idEstablecimiento, req, adminId) }); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    // ── Usuarios solicitados ──────────────────────────────────────────────────

    [HttpGet("{id:long}/usuarios-solicitados")]
    public async Task<IActionResult> ListarUsuariosSolicitados(long id)
    {
        try { return Ok(new { success = true, data = await _svc.ListarUsuariosSolicitadosAsync(id) }); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    [HttpPost("{id:long}/usuarios-solicitados")]
    public async Task<IActionResult> CrearUsuarioSolicitado(long id, [FromBody] CrearUsuarioSolicitadoRequest req)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized(new { success = false, message = "Token inválido." });
        try { return Ok(new { success = true, data = await _svc.CrearUsuarioSolicitadoAsync(id, req, adminId) }); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    [HttpPut("usuarios-solicitados/{idUsuarioSolicitado:long}")]
    public async Task<IActionResult> ActualizarUsuarioSolicitado(long idUsuarioSolicitado, [FromBody] ActualizarUsuarioSolicitadoRequest req)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized(new { success = false, message = "Token inválido." });
        try { return Ok(new { success = true, data = await _svc.ActualizarUsuarioSolicitadoAsync(idUsuarioSolicitado, req, adminId) }); }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    // ── Documentos ────────────────────────────────────────────────────────────

    [HttpGet("{id:long}/documentos")]
    public async Task<IActionResult> ListarDocumentos(long id)
    {
        try
        {
            var docs       = await _svc.ListarDocumentosAsync(id);
            var compleitud = await _svc.GetCompleitudAsync(id);
            return Ok(new { success = true, data = new { documentos = docs, compleitud } });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    [HttpPost("{id:long}/documentos")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<IActionResult> SubirDocumento(
        long id,
        IFormFile archivo,
        [FromForm] string tipoDocumento,
        [FromForm] string? observaciones = null)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized(new { success = false, message = "Token inválido." });
        try { return Ok(new { success = true, data = await _svc.SubirDocumentoAsync(id, tipoDocumento, archivo, observaciones, adminId) }); }
        catch (KeyNotFoundException ex)      { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno subiendo documento." }); }
    }

    [HttpGet("documentos/{idDocumento:long}/download")]
    public async Task<IActionResult> DescargarDocumento(long idDocumento)
    {
        try
        {
            var (stream, contentType, fileName) = await _svc.DescargarDocumentoAsync(idDocumento);
            return File(stream, contentType, fileName);
        }
        catch (KeyNotFoundException ex)  { return NotFound(new { success = false, message = ex.Message }); }
        catch (FileNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno descargando documento." }); }
    }

    [HttpPost("documentos/{idDocumento:long}/eliminar")]
    public async Task<IActionResult> EliminarDocumento(long idDocumento)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            await _svc.EliminarDocumentoAsync(idDocumento, adminId);
            return Ok(new { success = true, message = "Documento eliminado." });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno eliminando documento." }); }
    }
}
