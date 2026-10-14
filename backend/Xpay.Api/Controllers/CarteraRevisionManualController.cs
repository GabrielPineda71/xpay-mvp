using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.DTOs;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

// XPAY-702 - bandeja y resolucion de revision manual. Solo ADMIN_XPAY y
// SUPERUSUARIO. La resolucion no acepta monto: lo calcula XPAY por politica.
[ApiController]
[Route("api/cartera-ordinaria/admin/revision-manual")]
[Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
public sealed class CarteraRevisionManualController(
    ICarteraRevisionManualStore store, AuditLogService audit) : ControllerBase
{
    private long IdUsuarioActual => long.Parse(User.FindFirst("idUsuario")?.Value ?? "0");

    [HttpGet]
    public async Task<IActionResult> Listar()
        => Ok(await store.ListarPendientesAsync(HttpContext.RequestAborted));

    [HttpGet("{idSolicitud:long}")]
    public async Task<IActionResult> Detalle(long idSolicitud)
    {
        var d = await store.ObtenerDetalleAsync(idSolicitud, HttpContext.RequestAborted);
        return d is null ? NotFound(new { error = "Solicitud no encontrada." }) : Ok(d);
    }

    [HttpPost("{idSolicitud:long}/resolver")]
    public async Task<IActionResult> Resolver(long idSolicitud, [FromBody] ResolverRevisionManualRequest req)
    {
        var idAdmin = IdUsuarioActual;
        var r = await store.ResolverAsync(
            idSolicitud, req.Decision, req.MotivoCodigo, req.Observacion, idAdmin,
            HttpContext.RequestAborted);

        audit.LogSensitiveAction(HttpContext, "CARTERA_REVISION_MANUAL_RESOLVER",
            new { idSolicitud, idAdmin, req.Decision, req.MotivoCodigo, resultado = r.Resultado.ToString() });

        return r.Resultado switch
        {
            ResultadoResolucionManual.Resuelta => Ok(new
            {
                idSolicitud,
                estadoSolicitud = r.EstadoSolicitud,
                montoAprobado   = r.MontoAprobado,
            }),
            ResultadoResolucionManual.NoEncontrada => NotFound(new { error = "Solicitud no encontrada." }),
            ResultadoResolucionManual.YaResuelta   => Conflict(new { error = "La solicitud ya fue resuelta." }),
            ResultadoResolucionManual.NoElegible   => Conflict(new { error = "La solicitud no está en revisión manual." }),
            ResultadoResolucionManual.NoAprobable  => UnprocessableEntity(new { error = "La política no permite aprobar esta solicitud." }),
            _                                      => BadRequest(new { error = "Decisión o motivo inválido." }),
        };
    }
}
