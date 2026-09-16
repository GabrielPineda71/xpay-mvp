using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Integrations.Passport;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// XPAY-372 — tests offline de CuentaOperativaService con
// FakePassportCustomerAccountClient (CERO red, CERO HttpClient). A
// diferencia de BrebService, este servicio NO depende de XpayDbContext —
// por eso SÍ puede probarse de punta a punta (orquestación incluida), no
// sólo sus componentes puros — ver comentario de clase en
// CuentaOperativaService.cs.
public class CuentaOperativaServiceTests
{
    private static IConfiguration ConfigConAccountId(string? accountId)
    {
        var builder = new ConfigurationBuilder();
        if (accountId is not null)
            builder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [PassportOptions.EnvOperationalAccountId] = accountId,
            });
        return builder.Build();
    }

    private static PassportAccountResponse SyntheticAccountResponse() => new()
    {
        Id               = "synthetic-operational-account-id-001",
        Status           = "ACTIVE",
        AvailableBalance = new PassportBalance { Value = 2_000_000m, Currency = "COP" },
        PendingBalance   = new PassportBalance { Value = 50_000m, Currency = "COP" },
    };

    [Fact]
    public async Task ObtenerCuentaOperativaAsync_AccountIdConfigurado_ConsultaPassportYDevuelveSanitizado()
    {
        var fake = new FakePassportCustomerAccountClient(accountResult: SyntheticAccountResponse());
        var service = new CuentaOperativaService(
            fake, ConfigConAccountId("synthetic-operational-account-id-001"), NullLogger<CuentaOperativaService>.Instance);

        var result = await service.ObtenerCuentaOperativaAsync();

        Assert.Equal(1, fake.RetrieveAccountCallCount);
        Assert.Equal("synthetic-operational-account-id-001", fake.LastAccountIdRequested);
        Assert.Equal("ACTIVE", result.Estado);
        Assert.Equal(2_000_000m, result.SaldoDisponible);
        Assert.Equal(50_000m, result.SaldoPendiente);
        Assert.NotEqual("synthetic-operational-account-id-001", result.AccountIdFingerprint);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ObtenerCuentaOperativaAsync_AccountIdAusente_LanzaPassportConfigurationExceptionSinLlamarPassport(
        string? accountId)
    {
        var fake = new FakePassportCustomerAccountClient(accountResult: SyntheticAccountResponse());
        var service = new CuentaOperativaService(
            fake, ConfigConAccountId(accountId), NullLogger<CuentaOperativaService>.Instance);

        var ex = await Assert.ThrowsAsync<PassportConfigurationException>(
            () => service.ObtenerCuentaOperativaAsync());

        Assert.Contains(PassportOptions.EnvOperationalAccountId, ex.Message);
        // FASE 8 — fail-closed ANTES de cualquier llamada Passport.
        Assert.Equal(0, fake.RetrieveAccountCallCount);
    }

    [Fact]
    public async Task ObtenerCuentaOperativaAsync_PassportTransportException_SePropagaSinEnvolver()
    {
        var fake = new FakePassportCustomerAccountClient(
            toThrow: new PassportTransportException("Passport respondió con error HTTP 500.", statusCode: 500, safeErrorCode: null, safeErrorMessage: null));
        var service = new CuentaOperativaService(
            fake, ConfigConAccountId("synthetic-operational-account-id-001"), NullLogger<CuentaOperativaService>.Instance);

        await Assert.ThrowsAsync<PassportTransportException>(() => service.ObtenerCuentaOperativaAsync());
    }

    [Fact]
    public async Task ObtenerCuentaOperativaAsync_NuncaLlamaLinkMerchantLinkAccountORetrieveCustomer()
    {
        // El fake lanza NotImplementedException si esas 3 operaciones se
        // invocan alguna vez — este test simplemente confirma que el
        // camino feliz normal no las toca (si las tocara, el assert de
        // arriba en el test del camino feliz ya habría fallado con una
        // excepción distinta a la esperada). Se deja como test explícito
        // y nombrado para que la intención quede documentada en la suite,
        // no sólo como efecto colateral de otro test.
        var fake = new FakePassportCustomerAccountClient(accountResult: SyntheticAccountResponse());
        var service = new CuentaOperativaService(
            fake, ConfigConAccountId("synthetic-operational-account-id-001"), NullLogger<CuentaOperativaService>.Instance);

        await service.ObtenerCuentaOperativaAsync();

        Assert.Equal(1, fake.RetrieveAccountCallCount);
    }
}
