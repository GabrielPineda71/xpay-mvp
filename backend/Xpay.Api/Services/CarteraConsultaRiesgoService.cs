using Microsoft.Extensions.Logging;
using Xpay.Api.Common;
using Xpay.Api.Integrations.MiDecisor;

namespace Xpay.Api.Services;

// M2.3a — orquestación estructural Cartera ↔ MiDecisor (consulta de riesgo PN).
//
// Flujo por invocación:
//   PRE-FLIGHT (sin transacción, sin token, sin HTTP):
//     cargar contexto → ownership → estado == RECIBIDA → consentimiento
//     fail-closed → mapear Persona → MiDecisorConsultaRequest.
//     Cualquier fallo aquí deja la solicitud en RECIBIDA; 0 llamadas al proveedor.
//   TX-A: ganar la transición RECIBIDA → CONSULTANDO_RIESGO (applock + re-lectura).
//     El perdedor NO llama al proveedor.
//   HTTP: EXACTAMENTE UNA llamada a IMiDecisorClient. Sin transacción SQL abierta.
//   TX-B: completar el intento numero_intento=1 + transición final
//     (EN_EVALUACION | ERROR_PROVEEDOR). Sin retry, sin re-auth, sin invalidar
//     token, sin decisión de crédito, sin persistir score/monto.
//
// M2.3a NO se registra en ningún endpoint / scheduler / flujo. Junto con el
// consentimiento runtime fail-closed, son dos barreras independientes contra
// una llamada real al proveedor.
//
// Lifetime en DI: Scoped (necesita el XpayDbContext scoped vía el store).
public sealed class CarteraConsultaRiesgoService(
    ICarteraConsultaRiesgoStore store,
    IMiDecisorClient miDecisor,
    IConsultaRiesgoAutorizacion autorizacion,
    TimeProvider timeProvider,
    ILogger<CarteraConsultaRiesgoService> logger)
{
    public async Task<ConsultaRiesgoResultado> EjecutarConsultaRiesgoAsync(
        long idSolicitud,
        long idUsuario,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (idSolicitud <= 0) throw new ArgumentException("idSolicitud inválido", nameof(idSolicitud));
        if (idUsuario <= 0) throw new ArgumentException("idUsuario inválido", nameof(idUsuario));
        if (string.IsNullOrWhiteSpace(correlationId)) throw new ArgumentException("correlationId requerido", nameof(correlationId));

        // Cancelación observada ANTES de cualquier lectura / transición durable.
        cancellationToken.ThrowIfCancellationRequested();

        // ── PRE-FLIGHT ────────────────────────────────────────────────────
        var contexto = await store.CargarContextoAsync(idSolicitud, idUsuario, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Solicitud no encontrada.");

        if (!string.Equals(contexto.EstadoSolicitud, CarteraSolicitudCupoEstados.Recibida, StringComparison.Ordinal))
            throw new InvalidOperationException("La solicitud no está en un estado que permita consultar riesgo.");

        if (!await autorizacion.TieneAutorizacionVigenteAsync(idUsuario, idSolicitud, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Consulta de riesgo no autorizada para esta solicitud.");

        if (contexto.Persona is null)
            throw new KeyNotFoundException("Solicitud no encontrada."); // persona ausente → se trata como no encontrada

        if (!PersonaMiDecisorRequestMapper.TryMapear(contexto.Persona, out var request, out _))
            throw new MiDecisorRequestValidationException(
                "Los datos de identidad de la persona no permiten una consulta de riesgo.");

        // ── TX-A: ganar la transición RECIBIDA → CONSULTANDO_RIESGO ───────
        var inicioUtc = timeProvider.GetUtcNow().UtcDateTime;
        var gano = await store.IntentarIniciarConsultaAsync(idSolicitud, idUsuario, inicioUtc, cancellationToken).ConfigureAwait(false);
        if (!gano)
        {
            logger.LogInformation(
                "midecisor.orq: la consulta ya fue iniciada por otra ejecución (idSolicitud={IdSolicitud}).", idSolicitud);
            throw new InvalidOperationException("La consulta de riesgo ya fue iniciada para esta solicitud.");
        }

        // ── HTTP: exactamente una llamada, sin transacción SQL abierta ────
        var startedAt = timeProvider.GetTimestamp();
        MiDecisorResultado? resultado = null;
        MiDecisorException? falla = null;

        try
        {
            resultado = await miDecisor.ConsultarPersonaNaturalAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancelación del caller DURANTE la llamada: el proveedor pudo haber
            // sido contactado / procesado. Se persiste RESULTADO_INCIERTO con un
            // token no cancelable y se re-lanza la cancelación original.
            await store.CompletarIntentoAsync(
                idSolicitud,
                new ResultadoIntentoDurable(
                    CarteraSolicitudCupoEstados.ErrorProveedor,
                    CarteraConsultaRiesgoResultados.ResultadoIncierto,
                    HttpStatusObservado: null,
                    ContentStatusObservado: null,
                    EsResultadoUtil: false,
                    FechaFinUtc: timeProvider.GetUtcNow().UtcDateTime),
                CancellationToken.None).ConfigureAwait(false);

            logger.LogWarning(
                "midecisor.orq: cancelación del caller durante la consulta (idSolicitud={IdSolicitud}) → RESULTADO_INCIERTO.",
                idSolicitud);
            throw;
        }
        catch (MiDecisorException ex)
        {
            falla = ex;
        }
        // Cualquier otra excepción (DbUpdateException, etc.) se propaga: TX-B no
        // se ejecuta y la solicitud queda CONSULTANDO_RIESGO para reconciliación.

        var elapsedMs = timeProvider.GetElapsedTime(startedAt).TotalMilliseconds;

        var (estadoFinal, resultadoTecnico, util, httpStatus, contentStatus) =
            resultado is not null
                ? ClasificarExito(resultado)
                : ClasificarFalla(falla!);

        // ── TX-B: persistir el resultado durable ─────────────────────────
        await store.CompletarIntentoAsync(
            idSolicitud,
            new ResultadoIntentoDurable(
                estadoFinal, resultadoTecnico, httpStatus, contentStatus, util,
                timeProvider.GetUtcNow().UtcDateTime),
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "midecisor.orq: consulta completada (idSolicitud={IdSolicitud} estado={Estado} resultado={Resultado} util={Util} {Elapsed:F0}ms).",
            idSolicitud, estadoFinal, resultadoTecnico, util, elapsedMs);

        return new ConsultaRiesgoResultado(estadoFinal, resultadoTecnico, util);
    }

    // Clasificación de un resultado 200/ACCEPTED usando SÓLO la semántica
    // estructurada (ConInformacion). NUNCA inspecciona ScoreRaw / MontoSugeridoRaw.
    private (string estado, string resultado, bool util, int? httpStatus, string? contentStatus) ClasificarExito(
        MiDecisorResultado r)
    {
        var resultadoTecnico = r.ConInformacion == true
            ? CarteraConsultaRiesgoResultados.Aceptada
            : CarteraConsultaRiesgoResultados.SinInformacion;

        // content_status_observado es VARCHAR(30): si el proveedor manda algo
        // más largo, se descarta (null) en vez de escribir un valor truncado.
        string? contentStatus = r.ContentStatus;
        if (contentStatus is { Length: > 30 })
        {
            logger.LogWarning("midecisor.orq: content status del proveedor excede 30 caracteres — se omite.");
            contentStatus = null;
        }

        return (CarteraSolicitudCupoEstados.EnEvaluacion, resultadoTecnico, true, 200, contentStatus);
    }

    // Clasificación por tipo de excepción de dominio. Un error local /
    // configuración NO afirma que el proveedor haya sido consultado por red;
    // sólo describe por qué esta ejecución no obtuvo un resultado útil.
    private static (string estado, string resultado, bool util, int? httpStatus, string? contentStatus) ClasificarFalla(
        MiDecisorException ex)
    {
        var resultadoTecnico = ex switch
        {
            MiDecisorAuthenticationException    => CarteraConsultaRiesgoResultados.ErrorAutenticacion,
            MiDecisorQueryRejectedException     => CarteraConsultaRiesgoResultados.RechazadaProveedor,
            MiDecisorConfigurationException     => CarteraConsultaRiesgoResultados.ErrorConfiguracion,
            MiDecisorRequestValidationException => CarteraConsultaRiesgoResultados.ErrorValidacionLocal,
            MiDecisorProtocolException          => CarteraConsultaRiesgoResultados.ErrorProtocolo,
            MiDecisorTransportException         => CarteraConsultaRiesgoResultados.ResultadoIncierto,
            _                                   => CarteraConsultaRiesgoResultados.ResultadoIncierto,
        };

        return (CarteraSolicitudCupoEstados.ErrorProveedor, resultadoTecnico, false, null, null);
    }
}
