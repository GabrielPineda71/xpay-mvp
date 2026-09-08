using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// M2.4b — implementación EF Core de ICarteraDecisionCrediticia (infraestructura
// DORMIDA). Mismo patrón que CarteraConsultaRiesgoStore / CarteraMaterializacionCupoStore:
// BeginTransactionAsync → AppLockHelper.AdquirirAsync (owner=Transaction) →
// re-lectura autoritativa dentro de la transacción → SaveChangesAsync →
// CommitAsync, con rollback seguro.
//
// NO abre conexiones propias. NO usa SQL crudo salvo el sp_getapplock ya
// encapsulado en AppLockHelper. NO llama a MiDecisor. NO materializa cupo.
// NO está registrada en DI. NO tiene ningún caller de runtime.
public sealed class CarteraDecisionCrediticiaStore(XpayDbContext db)
    : ICarteraDecisionCrediticia
{
    public async Task<ResultadoAplicacionDecision> AplicarDecisionAsync(
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
                return await NoAplicarAsync(tx, ResultadoAplicacionDecision.NoElegible).ConfigureAwait(false);

            // ── Guard de idempotencia — AUTORITATIVO, antes que estado/consumo.
            var decisionYaEscrita = !string.Equals(
                solicitud.DecisionCrediticia, CarteraDecisionCrediticia.Pendiente, StringComparison.Ordinal);
            var fechaYaEscrita = solicitud.FechaDecision is not null;

            if (decisionYaEscrita != fechaYaEscrita)
                throw new CarteraDecisionInvarianteException(
                    "Estado de decisión incoherente: decision_crediticia y fecha_decision no concuerdan.");

            if (decisionYaEscrita && fechaYaEscrita)
                return await NoAplicarAsync(tx, ResultadoAplicacionDecision.YaDecidido).ConfigureAwait(false);

            // ── Guards de elegibilidad.
            if (!string.Equals(solicitud.EstadoSolicitud, CarteraSolicitudCupoEstados.EnEvaluacion, StringComparison.Ordinal))
                return await NoAplicarAsync(tx, ResultadoAplicacionDecision.NoElegible).ConfigureAwait(false);

            var intento = await db.CarteraSolicitudCupoIntentos
                .FirstOrDefaultAsync(i => i.IdSolicitud == idSolicitud && i.NumeroIntento == solicitud.NumeroIntento, cancellationToken)
                .ConfigureAwait(false);

            if (intento is null || intento.ResultadoConsumidoUtc is null)
                return await NoAplicarAsync(tx, ResultadoAplicacionDecision.NoElegible).ConfigureAwait(false);

            // ── Motor de decisión PURO (sin DbContext, sin clock, sin red).
            var snapshot = new CarteraDecisionSnapshot(
                ConInformacionObservado:            solicitud.ConInformacionObservado,
                ScoreObservado:                     solicitud.ScoreObservado,
                EstadoScore:                        solicitud.EstadoScore,
                ViabilidadObservada:                solicitud.ViabilidadObservada,
                RatingRecaudosObservado:            solicitud.RatingRecaudosObservado,
                TipoDocumentoObservado:             solicitud.TipoDocumentoObservado,
                EstadoDocumentoDatosBasicosRaw:     solicitud.EstadoDocumentoDatosBasicosRaw,
                EstadoDocumentoInfoDemograficaRaw:  solicitud.EstadoDocumentoInfoDemograficaRaw,
                EstadoDocumentoCaptura:             solicitud.EstadoDocumentoCaptura,
                RangoEdadDatosBasicosRaw:           solicitud.RangoEdadDatosBasicosRaw,
                RangoEdadInfoDemograficaRaw:        solicitud.RangoEdadInfoDemograficaRaw,
                RangoEdadCaptura:                   solicitud.RangoEdadCaptura,
                ComportamientoVectorJson:           solicitud.ComportamientoVectorJson,
                ComportamientoVectorCount:          solicitud.ComportamientoVectorCount);

            var context = CarteraEvaluationContext.DesdeConsultaRaw(solicitud.ConsultaAnioRaw, solicitud.ConsultaMesRaw);

            var resultado = CarteraDecisionEngine.Evaluar(snapshot, CarteraPolicyParameters.Vigente(), context);

            ValidarResultadoInvariantes(resultado);

            // ── Persistencia atómica (un solo timestamp del store, fuera del motor).
            var nowUtc = DateTime.UtcNow;

            switch (resultado.Decision)
            {
                case CarteraDecisionCrediticia.Aprobada:
                    solicitud.EstadoSolicitud      = CarteraSolicitudCupoEstados.AprobadaPendienteCupo;
                    solicitud.MontoAprobado        = resultado.MontoAprobado;
                    solicitud.CodigoMotivoDecision = null;
                    break;
                case CarteraDecisionCrediticia.Rechazada:
                    solicitud.EstadoSolicitud      = CarteraSolicitudCupoEstados.Rechazada;
                    solicitud.MontoAprobado        = 0m;
                    solicitud.CodigoMotivoDecision = resultado.MotivoPrimario;
                    break;
                case CarteraDecisionCrediticia.NoDecidible:
                    solicitud.EstadoSolicitud      = CarteraSolicitudCupoEstados.PendienteRevisionManual;
                    solicitud.MontoAprobado        = null;
                    solicitud.CodigoMotivoDecision = resultado.MotivoPrimario;
                    break;
                default:
                    throw new CarteraDecisionInvarianteException(
                        $"El motor devolvió una Decision no reconocida: {resultado.Decision}.");
            }

            solicitud.DecisionCrediticia        = resultado.Decision;
            solicitud.FechaDecision             = nowUtc;
            solicitud.FechaActualizacion        = nowUtc;
            solicitud.SenalPosibleSuplantacion  = resultado.SenalPosibleSuplantacion;

            for (var i = 0; i < resultado.MotivosOrdenados.Count; i++)
            {
                db.CarteraSolicitudCupoMotivosDecision.Add(new CarteraSolicitudCupoMotivoDecision
                {
                    IdSolicitud  = idSolicitud,
                    Orden        = (short)(i + 1),
                    CodigoMotivo = resultado.MotivosOrdenados[i],
                });
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return ResultadoAplicacionDecision.Aplicada;
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    // Invariantes del resultado del motor ANTES de persistir. Fail-closed:
    // cualquier incoherencia → excepción → rollback total (sin decisión parcial).
    private static void ValidarResultadoInvariantes(ResultadoDecisionCrediticia r)
    {
        if (r.MotivosOrdenados.Distinct(StringComparer.Ordinal).Count() != r.MotivosOrdenados.Count)
            throw new CarteraDecisionInvarianteException("La lista de motivos contiene códigos duplicados.");

        switch (r.Decision)
        {
            case CarteraDecisionCrediticia.Aprobada:
                if (r.MontoAprobado is null || r.MontoAprobado.Value <= 0m)
                    throw new CarteraDecisionInvarianteException("APROBADA sin monto_aprobado > 0.");
                if (r.MotivosOrdenados.Count != 0 || r.MotivoPrimario is not null)
                    throw new CarteraDecisionInvarianteException("APROBADA con motivos.");
                break;

            case CarteraDecisionCrediticia.Rechazada:
                if (r.MontoAprobado != 0m)
                    throw new CarteraDecisionInvarianteException("RECHAZADA con monto_aprobado != 0.");
                if (r.MotivosOrdenados.Count < 1)
                    throw new CarteraDecisionInvarianteException("RECHAZADA sin motivos.");
                if (!string.Equals(r.MotivoPrimario, r.MotivosOrdenados[0], StringComparison.Ordinal))
                    throw new CarteraDecisionInvarianteException("Motivo primario no coincide con orden 1.");
                break;

            case CarteraDecisionCrediticia.NoDecidible:
                if (r.MontoAprobado is not null)
                    throw new CarteraDecisionInvarianteException("NO_DECIDIBLE con monto_aprobado no NULL.");
                if (r.MotivosOrdenados.Count < 1)
                    throw new CarteraDecisionInvarianteException("NO_DECIDIBLE sin motivos.");
                if (!string.Equals(r.MotivoPrimario, r.MotivosOrdenados[0], StringComparison.Ordinal))
                    throw new CarteraDecisionInvarianteException("Motivo primario no coincide con orden 1.");
                if (r.MotivosOrdenados[0] is not (CarteraMotivoDecision.InformacionInsuficiente or CarteraMotivoDecision.ValorFueraPolitica))
                    throw new CarteraDecisionInvarianteException("NO_DECIDIBLE cuyo motivo primario no es una causa NO_DECIDIBLE.");
                break;

            default:
                throw new CarteraDecisionInvarianteException($"Decision no reconocida: {r.Decision}.");
        }
    }

    private static async Task<ResultadoAplicacionDecision> NoAplicarAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, ResultadoAplicacionDecision resultado)
    {
        await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        return resultado;
    }

    // Mismo criterio que CarteraConsultaRiesgoStore.ValidarResultadoLock:
    // 0/1 → adquirido ; -1/-2/-3 → contención transitoria ; otro → error técnico.
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
                    "Hay otra operación en curso para esta solicitud. Intenta de nuevo en unos segundos.");
            default:
                throw new Exception($"sp_getapplock devolvió un código inesperado: {resultado}.");
        }
    }
}
