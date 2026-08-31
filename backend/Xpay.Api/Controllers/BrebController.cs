using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.DTOs;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
public class BrebController : ControllerBase
{
    private readonly BrebService          _breb;
    private readonly AuditLogService      _audit;
    private readonly IConfiguration       _config;
    private readonly ComercioScopeService _scope;

    public BrebController(BrebService breb, AuditLogService audit, IConfiguration config, ComercioScopeService scope)
    {
        _breb   = breb;
        _audit  = audit;
        _config = config;
        _scope  = scope;
    }

    // KYC-GATING-001 / BREB-COMERCIO-IDOR-FIX-001: valida que el idComercio
    // solicitado esté dentro del scope operativo del usuario autenticado —
    // solo cuando el caller tiene rol COMERCIO. ADMIN_XPAY/SUPERUSUARIO
    // preservan su alcance administrativo actual sobre cualquier comercio
    // (no tienen fila propia en ComercioUsuarios, así que aplicarles el
    // mismo check los bloquearía incorrectamente — comportamiento actual
    // preservado a propósito, no una omisión).
    private async Task<IActionResult?> ValidarScopeComercioAsync(long idUsuario, long idComercio)
    {
        if (!User.IsInRole("COMERCIO")) return null;
        try
        {
            await _scope.RequireScopeForComercioAsync(idUsuario, idComercio);
            return null;
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
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
    [Authorize(Policy = "KycAprobado")]
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
    [Authorize(Policy = "KycAprobado")]
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
        if (!TryGetIdUsuario(out var idUsuario))
            return Unauthorized(new { success = false, message = "Token inválido." });
        if (await ValidarScopeComercioAsync(idUsuario, idComercio) is { } denied)
            return denied;
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
        if (await ValidarScopeComercioAsync(idUsuario, request.IdComercio.Value) is { } denied)
            return denied;

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
    // GET /api/breb/admin/retiros
    // Solo ADMIN_XPAY/SUPERUSUARIO. Lista todos los retiros Bre-B.
    // ──────────────────────────────────────────────────────────────────────
    [HttpGet("api/breb/admin/retiros")]
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    public async Task<IActionResult> GetAdminRetiros()
    {
        try
        {
            var retiros = await _breb.GetAdminRetirosAsync();
            return Ok(new { success = true, data = retiros });
        }
        catch
        { return StatusCode(500, new { success = false, message = "Error interno." }); }
    }

    // ──────────────────────────────────────────────────────────────────────
    // POST /api/breb/admin/retiros/{id}/confirmar
    // CREADO → CONFIRMADO. Descuenta saldo y crea ledger DR 210101 / CR 210204.
    // ──────────────────────────────────────────────────────────────────────
    [HttpPost("api/breb/admin/retiros/{id:long}/confirmar")]
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    public async Task<IActionResult> ConfirmarRetiro(long id)
    {
        if (!TryGetIdUsuario(out var adminId))
            return Unauthorized(new { success = false, message = "Token inválido." });

        _audit.LogSensitiveAction(HttpContext, "BREB_RETIRO_CONFIRMAR_ATTEMPT", new { idBrebRetiro = id, adminId });
        try
        {
            var msg = await _breb.ConfirmarRetiroAsync(id, adminId);
            _audit.LogSensitiveAction(HttpContext, "BREB_RETIRO_CONFIRMAR_OK", new { idBrebRetiro = id });
            return Ok(new { success = true, message = msg });
        }
        catch (InvalidOperationException ex)
        { return BadRequest(new { success = false, message = ex.Message }); }
        catch
        { return StatusCode(500, new { success = false, message = "Error interno confirmando retiro." }); }
    }

    // ──────────────────────────────────────────────────────────────────────
    // POST /api/breb/admin/retiros/{id}/liquidar
    // CONFIRMADO → LIQUIDADO. Crea ledger DR 210204 / CR 110102.
    // ──────────────────────────────────────────────────────────────────────
    [HttpPost("api/breb/admin/retiros/{id:long}/liquidar")]
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    public async Task<IActionResult> LiquidarRetiro(long id)
    {
        if (!TryGetIdUsuario(out var adminId))
            return Unauthorized(new { success = false, message = "Token inválido." });

        _audit.LogSensitiveAction(HttpContext, "BREB_RETIRO_LIQUIDAR_ATTEMPT", new { idBrebRetiro = id, adminId });
        try
        {
            var msg = await _breb.LiquidarRetiroAsync(id, adminId);
            _audit.LogSensitiveAction(HttpContext, "BREB_RETIRO_LIQUIDAR_OK", new { idBrebRetiro = id });
            return Ok(new { success = true, message = msg });
        }
        catch (InvalidOperationException ex)
        { return BadRequest(new { success = false, message = ex.Message }); }
        catch
        { return StatusCode(500, new { success = false, message = "Error interno liquidando retiro." }); }
    }

    // ──────────────────────────────────────────────────────────────────────
    // POST /api/breb/admin/retiros/{id}/rechazar
    // CREADO → RECHAZADO (sin ledger) o CONFIRMADO → RECHAZADO (reverso ledger).
    // ──────────────────────────────────────────────────────────────────────
    [HttpPost("api/breb/admin/retiros/{id:long}/rechazar")]
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    public async Task<IActionResult> RechazarRetiro(long id, [FromBody] RechazarRetiroRequest request)
    {
        if (!TryGetIdUsuario(out var adminId))
            return Unauthorized(new { success = false, message = "Token inválido." });

        _audit.LogSensitiveAction(HttpContext, "BREB_RETIRO_RECHAZAR_ATTEMPT", new { idBrebRetiro = id, adminId });
        try
        {
            var msg = await _breb.RechazarRetiroAsync(id, request.Motivo, adminId);
            _audit.LogSensitiveAction(HttpContext, "BREB_RETIRO_RECHAZAR_OK", new { idBrebRetiro = id });
            return Ok(new { success = true, message = msg });
        }
        catch (InvalidOperationException ex)
        { return BadRequest(new { success = false, message = ex.Message }); }
        catch
        { return StatusCode(500, new { success = false, message = "Error interno rechazando retiro." }); }
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
    [Authorize(Policy = "KycAprobado")]
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
        if (!TryGetIdUsuario(out var idUsuario))
            return Unauthorized(new { success = false, message = "Token inválido." });
        if (await ValidarScopeComercioAsync(idUsuario, idComercio) is { } denied)
            return denied;
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
    // KYC-GATING-001: separado del endpoint COMERCIO (antes una sola action
    // ramificaba por request.IdComercio) para poder exigir identidad
    // verificada exclusivamente en el contexto USUARIO sin bloquear COMERCIO,
    // que tiene sus propias reglas de autorización. Ruta sin cambios —
    // mismo contrato ya consumido por UserWalletPage.tsx.
    // Usuario final autenticado — crea retiro simulado propio.
    // En Fase 64: CREADO, sin tocar ledger ni saldo.
    // En Fase 65: llamará Passport real, moverá saldo transaccionalmente.
    // ──────────────────────────────────────────────────────────────────────
    [HttpPost("api/breb/retiros/simular")]
    [Authorize]
    [Authorize(Policy = "KycAprobado")]
    public async Task<IActionResult> SimularRetiro([FromBody] SimularRetiroRequest request)
    {
        if (!TryGetIdPersona(out var idPersona) || !TryGetIdUsuario(out var idUsuario))
            return Unauthorized(new { success = false, message = "Token inválido." });

        _audit.LogSensitiveAction(HttpContext, "BREB_RETIRO_SIMULAR_ATTEMPT",
            new { idUsuario, valor = request.Valor });
        try
        {
            var retiro = await _breb.SimularRetiroAsync(idPersona, idUsuario, request);
            _audit.LogSensitiveAction(HttpContext, "BREB_RETIRO_SIMULAR_OK",
                new { idUsuario, idBrebRetiro = retiro.IdBrebRetiro, estado = retiro.Estado });
            return Ok(new { success = true, data = retiro });
        }
        catch (InvalidOperationException ex)
        { return BadRequest(new { success = false, message = ex.Message }); }
        catch
        { return StatusCode(500, new { success = false, message = "Error interno creando retiro." }); }
    }

    // ──────────────────────────────────────────────────────────────────────
    // POST /api/breb/retiros/simular/comercio
    // KYC-GATING-001: nueva ruta — mismo patrón ya usado en este controller
    // para separar USUARIO/COMERCIO (mi-llave/comercio, mis-retiros/comercio).
    // COMERCIO o ADMIN — crea retiro simulado del comercio. Sin KycAprobado:
    // la identidad de persona natural verificada vía Veriff no aplica al
    // onboarding/KYB de un comercio (alcance futuro separado).
    // ──────────────────────────────────────────────────────────────────────
    [HttpPost("api/breb/retiros/simular/comercio")]
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO,COMERCIO")]
    public async Task<IActionResult> SimularRetiroComercio([FromBody] SimularRetiroRequest request)
    {
        if (!TryGetIdUsuario(out var idUsuario))
            return Unauthorized(new { success = false, message = "Token inválido." });
        if (request.IdComercio is null or <= 0)
            return BadRequest(new { success = false, message = "idComercio requerido para contexto COMERCIO." });
        if (await ValidarScopeComercioAsync(idUsuario, request.IdComercio.Value) is { } denied)
            return denied;

        _audit.LogSensitiveAction(HttpContext, "BREB_RETIRO_SIMULAR_COMERCIO_ATTEMPT",
            new { idUsuario, valor = request.Valor, idComercio = request.IdComercio });
        try
        {
            var retiro = await _breb.SimularRetiroComercioAsync(request.IdComercio!.Value, idUsuario, request);
            _audit.LogSensitiveAction(HttpContext, "BREB_RETIRO_SIMULAR_COMERCIO_OK",
                new { idUsuario, idBrebRetiro = retiro.IdBrebRetiro, estado = retiro.Estado });
            return Ok(new { success = true, data = retiro });
        }
        catch (InvalidOperationException ex)
        { return BadRequest(new { success = false, message = ex.Message }); }
        catch
        { return StatusCode(500, new { success = false, message = "Error interno creando retiro." }); }
    }
}
