using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;

namespace Xpay.Api.Services;

// M2.3a — implementación EF Core de ICarteraConsultaRiesgoStore. Mismo patrón
// que CarteraOrdinariaService / KycService: BeginTransactionAsync →
// AppLockHelper.AdquirirAsync (owner=Transaction) → re-lectura dentro del
// lock → SaveChangesAsync → CommitAsync, con rollback seguro.
//
// NO abre conexiones propias. NO usa SQL crudo salvo el sp_getapplock ya
// encapsulado en AppLockHelper. NO cambia el esquema.
public sealed class CarteraConsultaRiesgoStore(XpayDbContext db) : ICarteraConsultaRiesgoStore
{
    public async Task<ConsultaRiesgoContexto?> CargarContextoAsync(
        long idSolicitud, long idUsuario, CancellationToken cancellationToken)
    {
        var solicitud = await db.CarteraSolicitudesCupo
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.IdSolicitud == idSolicitud, cancellationToken)
            .ConfigureAwait(false);

        // No revelar existencia de una solicitud ajena.
        if (solicitud is null || solicitud.IdUsuario != idUsuario)
            return null;

        var persona = await db.Personas
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.IdPersona == solicitud.IdPersona, cancellationToken)
            .ConfigureAwait(false);

        return new ConsultaRiesgoContexto(
            solicitud.IdSolicitud, solicitud.IdUsuario, solicitud.IdPersona, solicitud.EstadoSolicitud, persona);
    }

    public async Task<bool> IntentarIniciarConsultaAsync(
        long idSolicitud, long idUsuario, DateTime fechaUtc, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ValidarResultadoLock(await AppLockHelper
                .AdquirirAsync(db, $"XPAY:CARTERA_RIESGO:{idSolicitud}", cancellationToken)
                .ConfigureAwait(false));

            var solicitud = await db.CarteraSolicitudesCupo
                .FirstOrDefaultAsync(s => s.IdSolicitud == idSolicitud, cancellationToken)
                .ConfigureAwait(false);

            if (solicitud is null
                || solicitud.IdUsuario != idUsuario
                || !string.Equals(solicitud.EstadoSolicitud, CarteraSolicitudCupoEstados.Recibida, StringComparison.Ordinal))
            {
                await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            solicitud.EstadoSolicitud    = CarteraSolicitudCupoEstados.ConsultandoRiesgo;
            solicitud.FechaActualizacion = fechaUtc;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task CompletarIntentoAsync(
        long idSolicitud, ResultadoIntentoDurable outcome, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var solicitud = await db.CarteraSolicitudesCupo
                .FirstOrDefaultAsync(s => s.IdSolicitud == idSolicitud, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("Solicitud de cupo desaparecida durante la consulta de riesgo — inconsistencia de datos.");

            var intento = await db.CarteraSolicitudCupoIntentos
                .Where(i => i.IdSolicitud == idSolicitud && i.NumeroIntento == 1)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("Intento PRE-CALL ausente durante la consulta de riesgo — inconsistencia de datos.");

            intento.ResultadoTecnico          = outcome.ResultadoTecnico;
            intento.HttpStatusObservado       = outcome.HttpStatusObservado;
            intento.ContentStatusObservado    = outcome.ContentStatusObservado;
            intento.FechaFin                  = outcome.FechaFinUtc;
            intento.EsIntentoConResultadoUtil = outcome.EsResultadoUtil;

            solicitud.EstadoSolicitud    = outcome.EstadoSolicitudFinal;
            solicitud.FechaActualizacion = outcome.FechaFinUtc;
            // decision_crediticia y las columnas de score/monto quedan intactas.

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    // Mismo criterio que CarteraOrdinariaService.ValidarResultadoLockSolicitudCupo:
    // 0/1 → adquirido; -1/-2/-3 → contención transitoria; otro → error técnico.
    private static void ValidarResultadoLock(int resultado)
    {
        switch (resultado)
        {
            case 0:
            case 1:
                return;
            case -1:
            case -2:
            case -3:
                throw new InvalidOperationException(
                    "Hay otra consulta de riesgo en curso para esta solicitud. Intenta de nuevo en unos segundos.");
            default:
                throw new Exception($"sp_getapplock devolvió un código inesperado: {resultado}.");
        }
    }
}
