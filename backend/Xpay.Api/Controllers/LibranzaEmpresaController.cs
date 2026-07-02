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
    private readonly ILogger<LibranzaEmpresaController>  _logger;

    public LibranzaEmpresaController(LibranzaEmpleadosService svc, ILogger<LibranzaEmpresaController> logger)
    {
        _svc    = svc;
        _logger = logger;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private bool TryGetIdUsuario(out long id) =>
        long.TryParse(User.FindFirst("idUsuario")?.Value, out id) && id > 0;

    // Returns (idConvenio, rol) if the caller is associated to a convenio, or 403.
    private async Task<(long IdConvenio, string Rol)?> GetConvenioEmpresaAsync()
    {
        if (!TryGetIdUsuario(out var idUsuario)) return null;
        var asoc = await _svc.GetAsociacionAsync(idUsuario);
        if (asoc is null) return null;
        return (asoc.IdConvenio, asoc.RolEmpresa);
    }

    // ── GET /api/libranza/empresa/mi-convenio ─────────────────────────────

    [HttpGet("mi-convenio")]
    public async Task<IActionResult> GetMiConvenio()
    {
        if (!TryGetIdUsuario(out var idUsuario))
            return Unauthorized(new { message = "Token inválido." });

        var ctx = await GetConvenioEmpresaAsync();
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

    // ── GET /api/libranza/empresa/empleados ───────────────────────────────

    [HttpGet("empleados")]
    public async Task<IActionResult> GetEmpleados()
    {
        var ctx = await GetConvenioEmpresaAsync();
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

    // ── POST /api/libranza/empresa/empleados/importar ─────────────────────

    [HttpPost("empleados/importar")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> ImportarEmpleados(IFormFile archivo)
    {
        if (!TryGetIdUsuario(out var idUsuario))
            return Unauthorized(new { message = "Token inválido." });

        var ctx = await GetConvenioEmpresaAsync();
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
}
