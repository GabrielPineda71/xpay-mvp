using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-517 — prueba, a nivel de DecodeQrDynamicExecutor (sin red), que
// M4-T5: (A) obtiene qr_code_data EXCLUSIVAMENTE del artefacto privado de
// M4-T4 correcto; (B/C) nunca versiona qr_code_data/qr_code_reference
// crudos; (D/E) calcula qr_code_reference_match_m4_t4 correctamente;
// (F/G/H) calcula amount_value/currency_matches_expected correctamente;
// (I/J) vat/inc presentes, tip nunca inventado; (L/M) invoca ÚNICAMENTE
// DecodeQrCodeWithRawCaptureAsync exactamente una vez (nunca CreateQrCodeAsync
// ni DecodeQrCodeAsync); (N/O) captura privada fuera del repo y fail-closed;
// (P) la captura privada preserva un campo no modelado por el DTO.
public class DecodeQrDynamicExecutorTests
{
    private sealed class FakeQrClient : IPassportQrClient
    {
        private readonly bool _throwOnDecode;
        private readonly PassportTransportException? _exceptionToThrow;
        private readonly PassportDecodeQrCodeResponse? _response;
        private readonly string? _rawResponseJson;

        public int CreateQrCodeCallCount { get; private set; }
        public int DecodeQrCodeCallCount { get; private set; }
        public int DecodeQrCodeWithRawCaptureCallCount { get; private set; }
        public PassportDecodeQrCodeRequest? LastRequest { get; private set; }

        public FakeQrClient(
            bool throwOnDecode = false,
            PassportDecodeQrCodeResponse? response = null,
            string? rawResponseJson = null,
            PassportTransportException? exceptionToThrow = null)
        {
            _throwOnDecode = throwOnDecode;
            _response = response;
            _rawResponseJson = rawResponseJson;
            _exceptionToThrow = exceptionToThrow;
        }

        public Task<PassportQrCodeResponse> CreateQrCodeAsync(
            PassportCreateQrCodeRequest request, CancellationToken cancellationToken = default)
        {
            CreateQrCodeCallCount++;
            throw new InvalidOperationException("CreateQrCodeAsync NUNCA debe invocarse desde decode-qr-dynamic — M4-T5 nunca crea un QR nuevo.");
        }

        public Task<PassportDecodeQrCodeResponse> DecodeQrCodeAsync(
            PassportDecodeQrCodeRequest request, CancellationToken cancellationToken = default)
        {
            DecodeQrCodeCallCount++;
            throw new InvalidOperationException("DecodeQrCodeAsync NUNCA debe invocarse desde decode-qr-dynamic — usa DecodeQrCodeWithRawCaptureAsync.");
        }

        public Task<(PassportDecodeQrCodeResponse? Response, string? RawResponseJson)> DecodeQrCodeWithRawCaptureAsync(
            PassportDecodeQrCodeRequest request, CancellationToken cancellationToken = default)
        {
            DecodeQrCodeWithRawCaptureCallCount++;
            LastRequest = request;
            if (_exceptionToThrow is not null)
                throw _exceptionToThrow;
            if (_throwOnDecode)
                throw new PassportTransportException("Passport respondió con error HTTP 400 (sintético).");

            var response = _response ?? new PassportDecodeQrCodeResponse
            {
                Status = "ACTIVE",
                Type = "DYNAMIC",
                Channel = "MPOS",
                Amount = new PassportQrAmountResponse { Value = "10000.00", Currency = "COP" },
                Vat = new PassportQrVatResponse { VatType = "FIXED", VatValue = "100.00", VatBaseValue = "100.00" },
                Inc = new PassportQrIncResponse { IncType = "FIXED", IncValue = "10.00" },
                QrCodeReference = "SYNTHQRREF001",
            };
            var rawJson = _rawResponseJson ?? """{"status":"ACTIVE","type":"DYNAMIC"}""";
            return Task.FromResult<(PassportDecodeQrCodeResponse?, string?)>((response, rawJson));
        }
    }

    private const string SyntheticKeyId      = "synthetic-executor-key-id-m4t5";
    private const string SyntheticCustomerId = "synthetic-executor-customer-id-m4t5";
    private const string SyntheticQrCodeData = "00020101synthetic-m4t4-qr-code-data-for-decode6304WXYZ";
    private const string SyntheticQrCodeReferenceReceived = "SYNTHQRREF001";

    private static readonly string SyntheticRepoRoot =
        Path.Combine(Path.GetTempPath(), "xpay-m4t5-synthetic-repo-root-for-tests");

    private static string NewTempDir(string prefix) =>
        Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));

    // Escribe un artefacto privado M4-T4 sintético (nunca un valor real de
    // M4-T4) con el shape exacto que M4T4ArtifactReader.Artifact espera.
    private static string WriteM4T4Artifact(
        string? caseId = "M4-T4",
        string? status = "ACTIVE",
        string? type = "DYNAMIC",
        string? keyIdFingerprint = null,
        string? customerIdFingerprint = null,
        string? qrCodeData = SyntheticQrCodeData,
        string? qrCodeReferenceReceived = SyntheticQrCodeReferenceReceived)
    {
        var dir = NewTempDir("xpay-m4t5-source-m4t4-artifact");
        Directory.CreateDirectory(dir);

        var content = new Dictionary<string, object?>
        {
            ["case_id"] = caseId,
            ["backend_commit_sha"] = "synthetic-commit-sha",
            ["key_id_fingerprint"] = keyIdFingerprint ?? Fingerprint.Compute(SyntheticKeyId),
            ["customer_id_fingerprint"] = customerIdFingerprint ?? Fingerprint.Compute(SyntheticCustomerId),
            ["status"] = status,
            ["type"] = type,
            ["qr_id_fingerprint"] = "SYNTHQRIDFP01",
            ["qr_code_reference_received"] = qrCodeReferenceReceived,
            ["qr_code_data"] = qrCodeData,
            ["qr_code_image"] = "synthetic-base64-image-data",
        };
        var json = System.Text.Json.JsonSerializer.Serialize(content, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(dir, "M4-T4-2026-01-01T00-00-00Z.private.json"), json);
        return dir;
    }

    private static IConfiguration ConfigWithTarget(
        string? keyId = SyntheticKeyId, string? customerId = SyntheticCustomerId,
        string? m4T4CaptureDir = "__AUTO_VALID_M4T4__",
        string? m4T5CaptureDir = "__AUTO_VALID_M4T5__")
    {
        var dict = new Dictionary<string, string?>();
        if (keyId is not null) dict[HarnessTargetConfig.EnvQrKeyId] = keyId;
        if (customerId is not null) dict[HarnessTargetConfig.EnvCustomerId] = customerId;

        var resolvedM4T4Dir = m4T4CaptureDir == "__AUTO_VALID_M4T4__" ? WriteM4T4Artifact() : m4T4CaptureDir;
        if (resolvedM4T4Dir is not null) dict[M4T4PrivateCapture.EnvPrivateCaptureDir] = resolvedM4T4Dir;

        var resolvedM4T5Dir = m4T5CaptureDir == "__AUTO_VALID_M4T5__" ? NewTempDir("xpay-m4t5-private-capture") : m4T5CaptureDir;
        if (resolvedM4T5Dir is not null) dict[M4T5PrivateCapture.EnvPrivateCaptureDir] = resolvedM4T5Dir;

        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static readonly DateTime FixedTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Task<KeyOperationResult> Execute(
        IConfiguration config, FakeQrClient client, ICommitShaProvider? commitShaProvider = null) =>
        DecodeQrDynamicExecutor.ExecuteAsync(
            config, client,
            commitShaProvider ?? new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000"),
            FixedTime, automatedTestReference: null, repositoryRootOverride: SyntheticRepoRoot);

    // ══════════════════════════════════════════════════════════════════
    // A/L/M — obtiene qr_code_data del artefacto correcto; exactamente una
    // llamada Decode-con-captura; ninguna otra llamada.
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_Success_ReadsQrCodeDataFromM4T4Artifact_SendsItInRequest()
    {
        var client = new FakeQrClient();
        var config = ConfigWithTarget();

        var result = await Execute(config, client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal(1, client.DecodeQrCodeWithRawCaptureCallCount);
        Assert.Equal(0, client.DecodeQrCodeCallCount);
        Assert.Equal(0, client.CreateQrCodeCallCount);
        Assert.Equal(SyntheticQrCodeData, client.LastRequest!.QrCodeData);
        Assert.Equal(SyntheticCustomerId, client.LastRequest.CustomerId);
    }

    // ── B/C — nunca versiona qr_code_data/qr_code_reference crudos ──────

    [Fact]
    public async Task ExecuteAsync_Success_EvidenceNeverContainsRawQrCodeDataOrReference()
    {
        var client = new FakeQrClient();
        var result = await Execute(ConfigWithTarget(), client);

        var evidenceJson = System.Text.Json.JsonSerializer.Serialize(result.Evidence);

        Assert.DoesNotContain(SyntheticQrCodeData, evidenceJson);
        Assert.DoesNotContain(SyntheticQrCodeReferenceReceived, evidenceJson);
        Assert.Contains("qr_code_data_fingerprint", evidenceJson);
        Assert.Contains("qr_code_reference_fingerprint", evidenceJson);
    }

    // ── D/E — qr_code_reference_match_m4_t4 ──────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Success_QrCodeReferenceMatch_TrueWhenFingerprintsCoincide()
    {
        var client = new FakeQrClient(response: new PassportDecodeQrCodeResponse
        {
            Status = "ACTIVE", Type = "DYNAMIC", QrCodeReference = SyntheticQrCodeReferenceReceived,
        });
        var result = await Execute(ConfigWithTarget(), client);

        Assert.True((bool)result.Evidence!.ResponseSanitized["qr_code_reference_match_m4_t4"]!);
    }

    [Fact]
    public async Task ExecuteAsync_Success_QrCodeReferenceMatch_FalseWhenFingerprintsDiffer()
    {
        var client = new FakeQrClient(response: new PassportDecodeQrCodeResponse
        {
            Status = "ACTIVE", Type = "DYNAMIC", QrCodeReference = "COMPLETELY-DIFFERENT-REF",
        });
        var result = await Execute(ConfigWithTarget(), client);

        Assert.False((bool)result.Evidence!.ResponseSanitized["qr_code_reference_match_m4_t4"]!);
    }

    // ── F/G/H — amount_value/currency_matches_expected ──────────────────

    [Fact]
    public async Task ExecuteAsync_Success_AmountValueMatchesExpected_TrueFor10000_00()
    {
        var client = new FakeQrClient(response: new PassportDecodeQrCodeResponse
        {
            Status = "ACTIVE", Type = "DYNAMIC",
            Amount = new PassportQrAmountResponse { Value = "10000.00", Currency = "COP" },
        });
        var result = await Execute(ConfigWithTarget(), client);

        Assert.True((bool)result.Evidence!.ResponseSanitized["amount_value_matches_expected"]!);
    }

    [Fact]
    public async Task ExecuteAsync_Success_AmountCurrencyMatchesExpected_TrueForCOP()
    {
        var client = new FakeQrClient(response: new PassportDecodeQrCodeResponse
        {
            Status = "ACTIVE", Type = "DYNAMIC",
            Amount = new PassportQrAmountResponse { Value = "10000.00", Currency = "COP" },
        });
        var result = await Execute(ConfigWithTarget(), client);

        Assert.True((bool)result.Evidence!.ResponseSanitized["amount_currency_matches_expected"]!);
    }

    [Fact]
    public async Task ExecuteAsync_Success_AmountMismatches_YieldFalse()
    {
        var client = new FakeQrClient(response: new PassportDecodeQrCodeResponse
        {
            Status = "ACTIVE", Type = "DYNAMIC",
            Amount = new PassportQrAmountResponse { Value = "999.00", Currency = "USD" },
        });
        var result = await Execute(ConfigWithTarget(), client);

        Assert.False((bool)result.Evidence!.ResponseSanitized["amount_value_matches_expected"]!);
        Assert.False((bool)result.Evidence.ResponseSanitized["amount_currency_matches_expected"]!);
    }

    [Fact]
    public async Task ExecuteAsync_Success_AmountAbsent_MatchesAreFalse_NeverThrows()
    {
        var client = new FakeQrClient(response: new PassportDecodeQrCodeResponse
        {
            Status = "ACTIVE", Type = "DYNAMIC", Amount = null,
        });
        var result = await Execute(ConfigWithTarget(), client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.False((bool)result.Evidence!.ResponseSanitized["amount_present"]!);
        Assert.False((bool)result.Evidence.ResponseSanitized["amount_value_matches_expected"]!);
        Assert.False((bool)result.Evidence.ResponseSanitized["amount_currency_matches_expected"]!);
    }

    // ── I/J — vat/inc presence; tip nunca inventado ──────────────────────

    [Fact]
    public async Task ExecuteAsync_Success_VatIncPresence_ReflectsResponse()
    {
        var client = new FakeQrClient(response: new PassportDecodeQrCodeResponse
        {
            Status = "ACTIVE", Type = "DYNAMIC",
            Vat = new PassportQrVatResponse { VatType = "FIXED", VatValue = "100.00", VatBaseValue = "100.00" },
            Inc = null,
        });
        var result = await Execute(ConfigWithTarget(), client);

        Assert.True((bool)result.Evidence!.ResponseSanitized["vat_present"]!);
        Assert.False((bool)result.Evidence.ResponseSanitized["inc_present"]!);
    }

    [Fact]
    public void EvidenceSchema_NeverContainsTipPresent_DtoDoesNotModelTip()
    {
        // J — confirmación estructural: PassportDecodeQrCodeResponse no
        // tiene una propiedad Tip; por tanto ningún evidence builder puede
        // "inventar" tip_present a partir de ella.
        var properties = typeof(PassportDecodeQrCodeResponse).GetProperties().Select(p => p.Name);
        Assert.DoesNotContain("Tip", properties);
    }

    [Fact]
    public async Task ExecuteAsync_Success_EvidenceDocumentsTipLimitation_Factually()
    {
        var client = new FakeQrClient();
        var result = await Execute(ConfigWithTarget(), client);

        var limitations = (Dictionary<string, object?>)result.Evidence!.ResponseSanitized["limitations"]!;
        Assert.True(limitations.ContainsKey("tip_not_modeled_by_decode_dto"));
    }

    // ── K/L/M — exactamente una llamada; ninguna otra ────────────────────

    [Fact]
    public async Task ExecuteAsync_PassportRejects_CallsOnlyDecodeWithRawCapture_ExactlyOnce_ProducesFailEvidence()
    {
        var client = new FakeQrClient(throwOnDecode: true);
        var result = await Execute(ConfigWithTarget(), client);

        Assert.Equal(1, client.DecodeQrCodeWithRawCaptureCallCount);
        Assert.Equal(0, client.CreateQrCodeCallCount);
        Assert.Equal(0, client.DecodeQrCodeCallCount);
        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        Assert.Equal("M4-T5", result.Evidence!.CaseId);
        Assert.Equal(EvidenceRecord.ResultFail, result.Evidence.Result);
    }

    // ── Precondiciones FAIL-CLOSED ───────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MissingKeyIdOrCustomerId_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeQrClient();
        var result = await Execute(ConfigWithTarget(keyId: null), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.DecodeQrCodeWithRawCaptureCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_M4T4CaptureDirMissing_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeQrClient();
        var result = await Execute(ConfigWithTarget(m4T4CaptureDir: null), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.DecodeQrCodeWithRawCaptureCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_M4T4ArtifactDirectoryEmpty_IsLocalBlocked_NeverCallsClient()
    {
        var emptyDir = NewTempDir("xpay-m4t5-empty-m4t4-dir");
        Directory.CreateDirectory(emptyDir);
        try
        {
            var client = new FakeQrClient();
            var result = await Execute(ConfigWithTarget(m4T4CaptureDir: emptyDir), client);

            Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
            Assert.Equal(0, client.DecodeQrCodeWithRawCaptureCallCount);
        }
        finally
        {
            Directory.Delete(emptyDir, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_M4T4ArtifactIdentityMismatch_KeyFingerprint_IsLocalBlocked()
    {
        var mismatchedDir = WriteM4T4Artifact(keyIdFingerprint: "WRONGFINGERPRINT001");
        try
        {
            var client = new FakeQrClient();
            var result = await Execute(ConfigWithTarget(m4T4CaptureDir: mismatchedDir), client);

            Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
            Assert.Equal(0, client.DecodeQrCodeWithRawCaptureCallCount);
        }
        finally
        {
            Directory.Delete(mismatchedDir, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_M4T4ArtifactWrongStatus_IsLocalBlocked()
    {
        var wrongStatusDir = WriteM4T4Artifact(status: "SUSPENDED");
        try
        {
            var client = new FakeQrClient();
            var result = await Execute(ConfigWithTarget(m4T4CaptureDir: wrongStatusDir), client);

            Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        }
        finally
        {
            Directory.Delete(wrongStatusDir, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_M4T4ArtifactWrongType_IsLocalBlocked()
    {
        var wrongTypeDir = WriteM4T4Artifact(type: "STATIC");
        try
        {
            var client = new FakeQrClient();
            var result = await Execute(ConfigWithTarget(m4T4CaptureDir: wrongTypeDir), client);

            Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        }
        finally
        {
            Directory.Delete(wrongTypeDir, recursive: true);
        }
    }

    // ── N — captura privada M4-T5 fuera del repo ─────────────────────────

    [Fact]
    public async Task ExecuteAsync_M4T5CaptureDirInsideRepository_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeQrClient();
        var insideRepoPath = Path.Combine(SyntheticRepoRoot, "docs", "certificacion", "passport-breb", "M4-T5-private");

        var result = await Execute(ConfigWithTarget(m4T5CaptureDir: insideRepoPath), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.DecodeQrCodeWithRawCaptureCallCount);
        Assert.Contains("InsideRepository", result.Detail);
        Assert.DoesNotContain(insideRepoPath, result.Detail!);
    }

    // ── O — fail-closed si falta configuración de captura M4-T5 ──────────

    [Fact]
    public async Task ExecuteAsync_M4T5CaptureDirMissing_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeQrClient();
        var result = await Execute(ConfigWithTarget(m4T5CaptureDir: null), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.DecodeQrCodeWithRawCaptureCallCount);
        Assert.Contains("Destino privado M4-T5", result.Detail);
    }

    // ── P — la captura privada preserva un campo no modelado por el DTO ──

    [Fact]
    public async Task ExecuteAsync_Success_PrivateCapture_PreservesUnmodeledFieldFromRawJson()
    {
        var m4T5Dir = NewTempDir("xpay-m4t5-private-capture-fidelity");
        try
        {
            const string rawJsonWithTip = """
                {
                  "status": "ACTIVE",
                  "type": "DYNAMIC",
                  "tip": { "tip_type": "FIXED", "tip_value": "100.00" }
                }
                """;
            var client = new FakeQrClient(
                response: new PassportDecodeQrCodeResponse { Status = "ACTIVE", Type = "DYNAMIC" },
                rawResponseJson: rawJsonWithTip);

            var result = await Execute(ConfigWithTarget(m4T5CaptureDir: m4T5Dir), client);

            Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
            Assert.True((bool)result.Evidence!.ResponseSanitized["private_artifact_captured"]!);

            var files = Directory.GetFiles(m4T5Dir, "*.private.json");
            Assert.Single(files);
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(files[0]));
            var tip = doc.RootElement.GetProperty("raw_response").GetProperty("tip");
            Assert.Equal("FIXED", tip.GetProperty("tip_type").GetString());
            Assert.Equal("100.00", tip.GetProperty("tip_value").GetString());

            // La evidencia VERSIONABLE, en cambio, nunca contiene tip ni
            // ningún material crudo — sólo el booleano de captura.
            var evidenceJson = System.Text.Json.JsonSerializer.Serialize(result.Evidence);
            Assert.DoesNotContain("tip_value", evidenceJson);
            Assert.DoesNotContain(m4T5Dir, evidenceJson);
        }
        finally
        {
            if (Directory.Exists(m4T5Dir)) Directory.Delete(m4T5Dir, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Success_DecodeSourceRecordedAsM4T4PrivateArtifact()
    {
        var client = new FakeQrClient();
        var result = await Execute(ConfigWithTarget(), client);

        Assert.Equal("M4-T4_PRIVATE_ARTIFACT", result.Evidence!.RequestSanitized["decode_source"]);
    }
}
