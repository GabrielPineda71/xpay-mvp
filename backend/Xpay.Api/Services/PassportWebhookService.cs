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
    private readonly ILogger<PassportWebhookService> _logger;

    public PassportWebhookService(
        XpayDbContext db,
        TimeProvider timeProvider,
        ILogger<PassportWebhookService> logger)
    {
        _db           = db;
        _timeProvider = timeProvider;
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

            var recorded = await RecordAsync(
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

        // Idempotencia lógica: (event_type, passport_payment_id). payment_id por
        // sí solo NO sirve — received/confirmed/settled lo comparten y son
        // entregas legítimamente distintas.
        var existing = await _db.PassportWebhookEvents
            .Where(e => e.EventType == payload.EventName
                     && e.PassportPaymentId == payload.PaymentId)
            .OrderByDescending(e => e.IdEvent)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null && existing.Processed)
        {
            _logger.LogInformation(
                "Passport webhook duplicado ya procesado: kind={Kind} idEvent={IdEvent} sameHash={SameHash}",
                payload.Kind, existing.IdEvent, existing.PayloadHash == hash);

            return new PassportWebhookProcessOutcome(
                PassportWebhookProcessResult.Duplicate, payload.Kind, existing.IdEvent);
        }

        if (existing is not null)
        {
            // Entrega previa registrada pero no procesada (p. ej. un fallo
            // transitorio que provocó reintento): se completa sobre la misma
            // fila en lugar de duplicarla.
            existing.SignatureValid       = signatureValid;
            existing.PayloadHash          = hash;
            existing.PayloadSanitizedJson = PassportWebhookPayloadParser.BuildSanitizedJson(payload);
            existing.Processed            = true;
            existing.FechaProcesado       = nowUtc;
            existing.ErrorMessage         = null;
            await _db.SaveChangesAsync(cancellationToken);

            return new PassportWebhookProcessOutcome(
                PassportWebhookProcessResult.Processed, payload.Kind, existing.IdEvent);
        }

        var inserted = await RecordAsync(
            payload, hash, signatureValid, processed: true, nowUtc,
            errorMessage: null, cancellationToken);

        _logger.LogInformation(
            "Passport webhook entrante procesado: kind={Kind} idEvent={IdEvent}",
            payload.Kind, inserted.IdEvent);

        return new PassportWebhookProcessOutcome(
            PassportWebhookProcessResult.Processed, payload.Kind, inserted.IdEvent);
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
