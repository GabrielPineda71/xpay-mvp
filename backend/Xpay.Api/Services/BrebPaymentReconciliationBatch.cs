using Microsoft.Extensions.Logging;
using Xpay.Api.Common;
using Xpay.Api.Integrations.Passport;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// XPAY-381 — orquestación de UN barrido de reconciliación, extraída de
// BrebPaymentReconciliationService (BackgroundService) para que sea
// testeable con fakes (sin DbContext real, sin HttpClient real) — mismo
// criterio de "extracción de función pura para testabilidad" ya aplicado a
// HarnessOrchestrator.Prepare/CreateQrStaticEvidenceBuilder en este
// repositorio. NO decide nada financiero por sí misma: cada retiro se
// resuelve delegando a `aplicarEstado`, que en producción es
// BrebPaymentService.ApplyPassportPaymentStatusAsync (la ÚNICA función que
// aplica un estado Payment — no se duplica aquí ninguna lógica financiera).
//
// Recibe IPassportPaymentClient (interfaz real, ya inyectable/fakeable) en
// vez de un delegate, porque ese cliente NUNCA se usa aquí para crear un
// Payment — sólo para RetrievePaymentAsync — así un test puede además
// verificar por construcción que ninguna otra operación del cliente fue
// invocada.
public static class BrebPaymentReconciliationBatch
{
    public sealed record ItemResultado(long IdBrebRetiro, bool Exitoso, string? ErrorTipo);

    public static async Task<IReadOnlyList<ItemResultado>> ProcesarLoteAsync(
        IReadOnlyList<PassportBrebRetiro> candidatos,
        IPassportPaymentClient paymentClient,
        Func<long, string?, string?, PassportPaymentErrorResponse?, CancellationToken, Task> aplicarEstado,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var resultados = new List<ItemResultado>(candidatos.Count);

        foreach (var retiro in candidatos)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Aislamiento por retiro — un fallo (Passport transitorio,
            // timeout, excepción de aplicación) en UNO no debe impedir que
            // los demás del mismo barrido se procesen. El retiro fallido
            // simplemente no avanza en este ciclo; el siguiente tick lo
            // reintentará automáticamente porque su Estado no cambió.
            try
            {
                var response = await paymentClient
                    .RetrievePaymentAsync(retiro.PassportPaymentId!, cancellationToken)
                    .ConfigureAwait(false);

                await aplicarEstado(
                    retiro.IdBrebRetiro, response.Id, response.Status, response.Error, cancellationToken)
                    .ConfigureAwait(false);

                resultados.Add(new ItemResultado(retiro.IdBrebRetiro, Exitoso: true, ErrorTipo: null));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // apagado normal del host — no es un error de este retiro
            }
            catch (Exception ex)
            {
                // Saneado por construcción: PassportException nunca incluye
                // secretos/tokens/PII en Message (ver PassportExceptions.cs);
                // cualquier otra excepción sólo registra su tipo, nunca su
                // Message crudo (podría venir de una capa que no garantiza
                // lo mismo). Nunca se libera saldo ni se cambia estado local
                // aquí — "aplicarEstado" simplemente no se llamó.
                logger.LogWarning(
                    "BREB_RECONCILIACION_ITEM_ERROR: retiro={Retiro} paymentFingerprint={Fp} errorTipo={ErrorTipo}",
                    retiro.IdBrebRetiro, Fingerprint.Compute(retiro.PassportPaymentId), ex.GetType().Name);
                resultados.Add(new ItemResultado(retiro.IdBrebRetiro, Exitoso: false, ErrorTipo: ex.GetType().Name));
            }
        }

        return resultados;
    }
}
