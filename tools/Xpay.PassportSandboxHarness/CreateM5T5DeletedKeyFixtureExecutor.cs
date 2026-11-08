using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-613 — FASE A (PREP-CREATE) de un fixture Sandbox NUEVO y DESECHABLE
// para M5-T5-E (envío/resolución hacia clave Bre-B ELIMINADA). Mirror
// ESTRUCTURAL de CreateM5T5SuspendedKeyFixtureExecutor (XPAY-575, M5-T5-D):
// misma disciplina exacta — el key_value se genera localmente y se
// persiste privadamente ANTES de la llamada HTTP (estado
// PREPARED_NOT_CREATED) y de nuevo, correlacionado con el key_id real,
// tras una respuesta exitosa (estado CREATED). Sólo cambian el
// display_name, el prefijo de case_id del fixture y el case_id de su
// evidence builder.
//
// POR QUÉ UN FIXTURE NUEVO Y NO UNO HISTÓRICO: el fixture de M5-T5-D
// (XPAY-575/576/577) terminó SUSPENDIDO, no eliminado — un Resolve contra
// él nunca probaría el caso E. El fixture histórico de M4-T3-B (XPAY-492)
// sí fue eliminado, pero nació bajo el patrón antiguo SIN private capture:
// su key_value se perdió de forma irrecuperable (misma causa raíz de
// XPAY-574) y Resolve exige key_value, nunca key_id. Reutilizar cualquiera
// de los dos es estructuralmente imposible, no sólo inconveniente.
//
// AISLAMIENTO: reutiliza ÚNICAMENTE IPassportKeyClient.CreateKeyAsync
// (cliente productivo, SIN modificarlo) y DisposableBcodeGenerator (SIN
// modificarlo). Este executor NUNCA invoca Suspend/Activate/Delete/
// Resolve/ListKeys — ni siquiera tras éxito (Delete es la Fase B, un
// ticket separado) — y NUNCA conoce IPassportPaymentClient.
//
// Reutiliza el directorio privado YA establecido para M5
// (M5PaymentPrivateCapture.EnvPrivateCaptureDir, mode 700, fuera del repo,
// ya excluido de Git) — sin crear ninguna variable de entorno nueva. El
// glob de este fixture ("M5-T5-DELETED-KEY-FIXTURE-*") nunca colisiona con
// "M5-T5-SUSPENDED-KEY-FIXTURE-*" (M5-T5-D), "M5-PAYMENT-CREATE-*" ni
// "M5-T2-RESOLVE-*", los otros prefijos presentes en ese mismo directorio
// compartido.
public static class CreateM5T5DeletedKeyFixtureExecutor
{
    public const string FixtureCaseIdPrefix = "M5-T5-DELETED-KEY-FIXTURE";
    private const string PreparedSuffix = "PREPARED";
    private const string CreatedSuffix  = "CREATED";
    private const string FailedSuffix   = "FAILED";

    public const string FixturePurpose = "M5-T5-E-DELETED-KEY-FIXTURE";

    // Mismo criterio que M5-T5-D / M4-T3: BCODE es el único key_type con
    // contrato de FORMATO confirmado en este repositorio (^00[0-9]{8}$,
    // XPAY-343/344).
    private const string CertificationKeyType = "BCODE";

    // display_name inequívoco y DISTINTO del de M5-T5-D
    // ("XPay M5-T5-D Suspended Fixture") y del histórico de M4-T3-B
    // ("XPay M4-T3 Deleted QR Fixture") — para que las tres llaves
    // desechables sean identificables sin ambigüedad en Passport Dashboard.
    private const string CertificationDisplayName = "XPay M5-T5-E Deleted Fixture";

    public static async Task<KeyOperationResult> ExecuteAsync(
        IConfiguration configuration,
        IPassportKeyClient keyClient,
        ICommitShaProvider commitShaProvider,
        DateTime executedAtUtc,
        string? automatedTestReference = null,
        string? repositoryRootOverride = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(keyClient);
        ArgumentNullException.ThrowIfNull(commitShaProvider);

        var repositoryRoot = repositoryRootOverride ?? Directory.GetCurrentDirectory();

        // Guard #1 — destino privado (compartido con M5 Payment/Resolve/
        // fixture de M5-T5-D), FAIL-CLOSED antes de HTTP.
        var privateCaptureDirRaw = configuration[M5PaymentPrivateCapture.EnvPrivateCaptureDir];
        var captureValidation = M5PaymentPrivateCapture.Validate(privateCaptureDirRaw, repositoryRoot);
        if (captureValidation != M5PaymentPrivateCapture.ValidationOutcome.Valid)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"Destino privado M5 Payment ({M5PaymentPrivateCapture.EnvPrivateCaptureDir}) inválido: {captureValidation}. " +
                "No se realizó ninguna llamada a Passport.");
        }

        string privateCaptureDirFullPath;
        try
        {
            privateCaptureDirFullPath = Path.GetFullPath(privateCaptureDirRaw!, repositoryRoot);
            M5PaymentPrivateCapture.EnsureDirectory(privateCaptureDirFullPath);
        }
        catch (Exception ex)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"No se pudo preparar el destino privado de captura M5 Payment: {ex.Message}");
        }

        // Guard #2 — repeat guard PERMANENTE propio de esta Fase A: si YA
        // existe cualquier registro de ESTE fixture (PREPARED, CREATED o
        // FAILED), esta fase ya se intentó alguna vez — nunca se repite,
        // sin importar el resultado previo. Fail-closed también ante un
        // intento previo interrumpido entre HTTP y la escritura del
        // registro CREATED. Glob propio: NUNCA se bloquea ni se desbloquea
        // por los artefactos de M5-T5-D.
        if (Directory.Exists(privateCaptureDirFullPath) &&
            Directory.GetFiles(privateCaptureDirFullPath, $"{FixtureCaseIdPrefix}-*.private.json").Length > 0)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "LOCAL_BLOCKED_ALREADY_EXECUTED: ya existe un registro privado de M5-T5-DELETED-KEY-FIXTURE " +
                "(preparado, creado, o fallido) — no se realizó ninguna llamada a Passport.");
        }

        // Guard #3 — cuenta Sandbox ya autorizada para fixtures (misma
        // variable compartida y estable; nunca se crea una cuenta/cliente
        // nuevo). Este executor NUNCA lee PASSPORT_TEST_NEW_KEY_*,
        // PASSPORT_TEST_QR_* ni ninguna variable de fixture histórico.
        var accountId = configuration[HarnessTargetConfig.EnvAccountId];
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_ACCOUNT_ID ausente al momento de intentar Create M5-T5-E Deleted Fixture Key.");
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

        // key_value NUEVO — SIEMPRE DisposableBcodeGenerator.Generate(),
        // nunca leído de configuración ni derivado de ningún valor
        // existente (ni del fixture de M5-T5-D, ni de KEY_A, ni de
        // PASSPORT_TEST_BREB_KEY/PASSPORT_TEST_NEW_KEY_VALUE).
        var keyValue = DisposableBcodeGenerator.Generate();

        // Guard #4 — nunca la key activa de certificación (comparación
        // directa en memoria, nunca vía fingerprint). Estructuralmente
        // casi imposible (BCODE aleatorio de 8 dígitos), pero se verifica
        // explícitamente en vez de asumirlo por argumento probabilístico.
        var activeKeyValue = configuration[HarnessTargetConfig.EnvBrebKeyValue];
        if (!string.IsNullOrEmpty(activeKeyValue) && string.Equals(keyValue, activeKeyValue, StringComparison.Ordinal))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "El key_value generado coincidió con la key activa de certificación (colisión estructuralmente " +
                "casi imposible) — abortado por seguridad, sin ninguna llamada a Passport.");
        }

        var timestampToken = ToTimestampForPath(executedAtUtc);
        var keyValueFingerprint = Fingerprint.Compute(keyValue);

        // Persistir el key_value ANTES de HTTP, para que un fallo del
        // proceso después de la respuesta de Passport nunca pueda volver a
        // perderlo (causa raíz corregida en XPAY-575 y mantenida aquí: es
        // exactamente la razón por la que el fixture de M4-T3-B no se
        // puede reutilizar para este caso).
        try
        {
            WritePrivateRecord(privateCaptureDirFullPath, PreparedSuffix, timestampToken, new Dictionary<string, object?>
            {
                ["purpose"]               = FixturePurpose,
                ["state"]                  = "PREPARED_NOT_CREATED",
                ["prepared_at_utc"]        = FormatInstant(executedAtUtc),
                ["key_type"]                = CertificationKeyType,
                ["key_value"]               = keyValue, // RAW — sólo en este archivo privado, nunca público.
                ["key_value_fingerprint"]   = keyValueFingerprint,
            });
        }
        catch (Exception ex)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"No se pudo persistir el registro privado PREPARED antes de intentar Create Key: {ex.Message}. " +
                "No se realizó ninguna llamada a Passport.");
        }

        var request = new PassportCreateKeyRequest(
            AccountId: accountId,
            Key: new PassportKeyRequest(
                KeyType: Enum.Parse<PassportKeyType>(CertificationKeyType),
                KeyValue: keyValue))
        {
            DisplayName = CertificationDisplayName,
        };

        try
        {
            // Exactamente UNA llamada — sin reintentos, sin segundo HTTP.
            // ÚNICAMENTE CreateKeyAsync — nunca Suspend/Activate/Delete/
            // Resolve/ListKeys.
            var response = await keyClient.CreateKeyAsync(request).ConfigureAwait(false);

            var privateArtifactCaptured = TryWriteCreatedRecord(
                privateCaptureDirFullPath, timestampToken, keyValue, keyValueFingerprint, response.Id, executedAtUtc);

            var evidence = CreateM5T5DeletedKeyFixtureEvidenceBuilder.BuildFixtureCreated(
                accountId, keyValueFingerprint, response.Id, response.Status, httpStatus: null,
                commitSha, executedAtUtc, automatedTestReference, privateArtifactCaptured);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            var (httpStatus, safeErrorCode, safeErrorMessage) = ex is PassportTransportException pte
                ? (pte.StatusCode, pte.SafeErrorCode, pte.SafeErrorMessage)
                : (null, null, null);

            TryWriteFailedRecord(privateCaptureDirFullPath, timestampToken, keyValueFingerprint,
                httpStatus, safeErrorCode, safeErrorMessage, executedAtUtc);

            var evidence = CreateM5T5DeletedKeyFixtureEvidenceBuilder.BuildPassportHttpFailure(
                keyValueFingerprint, ex.Message, httpStatus, commitSha, executedAtUtc, safeErrorCode, safeErrorMessage);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }

    // Best-effort DELIBERADO (mismo criterio que XPAY-575): Passport ya
    // respondió — un fallo aquí nunca debe perder la respuesta ya obtenida
    // ni disparar un reintento. El registro PREPARED ya garantiza que el
    // key_value no se pierde aunque esto falle.
    private static bool TryWriteCreatedRecord(
        string privateCaptureDirFullPath, string timestampToken, string keyValue, string keyValueFingerprint,
        string? keyId, DateTime executedAtUtc)
    {
        try
        {
            WritePrivateRecord(privateCaptureDirFullPath, CreatedSuffix, timestampToken, new Dictionary<string, object?>
            {
                ["purpose"]              = FixturePurpose,
                ["state"]                 = "CREATED",
                ["created_at_utc"]        = FormatInstant(executedAtUtc),
                ["key_type"]               = CertificationKeyType,
                ["key_value"]              = keyValue, // RAW — correlación completa en un solo registro.
                ["key_value_fingerprint"]  = keyValueFingerprint,
                ["key_id"]                 = keyId,    // RAW — nunca público.
                ["key_id_fingerprint"]     = Fingerprint.Compute(keyId),
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryWriteFailedRecord(
        string privateCaptureDirFullPath, string timestampToken, string keyValueFingerprint,
        int? httpStatus, string? safeErrorCode, string? safeErrorMessage, DateTime executedAtUtc)
    {
        try
        {
            WritePrivateRecord(privateCaptureDirFullPath, FailedSuffix, timestampToken, new Dictionary<string, object?>
            {
                ["purpose"]              = FixturePurpose,
                ["state"]                 = "CREATE_FAILED",
                ["failed_at_utc"]         = FormatInstant(executedAtUtc),
                ["key_type"]               = CertificationKeyType,
                ["key_value_fingerprint"]  = keyValueFingerprint,
                ["observed_http_status"]  = httpStatus,
                ["safe_error_code"]       = safeErrorCode,
                ["safe_error_message"]    = safeErrorMessage,
            });
        }
        catch
        {
            // Best-effort — el estado PREPARED ya escrito sigue siendo
            // suficiente para reconciliación manual offline.
        }
    }

    // Escritura privada con permisos 600 explícitos — M5PaymentPrivateCapture.Write
    // (reutilizado sin modificar) ya es atómico y "crear, nunca
    // sobrescribir"; aquí sólo se añade el chmod 600 del archivo
    // resultante, que ese método no aplica por sí mismo.
    private static void WritePrivateRecord(
        string privateCaptureDirFullPath, string suffix, string timestampToken, Dictionary<string, object?> content)
    {
        var path = M5PaymentPrivateCapture.Write(
            privateCaptureDirFullPath, $"{FixtureCaseIdPrefix}-{suffix}", timestampToken, content);

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (Exception ex) when (ex is PlatformNotSupportedException or IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static string FormatInstant(DateTime executedAtUtc) =>
        executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);

    private static string ToTimestampForPath(DateTime executedAtUtc) =>
        executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH-mm-ssZ", System.Globalization.CultureInfo.InvariantCulture);
}
