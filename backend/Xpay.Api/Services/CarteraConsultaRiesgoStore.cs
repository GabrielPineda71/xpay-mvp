using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;

namespace Xpay.Api.Services;

// M2.3b1 — implementación EF Core de ICarteraConsultaRiesgoStore. Mismo patrón
// que CarteraOrdinariaService / KycService: BeginTransactionAsync →
// AppLockHelper.AdquirirAsync (owner=Transaction) para la transición inicial
// → re-lectura dentro de la transacción → SaveChangesAsync → CommitAsync, con
// rollback seguro.
//
// NO abre conexiones propias. NO usa SQL crudo salvo el sp_getapplock ya
// encapsulado en AppLockHelper. NO cambia el esquema.
//
// M2.3b3 — también implementa ICarteraResultadoRiesgoPurga (infraestructura
// DORMIDA de purga de crudos): esa cara del contrato NO está registrada en DI
// y NO tiene ningún caller de runtime.
public sealed class CarteraConsultaRiesgoStore(XpayDbContext db)
    : ICarteraConsultaRiesgoStore, ICarteraResultadoRiesgoPurga
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
                await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
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

    public async Task MarcarEnvioInciertoAsync(
        long idSolicitud, long idUsuario, DateTime fechaUtc, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var (solicitud, intento) = await CargarSolicitudIntentoAsync(idSolicitud, cancellationToken).ConfigureAwait(false);

            ExigirGuard(
                solicitud is not null
                && solicitud.IdUsuario == idUsuario
                && string.Equals(solicitud.EstadoSolicitud, CarteraSolicitudCupoEstados.ConsultandoRiesgo, StringComparison.Ordinal)
                && intento is not null
                && string.Equals(intento.FaseIntento, CarteraIntentoFases.PreCall, StringComparison.Ordinal)
                && intento.FechaFin is null
                && intento.ResultadoTecnico is null,
                "No se puede marcar el envío como incierto: estado o fase de intento inesperados.");

            intento!.FaseIntento = CarteraIntentoFases.EnvioIncierto;
            solicitud!.FechaActualizacion = fechaUtc;
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

    public async Task FinalizarIntentoAsync(
        long idSolicitud, long idUsuario, ResultadoIntentoDurable outcome, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var (solicitud, intento) = await CargarSolicitudIntentoAsync(idSolicitud, cancellationToken).ConfigureAwait(false);

            ExigirGuard(
                solicitud is not null
                && solicitud.IdUsuario == idUsuario
                && string.Equals(solicitud.EstadoSolicitud, CarteraSolicitudCupoEstados.ConsultandoRiesgo, StringComparison.Ordinal)
                && intento is not null
                && string.Equals(intento.FaseIntento, CarteraIntentoFases.EnvioIncierto, StringComparison.Ordinal)
                && intento.FechaFin is null
                && intento.ResultadoTecnico is null,
                "No se puede finalizar el intento: estado o fase inesperados.");

            intento!.ResultadoTecnico          = outcome.ResultadoTecnico;
            intento.HttpStatusObservado        = outcome.HttpStatusObservado;
            intento.ContentStatusObservado     = outcome.ContentStatusObservado;
            intento.FechaFin                   = outcome.FechaFinUtc;
            intento.EsIntentoConResultadoUtil  = outcome.EsResultadoUtil;
            intento.ConInformacion             = outcome.ConInformacion;
            intento.ScoreRaw                   = outcome.ScoreRaw;
            intento.ViabilidadRaw              = outcome.ViabilidadRaw;
            intento.RatingRecaudosRaw          = outcome.RatingRecaudosRaw;
            intento.MontoSugeridoRaw           = outcome.MontoSugeridoRaw;
            intento.AlertasCount               = outcome.AlertasCount;
            intento.FaseIntento                = CarteraIntentoFases.Finalizado;

            solicitud!.EstadoSolicitud    = outcome.EstadoSolicitudFinal;
            solicitud.FechaActualizacion  = outcome.FechaFinUtc;
            // decision_crediticia y las columnas de decisión de la solicitud
            // (score_observado, monto_sugerido_observado, estado_score,
            // viabilidad_observada, rating_recaudos_observado) quedan intactas.

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

    // ── M2.3b3 — ICarteraResultadoRiesgoPurga (infraestructura DORMIDA) ──
    // Ver el doc del contrato: NO invocar operacionalmente sin política de
    // retención, evento de inicio, gate de consumo por el motor de decisión
    // e invocador autorizado definidos. Sin caller de runtime.
    public async Task<ResultadoPurgaIntento> PurgarResultadoIntentoAsync(
        long idSolicitud, int numeroIntento, DateTime cutoffUtc, CancellationToken cancellationToken)
    {
        // cutoffUtc debe ser un instante UTC inequívoco. Se exige
        // DateTimeKind.Utc exacto: Local y Unspecified se rechazan (sin
        // conversión automática), para que un llamador no pueda pasar una
        // fecha ambigua sin darse cuenta.
        if (cutoffUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("cutoffUtc debe tener DateTimeKind.Utc.", nameof(cutoffUtc));

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ValidarResultadoLock(await AppLockHelper
                .AdquirirAsync(db, $"XPAY:CARTERA_RIESGO:{idSolicitud}", cancellationToken)
                .ConfigureAwait(false));

            var intento = await db.CarteraSolicitudCupoIntentos
                .FirstOrDefaultAsync(i => i.IdSolicitud == idSolicitud && i.NumeroIntento == numeroIntento, cancellationToken)
                .ConfigureAwait(false);

            if (intento is null
                || !string.Equals(intento.FaseIntento, CarteraIntentoFases.Finalizado, StringComparison.Ordinal))
                return await NoPurgarAsync(tx, ResultadoPurgaIntento.NoElegible).ConfigureAwait(false);

            if (intento.ResultadoPurgadoUtc is not null)
                return await NoPurgarAsync(tx, ResultadoPurgaIntento.YaPurgado).ConfigureAwait(false);

            if (intento.FechaFin is null || intento.FechaFin >= cutoffUtc)
                return await NoPurgarAsync(tx, ResultadoPurgaIntento.NoElegible).ConfigureAwait(false);

            var tieneCrudo =
                intento.ConInformacion is not null
                || intento.ScoreRaw is not null
                || intento.ViabilidadRaw is not null
                || intento.RatingRecaudosRaw is not null
                || intento.MontoSugeridoRaw is not null
                || intento.AlertasCount is not null;

            if (!tieneCrudo)
                return await NoPurgarAsync(tx, ResultadoPurgaIntento.NoElegible).ConfigureAwait(false);

            intento.ConInformacion      = null;
            intento.ScoreRaw            = null;
            intento.ViabilidadRaw       = null;
            intento.RatingRecaudosRaw   = null;
            intento.MontoSugeridoRaw    = null;
            intento.AlertasCount        = null;
            intento.ResultadoPurgadoUtc = DateTime.UtcNow;
            // NO se toca resultado_tecnico / es_intento_con_resultado_util /
            // http_status_observado / content_status_observado / fase_intento /
            // fecha_inicio / fecha_fin / numero_intento / idempotency_key /
            // correlation_id, ni ninguna columna de cartera_solicitudes_cupo.

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return ResultadoPurgaIntento.Purgado;
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private static async Task<ResultadoPurgaIntento> NoPurgarAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, ResultadoPurgaIntento resultado)
    {
        await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        return resultado;
    }

    private async Task<(Models.CarteraSolicitudCupo? solicitud, Models.CarteraSolicitudCupoIntento? intento)>
        CargarSolicitudIntentoAsync(long idSolicitud, CancellationToken cancellationToken)
    {
        var solicitud = await db.CarteraSolicitudesCupo
            .FirstOrDefaultAsync(s => s.IdSolicitud == idSolicitud, cancellationToken)
            .ConfigureAwait(false);

        var intento = await db.CarteraSolicitudCupoIntentos
            .Where(i => i.IdSolicitud == idSolicitud && i.NumeroIntento == 1)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return (solicitud, intento);
    }

    private static void ExigirGuard(bool ok, string mensaje)
    {
        if (!ok)
            throw new InvalidOperationException(mensaje);
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
