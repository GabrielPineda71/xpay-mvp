using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Exceptions;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Route("api/cartera-ordinaria")]
[Authorize]
public class CarteraOrdinariaController(
    CarteraOrdinariaService svc,
    XpayDbContext db,
    ICarteraDecisionCrediticiaOrchestrator orchestrator,
    ICarteraSolicitudEvaluacionReader evaluacionReader,
    ICarteraConsultaRiesgoReconciliacion reconciliacion,
    AuditLogService audit) : ControllerBase
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

    // ── USUARIO: Autorización de consulta en centrales de riesgo (V1) ─────
    // ACTA 001 §3 · XPAY-195/196/197. Evidencia durable de aceptación. NO llama
    // a MiDecisor. NO avanza la solicitud. Regla V1 estricta: la aceptación
    // autoriza EXCLUSIVAMENTE la consulta de esta misma solicitud.

    // Texto vigente — única fuente de verdad = recurso backend. Autenticado
    // (hereda [Authorize] de la clase) ; el texto no es secreto.
    [HttpGet("autorizacion-consulta-riesgo/texto-vigente")]
    public IActionResult GetTextoAutorizacionConsultaRiesgo()
        => Ok(new AutorizacionConsultaRiesgoTextoResponse(
            CarteraAutorizacionConsultaRiesgoTextos.V1_Version,
            CarteraAutorizacionConsultaRiesgoTextos.V1_Texto,
            CarteraAutorizacionConsultaRiesgoTextos.V1_HashSha256));

    [Authorize(Policy = "KycAprobado")]
    [HttpPost("solicitudes/{idSolicitud:long}/autorizar-consulta-riesgo")]
    public async Task<IActionResult> AutorizarConsultaRiesgo(
        long idSolicitud, [FromBody] RegistrarAutorizacionConsultaRiesgoRequest req)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Version))
            return BadRequest(new { error = "Falta la versión del texto de autorización." });

        // correlationId controlado por el servidor (mismo patrón que SolicitarCupo).
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? HttpContext.TraceIdentifier;

        // El store se instancia con el XpayDbContext scoped (mismo patrón que las
        // primitivas dormidas M2.4b/M2.4c en sus tests). NO se registra en DI.
        var store = new CarteraAutorizacionConsultaRiesgoStore(db);
        var resultado = await store.RegistrarAceptacionAsync(
            idSolicitud, IdUsuarioActual, req.Version, correlationId, HttpContext.RequestAborted);

        return resultado switch
        {
            ResultadoRegistroAutorizacion.Registrada => Ok(new RegistrarAutorizacionConsultaRiesgoResponse(
                idSolicitud, CarteraAutorizacionConsultaRiesgoTextos.V1_Version, "registrada")),
            ResultadoRegistroAutorizacion.YaRegistrada => Ok(new RegistrarAutorizacionConsultaRiesgoResponse(
                idSolicitud, CarteraAutorizacionConsultaRiesgoTextos.V1_Version, "ya_registrada")),
            _ => BadRequest(new { error = "La solicitud no admite el registro de autorización en este momento." }),
        };
    }

    // ── USUARIO: Iniciar evaluación crediticia (M2.4d — trigger explícito) ─
    // Dispara el pipeline solicitud → consulta de riesgo → decisión →
    // materialización mediante el orquestador resume-aware. La idempotencia real
    // NO la aporta ningún header: es la transición durable
    // RECIBIDA→CONSULTANDO_RIESGO del servicio de consulta, el no-auto-retry tras
    // ENVIO_INCIERTO y los guards YaConsumido/YaDecidido/YaMaterializado
    // downstream. Por eso NO se exige Idempotency-Key (repetir la llamada es
    // seguro e idempotente). Con la DI actual la barrera de autorización
    // (AutorizacionConsultaRiesgoNoDisponible) impide alcanzar MiDecisor: el
    // consentimiento durable capturado NO basta.
    [Authorize(Policy = "KycAprobado")]
    [HttpPost("solicitudes/{idSolicitud:long}/iniciar-evaluacion")]
    public async Task<IActionResult> IniciarEvaluacion(long idSolicitud)
    {
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? HttpContext.TraceIdentifier;
        var idUsuario     = IdUsuarioActual;

        audit.LogSensitiveAction(HttpContext, "CARTERA_EVALUACION_INICIO",
            new { idSolicitud, idUsuario, correlationId });

        var r = await orchestrator.EvaluarAsync(idSolicitud, idUsuario, correlationId, HttpContext.RequestAborted);

        if (r.Resultado == OrquestacionEvaluacionEstado.NoEncontrada)
            return NotFound(new { error = "Solicitud no encontrada." });

        audit.LogSensitiveAction(HttpContext, "CARTERA_EVALUACION_RESULTADO",
            new { idSolicitud, idUsuario, resultado = r.Resultado.ToString(), estado = r.EstadoSolicitud, correlationId });

        var mensaje = r.Resultado switch
        {
            OrquestacionEvaluacionEstado.AutorizacionNoDisponible =>
                "No pudimos completar la evaluación. La solicitud requiere revisión.",
            OrquestacionEvaluacionEstado.DatosPersonaInsuficientes =>
                "No pudimos completar la evaluación con la información disponible. La solicitud requiere revisión.",
            OrquestacionEvaluacionEstado.RequiereReconciliacion =>
                "La evaluación quedó en un estado que requiere revisión.",
            _ when r.RequiereRevisionManual => "Tu solicitud pasó a revisión manual.",
            _ => "Evaluación procesada.",
        };

        return Ok(new IniciarEvaluacionResponse(
            idSolicitud,
            r.EstadoSolicitud ?? CarteraSolicitudCupoEstados.Recibida,
            r.DecisionCrediticia ?? CarteraDecisionCrediticia.Pendiente,
            r.MontoAprobado,
            r.FechaDecision,
            r.RequiereRevisionManual,
            mensaje));
    }

    // ── USUARIO: Estado de una solicitud de cupo (M2.4d) ──────────────────
    // Proyección segura: nunca expone provider raw, P0, score crudo,
    // resultado_tecnico, señal de suplantación ni correlación interna. La
    // decisión/monto sólo se exponen cuando ya hay una decisión final.
    [Authorize(Policy = "KycAprobado")]
    [HttpGet("solicitudes/{idSolicitud:long}")]
    public async Task<IActionResult> GetSolicitudEvaluacion(long idSolicitud)
    {
        var snap = await evaluacionReader.LeerAsync(idSolicitud, IdUsuarioActual, HttpContext.RequestAborted);
        if (snap is null)
            return NotFound(new { error = "Solicitud no encontrada." });

        var hayDecision      = snap.FechaDecision is not null;
        var decisionExpuesta = hayDecision ? snap.DecisionCrediticia : CarteraDecisionCrediticia.Pendiente;
        var montoExpuesto    = hayDecision ? snap.MontoAprobado : null;
        var requiereRevision = string.Equals(
            snap.EstadoSolicitud, CarteraSolicitudCupoEstados.PendienteRevisionManual, StringComparison.Ordinal);

        return Ok(new SolicitudEvaluacionEstadoResponse(
            snap.IdSolicitud, snap.EstadoSolicitud, decisionExpuesta, montoExpuesto, snap.FechaDecision, requiereRevision));
    }

    // ── ADMIN: Reconciliar una consulta de riesgo atascada (M2.4d — W2/W3) ─
    // Cierra fail-closed una solicitud en CONSULTANDO_RIESGO cuyo intento quedó
    // en PRE_CALL / ENVIO_INCIERTO tras un crash: estado → ERROR_PROVEEDOR,
    // intento → RESULTADO_INCIERTO. NUNCA vuelve a llamar a MiDecisor, no
    // reconstruye resultado, no hace retry. Idempotente. El actor / motivo /
    // correlación se registran vía AuditLogService (no hay columna durable
    // dedicada — gap conocido XPAY-204).
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    [HttpPost("admin/solicitudes/{idSolicitud:long}/reconciliar-consulta-atascada")]
    public async Task<IActionResult> ReconciliarConsultaAtascada(
        long idSolicitud, [FromBody] ReconciliarConsultaAtascadaRequest req)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.MotivoOperativo))
            return BadRequest(new { error = "Falta el motivo operativo." });

        var motivo = req.MotivoOperativo.Trim();
        if (motivo.Length > 300) motivo = motivo[..300];

        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? HttpContext.TraceIdentifier;
        var adminId       = IdUsuarioActual;

        audit.LogSensitiveAction(HttpContext, "CARTERA_CONSULTA_RIESGO_RECONCILIACION_ATTEMPT",
            new { idSolicitud, adminId, motivo, correlationId });

        var resultado = await reconciliacion.ReconciliarConsultaAtascadaAsync(idSolicitud, HttpContext.RequestAborted);

        audit.LogSensitiveAction(HttpContext, "CARTERA_CONSULTA_RIESGO_RECONCILIACION_RESULTADO",
            new { idSolicitud, adminId, motivo, correlationId, resultado = resultado.ToString() });

        return resultado switch
        {
            ResultadoReconciliacionConsulta.Reconciliada => Ok(new ReconciliarConsultaAtascadaResponse(
                idSolicitud, CarteraSolicitudCupoEstados.ErrorProveedor, "reconciliada")),
            ResultadoReconciliacionConsulta.SolicitudYaCerrada => Ok(new ReconciliarConsultaAtascadaResponse(
                idSolicitud, CarteraSolicitudCupoEstados.ErrorProveedor, "solicitud_ya_cerrada")),
            _ => Conflict(new { error = "La solicitud no está en un estado reconciliable." }),
        };
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
