using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-522 — prueba, a nivel de los tres executors M4-T6 (sin red), que:
// cada uno usa EXCLUSIVAMENTE su fixture/generador correspondiente
// (nunca cae en fallback), que un fingerprint incorrecto bloquea ANTES de
// cualquier HTTP, que el payload DYNAMIC construido coincide con el ya
// probado en M4-T4, que qr_code_reference es fresca por ejecución, que
// tanto SUCCESS como ERROR de Passport se capturan sin forzar un
// juicio de certificación (§10), y que cada executor invoca ÚNICAMENTE
// CreateQrCodeAsync como máximo una vez (nunca Decode/otros).
public class CreateQrDynamicNegativeCasesExecutorTests
{
    private sealed class FakeQrClient : IPassportQrClient
    {
        private readonly bool _throwOnCreate;
        private readonly PassportTransportException? _exceptionToThrow;
        private readonly PassportQrCodeResponse? _response;

        public int CreateQrCodeCallCount { get; private set; }
        public int DecodeQrCodeCallCount { get; private set; }
        public int DecodeQrCodeWithRawCaptureCallCount { get; private set; }
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
                throw new PassportTransportException("Passport respondió con error HTTP (sintético).");
            return Task.FromResult(_response ?? new PassportQrCodeResponse
            {
                Id = "synthetic-m4t6-qr-id",
                Status = "ACTIVE",
                Type = "DYNAMIC",
                QrCodeData = "00020101...synthetic-m4t6-emv-payload...6304ABCD",
                QrCodeImage = "data:image/png;base64,synthetic-m4t6-qr-image",
                KeyId = "synthetic-key-id-echoed-back",
                CustomerId = "synthetic-customer-id-echoed-back",
                QrCodeReference = "SYNM4T6REF01",
            });
        }

        public Task<PassportDecodeQrCodeResponse> DecodeQrCodeAsync(
            PassportDecodeQrCodeRequest request, CancellationToken cancellationToken = default)
        {
            DecodeQrCodeCallCount++;
            throw new InvalidOperationException("DecodeQrCodeAsync NUNCA debe invocarse desde ningún subcaso M4-T6.");
        }

        public Task<(PassportDecodeQrCodeResponse? Response, string? RawResponseJson)> DecodeQrCodeWithRawCaptureAsync(
            PassportDecodeQrCodeRequest request, CancellationToken cancellationToken = default)
        {
            DecodeQrCodeWithRawCaptureCallCount++;
            throw new InvalidOperationException("DecodeQrCodeWithRawCaptureAsync NUNCA debe invocarse desde ningún subcaso M4-T6.");
        }
    }

    private const string SyntheticSuspendedKeyId = "synthetic-m4t6-suspended-key-id";
    private const string SyntheticDeletedKeyId   = "synthetic-m4t6-deleted-key-id";
    private const string SyntheticActiveKeyId    = "synthetic-m4t6-active-key-id";
    private const string SyntheticCustomerId     = "synthetic-m4t6-customer-id";

    private static readonly DateTime FixedTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static IConfiguration Config(
        string? suspendedKeyId = SyntheticSuspendedKeyId,
        string? deletedKeyId = SyntheticDeletedKeyId,
        string? activeKeyId = SyntheticActiveKeyId,
        string? customerId = SyntheticCustomerId)
    {
        var dict = new Dictionary<string, string?>();
        if (suspendedKeyId is not null) dict[HarnessTargetConfig.EnvQrSuspendedKeyId] = suspendedKeyId;
        if (deletedKeyId is not null) dict[HarnessTargetConfig.EnvQrDeletedKeyId] = deletedKeyId;
        if (activeKeyId is not null) dict[HarnessTargetConfig.EnvQrKeyId] = activeKeyId;
        if (customerId is not null) dict[HarnessTargetConfig.EnvCustomerId] = customerId;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static ICommitShaProvider CommitSha() =>
        new FixedCommitShaProvider("synthetic-m4t6-commit-sha-0000000000000000000000000000000000000000");

    // XPAY-522 — SHA-256 no es reversible: un test no puede construir un
    // key_id sintético cuyo fingerprint coincida con la constante
    // histórica real hardcodeada en cada executor (ExpectedKeyIdFingerprint).
    // Por eso cada wrapper pasa SIEMPRE `expectedKeyIdFingerprintOverride`
    // = fingerprint del fixture sintético de ESTE archivo de tests — nunca
    // usado en producción (ese parámetro es null salvo en tests). Lo que
    // varía entre "fingerprint correcto"/"fingerprint incorrecto" es el
    // KEY_ID que trae la config, no el valor esperado.
    private static Task<KeyOperationResult> ExecuteSuspended(IConfiguration config, FakeQrClient client) =>
        CreateQrDynamicSuspendedKeyExecutor.ExecuteAsync(
            config, client, CommitSha(), FixedTime,
            expectedKeyIdFingerprintOverride: Fingerprint.Compute(SyntheticSuspendedKeyId));

    private static Task<KeyOperationResult> ExecuteDeleted(IConfiguration config, FakeQrClient client) =>
        CreateQrDynamicDeletedKeyExecutor.ExecuteAsync(
            config, client, CommitSha(), FixedTime,
            expectedKeyIdFingerprintOverride: Fingerprint.Compute(SyntheticDeletedKeyId));

    private static Task<KeyOperationResult> ExecuteInvalidCustomer(IConfiguration config, FakeQrClient client) =>
        CreateQrDynamicInvalidCustomerExecutor.ExecuteAsync(
            config, client, CommitSha(), FixedTime,
            expectedKeyIdFingerprintOverride: Fingerprint.Compute(SyntheticActiveKeyId));

    // ══════════════════════════════════════════════════════════════════
    // M4-T6-A — SUSPENDED (XPAY-522 §14)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SuspendedKey_CorrectFingerprint_PreparesAndCallsExactlyOnce()
    {
        var client = new FakeQrClient();
        var result = await ExecuteSuspended(Config(), client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal(1, client.CreateQrCodeCallCount);
        Assert.Equal(0, client.DecodeQrCodeCallCount);
        Assert.Equal(0, client.DecodeQrCodeWithRawCaptureCallCount);
    }

    [Fact]
    public async Task SuspendedKey_WrongFingerprint_IsLocalBlocked_ZeroHttp()
    {
        var client = new FakeQrClient();
        var result = await ExecuteSuspended(
            Config(suspendedKeyId: "some-other-key-id-not-the-fixture"), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateQrCodeCallCount);
        Assert.Null(result.Evidence);
    }

    [Fact]
    public async Task SuspendedKey_PayloadIsFullDynamicShape_WithFreshReference()
    {
        var client = new FakeQrClient();
        await ExecuteSuspended(Config(), client);

        var sent = client.LastRequest!;
        Assert.Equal(PassportQrType.DYNAMIC, sent.Type);
        Assert.Equal(PassportQrChannel.MPOS, sent.Channel);
        Assert.NotNull(sent.Vat);
        Assert.NotNull(sent.Inc);
        Assert.NotNull(sent.Tip);
        Assert.NotNull(sent.Amount);
        Assert.Equal("10000.00", sent.Amount!.Value);
        Assert.Equal("COP", sent.Amount.Currency);
        Assert.Null(sent.AdditionalInfo);
        Assert.NotNull(sent.QrCodeReference);
    }

    [Fact]
    public async Task SuspendedKey_QrCodeReference_FreshPerExecution()
    {
        var client1 = new FakeQrClient();
        var client2 = new FakeQrClient();
        await ExecuteSuspended(Config(), client1);
        await ExecuteSuspended(Config(), client2);

        Assert.NotEqual(client1.LastRequest!.QrCodeReference, client2.LastRequest!.QrCodeReference);
    }

    [Fact]
    public async Task SuspendedKey_FakePassportSuccess_CapturedAsRequestAccepted_NoJudgment()
    {
        var client = new FakeQrClient(response: new PassportQrCodeResponse { Id = "x", Status = "ACTIVE", Type = "DYNAMIC" });
        var result = await ExecuteSuspended(Config(), client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal("M4-T6-A", result.Evidence!.CaseId);
        Assert.True((bool)result.Evidence.ResponseSanitized["execution_completed"]!);
        Assert.True((bool)result.Evidence.ResponseSanitized["request_accepted"]!);
        Assert.Equal("QR_CREATED", result.Evidence.ResponseSanitized["passport_behavior_observed"]);
        // Result=PASS refleja únicamente "2xx recibido" — transporte, no
        // un veredicto de certificación (mismo criterio repo-wide).
        Assert.Equal(EvidenceRecord.ResultPass, result.Evidence.Result);
    }

    [Fact]
    public async Task SuspendedKey_FakePassportError_CapturedSanitized_NoJudgment()
    {
        var client = new FakeQrClient(exceptionToThrow: new PassportTransportException(
            "sintético", statusCode: 409, safeErrorCode: "key_suspended", safeErrorMessage: "Key is suspended."));
        var result = await ExecuteSuspended(Config(), client);

        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        Assert.Equal("M4-T6-A", result.Evidence!.CaseId);
        Assert.False((bool)result.Evidence.ResponseSanitized["request_accepted"]!);
        Assert.Equal("HTTP_ERROR_RECEIVED", result.Evidence.ResponseSanitized["passport_behavior_observed"]);
        Assert.Equal(409, result.Evidence.ResponseSanitized["observed_http_status"]);
        Assert.Equal("key_suspended", result.Evidence.ResponseSanitized["safe_error_code"]);
    }

    [Fact]
    public async Task SuspendedKey_MissingTarget_IsLocalBlocked()
    {
        var client = new FakeQrClient();
        var result = await ExecuteSuspended(Config(suspendedKeyId: null), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateQrCodeCallCount);
    }

    // ══════════════════════════════════════════════════════════════════
    // M4-T6-B — DELETED (XPAY-522 §15)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeletedKey_CorrectFingerprint_UsesExistingFixture_CallsExactlyOnce()
    {
        var client = new FakeQrClient();
        var result = await ExecuteDeleted(Config(), client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal(SyntheticDeletedKeyId, client.LastRequest!.KeyId);
        Assert.Equal(1, client.CreateQrCodeCallCount);
    }

    [Fact]
    public async Task DeletedKey_WrongFingerprint_IsLocalBlocked_ZeroHttp()
    {
        var client = new FakeQrClient();
        var result = await ExecuteDeleted(Config(deletedKeyId: "some-other-key-id"), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateQrCodeCallCount);
    }

    [Fact]
    public async Task DeletedKey_PayloadIsFullDynamicShape()
    {
        var client = new FakeQrClient();
        await ExecuteDeleted(Config(), client);

        var sent = client.LastRequest!;
        Assert.Equal(PassportQrType.DYNAMIC, sent.Type);
        Assert.Equal(PassportQrChannel.MPOS, sent.Channel);
        Assert.NotNull(sent.Vat);
        Assert.NotNull(sent.Inc);
        Assert.NotNull(sent.Tip);
        Assert.NotNull(sent.Amount);
    }

    [Fact]
    public async Task DeletedKey_FakeNotFoundError_CapturedSanitized()
    {
        var client = new FakeQrClient(exceptionToThrow: new PassportTransportException(
            "sintético", statusCode: 404, safeErrorCode: "resource_not_found", safeErrorMessage: "key not found."));
        var result = await ExecuteDeleted(Config(), client);

        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        Assert.False((bool)result.Evidence!.ResponseSanitized["request_accepted"]!);
        Assert.Equal(404, result.Evidence.ResponseSanitized["observed_http_status"]);
        Assert.Equal("resource_not_found", result.Evidence.ResponseSanitized["safe_error_code"]);
    }

    [Fact]
    public async Task DeletedKey_FakeSuccess_AlsoCapturedFactually_NoInventedContract()
    {
        // Regla explícita XPAY-522 §7/§15: un SUCCESS también debe poder
        // registrarse sin inventar que "debería" haber fallado.
        var client = new FakeQrClient(response: new PassportQrCodeResponse { Id = "x", Status = "ACTIVE", Type = "DYNAMIC" });
        var result = await ExecuteDeleted(Config(), client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.True((bool)result.Evidence!.ResponseSanitized["request_accepted"]!);
        Assert.Equal("QR_CREATED", result.Evidence.ResponseSanitized["passport_behavior_observed"]);
    }

    [Fact]
    public async Task DeletedKey_NeverCallsKeyMutation_OnlyCreateQrCode()
    {
        var client = new FakeQrClient();
        await ExecuteDeleted(Config(), client);

        Assert.Equal(1, client.CreateQrCodeCallCount);
        Assert.Equal(0, client.DecodeQrCodeCallCount);
        Assert.Equal(0, client.DecodeQrCodeWithRawCaptureCallCount);
        // IPassportQrClient no expone ningún método de mutación de llave
        // (Create/Delete/Suspend/Activate/List Keys viven en
        // IPassportKeyClient, ni siquiera inyectado en este executor) —
        // la propia firma del método es la garantía estructural.
    }

    // ══════════════════════════════════════════════════════════════════
    // M4-T6-C — INVALID CUSTOMER (XPAY-522 §16)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task InvalidCustomer_UsesInvalidCustomerIdGenerator_NeverRealCustomerId()
    {
        var client = new FakeQrClient();
        var result = await ExecuteInvalidCustomer(Config(), client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal(InvalidCustomerIdGenerator.Generate(), client.LastRequest!.CustomerId);
        Assert.NotEqual(SyntheticCustomerId, client.LastRequest.CustomerId);
    }

    [Fact]
    public async Task InvalidCustomer_WrongActiveKeyFingerprint_IsLocalBlocked_ZeroHttp()
    {
        var client = new FakeQrClient();
        var result = await ExecuteInvalidCustomer(Config(activeKeyId: "some-other-active-key-id"), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateQrCodeCallCount);
    }

    [Fact]
    public async Task InvalidCustomer_PayloadIsFullDynamicShape()
    {
        var client = new FakeQrClient();
        await ExecuteInvalidCustomer(Config(), client);

        var sent = client.LastRequest!;
        Assert.Equal(PassportQrType.DYNAMIC, sent.Type);
        Assert.Equal(PassportQrChannel.MPOS, sent.Channel);
        Assert.NotNull(sent.Amount);
        Assert.Equal("10000.00", sent.Amount!.Value);
    }

    [Fact]
    public async Task InvalidCustomer_FakeUuidError_CapturedSanitized()
    {
        var client = new FakeQrClient(exceptionToThrow: new PassportTransportException(
            "sintético", statusCode: 400, safeErrorCode: "invalid_parameter_value", safeErrorMessage: "UUID version 4 expected"));
        var result = await ExecuteInvalidCustomer(Config(), client);

        Assert.Equal(KeyOperationOutcome.PassportFailure, result.Outcome);
        Assert.False((bool)result.Evidence!.ResponseSanitized["request_accepted"]!);
        Assert.Equal(400, result.Evidence.ResponseSanitized["observed_http_status"]);
        Assert.Equal("invalid_parameter_value", result.Evidence.ResponseSanitized["safe_error_code"]);
    }

    [Fact]
    public async Task InvalidCustomer_FakeSuccess_AlsoCapturedFactually()
    {
        var client = new FakeQrClient(response: new PassportQrCodeResponse { Id = "x", Status = "ACTIVE", Type = "DYNAMIC" });
        var result = await ExecuteInvalidCustomer(Config(), client);

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.True((bool)result.Evidence!.ResponseSanitized["request_accepted"]!);
    }

    [Fact]
    public async Task InvalidCustomer_EvidenceMarksCustomerIdKindSyntheticInvalid_NeverRawValue()
    {
        var client = new FakeQrClient();
        var result = await ExecuteInvalidCustomer(Config(), client);

        Assert.Equal("SYNTHETIC_INVALID", result.Evidence!.RequestSanitized["customer_id_kind"]);
        var evidenceJson = System.Text.Json.JsonSerializer.Serialize(result.Evidence);
        Assert.DoesNotContain(InvalidCustomerIdGenerator.Generate(), evidenceJson);
    }

    [Fact]
    public async Task InvalidCustomer_MissingActiveKey_IsLocalBlocked()
    {
        var client = new FakeQrClient();
        var result = await ExecuteInvalidCustomer(Config(activeKeyId: null), client);

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateQrCodeCallCount);
    }

    // ══════════════════════════════════════════════════════════════════
    // Aislamiento entre los tres subcasos — cada uno usa su propio target
    // aunque los otros dos targets/keys estén también presentes.
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SuspendedKey_NeverFallsBackToDeletedOrActiveKey_EvenWhenBothPresent()
    {
        var client = new FakeQrClient();
        await ExecuteSuspended(Config(), client);

        Assert.Equal(SyntheticSuspendedKeyId, client.LastRequest!.KeyId);
    }

    [Fact]
    public async Task DeletedKey_NeverFallsBackToSuspendedOrActiveKey_EvenWhenBothPresent()
    {
        var client = new FakeQrClient();
        await ExecuteDeleted(Config(), client);

        Assert.Equal(SyntheticDeletedKeyId, client.LastRequest!.KeyId);
    }

    [Fact]
    public async Task InvalidCustomer_NeverFallsBackToSuspendedOrDeletedKey_EvenWhenBothPresent()
    {
        var client = new FakeQrClient();
        await ExecuteInvalidCustomer(Config(), client);

        Assert.Equal(SyntheticActiveKeyId, client.LastRequest!.KeyId);
    }
}
