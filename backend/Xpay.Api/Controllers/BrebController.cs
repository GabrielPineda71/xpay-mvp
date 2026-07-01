using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.DTOs;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
public class BrebController : ControllerBase
{
    private readonly BrebService     _breb;
    private readonly AuditLogService _audit;
    private readonly IConfiguration  _config;

    public BrebController(BrebService breb, AuditLogService audit, IConfiguration config)
    {
        _breb   = breb;
        _audit  = audit;
        _config = config;
    }

    private bool TryGetIdPersona(out long id) =>
        long.TryParse(User.FindFirst("idPersona")?.Value, out id) && id > 0;

    private bool TryGetIdUsuario(out long id) =>
        long.TryParse(User.FindFirst("idUsuario")?.Value, out id) && id > 0;

    // ──────────────────────────────────────────────────────────────────────
    // GET /api/passport/health-config
    // Sólo admin. Confirma presencia de variables, nunca devuelve valores.
    // ──────────────────────────────────────────────────────────────────────
    [HttpGet("api/passport/health-config")]
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    public IActionResult PassportHealthConfig()
    {
        var result = _breb.GetHealthConfig(_config);
        return Ok(new { success = true, data = result });
    }

    // ──────────────────────────────────────────────────────────────────────
    // GET /api/breb/mi-llave
    // Usuario autenticado — devuelve llave propia (USUARIO context).
    // ──────────────────────────────────────────────────────────────────────
    [HttpGet("api/breb/mi-llave")]
    [Authorize]
    public async Task<IActionResult> GetMiLlave()
    {
        if (!TryGetIdPersona(out var idPersona))
            return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var llave = await _breb.GetMiLlaveAsync(idPersona);
            return Ok(new { success = true, data = llave });
        }
        catch (InvalidOperationException ex)
        { return BadRequest(new { success = false, message = ex.Message }); }
        catch
        { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    // ──────────────────────────────────────────────────────────────────────
    // POST /api/breb/mi-llave
    // Usuario autenticado — registra o reemplaza llave propia (USUARIO).
    // Validaciones: formato, no duplicar llave ajena, hash en DB.
    // ──────────────────────────────────────────────────────────────────────
    [HttpPost("api/breb/mi-llave")]
    [Authorize]
    public async Task<IActionResult> RegistrarMiLlave([FromBody] RegistrarLlaveRequest request)
    {
        if (!TryGetIdPersona(out var idPersona) || !TryGetIdUsuario(out var idUsuario))
            return Unauthorized(new { success = false, message = "Token inválido." });

        _audit.LogSensitiveAction(HttpContext, "BREB_LLAVE_REGISTRO_ATTEMPT",
            new { idUsuario, keyType = request.KeyType });
        try
        {
            var llave = await _breb.RegistrarLlaveAsync(idPersona, idUsuario, request);
            _audit.LogSensitiveAction(HttpContext, "BREB_LLAVE_REGISTRO_OK",
                new { idUsuario, idBrebLlave = llave.IdBrebLlave });
            return Ok(new { success = true, data = llave });
        }
        catch (InvalidOperationException ex)
        { return BadRequest(new { success = false, message = ex.Message }); }
        catch
        { return StatusCode(500, new { success = false, message = "Error interno registrando llave." }); }
    }

    // ──────────────────────────────────────────────────────────────────────
    // GET /api/breb/mi-llave/comercio?idComercio={id}
    // COMERCIO o ADMIN — llave Bre-B del comercio.
    // ──────────────────────────────────────────────────────────────────────
    [HttpGet("api/breb/mi-llave/comercio")]
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO,COMERCIO")]
    public async Task<IActionResult> GetLlaveComercio([FromQuery] long idComercio)
    {
        if (idComercio <= 0)
            return BadRequest(new { success = false, message = "idComercio inválido." });
        try
        {
            var llave = await _breb.GetLlaveComercioAsync(idComercio);
            return Ok(new { success = true, data = llave });
        }
        catch (InvalidOperationException ex)
        { return BadRequest(new { success = false, message = ex.Message }); }
        catch
        { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    // ──────────────────────────────────────────────────────────────────────
    // POST /api/breb/mi-llave/comercio
    // COMERCIO o ADMIN — registra llave para el comercio.
    // ──────────────────────────────────────────────────────────────────────
    [HttpPost("api/breb/mi-llave/comercio")]
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO,COMERCIO")]
    public async Task<IActionResult> RegistrarLlaveComercio([FromBody] RegistrarLlaveRequest request)
    {
        if (!TryGetIdUsuario(out var idUsuario))
            return Unauthorized(new { success = false, message = "Token inválido." });
        if (request.IdComercio is null or <= 0)
            return BadRequest(new { success = false, message = "idComercio requerido para contexto COMERCIO." });

        _audit.LogSensitiveAction(HttpContext, "BREB_LLAVE_COMERCIO_REGISTRO_ATTEMPT",
            new { idUsuario, idComercio = request.IdComercio, keyType = request.KeyType });
        try
        {
            var llave = await _breb.RegistrarLlaveComercioAsync(request.IdComercio.Value, idUsuario, request);
            _audit.LogSensitiveAction(HttpContext, "BREB_LLAVE_COMERCIO_REGISTRO_OK",
                new { idComercio = request.IdComercio, idBrebLlave = llave.IdBrebLlave });
            return Ok(new { success = true, data = llave });
        }
        catch (InvalidOperationException ex)
        { return BadRequest(new { success = false, message = ex.Message }); }
        catch
        { return StatusCode(500, new { success = false, message = "Error interno registrando llave comercio." }); }
    }

    // ──────────────────────────────────────────────────────────────────────
    // GET /api/breb/admin/llaves
    // Solo ADMIN_XPAY/SUPERUSUARIO. Lista todas las llaves Bre-B.
    // No devuelve keyValueHash, keyValueEncrypted ni datos sensibles.
    // ──────────────────────────────────────────────────────────────────────
    [HttpGet("api/breb/admin/llaves")]
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    public async Task<IActionResult> GetAdminLlaves()
    {
        try
        {
            var llaves = await _breb.GetAdminLlavesAsync();
            return Ok(new { success = true, data = llaves });
        }
        catch
        { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    // ──────────────────────────────────────────────────────────────────────
    // POST /api/breb/admin/simular-validacion-llave
    // Solo ADMIN_XPAY/SUPERUSUARIO. QA only.
    // Marca llave como VALIDADA o RECHAZADA sin llamar Passport real.
    // ──────────────────────────────────────────────────────────────────────
    [HttpPost("api/breb/admin/simular-validacion-llave")]
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    public async Task<IActionResult> SimularValidacionLlave([FromBody] SimularValidacionLlaveRequest request)
    {
        if (!TryGetIdUsuario(out var idUsuario))
            return Unauthorized(new { success = false, message = "Token inválido." });

        _audit.LogSensitiveAction(HttpContext, "BREB_VALIDACION_SIMULADA_ATTEMPT",
            new { idUsuario, idBrebLlave = request.IdBrebLlave, estado = request.Estado });
        try
        {
            var msg = await _breb.SimularValidacionAsync(request, idUsuario);
            _audit.LogSensitiveAction(HttpContext, "BREB_VALIDACION_SIMULADA_OK",
                new { idBrebLlave = request.IdBrebLlave, estado = request.Estado });
            return Ok(new { success = true, message = msg });
        }
        catch (InvalidOperationException ex)
        { return BadRequest(new { success = false, message = ex.Message }); }
        catch
        { return StatusCode(500, new { success = false, message = "Error interno simulando validación." }); }
    }

    // ──────────────────────────────────────────────────────────────────────
    // GET /api/breb/mis-retiros
    // Usuario autenticado — lista retiros propios (USUARIO context).
    // ──────────────────────────────────────────────────────────────────────
    [HttpGet("api/breb/mis-retiros")]
    [Authorize]
    public async Task<IActionResult> GetMisRetiros()
    {
        if (!TryGetIdPersona(out var idPersona))
            return Unauthorized(new { success = false, message = "Token inválido." });
        try
        {
            var retiros = await _breb.GetMisRetirosAsync(idPersona);
            return Ok(new { success = true, data = retiros });
        }
        catch (InvalidOperationException ex)
        { return BadRequest(new { success = false, message = ex.Message }); }
        catch
        { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    // ──────────────────────────────────────────────────────────────────────
    // GET /api/breb/mis-retiros/comercio?idComercio={id}
    // COMERCIO o ADMIN — lista retiros del comercio.
    // ──────────────────────────────────────────────────────────────────────
    [HttpGet("api/breb/mis-retiros/comercio")]
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO,COMERCIO")]
    public async Task<IActionResult> GetRetirosComercio([FromQuery] long idComercio)
    {
        if (idComercio <= 0)
            return BadRequest(new { success = false, message = "idComercio inválido." });
        try
        {
            var retiros = await _breb.GetRetirosComercioAsync(idComercio);
            return Ok(new { success = true, data = retiros });
        }
        catch (InvalidOperationException ex)
        { return BadRequest(new { success = false, message = ex.Message }); }
        catch
        { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    // ──────────────────────────────────────────────────────────────────────
    // POST /api/breb/retiros/simular
    // Usuario autenticado — crea retiro simulado (USUARIO o COMERCIO).
    // En Fase 64: CREADO, sin tocar ledger ni saldo.
    // En Fase 65: llamará Passport real, moverá saldo transaccionalmente.
    // ──────────────────────────────────────────────────────────────────────
    [HttpPost("api/breb/retiros/simular")]
    [Authorize]
    public async Task<IActionResult> SimularRetiro([FromBody] SimularRetiroRequest request)
    {
        if (!TryGetIdPersona(out var idPersona) || !TryGetIdUsuario(out var idUsuario))
            return Unauthorized(new { success = false, message = "Token inválido." });

        _audit.LogSensitiveAction(HttpContext, "BREB_RETIRO_SIMULAR_ATTEMPT",
            new { idUsuario, valor = request.Valor, idComercio = request.IdComercio });
        try
        {
            BrebRetiroResponse retiro;
            if (request.IdComercio is > 0)
            {
                retiro = await _breb.SimularRetiroComercioAsync(request.IdComercio!.Value, idUsuario, request);
            }
            else
            {
                retiro = await _breb.SimularRetiroAsync(idPersona, idUsuario, request);
            }
            _audit.LogSensitiveAction(HttpContext, "BREB_RETIRO_SIMULAR_OK",
                new { idUsuario, idBrebRetiro = retiro.IdBrebRetiro, estado = retiro.Estado });
            return Ok(new { success = true, data = retiro });
        }
        catch (InvalidOperationException ex)
        { return BadRequest(new { success = false, message = ex.Message }); }
        catch
        { return StatusCode(500, new { success = false, message = "Error interno creando retiro." }); }
    }
}
