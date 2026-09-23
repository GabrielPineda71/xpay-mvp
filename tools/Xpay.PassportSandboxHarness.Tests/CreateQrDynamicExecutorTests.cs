using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-506/508/512 — prueba, a nivel de CreateQrDynamicExecutor (sin red),
// que M4-T4 invoca ÚNICAMENTE IPassportQrClient.CreateQrCodeAsync
// exactamente una vez (nunca DecodeQrCodeAsync ni ningún método de
// IPassportKeyClient — esta clase ni siquiera recibe una instancia de ese
// cliente), que el request construido es type=DYNAMIC con vat SIEMPRE
// presente (contrato) y (desde XPAY-512) amount SIEMPRE presente con el
// valor oficial del ejemplo Postman de Passport, que ningún identificador
// crudo (key_id/customer_id/qr_code_data/qr_code_image) sale jamás hacia
// la evidencia versionable, y (XPAY-508) que la captura privada del
// material crudo es FAIL-CLOSED, opt-in explícito, y nunca escribe dentro
// del "repositorio" (simulado con una raíz sintética por test). Mirror
// estructural de CreateQrStaticExecutorTests.cs (M4-T1).
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
                QrCodeReference = "SYNTH12345",
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

    // XPAY-508 — raíz de "repositorio" SINTÉTICA (nunca el repo real):
    // Validate() sólo hace manipulación de strings, nunca toca disco, así
    // que esta ruta no necesita existir. Todas las rutas "válidas" de
    // captura privada en estos tests son directorios reales bajo el temp
    // del sistema operativo, HERMANOS de esta raíz sintética (nunca
    // anidados bajo ella) — nunca el temp/working directory real del
    // proceso de test.
    private static readonly string SyntheticRepoRoot =
        Path.Combine(Path.GetTempPath(), "xpay-m4t4-synthetic-repo-root-for-tests");

    private static string NewValidPrivateCaptureDir() =>
        Path.Combine(Path.GetTempPath(), "xpay-m4t4-private-capture-" + Guid.NewGuid().ToString("N"));

    // XPAY-506/508 — M4-T4 usa EXCLUSIVAMENTE EnvQrKeyId/EnvCustomerId
    // (mismos targets que M4-T1) + (XPAY-508) el destino privado de
    // captura, FAIL-CLOSED. `privateCaptureDir`: pasar explícitamente
    // `null` simula la variable AUSENTE; omitir el parámetro genera un
    // directorio válido fresco automáticamente (comportamiento por
    // defecto de la mayoría de los tests, que no son sobre este aspecto).
    private static IConfiguration ConfigWithTarget(
        string? keyId = SyntheticKeyId, string? customerId = SyntheticCustomerId,
        string? newKeyId = null, string? newKeyValue = null,
        string? qrSuspendedKeyId = null, string? qrDeletedKeyId = null,
        string? privateCaptureDir = "__AUTO_VALID__")
    {
        var dict = new Dictionary<string, string?>();
        if (keyId is not null) dict[HarnessTargetConfig.EnvQrKeyId] = keyId;
        if (customerId is not null) dict[HarnessTargetConfig.EnvCustomerId] = customerId;
        if (newKeyId is not null) dict[HarnessTargetConfig.EnvNewKeyId] = newKeyId;
        if (newKeyValue is not null) dict[HarnessTargetConfig.EnvNewKeyValue] = newKeyValue;
        if (qrSuspendedKeyId is not null) dict[HarnessTargetConfig.EnvQrSuspendedKeyId] = qrSuspendedKeyId;
        if (qrDeletedKeyId is not null) dict[HarnessTargetConfig.EnvQrDeletedKeyId] = qrDeletedKeyId;

        var resolvedCaptureDir = privateCaptureDir == "__AUTO_VALID__" ? NewValidPrivateCaptureDir() : privateCaptureDir;
        if (resolvedCaptureDir is not null) dict[M4T4PrivateCapture.EnvPrivateCaptureDir] = resolvedCaptureDir;

        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static readonly DateTime FixedTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Task<KeyOperationResult> Execute(
        IConfiguration config, FakeQrClient client, ICommitShaProvider? commitShaProvider = null) =>
        CreateQrDynamicExecutor.ExecuteAsync(
            config, client,
            commitShaProvider ?? new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000"),
            FixedTime, automatedTestReference: null, repositoryRootOverride: SyntheticRepoRoot);

    // ══════════════════════════════════════════════════════════════════════
    // Contrato del request (sin cambios de comportamiento respecto a
    // XPAY-506/507 — sólo ahora requieren un destino de captura privada
    // válido, ya provisto por defecto por ConfigWithTarget()).
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_Success_CallsOnlyCreateQrCodeAsync_ExactlyOnce_FullContractShape()
    {
        var client = new FakeQrClient();
        var config = ConfigWithTarget();

        var result = await Execute(config, client);

        Assert.Equal(1, client.CreateQrCodeCallCount);
        Assert.Equal(0, client.DecodeQrCodeCallCount);

        var sent = client.LastRequest!;
        Assert.Equal(SyntheticKeyId, sent.KeyId);
        Assert.Equal(SyntheticCustomerId, sent.CustomerId);
        Assert.Equal(PassportQrType.DYNAMIC, sent.Type);
        Assert.Equal(PassportQrChannel.MPOS, sent.Channel);

        // XPAY-512 — amount ahora SIEMPRE presente (valor oficial del
        // ejemplo Postman de Passport, "QR Codes / Create Dynamic QR
        // Code"); additional_info sigue deliberadamente ausente — XPAY-512
        // §3 prohíbe copiar ese campo del mismo ejemplo sin evidencia
        // propia de que M4-T4 lo necesite.
        Assert.NotNull(sent.Amount);
        Assert.Equal("10000.00", sent.Amount!.Value);
        Assert.Equal("COP", sent.Amount.Currency);
        Assert.Null(sent.AdditionalInfo);

        Assert.NotNull(sent.Vat);
        Assert.Equal(PassportQrVatType.FIXED, sent.Vat.VatType);
        Assert.Equal("100.00", sent.Vat.VatValue);
        Assert.Equal("100.00", sent.Vat.VatBaseValue);

        Assert.NotNull(sent.Inc);
        Assert.Equal("10.00", sent.Inc.IncValue);
        Assert.NotNull(sent.Tip);
        Assert.Equal("100.00", sent.Tip.TipValue);

        Assert.NotNull(sent.QrCodeReference);
        Assert.True(sent.QrCodeReference!.Length <= 17);
        Assert.True(sent.QrCodeReference.All(char.IsLetterOrDigit));
        Assert.DoesNotContain('P', sent.QrCodeReference);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.NotNull(result.Evidence);
    }

    [Fact]
    public async Task ExecuteAsync_Success_UsesDynamicType_NeverStatic()
    {
        var client = new FakeQrClient();
        await Execute(ConfigWithTarget(), client);

        Assert.Equal(PassportQrType.DYNAMIC, client.LastRequest!.Type);
        Assert.NotEqual(PassportQrType.STATIC, client.LastRequest.Type);
    }

    [Fact]
    public async Task ExecuteAsync_Success_GeneratesDifferentQrCodeReferenceEachExecution()
    {
        var client1 = new FakeQrClient();
        var client2 = new FakeQrClient();

        await Execute(ConfigWithTarget(), client1);
        await Execute(ConfigWithTarget(), client2);

        Assert.NotEqual(client1.LastRequest!.QrCodeReference, client2.LastRequest!.QrCodeReference);
    }

    [Fact]
    public async Task ExecuteAsync_Success_NeverUsesAnyOtherKeyVariable_EvenWhenAllPresent()
    {
        var client = new FakeQrClient();

        await Execute(ConfigWithTarget(
            newKeyId: "synthetic-contaminated-m3-key-id",
            newKeyValue: "0099999999",
            qrSuspendedKeyId: "synthetic-suspended-fixture-key-id",
            qrDeletedKeyId: "synthetic-deleted-fixture-key-id"), client);

        Assert.Equal(SyntheticKeyId, client.LastRequest!.KeyId);
    }

    [Fact]
    public async Task ExecuteAsync_Success_EvidenceHasCorrectCaseId()
    {
        var client = new FakeQrClient();
        var result = await Execute(ConfigWithTarget(), client);
        Assert.Equal("M4-T4", result.Evidence!.CaseId);
    }

    [Fact]
    public async Task ExecuteAsync_Success_EvidenceHasCorrectOperation()
    {
        var client = new FakeQrClient();
        var result = await Execute(ConfigWithTarget(), client);
        Assert.Equal("POST /v1/qrcodes", result.Evidence!.Operation);
    }

    [Fact]
    public async Task ExecuteAsync_Success_EvidenceNotesIsNull()
    {
        var client = new FakeQrClient();
        var result = await Execute(ConfigWithTarget(), client);
        Assert.Null(result.Evidence!.Notes);
    }

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
        var config = ConfigWithTarget(keyId: realKeyId, customerId: realCustomerId);

        var result = await Execute(config, client);

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
        // XPAY-512 — amount ahora SIEMPRE presente en DYNAMIC; sólo el
        // booleano se registra en evidencia, nunca el valor/moneda crudos.
        Assert.True(requestSanitized.GetProperty("amount_present").GetBoolean());
        Assert.False(requestSanitized.GetProperty("additional_info_present").GetBoolean());
        Assert.True(requestSanitized.GetProperty("vat_present").GetBoolean());
        Assert.True(requestSanitized.GetProperty("inc_present").GetBoolean());
        Assert.True(requestSanitized.GetProperty("tip_present").GetBoolean());
        Assert.Equal("DYNAMIC", requestSanitized.GetProperty("type").GetString());
        Assert.DoesNotContain("vat_type", evidenceJson);
        Assert.DoesNotContain("vat_value", evidenceJson);
        Assert.DoesNotContain("inc_value", evidenceJson);
        Assert.DoesNotContain("tip_value", evidenceJson);
    }

    [Fact]
    public async Task ExecuteAsync_Success_EvidenceProvesImageAndPayloadReceived_WithoutRevealingThem()
    {
        var client = new FakeQrClient();
        var result = await Execute(ConfigWithTarget(), client);

        Assert.True((bool)result.Evidence!.ResponseSanitized["qr_code_data_present"]!);
        Assert.True((bool)result.Evidence.ResponseSanitized["qr_code_image_present"]!);
        Assert.NotEqual("ABSENT", result.Evidence.ResponseSanitized["qr_code_data_fingerprint"]);
        Assert.NotEqual("ABSENT", result.Evidence.ResponseSanitized["qr_code_image_fingerprint"]);
    }

    [Fact]
    public async Task ExecuteAsync_PassportRejects_CallsOnlyCreateQrCodeAsync_ExactlyOnce_ProducesFailEvidence()
    {
        var client = new FakeQrClient(throwOnCreate: true);
        var result = await Execute(ConfigWithTarget(), client);

        Assert.Equal(1, client.CreateQrCodeCallCount);
        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        Assert.Equal("M4-T4", result.Evidence!.CaseId);
        Assert.Equal(EvidenceRecord.ResultFail, result.Evidence.Result);
    }

    [Fact]
    public async Task ExecuteAsync_MissingQrKeyId_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeQrClient();
        var result = await Execute(ConfigWithTarget(keyId: null), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateQrCodeCallCount);
        Assert.Null(result.Evidence);
    }

    [Theory]
    [InlineData("newKeyId")]
    [InlineData("qrSuspendedKeyId")]
    [InlineData("qrDeletedKeyId")]
    public async Task ExecuteAsync_OtherKeyPresentButQrKeyIdMissing_IsLocalBlocked_NeverCallsClient_NoFallback(string which)
    {
        var client = new FakeQrClient();

        var config = which switch
        {
            "newKeyId" => ConfigWithTarget(keyId: null, newKeyId: "synthetic-contaminated-m3-key-id"),
            "qrSuspendedKeyId" => ConfigWithTarget(keyId: null, qrSuspendedKeyId: "synthetic-suspended-fixture-key-id"),
            _ => ConfigWithTarget(keyId: null, qrDeletedKeyId: "synthetic-deleted-fixture-key-id"),
        };

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateQrCodeCallCount);
        Assert.Null(result.Evidence);
    }

    [Fact]
    public async Task ExecuteAsync_MissingCustomerId_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeQrClient();
        var result = await Execute(ConfigWithTarget(customerId: null), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateQrCodeCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_CommitShaUnresolvable_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeQrClient();
        var result = await Execute(ConfigWithTarget(), client, new ThrowingCommitShaProvider());

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateQrCodeCallCount);
    }

    private sealed class ThrowingCommitShaProvider : ICommitShaProvider
    {
        public string GetCommitSha() => throw new InvalidOperationException("synthetic: commit SHA no resoluble.");
    }

    // ══════════════════════════════════════════════════════════════════════
    // XPAY-508 — captura privada: opt-in obligatorio, fail-closed, nunca
    // dentro del repositorio, nunca material crudo en evidence.json.
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_PrivateCaptureDirMissing_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeQrClient();
        var result = await Execute(ConfigWithTarget(privateCaptureDir: null), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateQrCodeCallCount);
        Assert.Null(result.Evidence);
        Assert.Contains("Destino privado de captura", result.Detail);
    }

    [Fact]
    public async Task ExecuteAsync_PrivateCaptureDirInsideRepository_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeQrClient();
        var insideRepoPath = Path.Combine(SyntheticRepoRoot, "docs", "certificacion", "passport-breb", "M4-T4-private");

        var result = await Execute(ConfigWithTarget(privateCaptureDir: insideRepoPath), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateQrCodeCallCount);
        Assert.Null(result.Evidence);
        Assert.Contains("InsideRepository", result.Detail);
        // Nunca se imprime/filtra la ruta real configurada en el detalle.
        Assert.DoesNotContain(insideRepoPath, result.Detail!);
    }

    [Fact]
    public async Task ExecuteAsync_PrivateCaptureDirEqualsRepositoryRoot_IsLocalBlocked()
    {
        var client = new FakeQrClient();
        var result = await Execute(ConfigWithTarget(privateCaptureDir: SyntheticRepoRoot), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateQrCodeCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_Success_WithValidPrivateCaptureDir_WritesArtifact_ContainsExpectedRawMaterial()
    {
        var captureDir = NewValidPrivateCaptureDir();
        try
        {
            var client = new FakeQrClient();
            var result = await Execute(ConfigWithTarget(privateCaptureDir: captureDir), client);

            Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
            var files = Directory.GetFiles(captureDir, "*.private.json");
            Assert.Single(files);

            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(files[0]));
            Assert.Equal("M4-T4", doc.RootElement.GetProperty("case_id").GetString());
            Assert.Equal("ACTIVE", doc.RootElement.GetProperty("status").GetString());
            Assert.Equal("DYNAMIC", doc.RootElement.GetProperty("type").GetString());
            Assert.Contains("synthetic-emv-payload-dynamic", doc.RootElement.GetProperty("qr_code_data").GetString());
            Assert.Contains("synthetic-dynamic-qr-image-payload", doc.RootElement.GetProperty("qr_code_image").GetString());
            Assert.Equal("SYNTH12345", doc.RootElement.GetProperty("qr_code_reference_received").GetString());
            // Nunca key_id/customer_id crudos, ni siquiera en el artefacto privado.
            Assert.False(doc.RootElement.TryGetProperty("key_id", out _));
            Assert.False(doc.RootElement.TryGetProperty("customer_id", out _));
            Assert.True(doc.RootElement.TryGetProperty("key_id_fingerprint", out _));
            Assert.True(doc.RootElement.TryGetProperty("customer_id_fingerprint", out _));
        }
        finally
        {
            if (Directory.Exists(captureDir)) Directory.Delete(captureDir, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Success_WithValidPrivateCaptureDir_EvidenceRecordsCapturedTrue()
    {
        var captureDir = NewValidPrivateCaptureDir();
        try
        {
            var client = new FakeQrClient();
            var result = await Execute(ConfigWithTarget(privateCaptureDir: captureDir), client);

            Assert.Equal(true, result.Evidence!.ResponseSanitized["private_artifact_captured"]);
        }
        finally
        {
            if (Directory.Exists(captureDir)) Directory.Delete(captureDir, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Success_VersionedEvidence_NeverContainsPrivateCaptureDirPath()
    {
        var captureDir = NewValidPrivateCaptureDir();
        try
        {
            var client = new FakeQrClient();
            var result = await Execute(ConfigWithTarget(privateCaptureDir: captureDir), client);

            var evidenceJson = System.Text.Json.JsonSerializer.Serialize(result.Evidence);
            Assert.DoesNotContain(captureDir, evidenceJson);
            Assert.DoesNotContain("qr_code_data\":\"00020101", evidenceJson); // nunca el prefijo crudo del payload
        }
        finally
        {
            if (Directory.Exists(captureDir)) Directory.Delete(captureDir, recursive: true);
        }
    }

    // La captura privada configurada NO sustituye ni habilita por sí sola
    // ninguna llamada HTTP — sigue exigiendo key_id/customer_id igual que
    // antes; sin ellos, LocalBlocked de todas formas, pese a tener un
    // destino privado perfectamente válido configurado.
    [Fact]
    public async Task PrivateCaptureConfigured_DoesNotBypass_MissingKeyIdCheck()
    {
        var captureDir = NewValidPrivateCaptureDir();
        try
        {
            var client = new FakeQrClient();
            var result = await Execute(ConfigWithTarget(keyId: null, privateCaptureDir: captureDir), client);

            Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
            Assert.Equal(0, client.CreateQrCodeCallCount);
        }
        finally
        {
            if (Directory.Exists(captureDir)) Directory.Delete(captureDir, recursive: true);
        }
    }
}
