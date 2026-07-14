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
    private readonly ComercioAliadoService                    _svc;
    private readonly ComercioDisponibilidadService            _disp;
    private readonly ComercioScopeService                     _scope;
    private readonly ComercioLiquidacionAutomaticaService     _liquidacion;

    public ComerciosAliadosController(
        ComercioAliadoService svc,
        ComercioDisponibilidadService disp,
        ComercioScopeService scope,
        ComercioLiquidacionAutomaticaService liquidacion)
    {
        _svc         = svc;
        _disp        = disp;
        _scope       = scope;
        _liquidacion = liquidacion;
    }

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

    // ── Vincular comercio operativo ───────────────────────────────────────────

    [HttpPost("{id:long}/vincular-operativo")]
    public async Task<IActionResult> VincularOperativo(long id, [FromBody] VincularComercioOperativoRequest req)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            await _disp.VincularComercioOperativoAsync(id, req.IdComercioExistente, adminId);
            return Ok(new { success = true, message = "Comercio operativo vinculado." });
        }
        catch (KeyNotFoundException ex)      { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    // ── Condiciones de negociación ────────────────────────────────────────────

    [HttpGet("{id:long}/condiciones")]
    public async Task<IActionResult> ListarCondiciones(long id)
    {
        try { return Ok(new { success = true, data = await _disp.ListarCondicionesAsync(id) }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    [HttpPost("{id:long}/condiciones")]
    public async Task<IActionResult> CrearCondicion(long id, [FromBody] CrearCondicionRequest req)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized(new { success = false, message = "Token inválido." });
        try { return Ok(new { success = true, data = await _disp.CrearCondicionAsync(id, req, adminId) }); }
        catch (KeyNotFoundException ex)      { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    [HttpPut("condiciones/{idCondicion:long}")]
    public async Task<IActionResult> ActualizarCondicion(long idCondicion, [FromBody] ActualizarCondicionRequest req)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized(new { success = false, message = "Token inválido." });
        try { return Ok(new { success = true, data = await _disp.ActualizarCondicionAsync(idCondicion, req, adminId) }); }
        catch (KeyNotFoundException ex)      { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    // ── Parámetros liquidación anticipada ─────────────────────────────────────

    [HttpGet("parametros-liquidacion")]
    public async Task<IActionResult> ListarParametros([FromQuery] long? idComercioAliado = null)
    {
        try { return Ok(new { success = true, data = await _disp.ListarParametrosAsync(idComercioAliado) }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    [HttpPost("parametros-liquidacion")]
    public async Task<IActionResult> CrearParametro([FromBody] CrearParametroLiquidacionRequest req)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized(new { success = false, message = "Token inválido." });
        try { return Ok(new { success = true, data = await _disp.CrearParametroAsync(req, adminId) }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    [HttpPut("parametros-liquidacion/{idParametro:long}")]
    public async Task<IActionResult> ActualizarParametro(long idParametro, [FromBody] ActualizarParametroLiquidacionRequest req)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized(new { success = false, message = "Token inválido." });
        try { return Ok(new { success = true, data = await _disp.ActualizarParametroAsync(idParametro, req, adminId) }); }
        catch (KeyNotFoundException ex)      { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    // ── Disponibilidad admin ──────────────────────────────────────────────────

    [HttpGet("{id:long}/disponibilidad")]
    public async Task<IActionResult> ListarDisponibilidad(long id)
    {
        try { return Ok(new { success = true, data = await _disp.ListarDisponibilidadAdminAsync(id) }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    [HttpPost("disponibilidad/{idDisponibilidad:long}/liberar")]
    public async Task<IActionResult> LiberarManual(long idDisponibilidad)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized(new { success = false, message = "Token inválido." });
        try { return Ok(new { success = true, data = await _disp.LiberarManualAsync(idDisponibilidad, adminId) }); }
        catch (KeyNotFoundException ex)      { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno liberando fondos." }); }
    }

    // ── Usuarios operativos ────────────────────────────────────────────────────

    [HttpGet("{id:long}/usuarios-operativos")]
    public async Task<IActionResult> ListarUsuariosOperativos(long id)
    {
        try { return Ok(new { success = true, data = await _scope.ListarUsuariosOperativosAsync(id) }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    [HttpPost("{id:long}/usuarios-operativos")]
    public async Task<IActionResult> CrearUsuarioOperativo(long id, [FromBody] CrearComercioUsuarioRequest req)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized(new { success = false, message = "Token inválido." });
        try { return Ok(new { success = true, data = await _scope.CrearUsuarioOperativoAsync(id, req, adminId) }); }
        catch (KeyNotFoundException ex)      { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno creando usuario operativo." }); }
    }

    [HttpPut("usuarios-operativos/{idComercioUsuario:long}")]
    public async Task<IActionResult> ActualizarUsuarioOperativo(long idComercioUsuario, [FromBody] ActualizarComercioUsuarioRequest req)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized(new { success = false, message = "Token inválido." });
        try { return Ok(new { success = true, data = await _scope.ActualizarUsuarioOperativoAsync(idComercioUsuario, req, adminId) }); }
        catch (KeyNotFoundException ex)      { return NotFound(new { success = false, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno actualizando usuario operativo." }); }
    }

    // ── Contexto ventas ────────────────────────────────────────────────────────

    [HttpGet("{id:long}/ventas-contexto")]
    public async Task<IActionResult> ListarVentasContexto(long id)
    {
        try { return Ok(new { success = true, data = await _scope.ListarVentasContextoAsync(id) }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    [HttpPost("ventas-contexto/backfill-demo")]
    public async Task<IActionResult> BackfillDemoContexto([FromBody] BackfillDemoContextoRequest req)
    {
        if (!TryGetAdminId(out _)) return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var count = await _scope.BackfillDemoContextoAsync(req.IdComercioAliado, req.IdComercioExistente, req.IdEstablecimiento);
            return Ok(new { success = true, message = $"{count} ventas backfilled.", count });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch { return StatusCode(500, new { success = false, message = "Error interno en backfill." }); }
    }

    // ── Liquidación automática ─────────────────────────────────────────────────

    [HttpPost("liquidacion-automatica/ejecutar")]
    public async Task<IActionResult> EjecutarLiquidacionAutomatica(
        [FromBody] EjecutarLiquidacionAutomaticaRequest? req)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var fechaCorte = req?.FechaCorte ?? DateTime.UtcNow;
            var result = await _liquidacion.LiquidarVentasVencidasAsync(
                fechaCorte,
                req?.SoloComercioAliadoId,
                req?.SoloIdDisponibilidad,
                actorId: adminId);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            var inner = ex.InnerException?.Message ?? string.Empty;
            var msg   = string.IsNullOrEmpty(inner) ? ex.Message : $"{ex.Message} | Inner: {inner}";
            return StatusCode(500, new { success = false, message = $"Error en liquidación automática: {msg}" });
        }
    }
}
