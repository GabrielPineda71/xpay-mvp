using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.DTOs;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Route("api/libranza/empresa")]
[Authorize]
public class LibranzaEmpresaController : ControllerBase
{
    private readonly LibranzaEmpleadosService            _svc;
    private readonly LibranzaAnticipoService             _anticipoSvc;
    private readonly ILogger<LibranzaEmpresaController>  _logger;

    public LibranzaEmpresaController(
        LibranzaEmpleadosService svc,
        LibranzaAnticipoService anticipoSvc,
        ILogger<LibranzaEmpresaController> logger)
    {
        _svc         = svc;
        _anticipoSvc = anticipoSvc;
        _logger      = logger;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private bool TryGetIdUsuario(out long id) =>
        long.TryParse(User.FindFirst("idUsuario")?.Value, out id) && id > 0;

    private async Task<(long IdConvenio, string Rol)?> GetConvenioEmpresaAsync(long? idConvenio = null)
    {
        if (!TryGetIdUsuario(out var idUsuario)) return null;
        var asoc = await _svc.GetAsociacionAsync(idUsuario, idConvenio);
        if (asoc is null) return null;
        return (asoc.IdConvenio, asoc.RolEmpresa);
    }

    // ── GET /api/libranza/empresa/mis-convenios ───────────────────────────

    [HttpGet("mis-convenios")]
    public async Task<IActionResult> GetMisConvenios()
    {
        if (!TryGetIdUsuario(out var idUsuario))
            return Unauthorized(new { message = "Token inválido." });

        try
        {
            var result = await _svc.GetMisConveniosAsync(idUsuario);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error GetMisConvenios idUsuario={Id}", idUsuario);
            return StatusCode(500, new { message = "Error interno." });
        }
    }

    // ── GET /api/libranza/empresa/mi-convenio?idConvenio=N ───────────────

    [HttpGet("mi-convenio")]
    public async Task<IActionResult> GetMiConvenio([FromQuery] long? idConvenio)
    {
        if (!TryGetIdUsuario(out var idUsuario))
            return Unauthorized(new { message = "Token inválido." });

        var ctx = await GetConvenioEmpresaAsync(idConvenio);
        if (ctx is null)
            return StatusCode(403, new { message = "No está asociado a ningún convenio de empresa." });

        try
        {
            var result = await _svc.GetMiConvenioAsync(idUsuario, ctx.Value.IdConvenio);
            return Ok(new { success = true, data = result });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error GetMiConvenio idUsuario={Id}", idUsuario);
            return StatusCode(500, new { message = "Error interno." });
        }
    }

    // ── GET /api/libranza/empresa/empleados?idConvenio=N ─────────────────

    [HttpGet("empleados")]
    public async Task<IActionResult> GetEmpleados([FromQuery] long? idConvenio)
    {
        var ctx = await GetConvenioEmpresaAsync(idConvenio);
        if (ctx is null)
            return StatusCode(403, new { message = "No está asociado a ningún convenio de empresa." });

        try
        {
            var data = await _svc.GetEmpleadosAsync(ctx.Value.IdConvenio);
            return Ok(new { success = true, data, total = data.Count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error GetEmpleados convenio={Id}", ctx.Value.IdConvenio);
            return StatusCode(500, new { message = "Error interno." });
        }
    }

    // ── GET /api/libranza/empresa/empleados/plantilla ─────────────────────

    [HttpGet("empleados/plantilla")]
    public IActionResult DescargarPlantilla()
    {
        try
        {
            var bytes = _svc.GenerarPlantillaCsv();
            return File(bytes, "text/csv; charset=utf-8", "plantilla_empleados_libranza.csv");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error DescargarPlantilla");
            return StatusCode(500, new { message = "Error generando plantilla." });
        }
    }

    // ── POST /api/libranza/empresa/empleados/importar?idConvenio=N ────────

    [HttpPost("empleados/importar")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> ImportarEmpleados(IFormFile archivo, [FromQuery] long? idConvenio)
    {
        if (!TryGetIdUsuario(out var idUsuario))
            return Unauthorized(new { message = "Token inválido." });

        var ctx = await GetConvenioEmpresaAsync(idConvenio);
        if (ctx is null)
            return StatusCode(403, new { message = "No está asociado a ningún convenio de empresa." });

        if (archivo is null || archivo.Length == 0)
            return BadRequest(new { message = "Debe adjuntar un archivo CSV." });

        if (archivo.Length > 2 * 1024 * 1024)
            return BadRequest(new { message = "El archivo no puede superar 2 MB." });

        var ext = Path.GetExtension(archivo.FileName).ToLowerInvariant();
        if (ext is not ".csv" and not ".txt")
            return BadRequest(new { message = "Solo se aceptan archivos CSV (.csv o .txt)." });

        try
        {
            await using var stream = archivo.OpenReadStream();
            var result = await _svc.ImportarEmpleadosAsync(
                ctx.Value.IdConvenio, stream, archivo.FileName, idUsuario);
            return Ok(new { success = true, data = result });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ImportarEmpleados convenio={Id}", ctx.Value.IdConvenio);
            return StatusCode(500, new { message = "Error interno procesando el archivo." });
        }
    }

    // ── GET /api/libranza/empresa/cobros?fechaPago=YYYY-MM-DD&idConvenio=N ─

    [HttpGet("cobros")]
    public async Task<IActionResult> GetCobros(
        [FromQuery] string fechaPago, [FromQuery] long? idConvenio)
    {
        var ctx = await GetConvenioEmpresaAsync(idConvenio);
        if (ctx is null)
            return StatusCode(403, new { message = "No está asociado a ningún convenio de empresa." });

        if (string.IsNullOrWhiteSpace(fechaPago) || !DateOnly.TryParse(fechaPago, out var fecha))
            return BadRequest(new { message = "Parámetro fechaPago inválido. Use YYYY-MM-DD." });

        try
        {
            var data = await _anticipoSvc.GetCobrosAsync(ctx.Value.IdConvenio, fecha);
            return Ok(new { success = true, data, total = data.Count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error GetCobros convenio={Id}", ctx.Value.IdConvenio);
            return StatusCode(500, new { message = "Error interno." });
        }
    }

    // ── POST /api/libranza/empresa/cobros/aplicar?idConvenio=N ───────────

    [HttpPost("cobros/aplicar")]
    public async Task<IActionResult> AplicarPago(
        [FromBody] AplicarPagoRequest req, [FromQuery] long? idConvenio)
    {
        if (!TryGetIdUsuario(out var idUsuario))
            return Unauthorized(new { message = "Token inválido." });

        var ctx = await GetConvenioEmpresaAsync(idConvenio);
        if (ctx is null)
            return StatusCode(403, new { message = "No está asociado a ningún convenio de empresa." });

        try
        {
            var result = await _anticipoSvc.AplicarPagoEmpresaAsync(ctx.Value.IdConvenio, req, idUsuario);
            return Ok(new { success = true, data = result });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error AplicarPago convenio={Id}", ctx.Value.IdConvenio);
            return StatusCode(500, new { message = "Error interno." });
        }
    }
}
