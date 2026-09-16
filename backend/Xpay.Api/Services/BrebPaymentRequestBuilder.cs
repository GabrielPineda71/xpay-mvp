using Xpay.Api.Integrations.Passport;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// XPAY-373 FASE 3/6 — construye el PassportCreatePaymentRequest para el
// retiro YA PERSISTIDO y YA RESERVADO (PassportBrebRetiro). Función PURA —
// no hace I/O, no llama a Passport, no toca la base de datos — 100%
// testeable offline (mismo criterio que BrebKeyResolutionRequestBuilder,
// XPAY-371).
//
// SOURCE ACCOUNT SIEMPRE SERVER-SIDE (FASE 6/13): operationalAccountId
// llega como parámetro explícito desde configuración
// (PassportOptions.EnvOperationalAccountId, leído por el llamador
// BrebPaymentService) — este builder NUNCA lee configuración ni acepta un
// account_id proveniente del retiro/usuario. No existe ningún camino por
// el que un caller externo pueda influir el account_id del payment.
//
// RESOLUTION_ID SIEMPRE DEL RETIRO (FASE 3): se usa exclusivamente
// retiro.PassportResolutionId — el snapshot inmutable capturado al
// reservar (ver PassportBrebRetiro.cs) — nunca una resolución "vigente" de
// la llave que pudiera haber cambiado después de crear este retiro
// específico.
public static class BrebPaymentRequestBuilder
{
    // XPAY-373 FASE 3 — "Si resolution está expirada: NO enviar payment.
    // Debe requerirse una nueva resolución." Tipo dedicado (no
    // InvalidOperationException genérica) para que el llamador pueda
    // distinguir este caso específico de cualquier otro error de
    // validación y decidir su manejo (FASE 5: esto ocurre SIEMPRE antes de
    // cualquier llamada HTTP — es seguro liberar la reserva).
    public sealed class ResolutionExpiradaException(string message) : InvalidOperationException(message);

    public static PassportCreatePaymentRequest Build(
        PassportBrebRetiro retiro, string? operationalAccountId, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(retiro);

        if (string.IsNullOrWhiteSpace(operationalAccountId))
            throw new PassportConfigurationException(
                $"{PassportOptions.EnvOperationalAccountId} no está configurado.");

        // "Ausente" y "vencida" comparten el mismo remedio (FASE 3: "Si
        // resolution está expirada: NO enviar payment. Debe requerirse una
        // nueva resolución") — se unifican en el mismo tipo de excepción
        // para que el llamador (BrebPaymentService) no necesite distinguir
        // dos casos que se resuelven exactamente igual.
        if (string.IsNullOrWhiteSpace(retiro.PassportResolutionId)
            || retiro.PassportResolutionExpiresAtUtc is null
            || retiro.PassportResolutionExpiresAtUtc <= nowUtc)
            throw new ResolutionExpiradaException(
                "La resolución Passport del retiro está vencida o ausente; se requiere resolver la llave nuevamente antes de reintentar.");

        if (retiro.Valor <= 0)
            throw new InvalidOperationException("El valor del retiro debe ser mayor a cero.");

        return new PassportCreatePaymentRequest(
            AccountId: operationalAccountId,
            ResolutionId: retiro.PassportResolutionId,
            Amount: new PassportPaymentAmountRequest(
                Value: retiro.Valor.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                Currency: PassportPaymentClient.CopCurrency));
    }
}
