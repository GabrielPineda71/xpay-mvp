using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-570 — M5-T5-MISSING-AMOUNT: demuestra que IPassportPaymentClient.
// CreateBrebPaymentAsync (stack PRODUCTIVO real, sin segunda integración ni
// request manual) rechaza LOCALMENTE, antes de cualquier HTTP, un Create
// Bre-B Payment con `amount` ausente. Mismo patrón exacto que
// CreateKeyMissingExecutor (M3-T6-MISSING, XPAY-344) aplicado al stack de
// Payment en vez de al de Key.
//
// Lee ÚNICAMENTE PASSPORT_ACCOUNT_ID aquí (precondición operativa, nunca el
// campo bajo prueba) — DELIBERADAMENTE nunca lee
// PASSPORT_TEST_M5_PAYMENT_AMOUNT_VALUE ni
// PASSPORT_TEST_M5_PAYMENT_RESOLUTION_ID: el objetivo del subcaso es
// demostrar la ausencia de `amount`, por lo que siempre se construye el
// request con Amount=null (forzado a través del tipo no-nullable — es la
// forma más fiel de representar "campo amount ausente" que el DTO
// productivo permite, ver XPAY-569 §2/§3) y con un ResolutionId SINTÉTICO
// fijo, nunca leído de ningún resolution_id real capturado — así los guards
// de AccountId/ResolutionId (que corren ANTES que el de Amount en
// PassportPaymentClient.CreateBrebPaymentAsync) nunca disparan por error,
// dejando que el ÚNICO guard ejercitado sea el de Amount ausente.
//
// GARANTÍA ESTRUCTURAL (verificada por lectura de PassportPaymentClient.
// CreateBrebPaymentAsync): `request.Amount is null` lanza ArgumentException
// de forma SÍNCRONA, ANTES de construir ningún HttpRequestMessage — por
// tanto esta llamada NUNCA alcanza IPassportHttpClient/OAuth/red real.
public static class CreatePaymentMissingAmountExecutor
{
    // Nunca real, nunca derivado de un resolution_id real ni de su
    // fingerprint — existe únicamente para que el guard de ResolutionId
    // (anterior al de Amount) no dispare por error. Nunca se transmite:
    // el guard de Amount siempre dispara antes de que este valor pueda
    // viajar a ningún HttpRequestMessage.
    private const string SyntheticNotUsedResolutionId = "M5-T5-MISSING-AMOUNT-SYNTHETIC-NEVER-SENT";

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

        var accountId = configuration[PassportOptions.EnvOperationalAccountId];

        // Defensa en profundidad: este comando no usa HarnessTargetConfig
        // (mismo criterio que CreateBrebPaymentCert) — este chequeo es la
        // única línea de defensa antes de construir el request.
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"{PassportOptions.EnvOperationalAccountId} ausente al momento de intentar Create Payment Missing Amount (M5-T5-MISSING-AMOUNT).");
        }

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

        // Amount = null deliberado — ES el campo bajo prueba.
        var request = new PassportCreatePaymentRequest(
            AccountId: accountId,
            ResolutionId: SyntheticNotUsedResolutionId,
            Amount: null!);

        try
        {
            await paymentClient.CreateBrebPaymentAsync(request).ConfigureAwait(false);

            // Estructuralmente inalcanzable con el contrato productivo
            // actual (ver comentario de clase) — si alguna vez se
            // alcanzara, significaría que el guard cambió sin actualizar
            // este executor. Se trata como bloqueo operativo genuino, sin
            // evidencia persistida (nunca se afirma un PASS falso).
            return new(KeyOperationOutcome.LocalBlocked, null,
                "El guard productivo de CreateBrebPaymentAsync no rechazó amount ausente como se esperaba — revisar contrato antes de continuar.");
        }
        catch (ArgumentException)
        {
            // ÉXITO del subcaso M5-T5-MISSING-AMOUNT: el guard productivo
            // rechazó el request antes de cualquier HTTP.
            var evidence = CreatePaymentMissingAmountEvidenceBuilder.BuildBlockedAsExpected(
                accountId, commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
    }
}
