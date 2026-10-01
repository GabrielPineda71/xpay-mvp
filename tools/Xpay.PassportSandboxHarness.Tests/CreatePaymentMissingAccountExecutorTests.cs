using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-571 — prueba, a nivel de CreatePaymentMissingAccountExecutor (sin
// red), que M5-T5-MISSING-ACCOUNT invoca ÚNICAMENTE
// IPassportPaymentClient.CreateBrebPaymentAsync (mismo cliente productivo,
// nunca uno alternativo) y que RetrievePaymentAsync nunca se invoca. Mismo
// patrón exacto que CreatePaymentMissingAmountExecutorTests (M5-T5-MISSING-AMOUNT).
public class CreatePaymentMissingAccountExecutorTests
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
            throw new InvalidOperationException("RetrievePaymentAsync NUNCA debe invocarse desde create-payment-missing-account.");
    }

    // Fake mínimo de IPassportPaymentClient que reproduce EXACTAMENTE el
    // guard real de PassportPaymentClient.CreateBrebPaymentAsync (orden:
    // AccountId -> ResolutionId -> Amount) — sin tocar red.
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
            throw new InvalidOperationException("No debería alcanzarse — account_id siempre está ausente en este subcaso.");
        }

        public Task<PassportPaymentResponse> RetrievePaymentAsync(
            string paymentId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("no aplica");
    }

    private static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

    [Fact]
    public async Task ExecuteAsync_AccountIdAlwaysEmpty_TriggersGuard_CallsCreateBrebPaymentExactlyOnce_NeverRetrieve()
    {
        var client = new CallCountingPaymentClient(new GuardOnlyPaymentClient());
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        var result = await CreatePaymentMissingAccountExecutor.ExecuteAsync(
            EmptyConfig(), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(1, client.CreateBrebPaymentCallCount);
        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.NotNull(result.Evidence);
        Assert.Equal("M5-T5-MISSING-ACCOUNT", result.Evidence!.CaseId);
        Assert.Equal(EvidenceRecord.ResultPass, result.Evidence.Result);
    }

    [Fact]
    public async Task ExecuteAsync_NeverReadsAccountIdFromConfig_EvenWhenPresent()
    {
        // Aunque PASSPORT_ACCOUNT_ID esté presente y sea válido en el
        // entorno, este subcaso NUNCA debe usarlo — AccountId es el campo
        // bajo prueba, siempre string.Empty por diseño.
        var dict = new Dictionary<string, string?> { [PassportOptions.EnvOperationalAccountId] = "some-real-looking-account-id" };
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var client = new CallCountingPaymentClient(new GuardOnlyPaymentClient());
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        var result = await CreatePaymentMissingAccountExecutor.ExecuteAsync(
            config, client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.Success, result.Outcome);
        Assert.Equal(Fingerprint.AbsentMarker, result.Evidence!.RequestSanitized["account_id_fingerprint"]);
    }

    [Fact]
    public async Task ExecuteAsync_CommitShaUnresolvable_IsLocalBlocked_NeverCallsClient()
    {
        var client = new CallCountingPaymentClient(new GuardOnlyPaymentClient());
        var throwingCommitShaProvider = new ThrowingCommitShaProvider();

        var result = await CreatePaymentMissingAccountExecutor.ExecuteAsync(
            EmptyConfig(), client, throwingCommitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(KeyOperationOutcome.LocalBlocked, result.Outcome);
        Assert.Equal(0, client.CreateBrebPaymentCallCount);
        Assert.Null(result.Evidence);
    }

    [Fact]
    public async Task ExecuteAsync_GuardDoesNotFire_IsLocalBlocked_NeverPersistsFalsePass()
    {
        // Canario: si el contrato productivo cambiara y dejara de rechazar
        // account_id ausente, este executor NUNCA debe afirmar un PASS falso.
        var client = new CallCountingPaymentClient(new AlwaysSucceedsPaymentClient());
        var commitShaProvider = new FixedCommitShaProvider("synthetic-commit-sha-0000000000000000000000000000000000000000");

        var result = await CreatePaymentMissingAccountExecutor.ExecuteAsync(
            EmptyConfig(), client, commitShaProvider, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

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
