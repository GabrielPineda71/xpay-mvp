using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.DTOs;
using Xpay.Api.Exceptions;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Route("api/cartera-ordinaria")]
[Authorize]
public class CarteraOrdinariaController(CarteraOrdinariaService svc) : ControllerBase
{
    private long IdUsuarioActual => long.Parse(User.FindFirst("idUsuario")?.Value ?? "0");

    // Idempotency-Key: header HTTP obligatorio generado por el cliente — nunca
    // por el backend. Mismo criterio que WalletsController/QrController
    // (presente, valor único, GUID válido) más el rechazo explícito de
    // Guid.Empty exigido por la originación de cupo.
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

    // Mensajes EXACTOS que CrearSolicitudCupoAsync (Etapa 3 + hardening 017)
    // lanza como InvalidOperationException para conflictos de originación /
    // idempotencia / concurrencia → HTTP 409. Cualquier OTRA
    // InvalidOperationException del service (p. ej. "No hay una política de
    // crédito activa", o una inconsistencia interna) NO entra aquí: se deja
    // propagar a ErrorHandlingMiddleware, que responde 500 genérico sin
    // exponer el mensaje.
    private static readonly string[] MensajesConflictoSolicitudCupo =
    {
        "Ya tienes una solicitud de cupo en curso",
        "Idempotency-Key ya utilizada para otra solicitud.",
        "Idempotency-Key ya utilizada con parámetros diferentes.",
        "Hay otra solicitud de cupo en proceso para este usuario. Intenta de nuevo en unos segundos.",
    };

    private static bool EsConflictoSolicitudCupo(string message) =>
        Array.Exists(MensajesConflictoSolicitudCupo, m => m == message);

    // ── ADMIN: Parámetros de utilización ──────────────────────────────
    [HttpGet("admin/parametros")]
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    public async Task<IActionResult> GetParametros()
        => Ok(await svc.GetParametrosAsync());

    [HttpPut("admin/parametros/{tipo}")]
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    public async Task<IActionResult> UpsertParametro(string tipo, [FromBody] UpsertParametroUtilizacionRequest req)
    {
        var tipos = new[] { "COMPRA_COMERCIO", "AVANCE_WALLET" };
        if (!tipos.Contains(tipo.ToUpperInvariant()))
            return BadRequest(new { error = "tipo_utilizacion debe ser COMPRA_COMERCIO o AVANCE_WALLET" });
        try
        {
            var result = await svc.UpsertParametroAsync(tipo.ToUpperInvariant(), req, IdUsuarioActual);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ── ADMIN: Gastos de cobranza ─────────────────────────────────────
    [HttpGet("admin/gastos-cobranza")]
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    public async Task<IActionResult> GetGastosCobranza()
        => Ok(await svc.GetGastosCobranzaAsync());

    [HttpPost("admin/gastos-cobranza")]
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    public async Task<IActionResult> CreateGastoCobranza([FromBody] UpsertGastosCobranzaRequest req)
        => Ok(await svc.UpsertGastoCobranzaAsync(null, req));

    [HttpPut("admin/gastos-cobranza/{id:long}")]
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    public async Task<IActionResult> UpdateGastoCobranza(long id, [FromBody] UpsertGastosCobranzaRequest req)
    {
        try { return Ok(await svc.UpsertGastoCobranzaAsync(id, req)); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    // ── ADMIN: Política de crédito ─────────────────────────────────────
    [HttpGet("admin/politica")]
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    public async Task<IActionResult> GetPolitica()
    {
        var politica = await svc.GetPoliticaVigenteAsync();
        return politica is null ? NotFound(new { error = "Sin política activa" }) : Ok(politica);
    }

    [HttpPut("admin/politica")]
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    public async Task<IActionResult> UpsertPolitica([FromBody] UpsertPoliticaCreditoRequest req)
        => Ok(await svc.UpsertPoliticaAsync(req, IdUsuarioActual));

    // ── ADMIN: Cupos ──────────────────────────────────────────────────
    [HttpGet("admin/cupos")]
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    public async Task<IActionResult> GetCupos()
        => Ok(await svc.GetCuposAsync());

    [HttpPost("admin/cupos")]
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    public async Task<IActionResult> AsignarCupo([FromBody] AsignarCupoRequest req)
    {
        try { return Ok(await svc.AsignarCupoAsync(req, IdUsuarioActual)); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (CarteraCupoConcurrenteException ex) { return Conflict(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // ── USUARIO: Mi cupo ──────────────────────────────────────────────
    [Authorize(Policy = "KycAprobado")]
    [HttpGet("mi-cupo")]
    public async Task<IActionResult> GetMiCupo()
    {
        var cupo = await svc.GetMiCupoAsync(IdUsuarioActual);
        return cupo is null ? NotFound(new { error = "No tienes un cupo ordinario activo" }) : Ok(cupo);
    }

    // ── USUARIO: Solicitar cupo ordinario (originación PRE-CALL) ──────
    // ETAPA 4: sólo expone CrearSolicitudCupoAsync. El service resuelve
    // idempotencia, AppLock, snapshot de política, replay + ownership y la
    // creación atómica de solicitud + primer intento. Sin proveedor, sin
    // decisión crediticia, sin cálculo de edad, sin uso de score.
    [Authorize(Policy = "KycAprobado")]
    [HttpPost("solicitar-cupo")]
    public async Task<IActionResult> SolicitarCupo([FromBody] SolicitarCupoRequest req)
    {
        if (!TryGetIdempotencyKey(out var idempotencyKey, out var idempotencyError))
            return BadRequest(new { error = idempotencyError });

        // correlationId controlado por el servidor — CorrelationIdMiddleware ya
        // lo dejó en HttpContext.Items["CorrelationId"] (del header
        // X-Correlation-ID entrante o un GUID nuevo). Nunca se toma del body.
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? HttpContext.TraceIdentifier;

        try
        {
            var result = await svc.CrearSolicitudCupoAsync(
                IdUsuarioActual, idempotencyKey, req.MontoSolicitado, correlationId);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex) when (EsConflictoSolicitudCupo(ex.Message))
        {
            return Conflict(new { error = ex.Message });
        }
        // Otras InvalidOperationException (config ausente / inconsistencia
        // interna) y cualquier excepción no prevista se propagan a
        // ErrorHandlingMiddleware → 500 genérico sin detalle.
    }

    // ── USUARIO: Simulador ────────────────────────────────────────────
    [HttpPost("simular")]
    public async Task<IActionResult> SimularUtilizacion([FromBody] SimularUtilizacionRequest req)
    {
        try { return Ok(await svc.SimularUtilizacionAsync(req, IdUsuarioActual)); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // ── USUARIO: Confirmación real de utilización (AVANCE_WALLET) ─────
    [Authorize(Policy = "KycAprobado")]
    [HttpPost("confirmar-avance-wallet")]
    public async Task<IActionResult> ConfirmarAvanceWallet([FromBody] SimularUtilizacionRequest req)
    {
        try { return Ok(await svc.ConfirmarAvanceWalletAsync(req, IdUsuarioActual)); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // ── USUARIO: Mis créditos y pago manual de cuotas ──────────────────
    [Authorize(Policy = "KycAprobado")]
    [HttpGet("mis-creditos")]
    public async Task<IActionResult> GetMisCreditos()
        => Ok(await svc.GetMisCreditosAsync(IdUsuarioActual));

    [Authorize(Policy = "KycAprobado")]
    [HttpGet("mis-creditos/{idUtilizacion:long}/cuotas")]
    public async Task<IActionResult> GetCuotasCredito(long idUtilizacion)
    {
        try { return Ok(await svc.GetCuotasCreditoAsync(idUtilizacion, IdUsuarioActual)); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    [Authorize(Policy = "KycAprobado")]
    [HttpPost("pagar-cuota-wallet")]
    public async Task<IActionResult> PagarCuotaWallet([FromBody] PagarCuotaWalletRequest req)
    {
        try { return Ok(await svc.PagarCuotaWalletAsync(req, IdUsuarioActual)); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // ── USUARIO: Compra QR con Cupo Ordinario ──────────────────────────
    [Authorize(Policy = "KycAprobado")]
    [HttpPost("pagar-qr-con-cupo")]
    public async Task<IActionResult> PagarQrConCupo([FromBody] PagarQrConCupoRequest req)
    {
        try { return Ok(await svc.PagarQrConCupoAsync(req, IdUsuarioActual)); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // ── CUALQUIER ROL AUTENTICADO: Parámetros públicos ────────────────
    [HttpGet("parametros/{tipo}")]
    public async Task<IActionResult> GetParametroPublico(string tipo)
    {
        var param = await svc.GetParametroByTipoAsync(tipo.ToUpperInvariant());
        return param is null ? NotFound() : Ok(param);
    }
}
