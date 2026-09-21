using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-465 — prueba, a nivel de DecodeQrStaticExecutor (sin red), que M4-T2
// invoca ÚNICAMENTE IPassportQrClient.DecodeQrCodeAsync exactamente una vez
// (nunca CreateQrCodeAsync ni ningún método de IPassportKeyClient), que el
// request construido corresponde exactamente al contrato confirmado
// (customer_id + qr_code_data, nada más), y que ningún dato crudo
// (qr_code_data/customer_id/campos sensibles de la respuesta) sale jamás
// hacia stdout o la evidencia.
public class DecodeQrStaticExecutorTests
{
    private sealed class FakeQrClient : IPassportQrClient
    {
        private readonly bool _throwOnDecode;
        private readonly PassportTransportException? _exceptionToThrow;
        private readonly PassportDecodeQrCodeResponse? _response;
        public int DecodeQrCodeCallCount { get; private set; }
        public int CreateQrCodeCallCount { get; private set; }
        public PassportDecodeQrCodeRequest? LastRequest { get; private set; }

        public FakeQrClient(
            bool throwOnDecode = false,
            PassportDecodeQrCodeResponse? response = null,
            PassportTransportException? exceptionToThrow = null)
        {
            _throwOnDecode = throwOnDecode;
            _response = response;
            _exceptionToThrow = exceptionToThrow;
        }

        public Task<PassportQrCodeResponse> CreateQrCodeAsync(
            PassportCreateQrCodeRequest request, CancellationToken cancellationToken = default)
        {
            CreateQrCodeCallCount++;
            throw new InvalidOperationException("CreateQrCodeAsync NUNCA debe invocarse desde decode-qr-static.");
        }

        public Task<PassportDecodeQrCodeResponse> DecodeQrCodeAsync(
            PassportDecodeQrCodeRequest request, CancellationToken cancellationToken = default)
        {
            DecodeQrCodeCallCount++;
            LastRequest = request;
            if (_exceptionToThrow is not null)
                throw _exceptionToThrow;
            if (_throwOnDecode)
                throw new PassportTransportException("Passport respondió con error HTTP 400 (sintético).");
            return Task.FromResult(_response ?? new PassportDecodeQrCodeResponse
            {
                Status = "ACTIVE",
                Type = "STATIC",
                Channel = "MPOS",
                QrCodeData = "00020101...synthetic-decoded-emv-payload...6304WXYZ",
                QrCodeReference = "SYNM4T2REF01",
            });
        }
    }

    private const string SyntheticCustomerId = "synthetic-decode-customer-id-001";

    // Archivo temporal real (fuera de git, en el directorio temp del SO) —
    // mismo criterio que el resto del harness: nunca un valor hardcodeado
    // en código de producción, siempre desde un artefacto externo al repo.
    private static string WriteTempQrDataFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"xpay-decode-test-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, content);
        return path;
    }

    private static IConfiguration ConfigWithTarget(string? qrDataFilePath, string? customerId = SyntheticCustomerId)
    {
        var dict = new Dictionary<string, string?>();
        if (qrDataFilePath is not null) dict[HarnessTargetConfig.EnvQrDecodeDataFilePath] = qrDataFilePath;
        if (customerId is not null) dict[HarnessTargetConfig.EnvCustomerId] = customerId;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    // A/B/C — exactamente 1 llamada lógica, usa IPassportQrClient.DecodeQrCodeAsync
    // (nunca CreateQrCodeAsync ni IPassportKeyClient), sin reintentos, request
    // EXACTAMENTE customer_id + qr_code_data (contrato confirmado — XPAY-465 §1).
    [Fact]
    public async Task ExecuteAsync_Success_CallsOnlyDecodeQrCodeAsync_ExactlyOnce_CorrectRequestShape()
    {
        const string syntheticQrData = "00020101...synthetic-raw-emv-payload-from-file...6304ABCD";
        var qrDataFilePath = WriteTempQrDataFile(syntheticQrData);
        try
        {
            var client = new FakeQrClient();
            var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

            var result = await DecodeQrStaticExecutor.ExecuteAsync(
                ConfigWithTarget(qrDataFilePath), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            Assert.Equal(1, client.DecodeQrCodeCallCount);
            Assert.Equal(0, client.CreateQrCodeCallCount);

            var sent = client.LastRequest!;
            Assert.Equal(SyntheticCustomerId, sent.CustomerId);
            Assert.Equal(syntheticQrData, sent.QrCodeData);

            Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
            Assert.NotNull(result.Evidence);
        }
        finally
        {
            File.Delete(qrDataFilePath);
        }
    }

    // Regresión: el contenido del archivo se lee verbatim salvo trim de
    // whitespace circundante (un editor/echo puede agregar un salto de
    // línea final) — nunca se transforma/normaliza el payload en sí.
    [Fact]
    public async Task ExecuteAsync_Success_TrimsSurroundingWhitespaceOnly()
    {
        const string syntheticQrData = "00020101...synthetic-raw-emv-payload...6304EFGH";
        var qrDataFilePath = WriteTempQrDataFile($"  {syntheticQrData}\n");
        try
        {
            var client = new FakeQrClient();
            var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

            await DecodeQrStaticExecutor.ExecuteAsync(
                ConfigWithTarget(qrDataFilePath), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            Assert.Equal(syntheticQrData, client.LastRequest!.QrCodeData);
        }
        finally
        {
            File.Delete(qrDataFilePath);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Success_EvidenceHasCorrectCaseId()
    {
        var qrDataFilePath = WriteTempQrDataFile("00020101...synthetic...6304IJKL");
        try
        {
            var client = new FakeQrClient();
            var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

            var result = await DecodeQrStaticExecutor.ExecuteAsync(
                ConfigWithTarget(qrDataFilePath), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            Assert.Equal("M4-T2", result.Evidence!.CaseId);
        }
        finally
        {
            File.Delete(qrDataFilePath);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Success_EvidenceHasCorrectOperation()
    {
        var qrDataFilePath = WriteTempQrDataFile("00020101...synthetic...6304MNOP");
        try
        {
            var client = new FakeQrClient();
            var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

            var result = await DecodeQrStaticExecutor.ExecuteAsync(
                ConfigWithTarget(qrDataFilePath), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            Assert.Equal("POST /v1/qrcodes/decode", result.Evidence!.Operation);
        }
        finally
        {
            File.Delete(qrDataFilePath);
        }
    }

    // Evidencia SIEMPRE JSON válido — se verifica parseándola de vuelta.
    [Fact]
    public async Task ExecuteAsync_Success_EvidenceIsValidJson()
    {
        var qrDataFilePath = WriteTempQrDataFile("00020101...synthetic...6304QRST");
        try
        {
            var client = new FakeQrClient();
            var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

            var result = await DecodeQrStaticExecutor.ExecuteAsync(
                ConfigWithTarget(qrDataFilePath), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            var evidenceJson = System.Text.Json.JsonSerializer.Serialize(result.Evidence);
            using var doc = System.Text.Json.JsonDocument.Parse(evidenceJson); // no debe lanzar
            Assert.Equal(System.Text.Json.JsonValueKind.Object, doc.RootElement.ValueKind);
        }
        finally
        {
            File.Delete(qrDataFilePath);
        }
    }

    // D — respuesta saneada pero SUFICIENTE: demuestra "JSON válido" vía
    // presencia de cada sub-estructura documentada, sin exponer valores.
    [Fact]
    public async Task ExecuteAsync_Success_EvidenceRecordsResponseStructurePresence()
    {
        var qrDataFilePath = WriteTempQrDataFile("00020101...synthetic...6304UVWX");
        try
        {
            var client = new FakeQrClient(response: new PassportDecodeQrCodeResponse
            {
                Status = "ACTIVE",
                Type = "STATIC",
                Channel = "MPOS",
                AcquirerNetworkIdentifier = "SYNTH-NETWORK",
                Amount = new PassportQrAmountResponse { Value = "80000.57", Currency = "COP" },
                AdditionalInfo = new PassportQrAdditionalInfoResponse { TransactionPurpose = "PURCHASE" },
                Inc = new PassportQrIncResponse { IncType = "FIXED", IncValue = "10.00" },
                Vat = new PassportQrVatResponse { VatType = "FIXED", VatValue = "0.00", VatBaseValue = "0.00" },
                Key = new PassportKeyResponseDetail { KeyType = "PHONE", KeyValue = "synthetic-real-phone-must-never-appear" },
                Merchant = new PassportDecodeMerchantResponse
                {
                    MerchantCategoryCode = "0412",
                    MerchantCountry = "CO",
                    MerchantName = "Synthetic Merchant Name",
                    MerchantCity = "Synthetic City",
                    MerchantPostCode = "000000",
                },
                QrCodeData = "00020101...synthetic-decoded-payload...6304YZAB",
                QrCodeReference = "SYNM4T2REF02",
            });
            var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

            var result = await DecodeQrStaticExecutor.ExecuteAsync(
                ConfigWithTarget(qrDataFilePath), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            var evidenceJson = System.Text.Json.JsonSerializer.Serialize(result.Evidence);
            using var doc = System.Text.Json.JsonDocument.Parse(evidenceJson);
            var responseSanitized = doc.RootElement.GetProperty("response_sanitized");

            Assert.Equal("ACTIVE", responseSanitized.GetProperty("status").GetString());
            Assert.Equal("STATIC", responseSanitized.GetProperty("type").GetString());
            Assert.Equal("MPOS", responseSanitized.GetProperty("channel").GetString());
            Assert.True(responseSanitized.GetProperty("amount_present").GetBoolean());
            Assert.True(responseSanitized.GetProperty("additional_info_present").GetBoolean());
            Assert.True(responseSanitized.GetProperty("inc_present").GetBoolean());
            Assert.True(responseSanitized.GetProperty("vat_present").GetBoolean());
            Assert.True(responseSanitized.GetProperty("key_present").GetBoolean());
            Assert.Equal("PHONE", responseSanitized.GetProperty("key_type").GetString());
            Assert.True(responseSanitized.GetProperty("merchant_present").GetBoolean());
            Assert.Equal("0412", responseSanitized.GetProperty("merchant_category_code").GetString());
            Assert.Equal("CO", responseSanitized.GetProperty("merchant_country").GetString());
            Assert.True(responseSanitized.GetProperty("qr_code_data_present").GetBoolean());
            Assert.True(responseSanitized.GetProperty("qr_code_reference_present").GetBoolean());
        }
        finally
        {
            File.Delete(qrDataFilePath);
        }
    }

    // L/M/N/O — evidencia NUNCA contiene identificadores/datos crudos; sólo
    // fingerprints/presencia/categorías no sensibles.
    [Fact]
    public async Task ExecuteAsync_Success_EvidenceNeverContainsRawSensitiveData()
    {
        const string realQrCodeDataSent    = "00020101REAL-SENT-EMV-PAYLOAD-must-never-appear-0000001";
        const string realCustomerId        = "REAL-CUSTOMER-ID-must-never-appear-in-evidence-0000002";
        const string realQrCodeDataEchoed  = "00020101REAL-ECHOED-EMV-PAYLOAD-must-never-appear-0000003";
        const string realKeyValue          = "REAL-KEY-VALUE-phone-or-email-must-never-appear-0000004";
        const string realMerchantName      = "Real Merchant Name Must Never Appear";
        const string realMerchantCity      = "Real Merchant City Must Never Appear";

        var qrDataFilePath = WriteTempQrDataFile(realQrCodeDataSent);
        try
        {
            var client = new FakeQrClient(response: new PassportDecodeQrCodeResponse
            {
                Status = "ACTIVE",
                Type = "STATIC",
                QrCodeData = realQrCodeDataEchoed,
                Key = new PassportKeyResponseDetail { KeyType = "PHONE", KeyValue = realKeyValue },
                Merchant = new PassportDecodeMerchantResponse
                {
                    MerchantName = realMerchantName,
                    MerchantCity = realMerchantCity,
                },
            });
            var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

            var result = await DecodeQrStaticExecutor.ExecuteAsync(
                ConfigWithTarget(qrDataFilePath, customerId: realCustomerId),
                client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            var evidenceJson = System.Text.Json.JsonSerializer.Serialize(result.Evidence);

            Assert.DoesNotContain(realQrCodeDataSent, evidenceJson);
            Assert.DoesNotContain(realCustomerId, evidenceJson);
            Assert.DoesNotContain(realQrCodeDataEchoed, evidenceJson);
            Assert.DoesNotContain(realKeyValue, evidenceJson);
            Assert.DoesNotContain(realMerchantName, evidenceJson);
            Assert.DoesNotContain(realMerchantCity, evidenceJson);

            // Fingerprints/presencia SÍ están (no simplemente omitidos).
            Assert.Contains("customer_id_fingerprint", evidenceJson);
            Assert.Contains("qr_code_data_fingerprint", evidenceJson);
            Assert.Contains("key_value_fingerprint", evidenceJson);
            Assert.Contains("merchant_name_fingerprint", evidenceJson);
            Assert.Contains("merchant_city_fingerprint", evidenceJson);
        }
        finally
        {
            File.Delete(qrDataFilePath);
        }
    }

    // Notes=null en éxito, mismo criterio que CreateQrStaticEvidenceBuilder (XPAY-464).
    [Fact]
    public async Task ExecuteAsync_Success_EvidenceNotesIsNull()
    {
        var qrDataFilePath = WriteTempQrDataFile("00020101...synthetic...6304CDEF");
        try
        {
            var client = new FakeQrClient();
            var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

            var result = await DecodeQrStaticExecutor.ExecuteAsync(
                ConfigWithTarget(qrDataFilePath), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            Assert.Null(result.Evidence!.Notes);
        }
        finally
        {
            File.Delete(qrDataFilePath);
        }
    }

    [Fact]
    public async Task ExecuteAsync_PassportRejects_CallsOnlyDecodeQrCodeAsync_ExactlyOnce_ProducesFailEvidence()
    {
        var qrDataFilePath = WriteTempQrDataFile("00020101...synthetic...6304GHIJ");
        try
        {
            var client = new FakeQrClient(throwOnDecode: true);
            var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

            var result = await DecodeQrStaticExecutor.ExecuteAsync(
                ConfigWithTarget(qrDataFilePath), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            Assert.Equal(1, client.DecodeQrCodeCallCount);
            Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
            Assert.NotNull(result.Evidence);
            Assert.Equal("M4-T2", result.Evidence!.CaseId);
            Assert.Equal(EvidenceRecord.ResultFail, result.Evidence.Result);
        }
        finally
        {
            File.Delete(qrDataFilePath);
        }
    }

    [Fact]
    public async Task ExecuteAsync_MissingQrDataFilePath_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeQrClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        var result = await DecodeQrStaticExecutor.ExecuteAsync(
            ConfigWithTarget(qrDataFilePath: null), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.DecodeQrCodeCallCount);
        Assert.Null(result.Evidence);
    }

    [Fact]
    public async Task ExecuteAsync_MissingCustomerId_IsLocalBlocked_NeverCallsClient()
    {
        var qrDataFilePath = WriteTempQrDataFile("00020101...synthetic...6304KLMN");
        try
        {
            var client = new FakeQrClient();
            var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

            var result = await DecodeQrStaticExecutor.ExecuteAsync(
                ConfigWithTarget(qrDataFilePath, customerId: null), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
            Assert.Equal(0, client.DecodeQrCodeCallCount);
        }
        finally
        {
            File.Delete(qrDataFilePath);
        }
    }

    // Archivo apuntado por la variable NO existe en disco => LocalBlocked,
    // nunca una excepción sin capturar, nunca contenido de archivo en Detail.
    [Fact]
    public async Task ExecuteAsync_QrDataFileDoesNotExist_IsLocalBlocked_NeverCallsClient()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"xpay-decode-test-nonexistent-{Guid.NewGuid():N}.txt");
        var client = new FakeQrClient();
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        var result = await DecodeQrStaticExecutor.ExecuteAsync(
            ConfigWithTarget(nonExistentPath), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.DecodeQrCodeCallCount);
        Assert.Null(result.Evidence);
    }

    // Archivo existe pero está vacío (o sólo whitespace) => LocalBlocked.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n")]
    public async Task ExecuteAsync_QrDataFileEmpty_IsLocalBlocked_NeverCallsClient(string emptyContent)
    {
        var qrDataFilePath = WriteTempQrDataFile(emptyContent);
        try
        {
            var client = new FakeQrClient();
            var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

            var result = await DecodeQrStaticExecutor.ExecuteAsync(
                ConfigWithTarget(qrDataFilePath), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
            Assert.Equal(0, client.DecodeQrCodeCallCount);
        }
        finally
        {
            File.Delete(qrDataFilePath);
        }
    }

    [Fact]
    public async Task ExecuteAsync_CommitShaUnresolvable_IsLocalBlocked_NeverCallsClient()
    {
        var qrDataFilePath = WriteTempQrDataFile("00020101...synthetic...6304OPQR");
        try
        {
            var client = new FakeQrClient();
            var throwingCommitShaProvider = new ThrowingCommitShaProvider();

            var result = await DecodeQrStaticExecutor.ExecuteAsync(
                ConfigWithTarget(qrDataFilePath), client, throwingCommitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
            Assert.Equal(0, client.DecodeQrCodeCallCount);
        }
        finally
        {
            File.Delete(qrDataFilePath);
        }
    }

    private sealed class ThrowingCommitShaProvider : ICommitShaProvider
    {
        public string GetCommitSha() => throw new InvalidOperationException("synthetic: commit SHA no resoluble.");
    }
}
