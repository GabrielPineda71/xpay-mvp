using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-531 — orquesta la ejecución REAL futura de la certificación M5-T1-A
// (Create/Link Customer B, POST /v1/customers/business/link). AISLAMIENTO
// ARQUITECTÓNICO OBLIGATORIO: este executor llama EXCLUSIVAMENTE a
// IPassportCustomerAccountClient.LinkMerchantAsync directamente — nunca a
// ningún servicio de producto XPAY. Mismo patrón exacto que
// CreateBrebPaymentCertificationExecutor (XPAY-529) frente a
// IPassportPaymentClient.
//
// Config LEÍDA EXCLUSIVAMENTE AQUÍ (nunca en dry-run):
//   - PASSPORT_TEST_M5_CUSTOMER_B_FIXTURE_FILE — ruta a un archivo JSON
//     PRIVADO (fuera del repo, ver M5CustomerBFixture) con los datos del
//     Customer B sintético a certificar. Nunca un customer real, nunca
//     datos de identificación reales hardcodeados en este repositorio
//     (XPAY-531 §7). Su ausencia bloquea localmente.
//   - M5CustomerBPrivateCapture.EnvPrivateCaptureDir — FAIL-CLOSED antes de
//     HTTP, mismo criterio que M4T4PrivateCapture/M5PaymentPrivateCapture:
//     sin destino privado válido, nunca se gasta la única llamada real
//     autorizada.
public static class CreateM5CustomerBExecutor
{
    public const string EnvFixtureFile = "PASSPORT_TEST_M5_CUSTOMER_B_FIXTURE_FILE";

    public static async Task<KeyOperationResult> ExecuteAsync(
        IConfiguration configuration,
        IPassportCustomerAccountClient customerAccountClient,
        ICommitShaProvider commitShaProvider,
        DateTime executedAtUtc,
        string? automatedTestReference = null,
        string? repositoryRootOverride = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(customerAccountClient);
        ArgumentNullException.ThrowIfNull(commitShaProvider);

        // Guard #1 — ruta a la fixture privada.
        var fixtureFilePath = configuration[EnvFixtureFile];
        if (string.IsNullOrWhiteSpace(fixtureFilePath))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"{EnvFixtureFile} ausente al momento de intentar Create Customer B (M5-T1-A). " +
                "No se realizó ninguna llamada a Passport.");
        }

        // Guard #2 — destino privado de captura, FAIL-CLOSED antes de HTTP
        // (mismo criterio XPAY-508/517/529). Se valida ANTES de leer/parsear
        // la fixture para no gastar trabajo si el destino ya es inválido.
        var repositoryRoot = repositoryRootOverride ?? Directory.GetCurrentDirectory();
        var privateCaptureDirRaw = configuration[M5CustomerBPrivateCapture.EnvPrivateCaptureDir];
        var captureValidation = M5CustomerBPrivateCapture.Validate(privateCaptureDirRaw, repositoryRoot);
        if (captureValidation != M5CustomerBPrivateCapture.ValidationOutcome.Valid)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"Destino privado M5 Customer B ({M5CustomerBPrivateCapture.EnvPrivateCaptureDir}) inválido: {captureValidation}. " +
                "No se realizó ninguna llamada a Passport.");
        }

        // Guard #3 — la fixture debe existir y ser JSON válido con todos los
        // campos requeridos presentes. Fallo aquí bloquea localmente —
        // nunca se sustituye con un valor por defecto ni se completa a medias.
        M5CustomerBFixture fixture;
        try
        {
            var json = File.ReadAllText(fixtureFilePath);
            var parsed = JsonSerializer.Deserialize<M5CustomerBFixture>(json);
            if (parsed is null)
            {
                return new(KeyOperationOutcome.LocalBlocked, null,
                    $"La fixture de Customer B ({EnvFixtureFile}) no pudo deserializarse. " +
                    "No se realizó ninguna llamada a Passport.");
            }
            fixture = parsed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"No se pudo leer la fixture de Customer B ({EnvFixtureFile}): {ex.Message}. " +
                "No se realizó ninguna llamada a Passport.");
        }

        if (string.IsNullOrWhiteSpace(fixture.BusinessName)
            || string.IsNullOrWhiteSpace(fixture.Email)
            || string.IsNullOrWhiteSpace(fixture.MobilePhoneNumber)
            || string.IsNullOrWhiteSpace(fixture.IdentificationNumber)
            || string.IsNullOrWhiteSpace(fixture.MerchantCategoryCode)
            || fixture.Address is null
            || string.IsNullOrWhiteSpace(fixture.Address.Line1)
            || string.IsNullOrWhiteSpace(fixture.Address.City)
            || string.IsNullOrWhiteSpace(fixture.Address.State)
            || string.IsNullOrWhiteSpace(fixture.Address.PostCode)
            || string.IsNullOrWhiteSpace(fixture.Address.Country))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"La fixture de Customer B ({EnvFixtureFile}) está incompleta — faltan campos requeridos. " +
                "No se realizó ninguna llamada a Passport.");
        }

        string privateCaptureDirFullPath;
        try
        {
            privateCaptureDirFullPath = Path.GetFullPath(privateCaptureDirRaw!, repositoryRoot);
            M5CustomerBPrivateCapture.EnsureDirectory(privateCaptureDirFullPath);
        }
        catch (Exception ex)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"No se pudo preparar el destino privado de captura M5 Customer B: {ex.Message}");
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

        var request = new PassportLinkMerchantRequest(
            BusinessName: fixture.BusinessName!,
            Email: fixture.Email!,
            MobilePhoneNumber: fixture.MobilePhoneNumber!,
            IdentificationNumber: fixture.IdentificationNumber!,
            MerchantCategoryCode: fixture.MerchantCategoryCode!,
            Address: new PassportMerchantAddressRequest(
                Line1: fixture.Address!.Line1!,
                City: fixture.Address.City!,
                State: fixture.Address.State!,
                PostCode: fixture.Address.PostCode!,
                Country: fixture.Address.Country!)
            {
                Line2 = fixture.Address.Line2,
                Line3 = fixture.Address.Line3,
            });

        try
        {
            // Exactamente UNA llamada — sin reintentos, sin segundo HTTP,
            // sin fallback. ÚNICAMENTE LinkMerchantAsync — nunca
            // RetrieveCustomerAsync ni ningún otro cliente Passport.
            var response = await customerAccountClient.LinkMerchantAsync(request).ConfigureAwait(false);

            var privateArtifactCaptured = TryWritePrivateArtifact(
                privateCaptureDirFullPath, commitSha, executedAtUtc, response);

            var evidence = CreateM5CustomerBEvidenceBuilder.BuildSuccess(
                request, response, httpStatus: null, commitSha, executedAtUtc, automatedTestReference,
                privateArtifactCaptured);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            var (httpStatus, safeErrorCode, safeErrorMessage) = ex is PassportTransportException pte
                ? (pte.StatusCode, pte.SafeErrorCode, pte.SafeErrorMessage)
                : (null, null, null);

            var evidence = CreateM5CustomerBEvidenceBuilder.BuildPassportHttpFailure(
                request, ex.Message, httpStatus, commitSha, executedAtUtc, safeErrorCode, safeErrorMessage);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }

    // Preserva ÚNICAMENTE customer_id crudo (fuera del repo, para que M5-T1-B
    // lo consuma) — nunca los datos de identificación de la fixture. Best-
    // effort DELIBERADO: Passport ya respondió — un fallo aquí nunca debe
    // perder el registro sanitizado ya obtenido.
    private static bool TryWritePrivateArtifact(
        string privateCaptureDirFullPath, string commitSha, DateTime executedAtUtc,
        PassportCustomerResponse response)
    {
        try
        {
            var content = new Dictionary<string, object?>
            {
                ["case_id"]           = CreateM5CustomerBEvidenceBuilder.CaseId,
                ["captured_at_utc"]    = executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
                ["backend_commit_sha"] = commitSha,
                ["customer_id"]        = response.Id,
                ["status_at_creation"] = response.Status,
            };

            M5CustomerBPrivateCapture.Write(privateCaptureDirFullPath, "M5-T1-A", ToTimestampForPath(executedAtUtc), content);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ToTimestampForPath(DateTime executedAtUtc) =>
        executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH-mm-ssZ", System.Globalization.CultureInfo.InvariantCulture);
}
