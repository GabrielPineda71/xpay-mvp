using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-351 — prueba, a nivel de CreateQrStaticExecutor (sin red), que M4-T1
// invoca ÚNICAMENTE IPassportQrClient.CreateQrCodeAsync exactamente una vez
// (nunca DecodeQrCodeAsync ni ningún método de IPassportKeyClient), que el
// request construido es type=STATIC sin amount, y que ningún identificador
// crudo (key_id/customer_id/qr_code_data/qr_code_image) sale jamás hacia la
// evidencia.
public class CreateQrStaticExecutorTests
{
    private sealed class FakeQrClient : IPassportQrClient
    {
        private readonly bool _throwOnCreate;
        private readonly PassportTransportException? _exceptionToThrow;
        private readonly PassportQrCodeResponse? _response;
        public int CreateQrCodeCallCount { get; private set; }
        public int DecodeQrCodeCallCount { get; private set; }
        public PassportCreateQrCodeRequest? LastRequest { get; private set; }

        public FakeQrClient(
            bool throwOnCreate = false,
            PassportQrCodeResponse? response = null,
            PassportTransportException? exceptionToThrow = null)
        {
            _throwOnCreate = throwOnCreate;
            _response = response;
            _exceptionToThrow = exceptionToThrow;
        }

        public Task<PassportQrCodeResponse> CreateQrCodeAsync(
            PassportCreateQrCodeRequest request, CancellationToken cancellationToken = default)
        {
            CreateQrCodeCallCount++;
            LastRequest = request;
            if (_exceptionToThrow is not null)
                throw _exceptionToThrow;
            if (_throwOnCreate)
                throw new PassportTransportException("Passport respondió con error HTTP 400 (sintético).");
            return Task.FromResult(_response ?? new PassportQrCodeResponse
            {
                Id = "synthetic-qr-id",
                Status = "ACTIVE",
                Type = "STATIC",
                QrCodeData = "00020101...synthetic-emv-payload...6304ABCD",
                QrCodeImage = "data:image/png;base64,synthetic-qr-image-payload",
                KeyId = "synthetic-key-id-echoed-back",
                CustomerId = "synthetic-customer-id-echoed-back",
            });
        }

        public Task<PassportDecodeQrCodeResponse> DecodeQrCodeAsync(
            PassportDecodeQrCodeRequest request, CancellationToken cancellationToken = default)
        {
            DecodeQrCodeCallCount++;
            throw new InvalidOperationException("DecodeQrCodeAsync NUNCA debe invocarse desde create-qr-static.");
        }
    }

    private const string SyntheticKeyId      = "synthetic-executor-key-id-001";
    private const string SyntheticCustomerId = "synthetic-executor-customer-id-001";

    // XPAY-460 — M4-T1 usa EXCLUSIVAMENTE EnvQrKeyId (nunca EnvNewKeyId, la
    // llave DELETED de M3). `newKeyId` (opcional) permite a los tests de
    // regresión de "no fallback" simular que PASSPORT_TEST_NEW_KEY_ID SÍ
    // está presente en el entorno sin que eso alcance para satisfacer el
    // target de M4.
    private static IConfiguration ConfigWithTarget(
        string? keyId = SyntheticKeyId, string? customerId = SyntheticCustomerId, string? newKeyId = null)
    {
        var dict = new Dictionary<string, string?>();
        if (keyId is not null) dict[HarnessTargetConfig.EnvQrKeyId] = keyId;
        if (customerId is not null) dict[HarnessTargetConfig.EnvCustomerId] = customerId;
        if (newKeyId is not null) dict[HarnessTargetConfig.EnvNewKeyId] = newKeyId;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    // F/G/H/I/Q — exactamente 1 llamada lógica, type=STATIC, amount ausente,
    // usa IPassportQrClient (nunca IPassportKeyClient), sin reintentos.
    //
    // XPAY-356 §9 / XPAY-357 §11 / XPAY-360 §11 / XPAY-458 — CONTRACT SHAPE
    // TEST: fortalecido para verificar SIMULTÁNEAMENTE la forma completa del
    // request tras cada corrección de contrato (channel APP→POS en XPAY-356,
    // luego POS→MPOS en XPAY-458; vat AUSENTE→PRESENTE en XPAY-360; inc/tip/
    // qr_code_reference agregados y additional_info eliminado en XPAY-458 —
    // contrato confirmado por Passport, Gustavo, 2026-09-21), protegiendo
    // contra una regresión silenciosa de CUALQUIER campo del contrato.
    [Fact]
    public async Task ExecuteAsync_Success_CallsOnlyCreateQrCodeAsync_ExactlyOnce_FullContractShape()
    {
        var client = new FakeQrClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");
        var config = ConfigWithTarget();

        var result = await CreateQrStaticExecutor.ExecuteAsync(
            config, client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(1, client.CreateQrCodeCallCount);
        Assert.Equal(0, client.DecodeQrCodeCallCount);

        var sent = client.LastRequest!;
        Assert.Equal(SyntheticKeyId, sent.KeyId);
        Assert.Equal(SyntheticCustomerId, sent.CustomerId);
        Assert.Equal(PassportQrType.STATIC, sent.Type);

        // XPAY-458 — corrección central: channel=MPOS (nunca POS/APP),
        // contrato confirmado por Passport específicamente para M4-T1.
        Assert.Equal(PassportQrChannel.MPOS, sent.Channel);

        Assert.Null(sent.Amount);                 // amount ausente.
        // XPAY-458 — additional_info ausente: el contrato confirmado por
        // Passport para M4-T1 no lo incluye en absoluto.
        Assert.Null(sent.AdditionalInfo);

        // XPAY-360 — vat RESTAURADO (confirmado requerido por Passport
        // Sandbox real en XPAY-359). XPAY-460 — valores actualizados a los
        // del fixture de certificación confirmado por Gustavo
        // (FIXED/"100.00"/"100.00") — Passport aclaró que son informativos y
        // no afectan el valor total; ver CreateQrStaticExecutor para el
        // razonamiento completo.
        Assert.NotNull(sent.Vat);
        Assert.Equal(PassportQrVatType.FIXED, sent.Vat.VatType);
        Assert.Equal("100.00", sent.Vat.VatValue);
        Assert.Equal("100.00", sent.Vat.VatBaseValue);

        // XPAY-458 — inc AGREGADO (confirmado requerido por Passport Sandbox
        // real en XPAY-452: HTTP 400 "Field 'inc' is required"). XPAY-460 —
        // valor actualizado a FIXED/"10.00" (fixture de certificación).
        Assert.NotNull(sent.Inc);
        Assert.Equal(PassportQrVatType.FIXED, sent.Inc.IncType);
        Assert.Equal("10.00", sent.Inc.IncValue);

        // XPAY-458 — tip AGREGADO (nuevo, sin evidencia empírica previa de
        // rechazo — incluido porque el ejemplo funcional de Gustavo lo trae).
        // XPAY-460 — valor actualizado a FIXED/"100.00" (fixture de certificación).
        Assert.NotNull(sent.Tip);
        Assert.Equal(PassportQrVatType.FIXED, sent.Tip.TipType);
        Assert.Equal("100.00", sent.Tip.TipValue);

        // XPAY-458 — qr_code_reference AGREGADO: NUEVA y única por
        // ejecución, nunca null, respeta el contrato ya validado por
        // PassportQrClient.Validate (≤17 alfanumérico, sin 'P' mayúscula).
        Assert.NotNull(sent.QrCodeReference);
        Assert.True(sent.QrCodeReference!.Length <= 17);
        Assert.True(sent.QrCodeReference.All(char.IsLetterOrDigit));
        Assert.DoesNotContain('P', sent.QrCodeReference);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.NotNull(result.Evidence);
    }

    // XPAY-458 — regresión explícita: dos ejecuciones sucesivas deben
    // generar qr_code_reference DISTINTOS (nunca reutilizar la referencia de
    // un intento anterior).
    [Fact]
    public async Task ExecuteAsync_Success_GeneratesDifferentQrCodeReferenceEachExecution()
    {
        var client1 = new FakeQrClient();
        var client2 = new FakeQrClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        await CreateQrStaticExecutor.ExecuteAsync(
            ConfigWithTarget(), client1, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await CreateQrStaticExecutor.ExecuteAsync(
            ConfigWithTarget(), client2, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.NotEqual(client1.LastRequest!.QrCodeReference, client2.LastRequest!.QrCodeReference);
    }

    // XPAY-360 §5/§11 / XPAY-460 — regresión explícita: CreateQrStaticExecutor
    // debe construir vat=PRESENTE con EXACTAMENTE FIXED/"100.00"/"100.00"
    // (fixture de certificación confirmado por Gustavo — nunca volver a
    // omitirlo silenciosamente, ni derivar a otro vat_type/valor). También
    // verifica la serialización JSON efectiva, no sólo propiedades del
    // objeto — via el fake que captura el request y lo serializa con el
    // mismo JsonSerializer que usaría el transporte real.
    [Fact]
    public async Task ExecuteAsync_Success_AlwaysIncludesVatWithCertificationValues()
    {
        var client = new FakeQrClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        await CreateQrStaticExecutor.ExecuteAsync(
            ConfigWithTarget(), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var sent = client.LastRequest!;
        Assert.NotNull(sent.Vat);
        Assert.Equal(PassportQrVatType.FIXED, sent.Vat.VatType);
        Assert.Equal("100.00", sent.Vat.VatValue);
        Assert.Equal("100.00", sent.Vat.VatBaseValue);

        var json = System.Text.Json.JsonSerializer.Serialize(sent);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var vat = doc.RootElement.GetProperty("vat");
        Assert.Equal("FIXED", vat.GetProperty("vat_type").GetString());
        Assert.Equal("100.00", vat.GetProperty("vat_value").GetString());
        Assert.Equal("100.00", vat.GetProperty("vat_base_value").GetString());
    }

    // XPAY-356 §7/§8 / XPAY-458 — regresión explícita: CreateQrStaticExecutor
    // debe construir channel=MPOS (corregido de POS en XPAY-458, tras el
    // ejemplo funcional confirmado por Passport) y NUNCA APP ni POS. Test
    // dedicado, separado del contract-shape test anterior, para que una
    // futura reversión accidental de sólo este campo falle con un mensaje
    // inequívoco.
    [Fact]
    public async Task ExecuteAsync_Success_UsesMposChannel_NeverPosOrApp()
    {
        var client = new FakeQrClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        await CreateQrStaticExecutor.ExecuteAsync(
            ConfigWithTarget(), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(PassportQrChannel.MPOS, client.LastRequest!.Channel);
        Assert.NotEqual(PassportQrChannel.APP, client.LastRequest.Channel);
        Assert.NotEqual(PassportQrChannel.POS, client.LastRequest.Channel);
    }

    // J. evidence: case_id=M4-T1.
    [Fact]
    public async Task ExecuteAsync_Success_EvidenceHasCorrectCaseId()
    {
        var client = new FakeQrClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        var result = await CreateQrStaticExecutor.ExecuteAsync(
            ConfigWithTarget(), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal("M4-T1", result.Evidence!.CaseId);
    }

    // K. evidence: operation=POST /v1/qrcodes.
    [Fact]
    public async Task ExecuteAsync_Success_EvidenceHasCorrectOperation()
    {
        var client = new FakeQrClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        var result = await CreateQrStaticExecutor.ExecuteAsync(
            ConfigWithTarget(), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal("POST /v1/qrcodes", result.Evidence!.Operation);
    }

    // L/M/N/O/P — evidencia nunca contiene identificadores crudos; sólo
    // fingerprints/presencia. Se verifica serializando el EvidenceRecord
    // completo a JSON y confirmando la AUSENCIA total de cada valor crudo.
    [Fact]
    public async Task ExecuteAsync_Success_EvidenceNeverContainsRawIdentifiers()
    {
        const string realKeyId      = "REAL-KEY-ID-must-never-appear-in-evidence-0000001";
        const string realCustomerId = "REAL-CUSTOMER-ID-must-never-appear-in-evidence-0000002";
        const string realQrCodeData = "00020101REAL-EMV-PAYLOAD-must-never-appear-0000003";
        const string realQrCodeImage = "data:image/png;base64,REAL-IMAGE-PAYLOAD-must-never-appear-0000004";

        var client = new FakeQrClient(response: new PassportQrCodeResponse
        {
            Id = "synthetic-qr-id-002",
            Status = "ACTIVE",
            Type = "STATIC",
            QrCodeData = realQrCodeData,
            QrCodeImage = realQrCodeImage,
            KeyId = realKeyId,
            CustomerId = realCustomerId,
        });
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");
        var config = ConfigWithTarget(keyId: realKeyId, customerId: realCustomerId);

        var result = await CreateQrStaticExecutor.ExecuteAsync(
            config, client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var evidenceJson = System.Text.Json.JsonSerializer.Serialize(result.Evidence);

        Assert.DoesNotContain(realKeyId, evidenceJson);
        Assert.DoesNotContain(realCustomerId, evidenceJson);
        Assert.DoesNotContain(realQrCodeData, evidenceJson);
        Assert.DoesNotContain(realQrCodeImage, evidenceJson);

        // P — fingerprint/presencia suficiente: los campos saneados SÍ
        // están presentes (no simplemente omitidos).
        Assert.Contains("key_id_fingerprint", evidenceJson);
        Assert.Contains("customer_id_fingerprint", evidenceJson);
        Assert.Contains("qr_code_data_fingerprint", evidenceJson);
        Assert.Contains("qr_code_data_present", evidenceJson);
        Assert.Contains("qr_code_image_fingerprint", evidenceJson);
        Assert.Contains("qr_code_image_present", evidenceJson);
        Assert.Contains("qr_id_fingerprint", evidenceJson);
    }

    // XPAY-360 §15 / XPAY-458 — desde que vat/inc/tip son siempre enviados,
    // la evidencia futura debe registrar los tres *_present=TRUE de forma
    // inequívoca, pero SIN exponer los valores concretos de sus subcampos
    // (no son sensibles, pero CreateQrStaticEvidenceBuilder deliberadamente
    // sólo registra presencia — mismo criterio ya aplicado a amount_present).
    [Fact]
    public async Task ExecuteAsync_Success_EvidenceRecordsVatIncTipPresentTrue()
    {
        var client = new FakeQrClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        var result = await CreateQrStaticExecutor.ExecuteAsync(
            ConfigWithTarget(), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var evidenceJson = System.Text.Json.JsonSerializer.Serialize(result.Evidence);
        using var doc = System.Text.Json.JsonDocument.Parse(evidenceJson);
        var requestSanitized = doc.RootElement.GetProperty("request_sanitized");

        Assert.True(requestSanitized.GetProperty("vat_present").GetBoolean());
        Assert.True(requestSanitized.GetProperty("inc_present").GetBoolean());
        Assert.True(requestSanitized.GetProperty("tip_present").GetBoolean());
        Assert.True(requestSanitized.GetProperty("qr_code_reference_present").GetBoolean());
        Assert.DoesNotContain("vat_type", evidenceJson);
        Assert.DoesNotContain("vat_value", evidenceJson);
        Assert.DoesNotContain("vat_base_value", evidenceJson);
        Assert.DoesNotContain("inc_type", evidenceJson);
        Assert.DoesNotContain("inc_value", evidenceJson);
        Assert.DoesNotContain("tip_type", evidenceJson);
        Assert.DoesNotContain("tip_value", evidenceJson);
    }

    [Fact]
    public async Task ExecuteAsync_PassportRejects_CallsOnlyCreateQrCodeAsync_ExactlyOnce_ProducesFailEvidence()
    {
        var client = new FakeQrClient(throwOnCreate: true);
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        var result = await CreateQrStaticExecutor.ExecuteAsync(
            ConfigWithTarget(), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(1, client.CreateQrCodeCallCount);
        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        Assert.NotNull(result.Evidence);
        Assert.Equal("M4-T1", result.Evidence!.CaseId);
        Assert.Equal(EvidenceRecord.ResultFail, result.Evidence.Result);
    }

    // XPAY-358 §17 — cuando la excepción SÍ trae diagnóstico estructurado
    // (StatusCode/SafeErrorCode/SafeErrorMessage, ya sanitizado por
    // PassportErrorBodySanitizer), el executor lo reenvía tal cual hacia la
    // evidencia — sin volver a sanitizar, sin inventar campos.
    [Fact]
    public async Task ExecuteAsync_PassportRejectsWithStructuredDiagnostics_PropagatesIntoEvidence()
    {
        var diagnosticException = new PassportTransportException(
            "Passport respondió con error HTTP 400.",
            statusCode: 400,
            safeErrorCode: "INVALID_CHANNEL",
            safeErrorMessage: "The channel value is not valid for this key type.");
        var client = new FakeQrClient(exceptionToThrow: diagnosticException);
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        var result = await CreateQrStaticExecutor.ExecuteAsync(
            ConfigWithTarget(), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        var evidenceJson = System.Text.Json.JsonSerializer.Serialize(result.Evidence);
        using var doc = System.Text.Json.JsonDocument.Parse(evidenceJson);
        var responseSanitized = doc.RootElement.GetProperty("response_sanitized");

        Assert.Equal(400, responseSanitized.GetProperty("observed_http_status").GetInt32());
        Assert.Equal("INVALID_CHANNEL", responseSanitized.GetProperty("error_code").GetString());
        Assert.Equal(
            "The channel value is not valid for this key type.",
            responseSanitized.GetProperty("error_message").GetString());

        // EvidenceRecord.http_status permanece null por diseño (convención
        // repo-wide preservada — ver CreateQrStaticEvidenceBuilder).
        Assert.Null(result.Evidence!.HttpStatus);
    }

    // Regresión: cuando la excepción NO trae diagnóstico (constructor de 1
    // argumento, p. ej. timeout/fallo de conexión reclasificado), la
    // evidencia sigue produciendo response_sanitized VACÍO — sin inventar
    // valores por defecto.
    [Fact]
    public async Task ExecuteAsync_PassportRejectsWithoutStructuredDiagnostics_ProducesEmptyResponseSanitized()
    {
        var client = new FakeQrClient(throwOnCreate: true); // constructor de 1 argumento, sin diagnóstico.
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        var result = await CreateQrStaticExecutor.ExecuteAsync(
            ConfigWithTarget(), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Empty(result.Evidence!.ResponseSanitized);
    }

    [Fact]
    public async Task ExecuteAsync_MissingQrKeyId_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeQrClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        var result = await CreateQrStaticExecutor.ExecuteAsync(
            ConfigWithTarget(keyId: null), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateQrCodeCallCount);
        Assert.Null(result.Evidence);
    }

    // XPAY-460 — regresión CRÍTICA de seguridad: PASSPORT_TEST_NEW_KEY_ID (la
    // llave DELETED de M3) NUNCA debe servir de fallback para M4-T1, ni
    // siquiera si está presente en el entorno — sólo PASSPORT_TEST_QR_KEY_ID
    // cuenta. Sin esta garantía, una ejecución real podría accidentalmente
    // volver a apuntar a la llave muerta.
    [Fact]
    public async Task ExecuteAsync_NewKeyIdPresentButQrKeyIdMissing_IsLocalBlocked_NeverCallsClient_NoFallback()
    {
        var client = new FakeQrClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        var result = await CreateQrStaticExecutor.ExecuteAsync(
            ConfigWithTarget(keyId: null, newKeyId: "synthetic-deleted-m3-key-id"),
            client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateQrCodeCallCount);
        Assert.Null(result.Evidence);
    }

    [Fact]
    public async Task ExecuteAsync_MissingCustomerId_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeQrClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        var result = await CreateQrStaticExecutor.ExecuteAsync(
            ConfigWithTarget(customerId: null), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateQrCodeCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_CommitShaUnresolvable_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeQrClient();
        var throwingCommitShaProvider = new ThrowingCommitShaProvider();

        var result = await CreateQrStaticExecutor.ExecuteAsync(
            ConfigWithTarget(), client, throwingCommitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateQrCodeCallCount);
    }

    private sealed class ThrowingCommitShaProvider : ICommitShaProvider
    {
        public string GetCommitSha() => throw new InvalidOperationException("synthetic: commit SHA no resoluble.");
    }
}
