using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Integrations.MiDecisor;

namespace Xpay.Api.Controllers;

// XPAY-702 - estado de la ultima solicitud de cupo del cliente, para que la UI
// se recupere tras refresh/timeout sin crear solicitudes nuevas ni repetir
// la consulta. Sin score, motivos ni datos de riesgo.
public sealed record MiSolicitudActualResponse(
    long      IdSolicitud,
    string    EstadoSolicitud,
    string    DecisionCrediticia,
    decimal?  MontoAprobado,
    DateTime  FechaSolicitud,
    DateTime? FechaDecision,
    bool      RequiereRevisionManual,
    bool      ConsentimientoRegistrado);

// Siempre 200 (el cliente HTTP del frontend no admite 204 sin cuerpo).
public sealed record MiSolicitudActualEnvelope(MiSolicitudActualResponse? Solicitud);

[ApiController]
[Route("api/cartera-ordinaria")]
[Authorize(Policy = "KycAprobado")]
public sealed class CarteraSolicitudClienteController(
    XpayDbContext db, AutorizacionConsultaRiesgoDurable consentimiento) : ControllerBase
{
    private long IdUsuarioActual => long.Parse(User.FindFirst("idUsuario")?.Value ?? "0");

    [HttpGet("mi-solicitud-actual")]
    public async Task<IActionResult> MiSolicitudActual()
    {
        var idUsuario = IdUsuarioActual;
        var ct = HttpContext.RequestAborted;
        var s = await db.CarteraSolicitudesCupo.AsNoTracking()
            .Where(x => x.IdUsuario == idUsuario)
            .OrderByDescending(x => x.FechaSolicitud)
            .ThenByDescending(x => x.IdSolicitud)
            .FirstOrDefaultAsync(ct);
        if (s is null)
            return Ok(new MiSolicitudActualEnvelope(null));

        var aprobada = s.EstadoSolicitud == CarteraSolicitudCupoEstados.Aprobada;
        var revision = s.EstadoSolicitud == CarteraSolicitudCupoEstados.PendienteRevisionManual;
        var conConsentimiento = await consentimiento
            .TieneAutorizacionVigenteAsync(idUsuario, s.IdSolicitud, ct);

        return Ok(new MiSolicitudActualEnvelope(new MiSolicitudActualResponse(
            s.IdSolicitud, s.EstadoSolicitud, s.DecisionCrediticia,
            aprobada ? s.MontoAprobado : null, s.FechaSolicitud, s.FechaDecision,
            revision, conConsentimiento)));
    }
}
