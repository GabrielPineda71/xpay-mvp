using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-471 — pruebas, a nivel de executor (sin red), de los tres subcasos
// negativos de M4-T3: CreateQrStaticSuspendedKeyExecutor (M4-T3-A),
// CreateQrStaticDeletedKeyExecutor (M4-T3-B),
// CreateQrStaticInvalidCustomerExecutor (M4-T3-C). Consolidados en un solo
// archivo (mismo criterio ya usado en PassportQrClientTests.cs para
// Create+Decode) dado que comparten fixture/infraestructura de prueba casi
// en su totalidad — a diferencia de la producción, donde cada subcaso SÍ
// tiene su propio executor/evidence builder dedicado (XPAY-471 §arquitectura).
public class CreateQrStaticNegativeCasesExecutorTests
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
                Id = "synthetic-negative-case-qr-id",
                Status = "ACTIVE",
                Type = "STATIC",
            });
        }

        public Task<PassportDecodeQrCodeResponse> DecodeQrCodeAsync(
            PassportDecodeQrCodeRequest request, CancellationToken cancellationToken = default)
        {
            DecodeQrCodeCallCount++;
            throw new InvalidOperationException("DecodeQrCodeAsync NUNCA debe invocarse desde los subcasos M4-T3.");
        }
    }

    private static readonly DateTime FixedTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static FixedCommitShaProvider CommitSha() =>
        new("synthetic-commit-sha-0000000000000000000000000000000000000000");

    private static void AssertStaticContractShape(PassportCreateQrCodeRequest sent)
    {
        Assert.Equal(PassportQrType.STATIC, sent.Type);
        Assert.Equal(PassportQrChannel.MPOS, sent.Channel);
        Assert.Null(sent.Amount);
        Assert.Null(sent.AdditionalInfo);
        Assert.NotNull(sent.Vat);
        Assert.Equal("100.00", sent.Vat!.VatValue);
        Assert.Equal("100.00", sent.Vat.VatBaseValue);
        Assert.NotNull(sent.Inc);
        Assert.Equal("10.00", sent.Inc!.IncValue);
        Assert.NotNull(sent.Tip);
        Assert.Equal("100.00", sent.Tip!.TipValue);
        Assert.NotNull(sent.QrCodeReference);
    }

    // ══════════════════════════════════════════════════════════════════════
    // M4-T3-A — Suspended Key
    // ══════════════════════════════════════════════════════════════════════

    private const string SyntheticSuspendedKeyId = "synthetic-suspended-key-id-001";
    private const string SyntheticCustomerId      = "synthetic-executor-customer-id-001";

    private static IConfiguration SuspendedKeyConfig(
        string? suspendedKeyId = SyntheticSuspendedKeyId, string? customerId = SyntheticCustomerId,
        string? qrKeyId = null, string? newKeyId = null)
    {
        var dict = new Dictionary<string, string?>();
        if (suspendedKeyId is not null) dict[HarnessTargetConfig.EnvQrSuspendedKeyId] = suspendedKeyId;
        if (customerId is not null) dict[HarnessTargetConfig.EnvCustomerId] = customerId;
        if (qrKeyId is not null) dict[HarnessTargetConfig.EnvQrKeyId] = qrKeyId;
        if (newKeyId is not null) dict[HarnessTargetConfig.EnvNewKeyId] = newKeyId;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public async Task SuspendedKey_Success_CallsOnlyCreateQrCodeAsync_ExactlyOnce_UsesSuspendedKeyIdOnly()
    {
        var client = new FakeQrClient();

        var result = await CreateQrStaticSuspendedKeyExecutor.ExecuteAsync(
            SuspendedKeyConfig(), client, CommitSha(), FixedTime);

        Assert.Equal(1, client.CreateQrCodeCallCount);
        Assert.Equal(0, client.DecodeQrCodeCallCount);
        Assert.Equal(SyntheticSuspendedKeyId, client.LastRequest!.KeyId);
        Assert.Equal(SyntheticCustomerId, client.LastRequest.CustomerId);
        AssertStaticContractShape(client.LastRequest);
        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.NotNull(result.Evidence);
        Assert.Equal("M4-T3-A", result.Evidence!.CaseId);
        Assert.Equal("POST /v1/qrcodes", result.Evidence.Operation);
    }

    // Regresión CRÍTICA: incluso si QR_KEY_ID (activa) y NEW_KEY_ID
    // (deleted) están presentes en el entorno, el executor de M4-T3-A
    // JAMÁS los usa — sólo SUSPENDED_KEY_ID.
    [Fact]
    public async Task SuspendedKey_Success_NeverUsesActiveOrDeletedKey_EvenWhenBothPresent()
    {
        const string activeKeyId  = "synthetic-active-qr-key-id-should-never-be-used";
        const string deletedKeyId = "synthetic-deleted-new-key-id-should-never-be-used";
        var client = new FakeQrClient();

        await CreateQrStaticSuspendedKeyExecutor.ExecuteAsync(
            SuspendedKeyConfig(qrKeyId: activeKeyId, newKeyId: deletedKeyId), client, CommitSha(), FixedTime);

        Assert.Equal(SyntheticSuspendedKeyId, client.LastRequest!.KeyId);
        Assert.NotEqual(activeKeyId, client.LastRequest.KeyId);
        Assert.NotEqual(deletedKeyId, client.LastRequest.KeyId);
    }

    [Fact]
    public async Task SuspendedKey_MissingSuspendedKeyId_IsLocalBlocked_NeverCallsClient_NoFallback()
    {
        var client = new FakeQrClient();

        var result = await CreateQrStaticSuspendedKeyExecutor.ExecuteAsync(
            SuspendedKeyConfig(suspendedKeyId: null, qrKeyId: "synthetic-active", newKeyId: "synthetic-deleted"),
            client, CommitSha(), FixedTime);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateQrCodeCallCount);
        Assert.Null(result.Evidence);
    }

    [Fact]
    public async Task SuspendedKey_MissingCustomerId_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeQrClient();

        var result = await CreateQrStaticSuspendedKeyExecutor.ExecuteAsync(
            SuspendedKeyConfig(customerId: null), client, CommitSha(), FixedTime);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateQrCodeCallCount);
    }

    [Fact]
    public async Task SuspendedKey_PassportRejects_CallsOnlyOnce_ProducesFailEvidence_NotesDoNotClaimCertificationJudgment()
    {
        var client = new FakeQrClient(throwOnCreate: true);

        var result = await CreateQrStaticSuspendedKeyExecutor.ExecuteAsync(
            SuspendedKeyConfig(), client, CommitSha(), FixedTime);

        Assert.Equal(1, client.CreateQrCodeCallCount);
        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        Assert.Equal("M4-T3-A", result.Evidence!.CaseId);
        Assert.Equal(EvidenceRecord.ResultFail, result.Evidence.Result);
        // XPAY-471 — el transporte FAIL nunca se traduce automáticamente a
        // un veredicto de certificación: la nota debe marcar el candidato,
        // no afirmar que corresponde al escenario.
        Assert.Contains("CERTIFICATION_EXPECTED_ERROR_CANDIDATE", result.Evidence.Notes);
        Assert.DoesNotContain("CONFIRMED", result.Evidence.Notes!.ToUpperInvariant());
    }

    [Fact]
    public async Task SuspendedKey_EvidenceNeverContainsRawIdentifiers()
    {
        const string realKeyId      = "REAL-SUSPENDED-KEY-ID-must-never-appear-0000001";
        const string realCustomerId = "REAL-CUSTOMER-ID-must-never-appear-0000002";
        var client = new FakeQrClient(response: new PassportQrCodeResponse
        {
            Id = "synthetic-qr-id", Status = "ACTIVE", Type = "STATIC",
            KeyId = realKeyId, CustomerId = realCustomerId,
        });

        var result = await CreateQrStaticSuspendedKeyExecutor.ExecuteAsync(
            SuspendedKeyConfig(suspendedKeyId: realKeyId, customerId: realCustomerId), client, CommitSha(), FixedTime);

        var evidenceJson = System.Text.Json.JsonSerializer.Serialize(result.Evidence);
        Assert.DoesNotContain(realKeyId, evidenceJson);
        Assert.DoesNotContain(realCustomerId, evidenceJson);
        Assert.Contains("key_id_fingerprint", evidenceJson);
        Assert.Contains("customer_id_fingerprint", evidenceJson);
    }

    // ══════════════════════════════════════════════════════════════════════
    // M4-T3-B — Deleted Key
    // ══════════════════════════════════════════════════════════════════════

    private const string SyntheticDeletedKeyId = "synthetic-deleted-new-key-id-001";

    private static IConfiguration DeletedKeyConfig(
        string? newKeyId = SyntheticDeletedKeyId, string? customerId = SyntheticCustomerId)
    {
        var dict = new Dictionary<string, string?>();
        if (newKeyId is not null) dict[HarnessTargetConfig.EnvNewKeyId] = newKeyId;
        if (customerId is not null) dict[HarnessTargetConfig.EnvCustomerId] = customerId;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public async Task DeletedKey_Success_CallsOnlyCreateQrCodeAsync_ExactlyOnce_UsesNewKeyIdOnly()
    {
        var client = new FakeQrClient();

        var result = await CreateQrStaticDeletedKeyExecutor.ExecuteAsync(
            DeletedKeyConfig(), client, CommitSha(), FixedTime);

        Assert.Equal(1, client.CreateQrCodeCallCount);
        Assert.Equal(0, client.DecodeQrCodeCallCount);
        Assert.Equal(SyntheticDeletedKeyId, client.LastRequest!.KeyId);
        Assert.Equal(SyntheticCustomerId, client.LastRequest.CustomerId);
        AssertStaticContractShape(client.LastRequest);
        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal("M4-T3-B", result.Evidence!.CaseId);
        Assert.Equal("POST /v1/qrcodes", result.Evidence.Operation);
    }

    [Fact]
    public async Task DeletedKey_MissingNewKeyId_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeQrClient();

        var result = await CreateQrStaticDeletedKeyExecutor.ExecuteAsync(
            DeletedKeyConfig(newKeyId: null), client, CommitSha(), FixedTime);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateQrCodeCallCount);
    }

    [Fact]
    public async Task DeletedKey_MissingCustomerId_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeQrClient();

        var result = await CreateQrStaticDeletedKeyExecutor.ExecuteAsync(
            DeletedKeyConfig(customerId: null), client, CommitSha(), FixedTime);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateQrCodeCallCount);
    }

    [Fact]
    public async Task DeletedKey_PassportRejects_CallsOnlyOnce_ProducesFailEvidence_NotesDoNotClaimCertificationJudgment()
    {
        var client = new FakeQrClient(throwOnCreate: true);

        var result = await CreateQrStaticDeletedKeyExecutor.ExecuteAsync(
            DeletedKeyConfig(), client, CommitSha(), FixedTime);

        Assert.Equal(1, client.CreateQrCodeCallCount);
        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        Assert.Equal("M4-T3-B", result.Evidence!.CaseId);
        Assert.Equal(EvidenceRecord.ResultFail, result.Evidence.Result);
        Assert.Contains("CERTIFICATION_EXPECTED_ERROR_CANDIDATE", result.Evidence.Notes);
        Assert.DoesNotContain("CONFIRMED", result.Evidence.Notes!.ToUpperInvariant());
    }

    [Fact]
    public async Task DeletedKey_EvidenceNeverContainsRawIdentifiers()
    {
        const string realKeyId      = "REAL-DELETED-KEY-ID-must-never-appear-0000003";
        const string realCustomerId = "REAL-CUSTOMER-ID-must-never-appear-0000004";
        var client = new FakeQrClient(response: new PassportQrCodeResponse
        {
            Id = "synthetic-qr-id", Status = "ACTIVE", Type = "STATIC",
            KeyId = realKeyId, CustomerId = realCustomerId,
        });

        var result = await CreateQrStaticDeletedKeyExecutor.ExecuteAsync(
            DeletedKeyConfig(newKeyId: realKeyId, customerId: realCustomerId), client, CommitSha(), FixedTime);

        var evidenceJson = System.Text.Json.JsonSerializer.Serialize(result.Evidence);
        Assert.DoesNotContain(realKeyId, evidenceJson);
        Assert.DoesNotContain(realCustomerId, evidenceJson);
    }

    // ══════════════════════════════════════════════════════════════════════
    // M4-T3-C — Invalid Customer
    // ══════════════════════════════════════════════════════════════════════

    private const string SyntheticActiveQrKeyId = "synthetic-active-qr-key-id-001";

    private static IConfiguration InvalidCustomerConfig(string? qrKeyId = SyntheticActiveQrKeyId)
    {
        var dict = new Dictionary<string, string?>();
        if (qrKeyId is not null) dict[HarnessTargetConfig.EnvQrKeyId] = qrKeyId;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public async Task InvalidCustomer_Success_CallsOnlyCreateQrCodeAsync_ExactlyOnce_UsesActiveQrKeyAndSyntheticCustomer()
    {
        var client = new FakeQrClient();

        var result = await CreateQrStaticInvalidCustomerExecutor.ExecuteAsync(
            InvalidCustomerConfig(), client, CommitSha(), FixedTime);

        Assert.Equal(1, client.CreateQrCodeCallCount);
        Assert.Equal(0, client.DecodeQrCodeCallCount);
        Assert.Equal(SyntheticActiveQrKeyId, client.LastRequest!.KeyId);
        Assert.Equal(InvalidCustomerIdGenerator.Generate(), client.LastRequest.CustomerId);
        AssertStaticContractShape(client.LastRequest);
        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal("M4-T3-C", result.Evidence!.CaseId);
    }

    // Regresión CRÍTICA #7: el customer_id enviado NUNCA es el real, sin
    // importar qué haya en PASSPORT_TEST_CUSTOMER_ID (que este executor ni
    // siquiera lee).
    [Fact]
    public async Task InvalidCustomer_Success_NeverUsesRealCustomerId()
    {
        const string realCustomerId = "REAL-CUSTOMER-ID-must-never-be-sent-0000005";
        var dict = new Dictionary<string, string?>
        {
            [HarnessTargetConfig.EnvQrKeyId] = SyntheticActiveQrKeyId,
            [HarnessTargetConfig.EnvCustomerId] = realCustomerId, // presente en el entorno, pero irrelevante para este executor.
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var client = new FakeQrClient();

        await CreateQrStaticInvalidCustomerExecutor.ExecuteAsync(config, client, CommitSha(), FixedTime);

        Assert.NotEqual(realCustomerId, client.LastRequest!.CustomerId);
        Assert.Equal(InvalidCustomerIdGenerator.Generate(), client.LastRequest.CustomerId);
    }

    [Fact]
    public async Task InvalidCustomer_MissingQrKeyId_IsLocalBlocked_NeverCallsClient()
    {
        var client = new FakeQrClient();

        var result = await CreateQrStaticInvalidCustomerExecutor.ExecuteAsync(
            InvalidCustomerConfig(qrKeyId: null), client, CommitSha(), FixedTime);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateQrCodeCallCount);
    }

    [Fact]
    public async Task InvalidCustomer_PassportRejects_CallsOnlyOnce_ProducesFailEvidence_NotesDoNotClaimCertificationJudgment()
    {
        var client = new FakeQrClient(throwOnCreate: true);

        var result = await CreateQrStaticInvalidCustomerExecutor.ExecuteAsync(
            InvalidCustomerConfig(), client, CommitSha(), FixedTime);

        Assert.Equal(1, client.CreateQrCodeCallCount);
        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        Assert.Equal("M4-T3-C", result.Evidence!.CaseId);
        Assert.Equal(EvidenceRecord.ResultFail, result.Evidence.Result);
        Assert.Contains("CERTIFICATION_EXPECTED_ERROR_CANDIDATE", result.Evidence.Notes);
        Assert.DoesNotContain("CONFIRMED", result.Evidence.Notes!.ToUpperInvariant());
    }

    [Fact]
    public async Task InvalidCustomer_EvidenceNeverContainsRawIdentifiers()
    {
        const string realKeyId = "REAL-ACTIVE-QR-KEY-ID-must-never-appear-0000006";
        var client = new FakeQrClient(response: new PassportQrCodeResponse
        {
            Id = "synthetic-qr-id", Status = "ACTIVE", Type = "STATIC", KeyId = realKeyId,
        });

        var result = await CreateQrStaticInvalidCustomerExecutor.ExecuteAsync(
            InvalidCustomerConfig(qrKeyId: realKeyId), client, CommitSha(), FixedTime);

        var evidenceJson = System.Text.Json.JsonSerializer.Serialize(result.Evidence);
        Assert.DoesNotContain(realKeyId, evidenceJson);
        // El customer_id sintético tampoco se expone crudo, mismo criterio
        // conservador que cualquier otro identificador.
        Assert.DoesNotContain(InvalidCustomerIdGenerator.Generate(), evidenceJson);
    }

    [Fact]
    public void InvalidCustomerIdGenerator_IsDeterministic()
    {
        Assert.Equal(InvalidCustomerIdGenerator.Generate(), InvalidCustomerIdGenerator.Generate());
    }

    [Fact]
    public void InvalidCustomerIdGenerator_IsUuidShaped()
    {
        var value = InvalidCustomerIdGenerator.Generate();
        Assert.Equal(36, value.Length);
        Assert.True(Guid.TryParse(value, out _));
    }

    // ── Presupuesto de llamadas / sin reintentos — compartido por los tres ──

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AllThreeExecutors_NeverExceedOneCallRegardlessOfOutcome(bool throwOnCreate)
    {
        var suspendedClient = new FakeQrClient(throwOnCreate: throwOnCreate);
        var deletedClient    = new FakeQrClient(throwOnCreate: throwOnCreate);
        var invalidClient    = new FakeQrClient(throwOnCreate: throwOnCreate);

        await CreateQrStaticSuspendedKeyExecutor.ExecuteAsync(SuspendedKeyConfig(), suspendedClient, CommitSha(), FixedTime);
        await CreateQrStaticDeletedKeyExecutor.ExecuteAsync(DeletedKeyConfig(), deletedClient, CommitSha(), FixedTime);
        await CreateQrStaticInvalidCustomerExecutor.ExecuteAsync(InvalidCustomerConfig(), invalidClient, CommitSha(), FixedTime);

        Assert.Equal(1, suspendedClient.CreateQrCodeCallCount);
        Assert.Equal(1, deletedClient.CreateQrCodeCallCount);
        Assert.Equal(1, invalidClient.CreateQrCodeCallCount);
    }
}
