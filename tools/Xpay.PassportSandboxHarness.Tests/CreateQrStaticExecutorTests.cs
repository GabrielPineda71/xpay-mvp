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

    private static IConfiguration ConfigWithTarget(string? keyId = SyntheticKeyId, string? customerId = SyntheticCustomerId)
    {
        var dict = new Dictionary<string, string?>();
        if (keyId is not null) dict[HarnessTargetConfig.EnvNewKeyId] = keyId;
        if (customerId is not null) dict[HarnessTargetConfig.EnvCustomerId] = customerId;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    // F/G/H/I/Q — exactamente 1 llamada lógica, type=STATIC, amount ausente,
    // usa IPassportQrClient (nunca IPassportKeyClient), sin reintentos.
    //
    // XPAY-356 §9 / XPAY-357 §11 — CONTRACT SHAPE TEST: fortalecido para
    // verificar SIMULTÁNEAMENTE la forma completa del request tras las
    // correcciones de channel (APP→POS, XPAY-356) y vat (PRESENTE→AUSENTE,
    // XPAY-357), protegiendo contra una regresión silenciosa de CUALQUIER
    // campo del contrato. transaction_purpose permanece "00"
    // (TRANSACTION_PURPOSE_CHANGE_BLOCKED=YES — ver CreateQrStaticExecutor).
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

        // XPAY-356 — corrección central: channel=POS (nunca APP).
        Assert.Equal(PassportQrChannel.POS, sent.Channel);

        Assert.Null(sent.Amount);                 // amount ausente.
        Assert.Null(sent.Vat);                    // XPAY-357 — vat ausente.
        Assert.Null(sent.Inc);                    // inc ausente.
        Assert.Null(sent.QrCodeReference);         // qr_code_reference ausente.

        // XPAY-357 §8 — transaction_purpose permanece "00": PURCHASE
        // bloqueado documentalmente (única aparición local es en la
        // RESPUESTA de Decode QR Code, no en el contrato de REQUEST).
        Assert.Equal("00", sent.AdditionalInfo.TransactionPurpose);
        Assert.Equal("XPAY-M4-T1-CERT", sent.AdditionalInfo.TerminalLabel);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.NotNull(result.Evidence);
    }

    // XPAY-357 §5/§13 — regresión explícita: CreateQrStaticExecutor debe
    // construir vat=ABSENT (nunca reintroducir vat silenciosamente).
    [Fact]
    public async Task ExecuteAsync_Success_NeverIncludesVat()
    {
        var client = new FakeQrClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        await CreateQrStaticExecutor.ExecuteAsync(
            ConfigWithTarget(), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Null(client.LastRequest!.Vat);
    }

    // XPAY-356 §7/§8 — regresión explícita: CreateQrStaticExecutor debe
    // construir channel=POS y NUNCA channel=APP. Test dedicado, separado
    // del contract-shape test anterior, para que una futura reversión
    // accidental de sólo este campo falle con un mensaje inequívoco.
    [Fact]
    public async Task ExecuteAsync_Success_UsesPosChannel_NeverApp()
    {
        var client = new FakeQrClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        await CreateQrStaticExecutor.ExecuteAsync(
            ConfigWithTarget(), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(PassportQrChannel.POS, client.LastRequest!.Channel);
        Assert.NotEqual(PassportQrChannel.APP, client.LastRequest.Channel);
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

    // XPAY-357 §14 — evidencia futura debe registrar vat_present=false de
    // forma inequívoca, sin inventar vat_type/vat_value/vat_base_value.
    [Fact]
    public async Task ExecuteAsync_Success_EvidenceRecordsVatPresentFalse()
    {
        var client = new FakeQrClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        var result = await CreateQrStaticExecutor.ExecuteAsync(
            ConfigWithTarget(), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var evidenceJson = System.Text.Json.JsonSerializer.Serialize(result.Evidence);
        using var doc = System.Text.Json.JsonDocument.Parse(evidenceJson);
        var requestSanitized = doc.RootElement.GetProperty("request_sanitized");

        Assert.False(requestSanitized.GetProperty("vat_present").GetBoolean());
        Assert.DoesNotContain("vat_type", evidenceJson);
        Assert.DoesNotContain("vat_value", evidenceJson);
        Assert.DoesNotContain("vat_base_value", evidenceJson);
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
    public async Task ExecuteAsync_MissingKeyId_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeQrClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        var result = await CreateQrStaticExecutor.ExecuteAsync(
            ConfigWithTarget(keyId: null), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

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
