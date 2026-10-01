using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-575 — FASE 1 (Create Key) de un fixture Sandbox NUEVO y DESECHABLE
// para M5-T5-D (suspended key), autorizado tras el bloqueo estructural de
// XPAY-574: el fixture histórico de M4-T3-A (XPAY-474/476/477) generó su
// key_value con DisposableBcodeGenerator sin persistirlo en ningún lugar
// recuperable — Resolve necesita key_value, nunca key_id, y ese valor se
// perdió para siempre. Esta clase corrige esa causa raíz: el key_value
// generado se persiste privadamente ANTES de la llamada HTTP (estado
// PREPARED_NOT_CREATED) y de nuevo, correlacionado con el key_id real, tras
// una respuesta exitosa (estado CREATED) — ambos registros inmutables
// (nunca se sobrescriben, mismo criterio "crear, nunca sobrescribir" ya
// aplicado en todo el proyecto), con el MISMO sufijo de timestamp para
// correlacionarlos sin depender de ninguna variable de entorno.
//
// AISLAMIENTO: reutiliza ÚNICAMENTE IPassportKeyClient.CreateKeyAsync
// (mismo cliente productivo que CreateM4T3SuspendedFixtureKeyExecutor,
// SIN modificarlo) y DisposableBcodeGenerator (SIN modificarlo) — no crea
// un segundo stack, no toca M4. Este executor NUNCA invoca Suspend/
// Activate/Delete/Resolve/ListKeys — bajo ninguna circunstancia, ni
// siquiera tras éxito (esa es la Fase 2, un ticket separado).
//
// Reutiliza el directorio privado YA establecido para M5
// (M5PaymentPrivateCapture.EnvPrivateCaptureDir, mode 700, fuera del repo,
// ya excluido de Git) — sin crear ninguna variable de entorno nueva. El
// glob de este fixture ("M5-T5-SUSPENDED-KEY-FIXTURE-*") nunca colisiona
// con "M5-PAYMENT-CREATE-*" ni "M5-T2-RESOLVE-*", los únicos otros
// prefijos ya presentes en ese mismo directorio compartido.
public static class CreateM5T5SuspendedKeyFixtureExecutor
{
    public const string FixtureCaseIdPrefix = "M5-T5-SUSPENDED-KEY-FIXTURE";
    private const string PreparedSuffix = "PREPARED";
    private const string CreatedSuffix  = "CREATED";
    private const string FailedSuffix   = "FAILED";

    private const string CertificationKeyType = "BCODE";

    // XPAY-575 — display_name inequívoco y distinto del fixture histórico
    // de M4-T3-A ("XPay M4-T3 Suspended QR Fixture") — para que ambas
    // llaves sean identificables sin ambigüedad en Passport Dashboard.
    private const string CertificationDisplayName = "XPay M5-T5-D Suspended Fixture";

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

        // Guard #1 — destino privado (compartido con M5 Payment/Resolve),
        // FAIL-CLOSED antes de HTTP.
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

        // Guard #2 — repeat guard PERMANENTE: si YA existe cualquier
        // registro de este fixture (PREPARED, CREATED o FAILED), esta fase
        // ya se intentó alguna vez — nunca se repite, sin importar el
        // resultado previo. Fail-closed también ante un intento previo
        // interrumpido entre HTTP y la escritura del registro CREATED.
        if (Directory.Exists(privateCaptureDirFullPath) &&
            Directory.GetFiles(privateCaptureDirFullPath, $"{FixtureCaseIdPrefix}-*.private.json").Length > 0)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "LOCAL_BLOCKED_ALREADY_EXECUTED: ya existe un registro privado de M5-T5-SUSPENDED-KEY-FIXTURE " +
                "(preparado, creado, o fallido) — no se realizó ninguna llamada a Passport.");
        }

        // Guard #3 — cuenta Sandbox ya autorizada para fixtures (misma
        // variable que CreateM4T3SuspendedFixtureKeyExecutor — nunca se
        // crea una cuenta/cliente nuevo).
        var accountId = configuration[HarnessTargetConfig.EnvAccountId];
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_ACCOUNT_ID ausente al momento de intentar Create M5-T5-D Suspended Fixture Key.");
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

        // key_value NUEVO — SIEMPRE DisposableBcodeGenerator.Generate()
        // (mismo generador ya usado y probado por M4-T3-A, sin
        // modificarlo), nunca leído de configuración ni derivado de
        // ningún valor existente.
        var keyValue = DisposableBcodeGenerator.Generate();

        // Guard #4 — nunca la key activa de certificación (comparación
        // directa en memoria, nunca vía fingerprint — un fingerprint
        // podría, en teoría, tener falsos positivos; una comparación
        // directa de dos valores ya en memoria en este mismo proceso no
        // los expone a ningún lugar nuevo). Estructuralmente casi
        // imposible (BCODE aleatorio de 8 dígitos vs. un valor arbitrario
        // de Dashboard), pero se verifica explícitamente en vez de
        // asumirlo solo por argumento probabilístico.
        var activeKeyValue = configuration[HarnessTargetConfig.EnvBrebKeyValue];
        if (!string.IsNullOrEmpty(activeKeyValue) && string.Equals(keyValue, activeKeyValue, StringComparison.Ordinal))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "El key_value generado coincidió con la key activa de certificación (colisión estructuralmente " +
                "casi imposible) — abortado por seguridad, sin ninguna llamada a Passport.");
        }

        var timestampToken = ToTimestampForPath(executedAtUtc);
        var keyValueFingerprint = Fingerprint.Compute(keyValue);

        // Paso 5 (XPAY-575 §5) — persistir el key_value ANTES de HTTP, para
        // que un fallo del proceso después de la respuesta de Passport
        // nunca pueda volver a perderlo (causa raíz corregida respecto al
        // fixture histórico de M4-T3-A).
        try
        {
            WritePrivateRecord(privateCaptureDirFullPath, PreparedSuffix, timestampToken, new Dictionary<string, object?>
            {
                ["purpose"]               = "M5-T5-D-SUSPENDED-KEY-FIXTURE",
                ["state"]                  = "PREPARED_NOT_CREATED",
                ["prepared_at_utc"]        = executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
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

            var evidence = CreateM5T5SuspendedKeyFixtureEvidenceBuilder.BuildFixtureCreated(
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

            var evidence = CreateM5T5SuspendedKeyFixtureEvidenceBuilder.BuildPassportHttpFailure(
                keyValueFingerprint, ex.Message, httpStatus, commitSha, executedAtUtc, safeErrorCode, safeErrorMessage);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }

    // Best-effort DELIBERADO (mismo criterio que TryWritePrivateArtifact en
    // M5ResolveExecutor/CreateBrebPaymentCertificationExecutor): Passport
    // ya respondió — un fallo aquí nunca debe perder la respuesta ya
    // obtenida ni disparar un reintento. El registro PREPARED (Paso 5) ya
    // garantiza que el key_value no se pierde aunque esto falle.
    private static bool TryWriteCreatedRecord(
        string privateCaptureDirFullPath, string timestampToken, string keyValue, string keyValueFingerprint,
        string? keyId, DateTime executedAtUtc)
    {
        try
        {
            WritePrivateRecord(privateCaptureDirFullPath, CreatedSuffix, timestampToken, new Dictionary<string, object?>
            {
                ["purpose"]              = "M5-T5-D-SUSPENDED-KEY-FIXTURE",
                ["state"]                 = "CREATED",
                ["created_at_utc"]        = executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
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
                ["purpose"]              = "M5-T5-D-SUSPENDED-KEY-FIXTURE",
                ["state"]                 = "CREATE_FAILED",
                ["failed_at_utc"]         = executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
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
    // sobrescribir"; aquí sólo se añade el chmod 600 del archivo resultante,
    // que ese método no aplica por sí mismo.
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

    private static string ToTimestampForPath(DateTime executedAtUtc) =>
        executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH-mm-ssZ", System.Globalization.CultureInfo.InvariantCulture);
}
