using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xpay.Api.Data;
using Xpay.Api.Integrations.Passport;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// XPAY-594 — M5-T6/T7: recepción y validación de webhooks de pago ENTRANTE.
//
// AISLAMIENTO FINANCIERO ABSOLUTO. Este servicio existe para la evidencia de
// certificación, y por diseño:
//   · NO llama BrebPaymentService.ApplyPassportPaymentStatusAsync;
//   · NO toca Wallet, Ledger ni breb_retiros;
//   · NO usa BrebPaymentStateMachine;
//   · NO inicia pagos ni llama a ninguna API de Passport.
// La única tabla que escribe es passport_webhook_events.
//
// El comportamiento financiero del producto (acreditación real de un pago
// entrante) es alcance de Fase 65 y permanece fuera de aquí.
//
// P0 anticipo - este servicio sigue sin tocar Wallet/Ledger/breb_retiros
// directamente. Tras registrar (y deduplicar de forma durable con
// uq_pwh_evento_pago) un evento ENTRANTE, delega en
// LibranzaRecaudoAplicacionService, que es la ÚNICA pieza que puede aplicar
// un pago entrante (solo settled, con firma válida y validaciones completas)
// a un lote de recaudo de anticipos. El evento queda processed=1 solo cuando
// ese paso termina; un fallo transitorio lo deja processed=0 y la excepción
// se propaga (500) para que Passport reintente sobre la MISMA fila.
public enum PassportWebhookProcessResult
{
    /// <summary>Entrega entrante válida, registrada y marcada como procesada.</summary>
    Processed,

    /// <summary>Misma (event_type, payment_id) ya procesada: no se reprocesa.</summary>
    Duplicate,

    /// <summary>Evento del ciclo saliente: se registra, no se procesa aquí.</summary>
    NotInbound,

    /// <summary>Nombre de evento ausente o desconocido: se registra, no se procesa.</summary>
    UnknownEvent,

    /// <summary>Evento entrante sin payment_id: no identificable.</summary>
    MissingPaymentId,
}

public sealed record PassportWebhookProcessOutcome(
    PassportWebhookProcessResult Result,
    PassportInboundEventKind     Kind,
    long?                        IdEvent);

public class PassportWebhookService
{
    private readonly XpayDbContext _db;
    private readonly TimeProvider  _timeProvider;
    private readonly LibranzaRecaudoAplicacionService _recaudo;
    private readonly ILogger<PassportWebhookService> _logger;

    public PassportWebhookService(
        XpayDbContext db,
        TimeProvider timeProvider,
        LibranzaRecaudoAplicacionService recaudo,
        ILogger<PassportWebhookService> logger)
    {
        _db           = db;
        _timeProvider = timeProvider;
        _recaudo      = recaudo;
        _logger       = logger;
    }

    /// <summary>
    /// XPAY-602 — procesa una entrega que puede traer UNO o VARIOS eventos.
    ///
    /// Passport entrega lotes con raíz JSON Array (verificado con tráfico real
    /// en XPAY-601). Cada elemento se procesa con <see cref="ProcessAsync"/> —
    /// el MISMO pipeline ya probado para un evento suelto—, de modo que la
    /// clasificación, la sanitización y la idempotencia por
    /// (event_type, passport_payment_id) se aplican POR EVENTO.
    ///
    /// La validación estructural del lote completo ya ocurrió en el parser
    /// (fail-closed), así que aquí no puede llegar un lote a medias.
    ///
    /// Si un elemento lanza, la excepción se propaga: el controller responde
    /// 500 y Passport reintenta. Los elementos ya registrados quedan
    /// protegidos por su propia idempotencia, así que el reintento no los
    /// duplica.
    /// </summary>
    public async Task<IReadOnlyList<PassportWebhookProcessOutcome>> ProcessDeliveryAsync(
        string rawBody,
        IReadOnlyList<PassportWebhookPayload> payloads,
        bool signatureValid,
        CancellationToken cancellationToken = default)
    {
        var outcomes = new List<PassportWebhookProcessOutcome>(payloads.Count);
        foreach (var payload in payloads)
        {
            outcomes.Add(await ProcessAsync(rawBody, payload, signatureValid, cancellationToken)
                .ConfigureAwait(false));
        }
        return outcomes;
    }

    /// <summary>
    /// Procesa una entrega ya autenticada (o explícitamente observada en
    /// Sandbox). El cuerpo crudo se usa sólo para calcular el hash y la
    /// proyección sanitizada: NUNCA se persiste ni se registra.
    /// </summary>
    public async Task<PassportWebhookProcessOutcome> ProcessAsync(
        string rawBody,
        PassportWebhookPayload payload,
        bool signatureValid,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var hash   = PassportWebhookPayloadParser.ComputePayloadHash(rawBody);

        // Eventos no entrantes o desconocidos: se registran para auditoría y se
        // dan por recibidos. Reintentar no cambiaría el resultado.
        if (!PassportInboundEventClassifier.IsInbound(payload.Kind))
        {
            var kindResult = payload.Kind == PassportInboundEventKind.NotInbound
                ? PassportWebhookProcessResult.NotInbound
                : PassportWebhookProcessResult.UnknownEvent;

            // P0 - con uq_pwh_evento_pago, el reintento de un mismo evento no
            // entrante NO inserta otra fila (y nunca provoca un 500).
            var previo = await BuscarExistenteAsync(payload, cancellationToken);
            var recorded = previo ?? await RecordOrGetExistingAsync(
                payload, hash, signatureValid, processed: false, nowUtc,
                errorMessage: $"Evento no procesable por M5-T6/T7: {kindResult}",
                cancellationToken);

            _logger.LogInformation(
                "Passport webhook no entrante: kind={Kind} result={Result} idEvent={IdEvent}",
                payload.Kind, kindResult, recorded.IdEvent);

            return new PassportWebhookProcessOutcome(kindResult, payload.Kind, recorded.IdEvent);
        }

        if (!payload.HasPaymentId)
        {
            var recorded = await RecordAsync(
                payload, hash, signatureValid, processed: false, nowUtc,
                errorMessage: "Evento entrante sin payment_id.",
                cancellationToken);

            return new PassportWebhookProcessOutcome(
                PassportWebhookProcessResult.MissingPaymentId, payload.Kind, recorded.IdEvent);
        }

        // Idempotencia: (event_type, passport_payment_id). payment_id por sí solo
        // NO sirve — received/confirmed/settled lo comparten y son entregas
        // legítimamente distintas. Desde P0 la garantía es DURABLE
        // (uq_pwh_evento_pago), no solo este SELECT previo.
        var existing = await BuscarExistenteAsync(payload, cancellationToken);

        if (existing is not null && existing.Processed)
        {
            _logger.LogInformation(
                "Passport webhook duplicado ya procesado: kind={Kind} idEvent={IdEvent} sameHash={SameHash}",
                payload.Kind, existing.IdEvent, existing.PayloadHash == hash);

            return new PassportWebhookProcessOutcome(
                PassportWebhookProcessResult.Duplicate, payload.Kind, existing.IdEvent);
        }

        PassportWebhookEvent fila;
        if (existing is not null)
        {
            // Entrega previa registrada pero no procesada (p. ej. un fallo
            // transitorio que provocó reintento): se completa sobre la misma
            // fila en lugar de duplicarla.
            existing.SignatureValid       = signatureValid;
            existing.PayloadHash          = hash;
            existing.PayloadSanitizedJson = PassportWebhookPayloadParser.BuildSanitizedJson(payload);
            await _db.SaveChangesAsync(cancellationToken);
            fila = existing;
        }
        else
        {
            var insertada = await TryInsertAsync(payload, hash, signatureValid, nowUtc, cancellationToken);
            if (insertada is null)
            {
                // Otra entrega concurrente del MISMO evento ganó el INSERT: esta
                // no hace nada más (la otra completa el proceso o, si falla,
                // responde 500 y Passport la reintenta).
                var ganadora = await BuscarExistenteAsync(payload, cancellationToken);
                return new PassportWebhookProcessOutcome(
                    PassportWebhookProcessResult.Duplicate, payload.Kind, ganadora?.IdEvent);
            }
            fila = insertada;
        }

        ResultadoRecaudo recaudo;
        try
        {
            recaudo = await _recaudo.ProcesarEventoEntranteAsync(
                payload.Kind, payload.PaymentId!, signatureValid, fila.IdEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _db.ChangeTracker.Clear();
            var pendiente = await _db.PassportWebhookEvents.FirstAsync(e => e.IdEvent == fila.IdEvent, cancellationToken);
            pendiente.Processed    = false;
            pendiente.ErrorMessage = $"RECAUDO_ERROR:{ex.GetType().Name}";
            await _db.SaveChangesAsync(cancellationToken);
            throw;
        }

        _db.ChangeTracker.Clear();
        var procesada = await _db.PassportWebhookEvents.FirstAsync(e => e.IdEvent == fila.IdEvent, cancellationToken);
        procesada.Processed      = true;
        procesada.FechaProcesado = nowUtc;
        procesada.ErrorMessage   = $"RECAUDO:{recaudo.Resultado}";
        procesada.IdLoteRecaudo  = recaudo.IdLote;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Passport webhook entrante procesado: kind={Kind} idEvent={IdEvent} recaudo={Recaudo} lote={Lote}",
            payload.Kind, fila.IdEvent, recaudo.Resultado, recaudo.IdLote);

        return new PassportWebhookProcessOutcome(
            PassportWebhookProcessResult.Processed, payload.Kind, fila.IdEvent);
    }

    private async Task<PassportWebhookEvent?> BuscarExistenteAsync(
        PassportWebhookPayload payload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload.EventName) || !payload.HasPaymentId) return null;
        return await _db.PassportWebhookEvents
            .Where(e => e.EventType == payload.EventName
                     && e.PassportPaymentId == payload.PaymentId)
            .OrderByDescending(e => e.IdEvent)
            .FirstOrDefaultAsync(cancellationToken);
    }

    // INSERT del evento entrante con processed=0. null si la restricción
    // uq_pwh_evento_pago indica que otra entrega concurrente ya lo insertó.
    private async Task<PassportWebhookEvent?> TryInsertAsync(
        PassportWebhookPayload payload, string hash, bool signatureValid, DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            return await RecordAsync(payload, hash, signatureValid, processed: false, nowUtc,
                errorMessage: null, cancellationToken);
        }
        catch (Exception ex) when (Xpay.Api.Common.SqlExceptionHelper.IsUniqueViolation(ex))
        {
            _db.ChangeTracker.Clear();
            return null;
        }
    }

    private async Task<PassportWebhookEvent> RecordOrGetExistingAsync(
        PassportWebhookPayload payload, string hash, bool signatureValid, bool processed, DateTime nowUtc,
        string? errorMessage, CancellationToken cancellationToken)
    {
        try
        {
            return await RecordAsync(payload, hash, signatureValid, processed, nowUtc, errorMessage, cancellationToken);
        }
        catch (Exception ex) when (Xpay.Api.Common.SqlExceptionHelper.IsUniqueViolation(ex))
        {
            _db.ChangeTracker.Clear();
            return (await BuscarExistenteAsync(payload, cancellationToken))!;
        }
    }

    private async Task<PassportWebhookEvent> RecordAsync(
        PassportWebhookPayload payload,
        string hash,
        bool signatureValid,
        bool processed,
        DateTime nowUtc,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var entity = new PassportWebhookEvent
        {
            Provider             = "PASSPORT",
            EventType            = payload.EventName,
            PassportPaymentId    = payload.PaymentId,
            // Un pago ENTRANTE no tiene retiro asociado: la columna queda NULL.
            IdBrebRetiro         = null,
            PayloadHash          = hash,
            PayloadSanitizedJson = PassportWebhookPayloadParser.BuildSanitizedJson(payload),
            SignatureValid       = signatureValid,
            Processed            = processed,
            FechaRecibido        = nowUtc,
            FechaProcesado       = processed ? nowUtc : null,
            ErrorMessage         = errorMessage,
        };

        _db.PassportWebhookEvents.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return entity;
    }
}
