using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-506 — prueba, a nivel de CreateQrDynamicExecutor (sin red), que M4-T4
// invoca ÚNICAMENTE IPassportQrClient.CreateQrCodeAsync exactamente una vez
// (nunca DecodeQrCodeAsync ni ningún método de IPassportKeyClient — esta
// clase ni siquiera recibe una instancia de ese cliente), que el request
// construido es type=DYNAMIC con vat SIEMPRE presente (contrato) y amount
// deliberadamente ausente, y que ningún identificador crudo
// (key_id/customer_id/qr_code_data/qr_code_image) sale jamás hacia la
// evidencia. Mirror estructural de CreateQrStaticExecutorTests.cs (M4-T1).
public class CreateQrDynamicExecutorTests
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
                Id = "synthetic-dynamic-qr-id",
                Status = "ACTIVE",
                Type = "DYNAMIC",
                QrCodeData = "00020101...synthetic-emv-payload-dynamic...6304ABCD",
                QrCodeImage = "data:image/png;base64,synthetic-dynamic-qr-image-payload",
                KeyId = "synthetic-key-id-echoed-back",
                CustomerId = "synthetic-customer-id-echoed-back",
            });
        }

        public Task<PassportDecodeQrCodeResponse> DecodeQrCodeAsync(
            PassportDecodeQrCodeRequest request, CancellationToken cancellationToken = default)
        {
            DecodeQrCodeCallCount++;
            throw new InvalidOperationException("DecodeQrCodeAsync NUNCA debe invocarse desde create-qr-dynamic.");
        }
    }

    private const string SyntheticKeyId      = "synthetic-executor-key-id-001";
    private const string SyntheticCustomerId = "synthetic-executor-customer-id-001";

    // XPAY-506 — M4-T4 usa EXCLUSIVAMENTE EnvQrKeyId/EnvCustomerId (mismos
    // targets que M4-T1). `newKeyId`/`qrSuspendedKeyId`/`qrDeletedKeyId`
    // (opcionales) permiten a los tests de regresión de "no fallback"
    // simular que las demás variables de llave SÍ están presentes sin que
    // eso alcance para satisfacer el target de M4-T4.
    private static IConfiguration ConfigWithTarget(
        string? keyId = SyntheticKeyId, string? customerId = SyntheticCustomerId,
        string? newKeyId = null, string? newKeyValue = null,
        string? qrSuspendedKeyId = null, string? qrDeletedKeyId = null)
    {
        var dict = new Dictionary<string, string?>();
        if (keyId is not null) dict[HarnessTargetConfig.EnvQrKeyId] = keyId;
        if (customerId is not null) dict[HarnessTargetConfig.EnvCustomerId] = customerId;
        if (newKeyId is not null) dict[HarnessTargetConfig.EnvNewKeyId] = newKeyId;
        if (newKeyValue is not null) dict[HarnessTargetConfig.EnvNewKeyValue] = newKeyValue;
        if (qrSuspendedKeyId is not null) dict[HarnessTargetConfig.EnvQrSuspendedKeyId] = qrSuspendedKeyId;
        if (qrDeletedKeyId is not null) dict[HarnessTargetConfig.EnvQrDeletedKeyId] = qrDeletedKeyId;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    // Contract-shape test: exactamente 1 llamada lógica, type=DYNAMIC,
    // amount AUSENTE (decisión de payload mínimo), additional_info AUSENTE,
    // vat SIEMPRE presente (contrato: "vat es requerido cuando type es
    // DYNAMIC", PassportQrClient.Validate), inc/tip presentes (mismo shape
    // ya confirmado para M4-T1), channel=MPOS, qr_code_reference válido.
    [Fact]
    public async Task ExecuteAsync_Success_CallsOnlyCreateQrCodeAsync_ExactlyOnce_FullContractShape()
    {
        var client = new FakeQrClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");
        var config = ConfigWithTarget();

        var result = await CreateQrDynamicExecutor.ExecuteAsync(
            config, client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(1, client.CreateQrCodeCallCount);
        Assert.Equal(0, client.DecodeQrCodeCallCount);

        var sent = client.LastRequest!;
        Assert.Equal(SyntheticKeyId, sent.KeyId);
        Assert.Equal(SyntheticCustomerId, sent.CustomerId);
        Assert.Equal(PassportQrType.DYNAMIC, sent.Type);
        Assert.Equal(PassportQrChannel.MPOS, sent.Channel);

        Assert.Null(sent.Amount);          // amount deliberadamente ausente (§5 del ticket).
        Assert.Null(sent.AdditionalInfo);  // opcional en ambos tipos, ausente aquí.

        // vat: OBLIGATORIO para DYNAMIC — a diferencia de STATIC.
        Assert.NotNull(sent.Vat);
        Assert.Equal(PassportQrVatType.FIXED, sent.Vat.VatType);
        Assert.Equal("100.00", sent.Vat.VatValue);
        Assert.Equal("100.00", sent.Vat.VatBaseValue);

        Assert.NotNull(sent.Inc);
        Assert.Equal(PassportQrVatType.FIXED, sent.Inc.IncType);
        Assert.Equal("10.00", sent.Inc.IncValue);

        Assert.NotNull(sent.Tip);
        Assert.Equal(PassportQrVatType.FIXED, sent.Tip.TipType);
        Assert.Equal("100.00", sent.Tip.TipValue);

        Assert.NotNull(sent.QrCodeReference);
        Assert.True(sent.QrCodeReference!.Length <= 17);
        Assert.True(sent.QrCodeReference.All(char.IsLetterOrDigit));
        Assert.DoesNotContain('P', sent.QrCodeReference);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.NotNull(result.Evidence);
    }

    // Regresión explícita: DYNAMIC, nunca STATIC — protege contra copiar
    // por error el tipo de M4-T1.
    [Fact]
    public async Task ExecuteAsync_Success_UsesDynamicType_NeverStatic()
    {
        var client = new FakeQrClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        await CreateQrDynamicExecutor.ExecuteAsync(
            ConfigWithTarget(), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(PassportQrType.DYNAMIC, client.LastRequest!.Type);
        Assert.NotEqual(PassportQrType.STATIC, client.LastRequest.Type);
    }

    [Fact]
    public async Task ExecuteAsync_Success_GeneratesDifferentQrCodeReferenceEachExecution()
    {
        var client1 = new FakeQrClient();
        var client2 = new FakeQrClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        await CreateQrDynamicExecutor.ExecuteAsync(
            ConfigWithTarget(), client1, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await CreateQrDynamicExecutor.ExecuteAsync(
            ConfigWithTarget(), client2, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.NotEqual(client1.LastRequest!.QrCodeReference, client2.LastRequest!.QrCodeReference);
    }

    // Regresión CRÍTICA: incluso si TODAS las demás variables de llave
    // (M3 contaminada, suspendida M4-T3-A, eliminada M4-T3-B) están
    // presentes en el entorno, este executor NUNCA las usa.
    [Fact]
    public async Task ExecuteAsync_Success_NeverUsesAnyOtherKeyVariable_EvenWhenAllPresent()
    {
        var client = new FakeQrClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        await CreateQrDynamicExecutor.ExecuteAsync(
            ConfigWithTarget(
                newKeyId: "synthetic-contaminated-m3-key-id",
                newKeyValue: "0099999999",
                qrSuspendedKeyId: "synthetic-suspended-fixture-key-id",
                qrDeletedKeyId: "synthetic-deleted-fixture-key-id"),
            client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(SyntheticKeyId, client.LastRequest!.KeyId);
    }

    [Fact]
    public async Task ExecuteAsync_Success_EvidenceHasCorrectCaseId()
    {
        var client = new FakeQrClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        var result = await CreateQrDynamicExecutor.ExecuteAsync(
            ConfigWithTarget(), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal("M4-T4", result.Evidence!.CaseId);
    }

    [Fact]
    public async Task ExecuteAsync_Success_EvidenceHasCorrectOperation()
    {
        var client = new FakeQrClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        var result = await CreateQrDynamicExecutor.ExecuteAsync(
            ConfigWithTarget(), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal("POST /v1/qrcodes", result.Evidence!.Operation);
    }

    [Fact]
    public async Task ExecuteAsync_Success_EvidenceNotesIsNull()
    {
        var client = new FakeQrClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        var result = await CreateQrDynamicExecutor.ExecuteAsync(
            ConfigWithTarget(), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Null(result.Evidence!.Notes);
    }

    // Evidencia nunca contiene identificadores crudos — sólo fingerprints/
    // presencia. También confirma amount_present=false / vat_present=true,
    // auto-verificables sin inspeccionar código.
    [Fact]
    public async Task ExecuteAsync_Success_EvidenceNeverContainsRawIdentifiers_RecordsExpectedPresenceFlags()
    {
        const string realKeyId       = "REAL-KEY-ID-must-never-appear-in-evidence-0000001";
        const string realCustomerId  = "REAL-CUSTOMER-ID-must-never-appear-in-evidence-0000002";
        const string realQrCodeData  = "00020101REAL-EMV-PAYLOAD-must-never-appear-0000003";
        const string realQrCodeImage = "data:image/png;base64,REAL-IMAGE-PAYLOAD-must-never-appear-0000004";

        var client = new FakeQrClient(response: new PassportQrCodeResponse
        {
            Id = "synthetic-dynamic-qr-id-002",
            Status = "ACTIVE",
            Type = "DYNAMIC",
            QrCodeData = realQrCodeData,
            QrCodeImage = realQrCodeImage,
            KeyId = realKeyId,
            CustomerId = realCustomerId,
        });
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");
        var config = ConfigWithTarget(keyId: realKeyId, customerId: realCustomerId);

        var result = await CreateQrDynamicExecutor.ExecuteAsync(
            config, client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var evidenceJson = System.Text.Json.JsonSerializer.Serialize(result.Evidence);

        Assert.DoesNotContain(realKeyId, evidenceJson);
        Assert.DoesNotContain(realCustomerId, evidenceJson);
        Assert.DoesNotContain(realQrCodeData, evidenceJson);
        Assert.DoesNotContain(realQrCodeImage, evidenceJson);

        Assert.Contains("key_id_fingerprint", evidenceJson);
        Assert.Contains("customer_id_fingerprint", evidenceJson);
        Assert.Contains("qr_code_data_fingerprint", evidenceJson);
        Assert.Contains("qr_code_data_present", evidenceJson);
        Assert.Contains("qr_code_image_fingerprint", evidenceJson);
        Assert.Contains("qr_code_image_present", evidenceJson);
        Assert.Contains("qr_id_fingerprint", evidenceJson);

        using var doc = System.Text.Json.JsonDocument.Parse(evidenceJson);
        var requestSanitized = doc.RootElement.GetProperty("request_sanitized");
        Assert.False(requestSanitized.GetProperty("amount_present").GetBoolean());
        Assert.False(requestSanitized.GetProperty("additional_info_present").GetBoolean());
        Assert.True(requestSanitized.GetProperty("vat_present").GetBoolean());
        Assert.True(requestSanitized.GetProperty("inc_present").GetBoolean());
        Assert.True(requestSanitized.GetProperty("tip_present").GetBoolean());
        Assert.Equal("DYNAMIC", requestSanitized.GetProperty("type").GetString());
        // Nunca valores literales de los subcampos vat/inc/tip.
        Assert.DoesNotContain("vat_type", evidenceJson);
        Assert.DoesNotContain("vat_value", evidenceJson);
        Assert.DoesNotContain("inc_value", evidenceJson);
        Assert.DoesNotContain("tip_value", evidenceJson);
    }

    // Demuestra que la evidencia SÍ permite auditar que Passport devolvió
    // imagen/payload (requisito del Anexo: "Imagen del QR + payload") de
    // forma sanitizada — presencia + fingerprint, nunca el valor crudo.
    [Fact]
    public async Task ExecuteAsync_Success_EvidenceProvesImageAndPayloadReceived_WithoutRevealingThem()
    {
        var client = new FakeQrClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        var result = await CreateQrDynamicExecutor.ExecuteAsync(
            ConfigWithTarget(), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.True((bool)result.Evidence!.ResponseSanitized["qr_code_data_present"]!);
        Assert.True((bool)result.Evidence.ResponseSanitized["qr_code_image_present"]!);
        Assert.NotEqual("ABSENT", result.Evidence.ResponseSanitized["qr_code_data_fingerprint"]);
        Assert.NotEqual("ABSENT", result.Evidence.ResponseSanitized["qr_code_image_fingerprint"]);
    }

    [Fact]
    public async Task ExecuteAsync_PassportRejects_CallsOnlyCreateQrCodeAsync_ExactlyOnce_ProducesFailEvidence()
    {
        var client = new FakeQrClient(throwOnCreate: true);
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        var result = await CreateQrDynamicExecutor.ExecuteAsync(
            ConfigWithTarget(), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(1, client.CreateQrCodeCallCount);
        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        Assert.Equal("M4-T4", result.Evidence!.CaseId);
        Assert.Equal(EvidenceRecord.ResultFail, result.Evidence.Result);
    }

    [Fact]
    public async Task ExecuteAsync_MissingQrKeyId_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeQrClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        var result = await CreateQrDynamicExecutor.ExecuteAsync(
            ConfigWithTarget(keyId: null), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateQrCodeCallCount);
        Assert.Null(result.Evidence);
    }

    // Regresión CRÍTICA de seguridad: ninguna otra variable de llave sirve
    // de fallback para M4-T4, ni siquiera si está presente.
    [Theory]
    [InlineData("newKeyId")]
    [InlineData("qrSuspendedKeyId")]
    [InlineData("qrDeletedKeyId")]
    public async Task ExecuteAsync_OtherKeyPresentButQrKeyIdMissing_IsLocalBlocked_NeverCallsClient_NoFallback(string which)
    {
        var client = new FakeQrClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        var config = which switch
        {
            "newKeyId" => ConfigWithTarget(keyId: null, newKeyId: "synthetic-contaminated-m3-key-id"),
            "qrSuspendedKeyId" => ConfigWithTarget(keyId: null, qrSuspendedKeyId: "synthetic-suspended-fixture-key-id"),
            _ => ConfigWithTarget(keyId: null, qrDeletedKeyId: "synthetic-deleted-fixture-key-id"),
        };

        var result = await CreateQrDynamicExecutor.ExecuteAsync(
            config, client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateQrCodeCallCount);
        Assert.Null(result.Evidence);
    }

    [Fact]
    public async Task ExecuteAsync_MissingCustomerId_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeQrClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        var result = await CreateQrDynamicExecutor.ExecuteAsync(
            ConfigWithTarget(customerId: null), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateQrCodeCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_CommitShaUnresolvable_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeQrClient();
        var throwingCommitShaProvider = new ThrowingCommitShaProvider();

        var result = await CreateQrDynamicExecutor.ExecuteAsync(
            ConfigWithTarget(), client, throwingCommitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateQrCodeCallCount);
    }

    private sealed class ThrowingCommitShaProvider : ICommitShaProvider
    {
        public string GetCommitSha() => throw new InvalidOperationException("synthetic: commit SHA no resoluble.");
    }
}
