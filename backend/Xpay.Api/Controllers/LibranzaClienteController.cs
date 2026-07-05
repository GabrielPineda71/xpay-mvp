using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.DTOs;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Route("api/libranza/cliente")]
[Authorize]
public class LibranzaClienteController : ControllerBase
{
    private readonly LibranzaAnticipoService            _svc;
    private readonly ILogger<LibranzaClienteController> _logger;

    public LibranzaClienteController(LibranzaAnticipoService svc, ILogger<LibranzaClienteController> logger)
    {
        _svc    = svc;
        _logger = logger;
    }

    private bool TryGetIdUsuario(out long id) =>
        long.TryParse(User.FindFirst("idUsuario")?.Value, out id) && id > 0;

    // ── GET /api/libranza/cliente/mi-cupo?fecha=YYYY-MM-DD ────────────────

    [HttpGet("mi-cupo")]
    public async Task<IActionResult> GetMiCupo([FromQuery] string? fecha)
    {
        if (!TryGetIdUsuario(out var idUsuario))
            return Unauthorized(new { message = "Token inválido." });

        DateOnly fechaSimulada;
        if (fecha is not null)
        {
            if (!DateOnly.TryParse(fecha, out fechaSimulada))
                return BadRequest(new { message = "Formato de fecha inválido. Use YYYY-MM-DD." });
        }
        else
        {
            fechaSimulada = DateOnly.FromDateTime(DateTime.UtcNow);
        }

        try
        {
            var result = await _svc.GetMiCupoAsync(idUsuario, fechaSimulada);
            return Ok(new { success = true, data = result });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error GetMiCupo idUsuario={Id}", idUsuario);
            return StatusCode(500, new { message = "Error interno." });
        }
    }

    // ── POST /api/libranza/cliente/anticipos ──────────────────────────────

    [HttpPost("anticipos")]
    public async Task<IActionResult> SolicitarAnticipo([FromBody] SolicitarAnticipoRequest req)
    {
        if (!TryGetIdUsuario(out var idUsuario))
            return Unauthorized(new { message = "Token inválido." });

        try
        {
            var result = await _svc.SolicitarAnticipoAsync(idUsuario, req);
            return Ok(new { success = true, data = result });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error SolicitarAnticipo idUsuario={Id}", idUsuario);
            return StatusCode(500, new { message = "Error interno." });
        }
    }

    // ── GET /api/libranza/cliente/anticipos ───────────────────────────────

    [HttpGet("anticipos")]
    public async Task<IActionResult> GetMisAnticipos()
    {
        if (!TryGetIdUsuario(out var idUsuario))
            return Unauthorized(new { message = "Token inválido." });

        try
        {
            var result = await _svc.GetMisAnticiposAsync(idUsuario);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error GetMisAnticipos idUsuario={Id}", idUsuario);
            return StatusCode(500, new { message = "Error interno." });
        }
    }
}
