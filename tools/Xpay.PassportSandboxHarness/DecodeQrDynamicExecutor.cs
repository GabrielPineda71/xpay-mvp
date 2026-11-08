using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-517 — orquesta la ejecución REAL de M4-T5 (Decode QR Code —
// DINÁMICO), reutilizando EXCLUSIVAMENTE el QR ya creado y exitoso de
// M4-T4 (XPAY-513). NO crea ningún QR nuevo. El input de Decode
// (qr_code_data) se lee ÚNICAMENTE del artefacto privado de M4-T4 — nunca
// del entorno, nunca de un archivo versionado, nunca impreso.
//
// Precondiciones FAIL-CLOSED, en orden, ANTES de cualquier HTTP:
//   1. customer_id/key_id (targets ya usados por create-qr-dynamic)
//      presentes — el segundo sólo se usa para el chequeo de identidad,
//      nunca se envía en el request de Decode.
//   2. destino privado de M4-T4 (M4T4PrivateCapture.EnvPrivateCaptureDir)
//      presente y con exactamente un artefacto ".private.json".
//   3. identidad del artefacto M4-T4 confirmada: case_id=M4-T4,
//      status=ACTIVE, type=DYNAMIC, fingerprints de key_id/customer_id
//      coincidentes con los targets configurados (M4T4ArtifactReader.
//      IdentityMatches).
//   4. qr_code_data presente y no vacío en el artefacto.
//   5. destino privado PROPIO de M4-T5 (M4T5PrivateCapture.EnvPrivateCaptureDir)
//      presente y válido — mismo criterio FAIL-CLOSED que XPAY-508: nunca
//      gastar la única llamada real autorizada sin poder preservar después
//      el JSON crudo de la respuesta.
//
// Exactamente UNA llamada — IPassportQrClient.DecodeQrCodeWithRawCaptureAsync
// (XPAY-517), nunca CreateQrCodeAsync ni ningún método de IPassportKeyClient.
public static class DecodeQrDynamicExecutor
{
    public static async Task<KeyOperationResult> ExecuteAsync(
        IConfiguration configuration,
        IPassportQrClient qrClient,
        ICommitShaProvider commitShaProvider,
        DateTime executedAtUtc,
        string? automatedTestReference = null,
        string? repositoryRootOverride = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(qrClient);
        ArgumentNullException.ThrowIfNull(commitShaProvider);

        var customerId = configuration[HarnessTargetConfig.EnvCustomerId];
        var keyId      = configuration[HarnessTargetConfig.EnvQrKeyId];

        if (string.IsNullOrWhiteSpace(customerId) || string.IsNullOrWhiteSpace(keyId))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "PASSPORT_TEST_CUSTOMER_ID/PASSPORT_TEST_QR_KEY_ID ausente(s) al momento de intentar Decode QR Dynamic.");
        }

        var repositoryRoot = repositoryRootOverride ?? Directory.GetCurrentDirectory();

        // Paso 2/3/4 — resolver y validar el artefacto privado de M4-T4
        // (SOLO LECTURA — este executor nunca escribe en el directorio de
        // M4-T4).
        var m4T4CaptureDirRaw = configuration[M4T4PrivateCapture.EnvPrivateCaptureDir];
        var m4T4Validation = M4T4PrivateCapture.Validate(m4T4CaptureDirRaw, repositoryRoot);
        if (m4T4Validation != M4T4PrivateCapture.ValidationOutcome.Valid)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"Destino privado M4-T4 ({M4T4PrivateCapture.EnvPrivateCaptureDir}) inválido: {m4T4Validation}. " +
                "No se realizó ninguna llamada a Passport.");
        }

        var m4T4CaptureDirFullPath = Path.GetFullPath(m4T4CaptureDirRaw!, repositoryRoot);
        var (resolutionOutcome, artifact) = M4T4ArtifactReader.Resolve(m4T4CaptureDirFullPath);
        if (resolutionOutcome != M4T4ArtifactReader.ResolutionOutcome.Found || artifact is null)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"No se pudo resolver el artefacto privado M4-T4: {resolutionOutcome}. " +
                "No se realizó ninguna llamada a Passport.");
        }

        var expectedKeyIdFingerprint      = Fingerprint.Compute(keyId);
        var expectedCustomerIdFingerprint = Fingerprint.Compute(customerId);
        if (!M4T4ArtifactReader.IdentityMatches(artifact, expectedKeyIdFingerprint, expectedCustomerIdFingerprint))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "El artefacto privado M4-T4 no coincide con la identidad esperada " +
                "(case_id/status/type/fingerprints) — bloqueado por seguridad. " +
                "No se realizó ninguna llamada a Passport.");
        }

        if (string.IsNullOrWhiteSpace(artifact.QrCodeData))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "El artefacto privado M4-T4 no contiene qr_code_data. No se realizó ninguna llamada a Passport.");
        }

        // Paso 5 — destino privado PROPIO de M4-T5, FAIL-CLOSED (mismo
        // criterio que XPAY-508 para M4-T4).
        var m4T5CaptureDirRaw = configuration[M4T5PrivateCapture.EnvPrivateCaptureDir];
        var m4T5Validation = M4T5PrivateCapture.Validate(m4T5CaptureDirRaw, repositoryRoot);
        if (m4T5Validation != M4T5PrivateCapture.ValidationOutcome.Valid)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"Destino privado M4-T5 ({M4T5PrivateCapture.EnvPrivateCaptureDir}) inválido: {m4T5Validation}. " +
                "No se realizó ninguna llamada a Passport.");
        }

        string m4T5CaptureDirFullPath;
        try
        {
            m4T5CaptureDirFullPath = Path.GetFullPath(m4T5CaptureDirRaw!, repositoryRoot);
            M4T5PrivateCapture.EnsureDirectory(m4T5CaptureDirFullPath);
        }
        catch (Exception ex)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"No se pudo preparar el destino privado de captura M4-T5: {ex.Message}");
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

        // customer_id + qr_code_data ÚNICAMENTE (contrato confirmado,
        // PassportQrClient.ValidateDecodeRequest) — qr_code_data leído
        // EXCLUSIVAMENTE en memoria desde el artefacto ya resuelto arriba,
        // nunca impreso, nunca copiado a ningún archivo versionado.
        var request = new PassportDecodeQrCodeRequest(
            CustomerId: customerId,
            QrCodeData: artifact.QrCodeData);

        try
        {
            // Exactamente UNA llamada — DecodeQrCodeWithRawCaptureAsync
            // (XPAY-517), nunca DecodeQrCodeAsync, nunca CreateQrCodeAsync,
            // nunca ningún método de IPassportKeyClient.
            var (response, rawJson) = await qrClient
                .DecodeQrCodeWithRawCaptureAsync(request)
                .ConfigureAwait(false);

            if (response is null)
                throw new PassportProtocolException("Respuesta de Passport vacía o ilegible (Decode QR Dynamic).");

            var privateArtifactCaptured = TryWritePrivateArtifact(
                m4T5CaptureDirFullPath, commitSha, executedAtUtc, customerId, rawJson);

            var evidence = DecodeQrDynamicEvidenceBuilder.BuildSuccess(
                request, response, artifact.QrCodeReferenceReceived,
                httpStatus: null, commitSha, executedAtUtc, automatedTestReference,
                privateArtifactCaptured);
            return new(KeyOperationOutcome.Success, evidence, null);
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            var (httpStatus, safeErrorCode, safeErrorMessage) = ex is PassportTransportException pte
                ? (pte.StatusCode, pte.SafeErrorCode, pte.SafeErrorMessage)
                : (null, null, null);

            var evidence = DecodeQrDynamicEvidenceBuilder.BuildPassportHttpFailure(
                ex.Message, httpStatus, commitSha, executedAtUtc, safeErrorCode, safeErrorMessage);
            return new(KeyOperationOutcome.PassportFailure, evidence, ex.Message);
        }
    }

    // Best-effort DELIBERADO (mismo criterio que XPAY-508
    // TryWritePrivateArtifact): Passport ya respondió — un fallo aquí
    // (disco lleno, condición de carrera) NUNCA debe hacer perder el
    // registro sanitizado ya obtenido; sólo se refleja como
    // private_artifact_captured=false.
    private static bool TryWritePrivateArtifact(
        string m4T5CaptureDirFullPath, string commitSha, DateTime executedAtUtc,
        string customerId, string? rawJson)
    {
        try
        {
            object? rawResponse = null;
            if (rawJson is not null)
            {
                try
                {
                    using var doc = JsonDocument.Parse(rawJson);
                    rawResponse = doc.RootElement.Clone();
                }
                catch (JsonException)
                {
                    rawResponse = rawJson; // fallback defensivo — nunca se pierde el material por un parseo fallido.
                }
            }

            var content = new Dictionary<string, object?>
            {
                ["case_id"]                = "M4-T5",
                ["captured_at_utc"]         = executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
                ["backend_commit_sha"]      = commitSha,
                ["customer_id_fingerprint"] = Fingerprint.Compute(customerId),
                ["decode_source"]           = DecodeQrDynamicEvidenceBuilder.DecodeSourceM4T4PrivateArtifact,
                ["raw_response"]            = rawResponse,
            };

            M4T5PrivateCapture.Write(m4T5CaptureDirFullPath, "M4-T5", ToTimestampForPath(executedAtUtc), content);
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
