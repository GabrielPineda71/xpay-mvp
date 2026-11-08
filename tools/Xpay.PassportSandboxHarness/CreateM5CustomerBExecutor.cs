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
//   - M5CustomerBPrivateCapture.EnvPrivateCaptureDir — FAIL-CLOSED antes de
//     HTTP, mismo criterio que M4T4PrivateCapture/M5PaymentPrivateCapture:
//     sin destino privado válido, nunca se gasta la única llamada real
//     autorizada. Leído PRIMERO (XPAY-533) — es una ruta de configuración,
//     no PII, y se necesita para el no-repeat guard antes de tocar nada
//     sensible.
//   - PASSPORT_TEST_M5_CUSTOMER_B_FIXTURE_FILE — ruta a un archivo JSON
//     PRIVADO (fuera del repo, ver M5CustomerBFixture) con los datos del
//     Customer B sintético a certificar. Nunca un customer real, nunca
//     datos de identificación reales hardcodeados en este repositorio
//     (XPAY-531 §7). Su ausencia bloquea localmente.
//
// XPAY-533 — ORDEN DE GUARDS (documentado explícitamente, ver §6 del
// ticket): destino privado válido -> EnsureDirectory -> ADQUIRIR LOCK DE
// EJECUCIÓN -> NO-REPEAT GUARD (¿ya existe un artefacto M5-T1-A válido?) ->
// ruta de fixture presente -> LEER/PARSEAR fixture (aquí se carga PII por
// primera vez) -> campos requeridos -> commit SHA -> LinkMerchantAsync. El
// no-repeat guard corre ANTES de leer cualquier dato de identificación —
// si el caso ya fue ejecutado, se bloquea sin procesar la fixture.
//
// El lock se libera en TODOS los caminos de salida ANTERIORES a la llamada
// Passport, y también tras una excepción capturada (falla clara, sin
// ambigüedad). Tras una respuesta EXITOSA de Passport, el lock SÓLO se
// libera si el artefacto privado se escribió correctamente
// (privateArtifactCaptured==true) — si la escritura falla, o el proceso
// muere antes de completarla, el lock queda huérfano deliberadamente: eso
// fuerza MANUAL_RECONCILIATION_REQUIRED en vez de permitir que una futura
// ejecución vuelva a llamar a Passport sin certeza de si Customer B ya fue
// creado (XPAY-533 §10).
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

        var repositoryRoot = repositoryRootOverride ?? Directory.GetCurrentDirectory();

        // Guard #1 — destino privado de captura, FAIL-CLOSED antes de HTTP
        // y antes de cualquier PII (mismo criterio XPAY-508/517/529).
        var privateCaptureDirRaw = configuration[M5CustomerBPrivateCapture.EnvPrivateCaptureDir];
        var captureValidation = M5CustomerBPrivateCapture.Validate(privateCaptureDirRaw, repositoryRoot);
        if (captureValidation != M5CustomerBPrivateCapture.ValidationOutcome.Valid)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"Destino privado M5 Customer B ({M5CustomerBPrivateCapture.EnvPrivateCaptureDir}) inválido: {captureValidation}. " +
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

        // Guard #2 — reserva de ejecución (XPAY-533 §9). Cierra la ventana
        // de carrera: si otra ejecución concurrente ya sostiene el lock (o
        // uno quedó huérfano tras un crash anterior), se bloquea sin llamar
        // a Passport y sin siquiera intentar el no-repeat guard.
        var lockOutcome = M5CustomerBPrivateCapture.TryAcquireLock(privateCaptureDirFullPath, out var lockFilePath);
        if (lockOutcome != M5CustomerBPrivateCapture.LockAcquisitionOutcome.Acquired)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "No se pudo adquirir la reserva de ejecución de Customer B (M5-T1-A) — " +
                "ya existe un lock activo (ejecución concurrente en curso, o pendiente de reconciliación manual " +
                "tras un intento previo interrumpido). No se realizó ninguna llamada a Passport.");
        }

        // Guard #3 — no-repeat guard (XPAY-533 §2/§3/§4). Reutiliza
        // M5CustomerBArtifactReader (mismo módulo que Account B usa para
        // leer el customer_id) sobre el propio directorio.
        var (repeatOutcome, repeatArtifact) = M5CustomerBArtifactReader.Resolve(privateCaptureDirFullPath);
        switch (repeatOutcome)
        {
            case M5CustomerBArtifactReader.ResolutionOutcome.Found when repeatArtifact is not null && M5CustomerBArtifactReader.IsValid(repeatArtifact):
                // Caso B — ya existe una ejecución exitosa previa del mismo
                // caso. NUNCA se imprime el customer_id crudo ni la ruta.
                M5CustomerBPrivateCapture.ReleaseLock(lockFilePath);
                return new(KeyOperationOutcome.LocalBlocked, null,
                    "LOCAL_BLOCKED_ALREADY_EXECUTED: ya existe un artefacto privado válido de Customer B (M5-T1-A) " +
                    "de una ejecución previa exitosa. No se realizó ninguna llamada a Passport.");

            case M5CustomerBArtifactReader.ResolutionOutcome.Found:
                // Caso C/D — el artefacto existe pero no pasa IsValid
                // (case_id inesperado, o customer_id vacío/ausente). Nunca
                // se asume "no artifact" ni se ignora — fail-closed.
                M5CustomerBPrivateCapture.ReleaseLock(lockFilePath);
                return new(KeyOperationOutcome.LocalBlocked, null,
                    "LOCAL_BLOCKED: existe un artefacto privado en el directorio de Customer B (M5-T1-A) con " +
                    "formato o case_id inesperado — requiere revisión manual antes de continuar. " +
                    "No se realizó ninguna llamada a Passport.");

            case M5CustomerBArtifactReader.ResolutionOutcome.MultipleArtifactsFound:
                // Caso E — ambigüedad, nunca se selecciona automáticamente.
                M5CustomerBPrivateCapture.ReleaseLock(lockFilePath);
                return new(KeyOperationOutcome.LocalBlocked, null,
                    "LOCAL_BLOCKED_AMBIGUOUS: se encontró más de un artefacto privado en el directorio de " +
                    "Customer B (M5-T1-A) — requiere revisión manual antes de continuar. " +
                    "No se realizó ninguna llamada a Passport.");

            case M5CustomerBArtifactReader.ResolutionOutcome.Unreadable:
                // Caso C — malformado/ilegible, fail-closed.
                M5CustomerBPrivateCapture.ReleaseLock(lockFilePath);
                return new(KeyOperationOutcome.LocalBlocked, null,
                    "LOCAL_BLOCKED: el directorio de Customer B (M5-T1-A) contiene un artefacto privado " +
                    "ilegible/malformado — requiere revisión manual antes de continuar. " +
                    "No se realizó ninguna llamada a Passport.");

            case M5CustomerBArtifactReader.ResolutionOutcome.DirectoryMissing:
            case M5CustomerBArtifactReader.ResolutionOutcome.NoArtifactFound:
                // Caso A — sin artefacto previo, se continúa con normalidad.
                break;
        }

        // Guard #4 — ruta a la fixture privada.
        var fixtureFilePath = configuration[EnvFixtureFile];
        if (string.IsNullOrWhiteSpace(fixtureFilePath))
        {
            M5CustomerBPrivateCapture.ReleaseLock(lockFilePath);
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"{EnvFixtureFile} ausente al momento de intentar Create Customer B (M5-T1-A). " +
                "No se realizó ninguna llamada a Passport.");
        }

        // Guard #5 — la fixture debe existir y ser JSON válido con todos los
        // campos requeridos presentes. Fallo aquí bloquea localmente —
        // nunca se sustituye con un valor por defecto ni se completa a medias.
        // Este es el primer punto donde se carga PII en memoria.
        M5CustomerBFixture fixture;
        try
        {
            var json = File.ReadAllText(fixtureFilePath);
            var parsed = JsonSerializer.Deserialize<M5CustomerBFixture>(json);
            if (parsed is null)
            {
                M5CustomerBPrivateCapture.ReleaseLock(lockFilePath);
                return new(KeyOperationOutcome.LocalBlocked, null,
                    $"La fixture de Customer B ({EnvFixtureFile}) no pudo deserializarse. " +
                    "No se realizó ninguna llamada a Passport.");
            }
            fixture = parsed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            M5CustomerBPrivateCapture.ReleaseLock(lockFilePath);
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
            M5CustomerBPrivateCapture.ReleaseLock(lockFilePath);
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"La fixture de Customer B ({EnvFixtureFile}) está incompleta — faltan campos requeridos. " +
                "No se realizó ninguna llamada a Passport.");
        }

        string commitSha;
        try
        {
            commitSha = commitShaProvider.GetCommitSha();
        }
        catch (Exception ex)
        {
            M5CustomerBPrivateCapture.ReleaseLock(lockFilePath);
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

            // XPAY-533 §10 — ventana de crash: si el proceso muere entre
            // esta línea y la siguiente, el lock queda huérfano
            // deliberadamente (ver comentario de clase). El lock SÓLO se
            // libera si el artefacto se capturó con éxito.
            var privateArtifactCaptured = TryWritePrivateArtifact(
                privateCaptureDirFullPath, commitSha, executedAtUtc, response);

            if (privateArtifactCaptured)
                M5CustomerBPrivateCapture.ReleaseLock(lockFilePath);

            var evidence = CreateM5CustomerBEvidenceBuilder.BuildSuccess(
                request, response, httpStatus: null, commitSha, executedAtUtc, automatedTestReference,
                privateArtifactCaptured);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            // Falla clara y sin ambigüedad — Passport no creó el recurso.
            // Seguro liberar el lock: una futura ejecución deliberada puede
            // reintentar sin riesgo de duplicado.
            M5CustomerBPrivateCapture.ReleaseLock(lockFilePath);

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
