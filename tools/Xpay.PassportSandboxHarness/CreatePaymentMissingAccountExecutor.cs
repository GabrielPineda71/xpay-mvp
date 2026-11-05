using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-571 — M5-T5-MISSING-ACCOUNT: demuestra que IPassportPaymentClient.
// CreateBrebPaymentAsync (stack PRODUCTIVO real, sin segunda integración ni
// request manual) rechaza LOCALMENTE, antes de cualquier HTTP, un Create
// Bre-B Payment con `account_id` ausente. Mismo patrón exacto que
// CreatePaymentMissingAmountExecutor (M5-T5-MISSING-AMOUNT, XPAY-570), con
// el campo bajo prueba invertido.
//
// A diferencia de XPAY-570 (donde AccountId era la precondición operativa
// real y Amount el campo bajo prueba), aquí AccountId ES el campo bajo
// prueba — por tanto NUNCA se lee PASSPORT_ACCOUNT_ID del entorno, ni
// siquiera para verificar su presencia: el request siempre se construye
// con AccountId=string.Empty, sin importar qué contenga el entorno.
// ResolutionId/Amount reciben placeholders sintéticos fijos, nunca leídos
// de ningún valor real capturado — como el guard de AccountId es el
// PRIMERO en el orden de PassportPaymentClient.CreateBrebPaymentAsync
// (AccountId -> ResolutionId -> Amount), estos placeholders nunca influyen
// en el resultado ni viajan a ningún transporte.
//
// GARANTÍA ESTRUCTURAL (verificada por lectura de PassportPaymentClient.
// CreateBrebPaymentAsync): `string.IsNullOrWhiteSpace(request.AccountId)`
// lanza ArgumentException de forma SÍNCRONA, ANTES de construir ningún
// HttpRequestMessage — por tanto esta llamada NUNCA alcanza
// IPassportHttpClient/OAuth/red real.
public static class CreatePaymentMissingAccountExecutor
{
    // Nunca reales, nunca derivados de ningún valor real capturado —
    // existen únicamente para completar el DTO no-nullable. El guard de
    // AccountId siempre dispara antes de que estos valores puedan viajar a
    // ningún HttpRequestMessage.
    private const string SyntheticNotUsedResolutionId = "M5-T5-MISSING-ACCOUNT-SYNTHETIC-NEVER-SENT";
    private static readonly PassportPaymentAmountRequest SyntheticNotUsedAmount = new(Value: "0.00", Currency: PassportPaymentClient.CopCurrency);

    public static async Task<KeyOperationResult> ExecuteAsync(
        IConfiguration configuration,
        IPassportPaymentClient paymentClient,
        ICommitShaProvider commitShaProvider,
        DateTime executedAtUtc,
        string? automatedTestReference = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(paymentClient);
        ArgumentNullException.ThrowIfNull(commitShaProvider);

        string commitSha;
        try
        {
            commitSha = commitShaProvider.GetCommitSha();
        }
        catch (Exception ex)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"No se pudo resolver el commit SHA del backend: {ex.Message}");
        }

        // AccountId = string.Empty deliberado — ES el campo bajo prueba.
        // DELIBERADAMENTE nunca lee PASSPORT_ACCOUNT_ID de `configuration`
        // (parámetro conservado sólo por simetría de firma con los demás
        // executores del harness — este subcaso no depende de ningún
        // valor de entorno).
        var request = new PassportCreatePaymentRequest(
            AccountId: string.Empty,
            ResolutionId: SyntheticNotUsedResolutionId,
            Amount: SyntheticNotUsedAmount);

        try
        {
            await paymentClient.CreateBrebPaymentAsync(request).ConfigureAwait(false);

            // Estructuralmente inalcanzable con el contrato productivo
            // actual (ver comentario de clase) — si alguna vez se
            // alcanzara, significaría que el guard cambió sin actualizar
            // este executor. Se trata como bloqueo operativo genuino, sin
            // evidencia persistida (nunca se afirma un PASS falso).
            return new(KeyOperationOutcome.LocalBlocked, null,
                "El guard productivo de CreateBrebPaymentAsync no rechazó account_id ausente como se esperaba — revisar contrato antes de continuar.");
        }
        catch (ArgumentException)
        {
            // ÉXITO del subcaso M5-T5-MISSING-ACCOUNT: el guard productivo
            // rechazó el request antes de cualquier HTTP.
            var evidence = CreatePaymentMissingAccountEvidenceBuilder.BuildBlockedAsExpected(
                commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
    }
}
