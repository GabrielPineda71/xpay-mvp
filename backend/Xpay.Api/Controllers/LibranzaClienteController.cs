using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.Common;
using Xpay.Api.DTOs;
using Xpay.Api.Exceptions;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Route("api/libranza/cliente")]
[Authorize]
public class LibranzaClienteController : ControllerBase
{
    private readonly LibranzaAnticipoService            _svc;
    private readonly ILogger<LibranzaClienteController> _logger;
    private readonly IAmbienteXpay                      _ambiente;

    // XPAY-717 - fecha simulada del cliente: prohibida en ambiente productivo.
    internal const string MensajeFechaSimuladaNoPermitida = "La fecha simulada no está permitida.";
    internal const string MensajeFormatoFechaInvalido     = "Formato de fecha inválido. Use YYYY-MM-DD.";

    public LibranzaClienteController(
        LibranzaAnticipoService svc, ILogger<LibranzaClienteController> logger, IAmbienteXpay ambiente)
    {
        _svc      = svc;
        _logger   = logger;
        _ambiente = ambiente;
    }

    private bool TryGetIdUsuario(out long id) =>
        long.TryParse(User.FindFirst("idUsuario")?.Value, out id) && id > 0;

    // P0 anticipo - Idempotency-Key: header obligatorio generado por el
    // cliente. Mismo criterio que CarteraOrdinariaController (XPAY-712):
    // presente, valor único, GUID válido y distinto de Guid.Empty.
    private bool TryGetIdempotencyKey(out Guid idempotencyKey, out string errorMessage)
    {
        idempotencyKey = Guid.Empty;
        if (!Request.Headers.TryGetValue("Idempotency-Key", out var values) || values.Count == 0)
        {
            errorMessage = "Falta el encabezado Idempotency-Key.";
            return false;
        }
        if (values.Count > 1)
        {
            errorMessage = "Se recibió más de un valor para Idempotency-Key.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(values[0]) || !Guid.TryParse(values[0], out idempotencyKey) || idempotencyKey == Guid.Empty)
        {
            errorMessage = "Idempotency-Key debe ser un identificador válido.";
            return false;
        }
        errorMessage = string.Empty;
        return true;
    }

    // XPAY-717 - resuelve la fecha ANTES de cualquier llamada al servicio.
    // Devuelve el 400 a responder, o null si la solicitud puede continuar.
    private IActionResult? RechazoFecha(ResultadoFechaLibranza resultado) => resultado switch
    {
        ResultadoFechaLibranza.RechazadaAmbienteProductivo => BadRequest(new { message = MensajeFechaSimuladaNoPermitida }),
        ResultadoFechaLibranza.FormatoInvalido             => BadRequest(new { message = MensajeFormatoFechaInvalido }),
        _                                                  => null,
    };

    // ── GET /api/libranza/cliente/mi-cupo?fecha=YYYY-MM-DD ────────────────

    [Authorize(Policy = "KycAprobado")]
    [HttpGet("mi-cupo")]
    public async Task<IActionResult> GetMiCupo([FromQuery] string? fecha)
    {
        if (!TryGetIdUsuario(out var idUsuario))
            return Unauthorized(new { message = "Token inválido." });

        var resultadoFecha = FechaOperacionLibranza.Resolver(
            _ambiente, fecha, DateOnly.FromDateTime(DateTime.UtcNow), out var fechaSimulada);
        if (RechazoFecha(resultadoFecha) is { } rechazo)
            return rechazo;

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

    [Authorize(Policy = "KycAprobado")]
    [HttpPost("anticipos")]
    public async Task<IActionResult> SolicitarAnticipo([FromBody] SolicitarAnticipoRequest req)
    {
        if (!TryGetIdUsuario(out var idUsuario))
            return Unauthorized(new { message = "Token inválido." });

        if (!TryGetIdempotencyKey(out var idempotencyKey, out var idempotencyError))
            return BadRequest(new { message = idempotencyError });

        var resultadoFecha = FechaOperacionLibranza.Resolver(
            _ambiente, req.FechaSimulada, DateOnly.FromDateTime(DateTime.UtcNow), out var fechaSimulada);
        if (RechazoFecha(resultadoFecha) is { } rechazo)
            return rechazo;
        // Simulada válida (sólo no productivo): ISO para el parseo del servicio.
        // No suministrada: se conserva null y el servicio usa la fecha UTC actual.
        if (resultadoFecha == ResultadoFechaLibranza.Simulada)
            req.FechaSimulada = fechaSimulada.ToString("yyyy-MM-dd");

        try
        {
            var result = await _svc.SolicitarAnticipoAsync(idUsuario, req, idempotencyKey);
            if (result.Replayed)
                Response.Headers["Idempotent-Replayed"] = "true";
            return Ok(new { success = true, data = result.Data });
        }
        catch (IdempotencyConflictException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (IdempotencyUnavailableException ex)
        {
            return StatusCode(503, new { message = ex.Message });
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

    [Authorize(Policy = "KycAprobado")]
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
