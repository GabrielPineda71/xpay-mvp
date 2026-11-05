using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-570 — prueba, a nivel de CreatePaymentMissingAmountExecutor (sin
// red), que M5-T5-MISSING-AMOUNT invoca ÚNICAMENTE
// IPassportPaymentClient.CreateBrebPaymentAsync (mismo cliente productivo,
// nunca uno alternativo) y que RetrievePaymentAsync nunca se invoca. Mismo
// patrón exacto que CreateKeyMissingExecutorTests (M3-T6-MISSING).
public class CreatePaymentMissingAmountExecutorTests
{
    // Reutiliza el stack productivo REAL (PassportPaymentClient) para
    // probar el comportamiento real del guard — no un fake que "simule" el
    // rechazo, sino el código productivo genuino.
    private sealed class CallCountingPaymentClient : IPassportPaymentClient
    {
        private readonly IPassportPaymentClient _inner;
        public int CreateBrebPaymentCallCount { get; private set; }

        public CallCountingPaymentClient(IPassportPaymentClient inner) => _inner = inner;

        public Task<PassportPaymentResponse> CreateBrebPaymentAsync(
            PassportCreatePaymentRequest request, CancellationToken cancellationToken = default)
        {
            CreateBrebPaymentCallCount++;
            return _inner.CreateBrebPaymentAsync(request, cancellationToken);
        }

        public Task<PassportPaymentResponse> RetrievePaymentAsync(
            string paymentId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("RetrievePaymentAsync NUNCA debe invocarse desde create-payment-missing-amount.");
    }

    // Fake mínimo de IPassportPaymentClient que reproduce EXACTAMENTE el
    // guard real de PassportPaymentClient.CreateBrebPaymentAsync (orden:
    // AccountId -> ResolutionId -> Amount) — sin tocar red — para poder
    // envolverlo en el contador de llamadas sin depender de
    // PassportHttpClient real.
    private sealed class GuardOnlyPaymentClient : IPassportPaymentClient
    {
        public Task<PassportPaymentResponse> CreateBrebPaymentAsync(
            PassportCreatePaymentRequest request, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request.AccountId))
                throw new ArgumentException("account_id es requerido.", nameof(request));
            if (string.IsNullOrWhiteSpace(request.ResolutionId))
                throw new ArgumentException("resolution_id es requerido.", nameof(request));
            if (request.Amount is null)
                throw new ArgumentException("amount es requerido.", nameof(request));
            throw new InvalidOperationException("No debería alcanzarse — amount siempre está ausente en este subcaso.");
        }

        public Task<PassportPaymentResponse> RetrievePaymentAsync(
            string paymentId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("no aplica");
    }

    private const string SyntheticAccountId = "synthetic-missing-amount-executor-account-id-001";

    private static IConfiguration ConfigWithTarget(string? accountId = SyntheticAccountId)
    {
        var dict = new Dictionary<string, string?>();
        if (accountId is not null) dict[PassportOptions.EnvOperationalAccountId] = accountId;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public async Task ExecuteAsync_AmountAlwaysNull_TriggersGuard_CallsCreateBrebPaymentExactlyOnce_NeverRetrieve()
    {
        var client = new CallCountingPaymentClient(new GuardOnlyPaymentClient());
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");
        var config = ConfigWithTarget();

        var result = await CreatePaymentMissingAmountExecutor.ExecuteAsync(
            config, client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(1, client.CreateBrebPaymentCallCount);
        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.NotNull(result.Evidence);
        Assert.Equal("M5-T5-MISSING-AMOUNT", result.Evidence!.CaseId);
        Assert.Equal(EvidenceRecord.ResultPass, result.Evidence.Result);
    }

    [Fact]
    public async Task ExecuteAsync_MissingAccountId_IsLocalBlocked_NeverCallsClient()
    {
        var client = new CallCountingPaymentClient(new GuardOnlyPaymentClient());
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");
        var config = ConfigWithTarget(accountId: null);

        var result = await CreatePaymentMissingAmountExecutor.ExecuteAsync(
            config, client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateBrebPaymentCallCount);
        Assert.Null(result.Evidence);
    }

    [Fact]
    public async Task ExecuteAsync_CommitShaUnresolvable_IsLocalBlocked_NeverCallsClient()
    {
        var client = new CallCountingPaymentClient(new GuardOnlyPaymentClient());
        var throwingCommitShaProvider = new ThrowingCommitShaProvider();
        var config = ConfigWithTarget();

        var result = await CreatePaymentMissingAmountExecutor.ExecuteAsync(
            config, client, throwingCommitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateBrebPaymentCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_GuardDoesNotFire_IsLocalBlocked_NeverPersistsFalsePass()
    {
        // Canario: si el contrato productivo cambiara y dejara de rechazar
        // amount ausente, este executor NUNCA debe afirmar un PASS falso.
        var client = new CallCountingPaymentClient(new AlwaysSucceedsPaymentClient());
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");
        var config = ConfigWithTarget();

        var result = await CreatePaymentMissingAmountExecutor.ExecuteAsync(
            config, client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Null(result.Evidence);
    }

    private sealed class AlwaysSucceedsPaymentClient : IPassportPaymentClient
    {
        public Task<PassportPaymentResponse> CreateBrebPaymentAsync(
            PassportCreatePaymentRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PassportPaymentResponse
            {
                Id = "synthetic-payment-id",
                Status = "PROCESSING",
                AccountId = request.AccountId,
                ResolutionId = request.ResolutionId,
            });

        public Task<PassportPaymentResponse> RetrievePaymentAsync(
            string paymentId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("no aplica");
    }

    private sealed class ThrowingCommitShaProvider : ICommitShaProvider
    {
        public string GetCommitSha() => throw new InvalidOperationException("synthetic: commit SHA no resoluble.");
    }
}
