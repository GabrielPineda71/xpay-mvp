using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xpay.Api.Common;
using Xpay.Api.Data;

namespace Xpay.Api.Services;

// XPAY-213/214 — batch runner DORMIDO del purge B4 (política de retención
// XPAY-212 §Q.8: 5 años desde fecha_fin de cada consulta, SCOPE_OPTION_C).
//
// Selecciona candidatos por keyset pagination (sin cargar la tabla completa),
// prefiltra PENDIENTE_REVISION_MANUAL por eficiencia (el store sigue siendo
// la autoridad final del hold — ver CarteraConsultaRiesgoStore), invoca
// ICarteraResultadoRiesgoPurga.PurgarConsultaRiesgoCompletaAsync fila por
// fila, y agrega EXCLUSIVAMENTE conteos seguros (sin idSolicitud en el
// resultado agregado, sin PII, sin raw).
//
// NO es un scheduler: expone un único método invocable explícitamente. En
// XPAY-214 sólo lo invoca (potencialmente) el endpoint admin manual — que a
// su vez NO se ejecuta en este prompt. Un scheduler que reutilice esta misma
// clase es una fase posterior separada (XPAY-213 §9), NO implementada aquí.
public interface ICarteraConsultaRiesgoPurgaBatchRunner
{
    Task<CarteraPurgaB4LoteResultado> EjecutarLoteAsync(
        CarteraPurgaB4LoteOpciones? opciones = null, CancellationToken cancellationToken = default);
}

// batchSize / maxBatches acotan el trabajo de una sola invocación — nunca se
// carga la tabla completa. Defaults razonables para un primer despliegue
// manual/controlado (XPAY-213 §8): 200 filas por lote, máximo 10 lotes
// (≤2000 filas) por invocación.
public sealed record CarteraPurgaB4LoteOpciones(
    int BatchSize = 200,
    int MaxBatches = 10);

// XPAY-216 (P1-1) — estado de ejecución del lote. Distingue explícitamente
// "gate deshabilitado, no se tocó nada" de "se ejecutó y no hubo candidatos"
// — ambos casos tendrían Candidatos=0, pero sólo el segundo representa una
// ejecución real contra la base de datos.
public enum CarteraPurgaB4EstadoEjecucion
{
    Ejecutado,
    Deshabilitado,
}

// Resultado agregado y seguro — NUNCA expone idSolicitud, raw, score,
// documento, comportamiento ni ningún dato del proveedor.
public sealed record CarteraPurgaB4LoteResultado(
    CarteraPurgaB4EstadoEjecucion Estado,
    int Candidatos,
    int Purgados,
    // XPAY-216 (P1-2) — NO es un censo del total de solicitudes actualmente
    // en PENDIENTE_REVISION_MANUAL: el query de candidatos (ObtenerCandidatosAsync)
    // ya las prefiltra con `s.EstadoSolicitud != PendienteRevisionManual`, así
    // que una solicitud en hold normalmente ni siquiera llega a ser candidata.
    // Este contador SÓLO puede incrementarse por una ventana de carrera: la
    // solicitud pasó a PENDIENTE_REVISION_MANUAL DESPUÉS de la selección
    // (lectura sin lock) pero ANTES de la revalidación autoritativa dentro del
    // AppLock del store (PurgarConsultaRiesgoCompletaAsync). Es una métrica de
    // carrera/revalidación, no un inventario de holds — no usar como proxy del
    // total de solicitudes en revisión manual.
    int RetenidosPorRevisionManual,
    int YaPurgados,
    int NoElegibles,
    int Errores,
    TimeSpan Duracion)
{
    // Construido cuando el gate de activación (ver GateHabilitado) está OFF —
    // ninguna consulta de candidatos, ningún AppLock, ningún acceso al store
    // ni a la base de datos ocurrió antes de este punto.
    public static CarteraPurgaB4LoteResultado Deshabilitado() =>
        new(CarteraPurgaB4EstadoEjecucion.Deshabilitado, 0, 0, 0, 0, 0, 0, TimeSpan.Zero);
}

public sealed class CarteraConsultaRiesgoPurgaBatchRunner(
    XpayDbContext db,
    ICarteraResultadoRiesgoPurga purga,
    TimeProvider timeProvider,
    IConfiguration configuration,
    ILogger<CarteraConsultaRiesgoPurgaBatchRunner> logger)
    : ICarteraConsultaRiesgoPurgaBatchRunner
{
    // XPAY-216 (P1-1) — gate técnico fail-closed del purge B4. XPAY-215
    // encontró que, tras el DI de XPAY-214, el purge quedaba técnicamente
    // ejecutable post-deploy pese a PURGE_ACTIVATION_AUTHORIZED=NO (política
    // XPAY-212 §Q.8) — la única barrera era procedimental, no técnica. Este
    // gate cierra esa brecha: ausente o cualquier valor distinto de "true"
    // (case-insensitive) ⇒ deshabilitado. NO hay override desde el request
    // del endpoint admin (éste no acepta parámetros — ver
    // CarteraOrdinariaController.EjecutarLotePurgaB4). NO se registra ningún
    // scheduler/IHostedService aquí ni en CarteraRiesgoRuntimeWiring — esta
    // es la fase manual/controlada; un scheduler es una fase posterior
    // separada, no autorizada en XPAY-216.
    public const string ConfigKeyHabilitado = "CARTERA_PURGE_B4_ENABLED";

    public async Task<CarteraPurgaB4LoteResultado> EjecutarLoteAsync(
        CarteraPurgaB4LoteOpciones? opciones = null, CancellationToken cancellationToken = default)
    {
        // Gate ANTES de cualquier otra cosa: antes de validar opciones, antes
        // de calcular el cutoff, antes de tocar la base de datos. Deshabilitado
        // ⇒ retorno inmediato, cero consultas de candidatos, cero AppLock,
        // cero llamadas al store.
        if (!GateHabilitado())
        {
            logger.LogInformation(
                "purge.b4: lote NO ejecutado — gate {ConfigKey} deshabilitado (ausente, \"false\" o valor no reconocido).",
                ConfigKeyHabilitado);
            return CarteraPurgaB4LoteResultado.Deshabilitado();
        }

        opciones ??= new CarteraPurgaB4LoteOpciones();
        if (opciones.BatchSize <= 0) throw new ArgumentException("BatchSize debe ser > 0.", nameof(opciones));
        if (opciones.MaxBatches <= 0) throw new ArgumentException("MaxBatches debe ser > 0.", nameof(opciones));

        // XPAY-213 §7/§11: cutoff calculado en UTC vía TimeProvider (testeable),
        // AddYears(-5) — NUNCA 5*365 días, NUNCA DateTime.Now / hora local.
        var cutoffUtc = DateTime.SpecifyKind(timeProvider.GetUtcNow().UtcDateTime.AddYears(-5), DateTimeKind.Utc);

        var totalCandidatos = 0;
        var purgados        = 0;
        var retenidos       = 0;
        var yaPurgados      = 0;
        var noElegibles     = 0;
        var errores         = 0;
        var lastIdSolicitud = 0L;

        var startedAt = timeProvider.GetTimestamp();

        for (var lote = 0; lote < opciones.MaxBatches; lote++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidatos = await ObtenerCandidatosAsync(lastIdSolicitud, opciones.BatchSize, cutoffUtc, cancellationToken)
                .ConfigureAwait(false);

            if (candidatos.Count == 0)
                break;

            totalCandidatos += candidatos.Count;

            foreach (var idSolicitud in candidatos)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var resultado = await purga
                        .PurgarConsultaRiesgoCompletaAsync(idSolicitud, cutoffUtc, cancellationToken)
                        .ConfigureAwait(false);

                    switch (resultado)
                    {
                        case ResultadoPurgaConsultaCompleta.Purgado:
                            purgados++;
                            break;
                        case ResultadoPurgaConsultaCompleta.YaPurgado:
                            yaPurgados++;
                            break;
                        case ResultadoPurgaConsultaCompleta.RetenidoPorRevisionManual:
                            retenidos++;
                            break;
                        case ResultadoPurgaConsultaCompleta.NoElegible:
                            noElegibles++;
                            break;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // idSolicitud es un identificador interno (PK numérica), no
                    // PII ni dato del proveedor — necesario para localizar la
                    // fila ante una CarteraPurgaB4InvarianteException u otro
                    // error técnico. NUNCA se loggea raw/score/documento/
                    // comportamiento/secretos.
                    errores++;
                    logger.LogWarning(ex,
                        "purge.b4: error al purgar una consulta de riesgo (idSolicitud={IdSolicitud}).", idSolicitud);
                }

                lastIdSolicitud = idSolicitud;
            }

            if (candidatos.Count < opciones.BatchSize)
                break; // último lote parcial: no hay más candidatos
        }

        var duracion = timeProvider.GetElapsedTime(startedAt);

        logger.LogInformation(
            "purge.b4: lote ejecutado (candidatos={Candidatos} purgados={Purgados} retenidos={Retenidos} yaPurgados={YaPurgados} noElegibles={NoElegibles} errores={Errores} duracionMs={DuracionMs}).",
            totalCandidatos, purgados, retenidos, yaPurgados, noElegibles, errores, duracion.TotalMilliseconds);

        return new CarteraPurgaB4LoteResultado(
            CarteraPurgaB4EstadoEjecucion.Ejecutado,
            totalCandidatos, purgados, retenidos, yaPurgados, noElegibles, errores, duracion);
    }

    // XPAY-216 (P1-1) — fail-closed: sólo "true" (case-insensitive) habilita.
    // Config ausente, "false", o cualquier otro valor no reconocido (typo,
    // "1", "yes", cadena vacía, etc.) ⇒ deshabilitado. NUNCA se loggea el
    // valor leído (podría ser un string arbitrario mal configurado). No se
    // añade la variable a ningún appsettings/ambiente en XPAY-216 — por
    // tanto, sin configuración explícita post-deploy, el gate permanece OFF.
    private bool GateHabilitado() =>
        string.Equals(configuration[ConfigKeyHabilitado], "true", StringComparison.OrdinalIgnoreCase);

    // Keyset pagination por IdSolicitud — nunca OFFSET/FETCH, nunca carga la
    // tabla completa. Prefiltra PENDIENTE_REVISION_MANUAL por eficiencia; el
    // store (PurgarConsultaRiesgoCompletaAsync) re-verifica el mismo guard de
    // forma autoritativa dentro de su propia transacción.
    private async Task<List<long>> ObtenerCandidatosAsync(
        long lastIdSolicitud, int batchSize, DateTime cutoffUtc, CancellationToken cancellationToken)
    {
        var query =
            from s in db.CarteraSolicitudesCupo.AsNoTracking()
            join i in db.CarteraSolicitudCupoIntentos.AsNoTracking()
                on new { s.IdSolicitud, s.NumeroIntento } equals new { i.IdSolicitud, i.NumeroIntento }
            where s.IdSolicitud > lastIdSolicitud
               && i.FaseIntento == CarteraIntentoFases.Finalizado
               && i.ResultadoPurgadoUtc == null
               && i.ResultadoConsumidoUtc != null
               && i.FechaFin != null
               && i.FechaFin < cutoffUtc
               && s.EstadoSolicitud != CarteraSolicitudCupoEstados.PendienteRevisionManual
            orderby s.IdSolicitud
            select s.IdSolicitud;

        return await query.Take(batchSize).ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}
