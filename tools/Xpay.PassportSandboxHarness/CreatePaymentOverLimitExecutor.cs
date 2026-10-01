using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-619 — ejecutor dedicado del subcaso M5-T5-C ("Exceso de límites de
// Bre-B — transacción superior a $12 millones COP", requisito verificado
// literalmente contra el Anexo en XPAY-618). Realiza, como máximo UNA vez
// en la vida de un directorio privado, un único POST /v1/payments/breb con
// un monto por encima del umbral certificable, esperando que Passport lo
// RECHACE.
//
// POR QUÉ UN EJECUTOR NUEVO Y NO CreateBrebPaymentCertificationExecutor:
// aquél lleva case_id "M5-PAYMENT-CREATE", el del caso POSITIVO cuya
// evidencia ya fue enviada a Passport. Ejecutarlo con un monto >12M
// contaminaría ese expediente con un intento negativo y escribiría su
// captura privada bajo ese mismo prefijo. Son casos distintos y no
// comparten case_id, guard ni evidencia.
//
// AISLAMIENTO: la única capacidad remota de este tipo es
// IPassportPaymentClient.CreateBrebPaymentAsync. NUNCA RetrievePaymentAsync
// (no se consulta el ciclo de vida, ni siquiera ante una aceptación
// inesperada) y NUNCA IPassportKeyClient (no resuelve, no crea, no
// suspende, no elimina, no activa, no lista llaves). La garantía es
// estructural: este tipo no recibe ni referencia IPassportKeyClient.
//
// EL MONTO NO SE LEE DEL ENTORNO. Es la constante CertificationAmountValue
// de esta clase. Decisión deliberada frente a leer
// PASSPORT_TEST_M5_PAYMENT_AMOUNT_VALUE (que vale "1.00" y pertenece al
// caso positivo): elimina por completo el riesgo de operador que XPAY-618
// §2 identificó — un "12.000.001,00" o un "12000001" mal escrito en el
// entorno no puede llegar a Passport, porque el entorno no participa. La
// validación de formato se aplica igualmente sobre la constante como
// defensa en profundidad ante una edición futura del código.
public static class CreatePaymentOverLimitExecutor
{
    // Umbral CERTIFICABLE, única cifra verificada en fuente primaria
    // (Anexo §3.5): el Anexo pide "superior a $12 millones COP".
    // DELIBERADAMENTE no se hardcodea el 11,5M de la documentación de API:
    // XPAY-618 no pudo re-verificarlo en ninguna fuente local y no es el
    // criterio del caso.
    public const decimal MinCertificationThresholdCop = 12000000.00m;

    // Monto EXACTO del caso de certificación: mínimo exceso posible sobre
    // el umbral del Anexo.
    public const string CertificationAmountValue = "12000001.00";

    public const string GuardCaseId = "M5-T5-OVER-LIMIT-CONSUMED";
    private const string GuardTimestampToken = "MARKER";
    private const string OutcomeCaseId = "M5-T5-OVER-LIMIT-OUTCOME";

    // Margen mínimo de vigencia exigido a la resolución ANTES de HTTP.
    // Más estricto que "expires_at > now": evita que la resolución venza
    // en vuelo. Sobre un TTL de 30 min (medido empíricamente en XPAY-618)
    // es un coste despreciable.
    public const int MinimumRemainingResolutionSeconds = 60;

    public enum AmountValidation
    {
        Valid,
        Missing,
        MalformedFormat,
        NotAboveThreshold,
        UnsupportedCurrency,
    }

    // Validación PURA del monto — sin I/O. El parseo decimal se usa
    // EXCLUSIVAMENTE para comparar contra el umbral; el string que viaja a
    // Passport es siempre el original, nunca un valor reformateado.
    public static AmountValidation ValidateAmount(string? value, string? currency)
    {
        if (string.IsNullOrWhiteSpace(value))
            return AmountValidation.Missing;

        if (!string.Equals(currency, PassportPaymentClient.CopCurrency, StringComparison.Ordinal))
            return AmountValidation.UnsupportedCurrency;

        // Formato exacto: dígitos, punto, EXACTAMENTE dos decimales.
        // Rechaza "12000001", "12000001.0", "12000001.000",
        // "12.000.001,00", "12000001,00", " 12000001.00 ", "+12000001.00".
        if (!System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d+\.\d{2}$"))
            return AmountValidation.MalformedFormat;

        if (!decimal.TryParse(
                value, System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return AmountValidation.MalformedFormat;
        }

        // ESTRICTAMENTE mayor: 12000000.00 exacto NO satisface "superior a".
        return parsed > MinCertificationThresholdCop
            ? AmountValidation.Valid
            : AmountValidation.NotAboveThreshold;
    }

    public enum ResolutionValidation
    {
        Valid,
        DirectoryMissing,
        ArtifactMissing,
        ArtifactAmbiguous,
        ArtifactUnreadable,
        ArtifactInvalid,
        AlreadyConsumed,
        ExpiresAtUnparseable,
        Expired,
        KeyCorrelationMismatch,
    }

    public static async Task<KeyOperationResult> ExecuteAsync(
        IConfiguration configuration,
        IPassportPaymentClient paymentClient,
        ICommitShaProvider commitShaProvider,
        DateTime executedAtUtc,
        string? automatedTestReference = null,
        string? repositoryRootOverride = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(paymentClient);
        ArgumentNullException.ThrowIfNull(commitShaProvider);

        var repositoryRoot = repositoryRootOverride ?? Directory.GetCurrentDirectory();

        // Guard #1 — destino privado compartido, FAIL-CLOSED antes de HTTP.
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

        // Guard #2 — repeat guard PERMANENTE propio de M5-T5-C. Su sola
        // existencia bloquea, sin importar el resultado del intento previo
        // (rechazo, aceptación o error inesperado). Nunca se comparte con
        // missing-amount / missing-account / invalid-resolution /
        // suspended-key / deleted-key.
        if (File.Exists(GuardMarkerPath(privateCaptureDirFullPath)))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "LOCAL_BLOCKED_ALREADY_EXECUTED: M5-T5-OVER-LIMIT ya fue intentado anteriormente " +
                "(marcador de consumo presente) — no se realizó ninguna llamada a Passport.");
        }

        // Guard #3 — monto. Se valida la constante de esta clase, nunca un
        // valor de entorno.
        var amountValidation = ValidateAmount(CertificationAmountValue, PassportPaymentClient.CopCurrency);
        if (amountValidation != AmountValidation.Valid)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"LOCAL_BLOCKED_INVALID_AMOUNT: el monto de certificación no supera la validación local " +
                $"({amountValidation}). No se realizó ninguna llamada a Passport.");
        }

        // Guard #4 — cuenta operativa de XPAY (JAMÁS una cuenta de usuario).
        var accountId = configuration[PassportOptions.EnvOperationalAccountId];
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"{PassportOptions.EnvOperationalAccountId} ausente al momento de intentar Create Payment (M5-T5-C). " +
                "No se realizó ninguna llamada a Passport.");
        }

        // Guard #5 — resolución: presente, única, válida, NO consumida,
        // con expires_at parseable y vigente con margen, y correlacionada
        // con la llave positiva de certificación.
        var (resolutionValidation, artifact) = ValidateResolution(
            privateCaptureDirFullPath, configuration, executedAtUtc);
        if (resolutionValidation != ResolutionValidation.Valid)
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                $"LOCAL_BLOCKED_INVALID_OR_EXPIRED_RESOLUTION: {resolutionValidation}. " +
                "No se realizó ninguna llamada a Passport. Este ejecutor NUNCA ejecuta Resolve por su cuenta.");
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

        var resolutionId = artifact!.ResolutionId!;
        var resolutionIdFingerprint = Fingerprint.Compute(resolutionId);
        var accountIdFingerprint = Fingerprint.Compute(accountId);

        // El repeat guard se consume ANTES del HTTP (estado ATTEMPTED).
        // Divergencia DELIBERADA respecto a T5-D/T5-E, que lo escriben tras
        // la respuesta: aquí la operación es un PAGO, y una caída del
        // proceso entre el request y la escritura del marcador dejaría el
        // guard disponible y habilitaría un segundo pago. Fail-closed:
        // preferimos un guard consumido sin respuesta (reconciliable
        // offline) a un segundo Payment.
        if (!TryWriteGuardMarker(privateCaptureDirFullPath, commitSha, executedAtUtc,
                resolutionIdFingerprint, accountIdFingerprint))
        {
            return new(KeyOperationOutcome.LocalBlocked, null,
                "No se pudo persistir el marcador de consumo de M5-T5-OVER-LIMIT antes del Payment. " +
                "No se realizó ninguna llamada a Passport.");
        }

        var request = new PassportCreatePaymentRequest(
            AccountId: accountId,
            ResolutionId: resolutionId,
            Amount: new PassportPaymentAmountRequest(
                // El string validado viaja VERBATIM — nunca reformateado.
                Value: CertificationAmountValue,
                Currency: PassportPaymentClient.CopCurrency));

        try
        {
            // Exactamente UNA llamada — sin reintentos, sin segundo HTTP,
            // sin fallback. ÚNICAMENTE CreateBrebPaymentAsync.
            var response = await paymentClient.CreateBrebPaymentAsync(request).ConfigureAwait(false);

            // Un 200 con `error` poblado es un rechazo del proveedor dentro
            // de una respuesta exitosa de transporte — se clasifica igual
            // que un rechazo HTTP, nunca como aceptación.
            if (response.Error is not null)
            {
                var signal = ClassifyLimitSignal(
                    httpStatus: null, response.Error.ErrorCode, response.Error.ErrorDescription);

                TryWriteOutcomeRecord(privateCaptureDirFullPath, executedAtUtc, resolutionIdFingerprint,
                    outcome: "PROVIDER_REJECTION_IN_BODY", httpStatus: null,
                    safeErrorCode: response.Error.ErrorCode, safeErrorMessage: response.Error.ErrorDescription,
                    paymentIdFingerprint: Fingerprint.Compute(response.Id), returnedStatus: response.Status);

                var bodyEvidence = CreatePaymentOverLimitEvidenceBuilder.BuildExpectedRejection(
                    resolutionIdFingerprint, accountIdFingerprint, httpStatus: null,
                    response.Error.ErrorCode, response.Error.ErrorDescription, signal,
                    commitSha, executedAtUtc, automatedTestReference);
                return new(KeyOperationOutcome.PassportFailure, bodyEvidence,
                    signal == true
                        ? "LIMIT_REJECTION=YES (rechazo en body)"
                        : "LIMIT_REJECTION=UNKNOWN (rechazo en body sin señal inequívoca de límite)");
            }

            // Passport ACEPTÓ el pago — resultado INESPERADO. Nunca se
            // interpreta como PASS del caso negativo, nunca se consulta el
            // ciclo de vida (este tipo no conoce RetrievePaymentAsync) y
            // nunca se intenta un segundo Payment ni una compensación.
            TryWriteOutcomeRecord(privateCaptureDirFullPath, executedAtUtc, resolutionIdFingerprint,
                outcome: "UNEXPECTED_PAYMENT_ACCEPTANCE", httpStatus: null,
                safeErrorCode: null, safeErrorMessage: null,
                paymentIdFingerprint: Fingerprint.Compute(response.Id), returnedStatus: response.Status);

            var acceptanceEvidence = CreatePaymentOverLimitEvidenceBuilder.BuildUnexpectedAcceptance(
                resolutionIdFingerprint, accountIdFingerprint, response, commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.Success, acceptanceEvidence,
                "PAYMENT_ACCEPTED_UNEXPECTEDLY=YES — T5_C_RESULT=INCONCLUSIVE. Requiere revisión inmediata del " +
                "director. NO se ejecutó Retrieve, NO se intentó un segundo Payment.");
        }
        catch (Exception ex) when (ex is PassportAuthenticationException or PassportTransportException or PassportProtocolException)
        {
            var (httpStatus, safeErrorCode, safeErrorMessage) = ex is PassportTransportException pte
                ? (pte.StatusCode, pte.SafeErrorCode, pte.SafeErrorMessage)
                : (null, null, null);

            var signal = ClassifyLimitSignal(httpStatus, safeErrorCode, safeErrorMessage);

            TryWriteOutcomeRecord(privateCaptureDirFullPath, executedAtUtc, resolutionIdFingerprint,
                outcome: signal == true ? "EXPECTED_LIMIT_REJECTION" : "REJECTION_WITHOUT_LIMIT_SIGNAL",
                httpStatus, safeErrorCode, safeErrorMessage,
                paymentIdFingerprint: null, returnedStatus: null);

            var evidence = CreatePaymentOverLimitEvidenceBuilder.BuildExpectedRejection(
                resolutionIdFingerprint, accountIdFingerprint, httpStatus, safeErrorCode, safeErrorMessage,
                signal, commitSha, executedAtUtc, automatedTestReference);
            return new(KeyOperationOutcome.PassportFailure, evidence,
                signal == true
                    ? "LIMIT_REJECTION=YES"
                    : "LIMIT_REJECTION=UNKNOWN — rechazo real pero sin señal inequívoca de límite de monto.");
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // CLASIFICADOR DE SEÑAL DE LÍMITE
    // ══════════════════════════════════════════════════════════════════
    //
    // Devuelve true (=YES) o null (=UNKNOWN). NUNCA false: la ausencia de
    // señal no prueba que el rechazo no fuera por límite.
    //
    // Calibrado contra la realidad YA OBSERVADA en este proyecto:
    //   T5-D -> safe_error_code="generic_error" + mensaje útil ("...(U805)")
    //   T5-E -> safe_error_code="generic_error" + mensaje NULL
    // Por eso "generic_error" por sí solo NUNCA basta: la señal tiene que
    // estar en el código estructurado o en el texto del mensaje.
    //
    // Los códigos U112/B107/B108/B109 provienen de XPAY-611 y NO pudieron
    // re-verificarse en fuente local (XPAY-618): se reconocen si aparecen,
    // pero NO son condición necesaria. Un mensaje inequívoco de máximo de
    // monto basta por sí mismo.
    public static bool? ClassifyLimitSignal(int? httpStatus, string? safeErrorCode, string? safeErrorMessage)
    {
        var code = (safeErrorCode ?? string.Empty).Trim();

        // Códigos estructurados históricos de exceso de monto. Se exige
        // coincidencia de token completo para no disparar con un
        // "AB107XYZ" accidental.
        foreach (var known in KnownLimitErrorCodes)
        {
            if (string.Equals(code, known, StringComparison.OrdinalIgnoreCase))
                return true;
            if (ContainsToken(safeErrorMessage, known))
                return true;
        }

        // Señal textual inequívoca de máximo/límite de monto.
        var text = $"{safeErrorCode} {safeErrorMessage}";
        if (LimitPhrasePattern.IsMatch(text))
            return true;

        // Cualquier otra cosa — incluido un 400 genérico, un
        // "generic_error" sin mensaje, o un error no relacionado con el
        // monto — es UNKNOWN, nunca PASS.
        return null;
    }

    public static readonly string[] KnownLimitErrorCodes = ["U112", "B107", "B108", "B109"];

    // El separador es FLEXIBLE ([\s_-]+): los proveedores expresan la misma
    // señal como texto ("limit exceeded") o como código estructurado
    // ("limit_exceeded", "maximum-amount"). Ambas formas son igual de
    // inequívocas y ambas deben reconocerse.
    private static readonly System.Text.RegularExpressions.Regex LimitPhrasePattern = new(
        @"(?ix)
          ( maximum [\s_-]+ amount
          | max [\s_-]+ amount
          | amount [\s_-]+ (check [\s_-]+)? (exceed|exceeds|exceeded)
          | exceed(s|ed)? [\s_-]+ (the [\s_-]+)? maximum
          | exceed(s|ed)? [\s_-]+ (the [\s_-]+)? (allowed [\s_-]+)? limit
          | amount [\s_-]+ limit
          | limit [\s_-]+ exceeded
          | monto [\s_-]+ m[áa]ximo
          | excede .{0,24} monto
          | l[íi]mite .{0,24} monto
          | monto .{0,24} l[íi]mite )",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private static bool ContainsToken(string? haystack, string token) =>
        !string.IsNullOrEmpty(haystack)
        && System.Text.RegularExpressions.Regex.IsMatch(
            haystack, $@"(?<![A-Za-z0-9]){System.Text.RegularExpressions.Regex.Escape(token)}(?![A-Za-z0-9])",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    // ══════════════════════════════════════════════════════════════════
    // VALIDACIÓN DE LA RESOLUCIÓN
    // ══════════════════════════════════════════════════════════════════
    //
    // Lee la captura privada del flujo POSITIVO de Resolve
    // (M5ResolveArtifactReader, sin modificarlo). NUNCA acepta un
    // resolution_id de entorno ni de CLI, y NUNCA ejecuta Resolve.
    public static (ResolutionValidation Validation, M5ResolveArtifactReader.Artifact? Artifact) ValidateResolution(
        string privateCaptureDirFullPath, IConfiguration configuration, DateTime nowUtc)
    {
        var (outcome, artifact) = M5ResolveArtifactReader.Resolve(privateCaptureDirFullPath);
        switch (outcome)
        {
            case M5ResolveArtifactReader.ResolutionOutcome.DirectoryMissing:
                return (ResolutionValidation.DirectoryMissing, null);
            case M5ResolveArtifactReader.ResolutionOutcome.NoArtifactFound:
                return (ResolutionValidation.ArtifactMissing, null);
            case M5ResolveArtifactReader.ResolutionOutcome.MultipleArtifactsFound:
                return (ResolutionValidation.ArtifactAmbiguous, null);
            case M5ResolveArtifactReader.ResolutionOutcome.Unreadable:
                return (ResolutionValidation.ArtifactUnreadable, null);
        }

        if (artifact is null || !M5ResolveArtifactReader.IsValid(artifact))
            return (ResolutionValidation.ArtifactInvalid, null);

        // Una resolución ya consumida fue transferida a entorno y
        // probablemente usada por otro Payment — nunca se reutiliza aquí.
        if (M5ResolveArtifactReader.HasConsumedMarker(privateCaptureDirFullPath, artifact))
            return (ResolutionValidation.AlreadyConsumed, null);

        if (!DateTime.TryParse(
                artifact.ExpiresAtRaw, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                out var expiresAtUtc))
        {
            // Fail-closed: sin vigencia verificable NO se intenta el pago.
            // Más estricto que el caso positivo (XPAY-529 §16), donde la
            // ausencia de metadata se documentaba como gap en vez de
            // bloquear — aquí el riesgo de gastar el único intento
            // autorizado en una resolución vencida no lo justifica.
            return (ResolutionValidation.ExpiresAtUnparseable, null);
        }

        if (expiresAtUtc <= nowUtc.ToUniversalTime().AddSeconds(MinimumRemainingResolutionSeconds))
            return (ResolutionValidation.Expired, null);

        // Correlación con la llave POSITIVA de certificación: la resolución
        // debe pertenecer a PASSPORT_TEST_BREB_KEY, nunca a un fixture
        // negativo (T5-D suspendido, T5-E eliminado) ni a una llave
        // histórica. Se compara por FINGERPRINT — el key_value nunca entra
        // en el request del Payment, que sólo necesita el resolution_id.
        var expectedKeyValue = configuration[HarnessTargetConfig.EnvBrebKeyValue];
        if (!string.IsNullOrEmpty(expectedKeyValue)
            && !string.IsNullOrEmpty(artifact.KeyValueFingerprint)
            && !string.Equals(artifact.KeyValueFingerprint, Fingerprint.Compute(expectedKeyValue), StringComparison.Ordinal))
        {
            return (ResolutionValidation.KeyCorrelationMismatch, null);
        }

        return (ResolutionValidation.Valid, artifact);
    }

    // ══════════════════════════════════════════════════════════════════
    // PERSISTENCIA PRIVADA
    // ══════════════════════════════════════════════════════════════════

    public static string GuardMarkerPath(string privateCaptureDirFullPath) =>
        Path.Combine(privateCaptureDirFullPath, $"{GuardCaseId}-{GuardTimestampToken}.private.json");

    private static bool TryWriteGuardMarker(
        string privateCaptureDirFullPath, string commitSha, DateTime executedAtUtc,
        string resolutionIdFingerprint, string accountIdFingerprint)
    {
        try
        {
            WritePrivateRecord(privateCaptureDirFullPath, GuardCaseId, GuardTimestampToken, new Dictionary<string, object?>
            {
                ["case_id"]                   = GuardCaseId,
                ["purpose"]                    = "M5-T5-C-OVER-LIMIT",
                ["phase"]                      = "PAYMENT",
                ["state"]                      = "ATTEMPTED",
                ["attempted_utc"]              = FormatInstant(executedAtUtc),
                ["backend_commit_sha"]         = commitSha,
                ["amount_value"]               = CertificationAmountValue,
                ["currency"]                   = PassportPaymentClient.CopCurrency,
                ["resolution_id_fingerprint"]  = resolutionIdFingerprint,
                ["account_id_fingerprint"]     = accountIdFingerprint,
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Best-effort DELIBERADO: Passport ya respondió — un fallo aquí nunca
    // debe disparar un reintento. El marcador de guard ya está escrito, así
    // que la protección anti-repetición no depende de este registro.
    // FINGERPRINT-ONLY: ni resolution_id ni payment_id crudos.
    private static void TryWriteOutcomeRecord(
        string privateCaptureDirFullPath, DateTime executedAtUtc, string resolutionIdFingerprint,
        string outcome, int? httpStatus, string? safeErrorCode, string? safeErrorMessage,
        string? paymentIdFingerprint, string? returnedStatus)
    {
        try
        {
            WritePrivateRecord(privateCaptureDirFullPath, OutcomeCaseId, ToTimestampForPath(executedAtUtc),
                new Dictionary<string, object?>
                {
                    ["case_id"]                   = OutcomeCaseId,
                    ["purpose"]                    = "M5-T5-C-OVER-LIMIT",
                    ["observed_utc"]               = FormatInstant(executedAtUtc),
                    ["outcome"]                    = outcome,
                    ["amount_value"]               = CertificationAmountValue,
                    ["observed_http_status"]       = httpStatus,
                    ["safe_error_code"]            = safeErrorCode,
                    ["safe_error_message"]         = safeErrorMessage,
                    ["returned_status"]            = returnedStatus,
                    ["resolution_id_fingerprint"]  = resolutionIdFingerprint,
                    ["payment_id_fingerprint"]     = paymentIdFingerprint,
                });
        }
        catch
        {
        }
    }

    private static void WritePrivateRecord(
        string privateCaptureDirFullPath, string caseId, string timestampToken, Dictionary<string, object?> content)
    {
        var path = M5PaymentPrivateCapture.Write(privateCaptureDirFullPath, caseId, timestampToken, content);

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
