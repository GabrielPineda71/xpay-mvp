using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;

namespace Xpay.Api.Services;

// M2.4d — lectura mínima y AsNoTracking del estado durable de una solicitud de
// cupo, con verificación de ownership (devuelve null si la solicitud no existe
// O no pertenece al usuario — no se distingue, para no revelar existencia).
//
// Usado por el orquestador (para decidir la fase a reanudar) y por el endpoint
// de estado. NO escribe. NO llama a MiDecisor. NO expone crudos del proveedor
// ni P0.
public interface ICarteraSolicitudEvaluacionReader
{
    Task<CarteraSolicitudEvaluacionSnapshot?> LeerAsync(
        long idSolicitud, long idUsuario, CancellationToken cancellationToken = default);
}

public sealed record CarteraSolicitudEvaluacionSnapshot(
    long      IdSolicitud,
    long      IdUsuario,
    string    EstadoSolicitud,
    int       NumeroIntento,
    string    DecisionCrediticia,
    decimal?  MontoAprobado,
    DateTime? FechaDecision,
    string?   CodigoMotivoDecision,
    long?     IdCupoOrdinario);

public sealed class CarteraSolicitudEvaluacionReader(XpayDbContext db) : ICarteraSolicitudEvaluacionReader
{
    public async Task<CarteraSolicitudEvaluacionSnapshot?> LeerAsync(
        long idSolicitud, long idUsuario, CancellationToken cancellationToken = default)
    {
        var s = await db.CarteraSolicitudesCupo
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdSolicitud == idSolicitud, cancellationToken)
            .ConfigureAwait(false);

        if (s is null || s.IdUsuario != idUsuario)
            return null;

        return new CarteraSolicitudEvaluacionSnapshot(
            s.IdSolicitud,
            s.IdUsuario,
            s.EstadoSolicitud,
            s.NumeroIntento,
            s.DecisionCrediticia,
            s.MontoAprobado,
            s.FechaDecision,
            s.CodigoMotivoDecision,
            s.IdCupoOrdinario);
    }
}
