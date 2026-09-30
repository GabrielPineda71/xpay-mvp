namespace Xpay.Api.Integrations.Passport;

// XPAY-594 — clasificador AISLADO de eventos de webhook entrante.
//
// Deliberadamente NO reutiliza BrebPaymentStateMachine, por dos razones
// independientes documentadas en el contrato XPAY-593:
//
//   1. Esa máquina de estados sólo conoce PENDING/PROCESSING/SETTLED/REJECTED:
//      NO contempla CONFIRMED, que sí existe en el flujo entrante. Añadirlo
//      modificaría una clase compartida con el flujo financiero de retiros.
//   2. Sus métodos Decide/ApplyPassportPaymentStatusAsync exigen un
//      idBrebRetiro real persistido y son la única ruta del proyecto que mueve
//      Wallet/Ledger. Un pago entrante no tiene retiro asociado.
//
// La clasificación se hace por NOMBRE DE EVENTO, no por status, para no acoplar
// la certificación al vocabulario de estados del producto.
public enum PassportInboundEventKind
{
    /// <summary>Evento fuera del conjunto entrante (p. ej. payment.outbound.*).</summary>
    NotInbound,

    /// <summary>Nombre de evento ausente o desconocido.</summary>
    Unknown,

    Received,
    Confirmed,
    Settled,
    Rejected,
}

public static class PassportInboundEventClassifier
{
    public const string EventInboundReceived  = "payment.inbound.received";
    public const string EventInboundConfirmed = "payment.inbound.confirmed";
    public const string EventInboundSettled   = "payment.inbound.settled";
    public const string EventInboundRejected  = "payment.inbound.rejected";

    private const string OutboundPrefix = "payment.outbound.";

    /// <summary>
    /// Clasifica el nombre de evento. Nunca lanza: un nombre desconocido es un
    /// resultado explícito, no un error.
    /// </summary>
    public static PassportInboundEventKind Classify(string? eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName)) return PassportInboundEventKind.Unknown;

        var name = eventName.Trim();

        if (name.Equals(EventInboundReceived,  StringComparison.OrdinalIgnoreCase)) return PassportInboundEventKind.Received;
        if (name.Equals(EventInboundConfirmed, StringComparison.OrdinalIgnoreCase)) return PassportInboundEventKind.Confirmed;
        if (name.Equals(EventInboundSettled,   StringComparison.OrdinalIgnoreCase)) return PassportInboundEventKind.Settled;
        if (name.Equals(EventInboundRejected,  StringComparison.OrdinalIgnoreCase)) return PassportInboundEventKind.Rejected;

        if (name.StartsWith(OutboundPrefix, StringComparison.OrdinalIgnoreCase))
            return PassportInboundEventKind.NotInbound;

        return PassportInboundEventKind.Unknown;
    }

    /// <summary>
    /// Verdadero para los cuatro eventos del flujo entrante — los únicos que
    /// M5-T6/T7 certifica.
    /// </summary>
    public static bool IsInbound(PassportInboundEventKind kind) => kind
        is PassportInboundEventKind.Received
        or PassportInboundEventKind.Confirmed
        or PassportInboundEventKind.Settled
        or PassportInboundEventKind.Rejected;

    /// <summary>
    /// Estado terminal del flujo entrante. Informativo: ninguna consecuencia
    /// financiera se deriva de ello en T6/T7.
    /// </summary>
    public static bool IsTerminal(PassportInboundEventKind kind) => kind
        is PassportInboundEventKind.Settled
        or PassportInboundEventKind.Rejected;
}
