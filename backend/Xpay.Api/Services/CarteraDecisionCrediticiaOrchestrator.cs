using Microsoft.Extensions.Logging;
using Xpay.Api.Common;
using Xpay.Api.Integrations.MiDecisor;

namespace Xpay.Api.Services;

// M2.4d — ORQUESTADOR de runtime del pipeline de decisión crediticia de Cartera
// Ordinaria. COMPONE las primitivas ya existentes SIN duplicar ni modificar su
// lógica:
//
//   CarteraConsultaRiesgoService.EjecutarConsultaRiesgoAsync   (consulta MiDecisor: pasos 3-6)
//   ICarteraResultadoRiesgoConsumo.ConsumirResultadoRiesgoAsync (M2.4a: paso 7)
//   ICarteraDecisionCrediticia.AplicarDecisionAsync             (M2.4b: pasos 8-9)
//   ICarteraMaterializacionCupo.MaterializarCupoAsync           (M2.4c: paso 10)
//
// RESUME-AWARE: lee el estado durable actual y ejecuta SÓLO la siguiente
// transición pendiente. NUNCA usa excepciones como mecanismo normal para
// decidir qué fase ya ocurrió — consulta el estado explícitamente.
//
// NO ACTIVA EL PROVEEDOR: con la DI actual, IConsultaRiesgoAutorizacion es el
// stub AutorizacionConsultaRiesgoNoDisponible (devuelve false). En el pre-flight
// de CarteraConsultaRiesgoService eso lanza InvalidOperationException ANTES de
// TX-A y ANTES de cualquier llamada HTTP → aquí se traduce a
// AutorizacionNoDisponible (fail-closed, sin proveedor, sin avanzar estado).
//
// Ante una invariante durable (consumo/decisión/materialización) la excepción se
// PROPAGA (fail-closed, sin auto-repair) → ErrorHandlingMiddleware responde 500
// genérico.
public interface ICarteraDecisionCrediticiaOrchestrator
{
    Task<OrquestacionEvaluacionResultado> EvaluarAsync(
        long idSolicitud, long idUsuario, string correlationId, CancellationToken cancellationToken = default);
}

public enum OrquestacionEvaluacionEstado
{
    // La solicitud no existe o no pertenece al usuario.
    NoEncontrada,
    // Barrera de autorización fail-closed: NO se alcanzó el proveedor. La
    // solicitud sigue en RECIBIDA.
    AutorizacionNoDisponible,
    // Los datos de identidad de la persona no permiten construir la consulta.
    // Sigue en RECIBIDA, 0 llamadas al proveedor.
    DatosPersonaInsuficientes,
    // Solicitud atascada en CONSULTANDO_RIESGO (intento PRE_CALL / ENVIO_INCIERTO):
    // requiere reconciliación manual admin. NO se reintenta el proveedor.
    RequiereReconciliacion,
    // Se aplicó (o ya estaba aplicada) una decisión y, si correspondía, se
    // materializó (o ya estaba materializado) el cupo.
    Procesada,
    // La solicitud ya estaba en un estado terminal — se devuelve tal cual.
    Terminal,
    // Estado no accionable por el orquestador (p. ej. VALIDANDO) — sin cambios.
    SinCambio,
}

public sealed record OrquestacionEvaluacionResultado(
    OrquestacionEvaluacionEstado Resultado,
    string?   EstadoSolicitud,
    string?   DecisionCrediticia,
    decimal?  MontoAprobado,
    DateTime? FechaDecision,
    long?     IdCupoOrdinario)
{
    public bool RequiereRevisionManual =>
        string.Equals(EstadoSolicitud, CarteraSolicitudCupoEstados.PendienteRevisionManual, StringComparison.Ordinal);
}

public sealed class CarteraDecisionCrediticiaOrchestrator(
    ICarteraSolicitudEvaluacionReader reader,
    CarteraConsultaRiesgoService consultaRiesgo,
    ICarteraResultadoRiesgoConsumo consumo,
    ICarteraDecisionCrediticia decision,
    ICarteraMaterializacionCupo materializacion,
    ILogger<CarteraDecisionCrediticiaOrchestrator> logger)
    : ICarteraDecisionCrediticiaOrchestrator
{
    // Mensaje EXACTO que CarteraConsultaRiesgoService lanza cuando
    // IConsultaRiesgoAutorizacion devuelve false (pre-flight, sin proveedor).
    // El servicio no expone una excepción tipada para este caso.
    private const string MsgAutorizacionNoDisponible = "Consulta de riesgo no autorizada para esta solicitud.";

    // Cota de seguridad: cada iteración avanza a lo sumo una transición durable
    // (RECIBIDA→consulta, EN_EVALUACION→consumo+decisión, APROBADA_PENDIENTE_CUPO→
    // materialización). 6 es holgado.
    private const int MaxIteraciones = 6;

    public async Task<OrquestacionEvaluacionResultado> EvaluarAsync(
        long idSolicitud, long idUsuario, string correlationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("correlationId requerido", nameof(correlationId));

        for (var i = 0; i < MaxIteraciones; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var snap = await reader.LeerAsync(idSolicitud, idUsuario, cancellationToken).ConfigureAwait(false);
            if (snap is null)
                return Resultado(OrquestacionEvaluacionEstado.NoEncontrada, null);

            switch (snap.EstadoSolicitud)
            {
                case CarteraSolicitudCupoEstados.Recibida:
                {
                    var parar = await IntentarConsultaAsync(idSolicitud, idUsuario, correlationId, cancellationToken)
                        .ConfigureAwait(false);
                    if (parar is not null)
                        return parar;
                    continue; // reanudar desde el nuevo estado durable
                }

                case CarteraSolicitudCupoEstados.ConsultandoRiesgo:
                    logger.LogWarning(
                        "m2.4d: solicitud atascada en CONSULTANDO_RIESGO — requiere reconciliación admin (idSolicitud={IdSolicitud} correlationId={CorrelationId}).",
                        idSolicitud, correlationId);
                    return Resultado(OrquestacionEvaluacionEstado.RequiereReconciliacion, snap);

                case CarteraSolicitudCupoEstados.EnEvaluacion:
                {
                    var cons = await consumo.ConsumirResultadoRiesgoAsync(idSolicitud, snap.NumeroIntento, cancellationToken)
                        .ConfigureAwait(false);
                    if (cons == ResultadoConsumoRiesgo.NoElegible)
                    {
                        logger.LogWarning(
                            "m2.4d: consumo NoElegible en EN_EVALUACION — sin avanzar (idSolicitud={IdSolicitud}).", idSolicitud);
                        return Resultado(OrquestacionEvaluacionEstado.SinCambio, snap);
                    }

                    var dec = await decision.AplicarDecisionAsync(idSolicitud, cancellationToken).ConfigureAwait(false);
                    if (dec == ResultadoAplicacionDecision.NoElegible)
                    {
                        var actual = await reader.LeerAsync(idSolicitud, idUsuario, cancellationToken).ConfigureAwait(false);
                        logger.LogWarning(
                            "m2.4d: decisión NoElegible en EN_EVALUACION — sin avanzar (idSolicitud={IdSolicitud}).", idSolicitud);
                        return Resultado(OrquestacionEvaluacionEstado.SinCambio, actual);
                    }
                    continue; // estado ahora APROBADA_PENDIENTE_CUPO | RECHAZADA | PENDIENTE_REVISION_MANUAL
                }

                case CarteraSolicitudCupoEstados.AprobadaPendienteCupo:
                {
                    var mat = await materializacion.MaterializarCupoAsync(idSolicitud, cancellationToken).ConfigureAwait(false);
                    var actual = await reader.LeerAsync(idSolicitud, idUsuario, cancellationToken).ConfigureAwait(false);
                    logger.LogInformation(
                        "m2.4d: materialización {Resultado} (idSolicitud={IdSolicitud} estadoFinal={Estado}).",
                        mat, idSolicitud, actual?.EstadoSolicitud);
                    return Resultado(OrquestacionEvaluacionEstado.Procesada, actual);
                }

                case CarteraSolicitudCupoEstados.Aprobada:
                case CarteraSolicitudCupoEstados.Rechazada:
                case CarteraSolicitudCupoEstados.PendienteRevisionManual:
                case CarteraSolicitudCupoEstados.ErrorProveedor:
                    return Resultado(OrquestacionEvaluacionEstado.Terminal, snap);

                default:
                    logger.LogInformation(
                        "m2.4d: estado no accionable por el orquestador (idSolicitud={IdSolicitud} estado={Estado}).",
                        idSolicitud, snap.EstadoSolicitud);
                    return Resultado(OrquestacionEvaluacionEstado.SinCambio, snap);
            }
        }

        var ultimo = await reader.LeerAsync(idSolicitud, idUsuario, cancellationToken).ConfigureAwait(false);
        logger.LogWarning(
            "m2.4d: se agotaron las iteraciones del orquestador (idSolicitud={IdSolicitud} estado={Estado}).",
            idSolicitud, ultimo?.EstadoSolicitud);
        return Resultado(OrquestacionEvaluacionEstado.SinCambio, ultimo);
    }

    // Devuelve un resultado NO-null para terminar de inmediato; null para
    // continuar el bucle (reanudar desde el nuevo estado durable).
    private async Task<OrquestacionEvaluacionResultado?> IntentarConsultaAsync(
        long idSolicitud, long idUsuario, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            var r = await consultaRiesgo
                .EjecutarConsultaRiesgoAsync(idSolicitud, idUsuario, correlationId, cancellationToken)
                .ConfigureAwait(false);
            logger.LogInformation(
                "m2.4d: consulta de riesgo ejecutada (idSolicitud={IdSolicitud} estado={Estado} util={Util}).",
                idSolicitud, r.EstadoSolicitud, r.EsResultadoUtil);
            return null; // continuar → EN_EVALUACION | ERROR_PROVEEDOR
        }
        catch (InvalidOperationException ex) when (string.Equals(ex.Message, MsgAutorizacionNoDisponible, StringComparison.Ordinal))
        {
            logger.LogInformation(
                "m2.4d: autorización de consulta de riesgo no disponible (stub fail-closed) — 0 llamadas al proveedor (idSolicitud={IdSolicitud}).",
                idSolicitud);
            var snap = await reader.LeerAsync(idSolicitud, idUsuario, cancellationToken).ConfigureAwait(false);
            return Resultado(OrquestacionEvaluacionEstado.AutorizacionNoDisponible, snap);
        }
        catch (MiDecisorRequestValidationException)
        {
            logger.LogWarning(
                "m2.4d: datos de identidad insuficientes para la consulta de riesgo (idSolicitud={IdSolicitud}).", idSolicitud);
            var snap = await reader.LeerAsync(idSolicitud, idUsuario, cancellationToken).ConfigureAwait(false);
            return Resultado(OrquestacionEvaluacionEstado.DatosPersonaInsuficientes, snap);
        }
        catch (KeyNotFoundException)
        {
            return Resultado(OrquestacionEvaluacionEstado.NoEncontrada, null);
        }
        catch (InvalidOperationException)
        {
            // "La consulta de riesgo ya fue iniciada" / "no está en un estado
            // que permita consultar riesgo" — concurrencia: otra ejecución ya
            // avanzó el estado. Reanudar desde el estado durable actual.
            return null;
        }
    }

    private static OrquestacionEvaluacionResultado Resultado(
        OrquestacionEvaluacionEstado estado, CarteraSolicitudEvaluacionSnapshot? snap) =>
        new(estado,
            snap?.EstadoSolicitud,
            snap?.DecisionCrediticia,
            snap?.MontoAprobado,
            snap?.FechaDecision,
            snap?.IdCupoOrdinario);
}
