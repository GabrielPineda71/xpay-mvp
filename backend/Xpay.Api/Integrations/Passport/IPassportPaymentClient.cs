namespace Xpay.Api.Integrations.Passport;

// XPAY-373 — contrato tipado para Bre-B Payment, confirmado documentalmente
// en XPAY-372/373 (consulta en solo lectura de docs.passportfintech.com,
// nunca copiada literalmente a código — sólo la forma de campos). Construido
// SOBRE IPassportHttpClient (XPAY-272) — no introduce un segundo stack de
// HTTP/OAuth, mismo criterio que IPassportKeyClient/
// IPassportCustomerAccountClient.
//
// NO implementa: webhooks (XPAY-373 FASE 11 — el mismo BrebPaymentStateMachine
// que consume RetrievePaymentAsync está diseñado para reutilizarse desde un
// futuro handler de webhook, sin duplicar lógica financiera — pero ese
// handler en sí queda fuera de alcance de XPAY-373).
public interface IPassportPaymentClient
{
    // POST /v1/payments/breb
    Task<PassportPaymentResponse> CreateBrebPaymentAsync(
        PassportCreatePaymentRequest request, CancellationToken cancellationToken = default);

    // GET /v1/payments/{payment_id}
    Task<PassportPaymentResponse> RetrievePaymentAsync(
        string paymentId, CancellationToken cancellationToken = default);
}
