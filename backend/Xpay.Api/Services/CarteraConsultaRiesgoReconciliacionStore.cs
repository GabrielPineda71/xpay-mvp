using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;

namespace Xpay.Api.Services;

// M2.4d — RECONCILIACIÓN FAIL-CLOSED de una consulta de riesgo ATASCADA
// (ventanas de crash W2/W3 de XPAY-203).
//
// Cierra de forma determinista un intento cuya solicitud quedó en
// CONSULTANDO_RIESGO con el intento en fase PRE_CALL o ENVIO_INCIERTO — estados
// desde los que el servicio de consulta NO puede avanzar solo (frontera de
// no-retry-automático) y para los que TX-B no es invocable.
//
// OBJETIVO ÚNICO:
//   solicitud.estado_solicitud   → ERROR_PROVEEDOR
//   intento.resultado_tecnico     → RESULTADO_INCIERTO   (fase → FINALIZADO)
//
// NO vuelve a llamar a MiDecisor. NO reconstruye ningún resultado. NO hace
// auto-retry. NO toca crudos ni P0. NO reconcilia EN_EVALUACION / APROBADA* /
// RECHAZADA / PENDIENTE_REVISION_MANUAL (→ NoElegible).
//
// Idempotencia: si la solicitud ya está cerrada en ERROR_PROVEEDOR con un
// intento FINALIZADO coherente → SolicitudYaCerrada (no-op). NO se afirma que
// ese cierre haya venido de una reconciliación W2/W3: ERROR_PROVEEDOR también
// es el resultado normal de TX-B ante una falla técnica del proveedor, y no
// existe marca durable que distinga ambos orígenes.
//
// Concurrencia: mismo AppLock de riesgo (XPAY:CARTERA_RIESGO:{idSolicitud},
// owner=Transaction) que usan TX-A / consumo / decisión, para no competir.
public interface ICarteraConsultaRiesgoReconciliacion
{
    Task<ResultadoReconciliacionConsulta> ReconciliarConsultaAtascadaAsync(
        long idSolicitud, CancellationToken cancellationToken = default);
}

public enum ResultadoReconciliacionConsulta
{
    // Esta llamada realizó AHORA la transición fail-closed desde CONSULTANDO_RIESGO
    // + intento PRE_CALL/ENVIO_INCIERTO → ERROR_PROVEEDOR / RESULTADO_INCIERTO.
    Reconciliada,
    // La solicitud ya está cerrada en ERROR_PROVEEDOR (intento FINALIZADO
    // coherente) — esta llamada NO realiza cambios. NEUTRAL: no afirma que el
    // cierre provenga de una reconciliación previa.
    SolicitudYaCerrada,
    // No existe; no está en CONSULTANDO_RIESGO ni en ERROR_PROVEEDOR-coherente;
    // o el intento no está en PRE_CALL / ENVIO_INCIERTO (para CONSULTANDO_RIESGO)
    // o no está FINALIZADO coherente (para ERROR_PROVEEDOR). NO se modifica nada.
    NoElegible,
}

public sealed class CarteraConsultaRiesgoReconciliacionStore(XpayDbContext db)
    : ICarteraConsultaRiesgoReconciliacion
{
    private static readonly string[] FasesReconciliables =
    {
        CarteraIntentoFases.PreCall,
        CarteraIntentoFases.EnvioIncierto,
    };

    public async Task<ResultadoReconciliacionConsulta> ReconciliarConsultaAtascadaAsync(
        long idSolicitud, CancellationToken cancellationToken = default)
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

            if (solicitud is null)
                return await SalirAsync(tx, ResultadoReconciliacionConsulta.NoElegible).ConfigureAwait(false);

            var intento = await db.CarteraSolicitudCupoIntentos
                .FirstOrDefaultAsync(
                    i => i.IdSolicitud == idSolicitud && i.NumeroIntento == solicitud.NumeroIntento, cancellationToken)
                .ConfigureAwait(false);

            // Idempotencia NEUTRAL — la solicitud ya está cerrada en ERROR_PROVEEDOR.
            // Se exige que el intento actual sea un cierre coherente (FINALIZADO
            // con fecha_fin y resultado_tecnico). NO se afirma que ese cierre
            // venga de una reconciliación W2/W3 — pudo ser TX-B por falla real
            // del proveedor. Sin cierre coherente → NoElegible (fail-closed suave,
            // sin tocar la BD).
            if (string.Equals(solicitud.EstadoSolicitud, CarteraSolicitudCupoEstados.ErrorProveedor, StringComparison.Ordinal))
            {
                var cerradoCoherente = intento is not null
                    && string.Equals(intento.FaseIntento, CarteraIntentoFases.Finalizado, StringComparison.Ordinal)
                    && intento.FechaFin is not null
                    && intento.ResultadoTecnico is not null;

                return await SalirAsync(tx, cerradoCoherente
                    ? ResultadoReconciliacionConsulta.SolicitudYaCerrada
                    : ResultadoReconciliacionConsulta.NoElegible).ConfigureAwait(false);
            }

            if (!string.Equals(solicitud.EstadoSolicitud, CarteraSolicitudCupoEstados.ConsultandoRiesgo, StringComparison.Ordinal))
                return await SalirAsync(tx, ResultadoReconciliacionConsulta.NoElegible).ConfigureAwait(false);

            if (intento is null
                || !FasesReconciliables.Contains(intento.FaseIntento, StringComparer.Ordinal)
                || intento.FechaFin is not null
                || intento.ResultadoTecnico is not null)
                return await SalirAsync(tx, ResultadoReconciliacionConsulta.NoElegible).ConfigureAwait(false);

            var nowUtc = DateTime.UtcNow;

            intento.ResultadoTecnico          = CarteraConsultaRiesgoResultados.ResultadoIncierto;
            intento.HttpStatusObservado       = null;
            intento.ContentStatusObservado    = null;
            intento.FechaFin                  = nowUtc;
            intento.EsIntentoConResultadoUtil = false;
            intento.FaseIntento               = CarteraIntentoFases.Finalizado;
            // Crudos y P0ProviderRawJson permanecen NULL (no se recibió resultado).

            solicitud.EstadoSolicitud    = CarteraSolicitudCupoEstados.ErrorProveedor;
            solicitud.FechaActualizacion = nowUtc;
            // decision_crediticia / monto_aprobado / observados quedan intactos.

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return ResultadoReconciliacionConsulta.Reconciliada;
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private static async Task<ResultadoReconciliacionConsulta> SalirAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, ResultadoReconciliacionConsulta resultado)
    {
        await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        return resultado;
    }

    // Mismo criterio que CarteraConsultaRiesgoStore.ValidarResultadoLock.
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
                    "Hay otra operación de riesgo en curso para esta solicitud. Intenta de nuevo en unos segundos.");
            default:
                throw new Exception($"sp_getapplock devolvió un código inesperado: {resultado}.");
        }
    }
}
