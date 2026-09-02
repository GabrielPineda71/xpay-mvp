using Microsoft.Extensions.Logging;
using Xpay.Api.Common;
using Xpay.Api.Integrations.MiDecisor;

namespace Xpay.Api.Services;

// M2.3a/b1 — orquestación estructural Cartera ↔ MiDecisor (consulta de riesgo PN).
//
// Flujo por invocación:
//   PRE-FLIGHT (sin transacción, sin token, sin HTTP):
//     cargar contexto → ownership → estado == RECIBIDA → consentimiento
//     fail-closed → mapear Persona → MiDecisorConsultaRequest.
//     Cualquier fallo aquí deja la solicitud en RECIBIDA; 0 llamadas al proveedor.
//   TX-A: ganar la transición RECIBIDA → CONSULTANDO_RIESGO (applock + re-lectura).
//     El perdedor NO llama al proveedor.
//   TX-ENVIO_INCIERTO: marcar el intento numero_intento=1 como ENVIO_INCIERTO
//     ANTES de SendAsync. Si esta marca falla, NO se llama al proveedor y la
//     solicitud queda CONSULTANDO_RIESGO / intento PRE_CALL para reconciliación.
//   HTTP: EXACTAMENTE UNA llamada a IMiDecisorClient. Sin transacción SQL abierta.
//   TX-B: transición ÚNICA guardada — completar el intento numero_intento=1
//     (resultado_tecnico, http/content status, fecha_fin, es_intento_util, los 6
//     campos normalizados CRUDOS, fase = FINALIZADO) + estado final de la
//     solicitud (EN_EVALUACION | ERROR_PROVEEDOR). Sin retry, sin re-auth, sin
//     invalidar token, sin decisión de crédito, sin interpretar score/monto.
//
// M2.3a/b1 NO se registra en ningún endpoint / scheduler / flujo. Junto con el
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
    // Longitud máxima de cada columna cruda (migración 036). Un valor del
    // proveedor más largo => se rechaza TODA la invocación como ERROR_PROTOCOLO
    // sin persistir ningún crudo (fail-closed: nada de datos parciales).
    private const int MaxScoreRaw          = 20;
    private const int MaxViabilidadRaw     = 10;
    private const int MaxRatingRecaudosRaw = 2;
    private const int MaxMontoSugeridoRaw  = 20;

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

        // ── TX-ENVIO_INCIERTO: marcar la frontera de no-retry-automático ──
        // Si falla, se propaga SIN llamar al proveedor: la solicitud queda
        // CONSULTANDO_RIESGO y el intento en PRE_CALL (reconciliación externa).
        await store.MarcarEnvioInciertoAsync(
            idSolicitud, idUsuario, timeProvider.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);

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
            // Cancelación del caller DESPUÉS de ENVIO_INCIERTO: el proveedor pudo
            // haber sido contactado / procesado. Se cierra el intento como
            // RESULTADO_INCIERTO con un token NO cancelable y se re-lanza la
            // cancelación original. Ningún crudo se persiste.
            await store.FinalizarIntentoAsync(
                idSolicitud,
                idUsuario,
                CrearOutcome(
                    CarteraSolicitudCupoEstados.ErrorProveedor,
                    CarteraConsultaRiesgoResultados.ResultadoIncierto,
                    httpStatus: null,
                    contentStatus: null,
                    util: false),
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
        // se ejecuta y la solicitud queda CONSULTANDO_RIESGO / intento
        // ENVIO_INCIERTO para reconciliación.

        var elapsedMs = timeProvider.GetElapsedTime(startedAt).TotalMilliseconds;

        var outcome = resultado is not null
            ? ClasificarExito(resultado)
            : ClasificarFalla(falla!);

        // ── TX-B: persistir el resultado durable (transición única) ──────
        await store.FinalizarIntentoAsync(idSolicitud, idUsuario, outcome, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "midecisor.orq: consulta completada (idSolicitud={IdSolicitud} estado={Estado} resultado={Resultado} util={Util} {Elapsed:F0}ms).",
            idSolicitud, outcome.EstadoSolicitudFinal, outcome.ResultadoTecnico, outcome.EsResultadoUtil, elapsedMs);

        return new ConsultaRiesgoResultado(outcome.EstadoSolicitudFinal, outcome.ResultadoTecnico, outcome.EsResultadoUtil);
    }

    // Clasificación de un resultado 200/ACCEPTED usando SÓLO la semántica
    // estructurada (ConInformacion). NUNCA convierte ni interpreta los crudos:
    // sólo verifica que quepan en su columna. Si alguno se desborda, TODA la
    // invocación es ERROR_PROTOCOLO y NINGÚN crudo se persiste.
    private ResultadoIntentoDurable ClasificarExito(MiDecisorResultado r)
    {
        if (Desborda(r.ScoreRaw, MaxScoreRaw)
            || Desborda(r.Viabilidad, MaxViabilidadRaw)
            || Desborda(r.RatingRecaudos, MaxRatingRecaudosRaw)
            || Desborda(r.MontoSugeridoRaw, MaxMontoSugeridoRaw))
        {
            logger.LogWarning(
                "midecisor.orq: un campo normalizado del proveedor excede la longitud de su columna — se clasifica ERROR_PROTOCOLO sin persistir crudos.");
            return CrearOutcome(
                CarteraSolicitudCupoEstados.ErrorProveedor,
                CarteraConsultaRiesgoResultados.ErrorProtocolo,
                httpStatus: null,
                contentStatus: null,
                util: false);
        }

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

        return new ResultadoIntentoDurable(
            CarteraSolicitudCupoEstados.EnEvaluacion,
            resultadoTecnico,
            HttpStatusObservado: 200,
            ContentStatusObservado: contentStatus,
            EsResultadoUtil: true,
            FechaFinUtc: timeProvider.GetUtcNow().UtcDateTime,
            // Crudos VERBATIM — sin trim, sin normalizar, sin convertir.
            ConInformacion: r.ConInformacion,
            ScoreRaw: r.ScoreRaw,
            ViabilidadRaw: r.Viabilidad,
            RatingRecaudosRaw: r.RatingRecaudos,
            MontoSugeridoRaw: r.MontoSugeridoRaw,
            AlertasCount: r.AlertasCount);
    }

    // Clasificación por tipo de excepción de dominio. Un error local /
    // configuración NO afirma que el proveedor haya sido consultado por red;
    // sólo describe por qué esta ejecución no obtuvo un resultado útil. Ningún
    // crudo se persiste (no se recibió MiDecisorResultado).
    private ResultadoIntentoDurable ClasificarFalla(MiDecisorException ex)
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

        return CrearOutcome(
            CarteraSolicitudCupoEstados.ErrorProveedor, resultadoTecnico,
            httpStatus: null, contentStatus: null, util: false);
    }

    // Outcome SIN crudos (todos NULL) — para fallas, cancelación y desbordamiento.
    private ResultadoIntentoDurable CrearOutcome(
        string estadoFinal, string resultadoTecnico, int? httpStatus, string? contentStatus, bool util) =>
        new(estadoFinal, resultadoTecnico, httpStatus, contentStatus, util,
            timeProvider.GetUtcNow().UtcDateTime,
            ConInformacion: null, ScoreRaw: null, ViabilidadRaw: null,
            RatingRecaudosRaw: null, MontoSugeridoRaw: null, AlertasCount: null);

    private static bool Desborda(string? valor, int maxLen) => valor is not null && valor.Length > maxLen;
}
