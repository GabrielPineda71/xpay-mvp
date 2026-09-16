using Xpay.Api.Integrations.Passport;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// XPAY-372 — tests offline/puros de CuentaOperativaResponseMapper. CERO
// I/O, CERO red.
public class CuentaOperativaResponseMapperTests
{
    private const string AccountId = "synthetic-operational-account-id-001";

    private static PassportAccountResponse FullResponse() => new()
    {
        Id               = AccountId,
        CustomerId       = "synthetic-operational-customer-id-001",
        AccountNumber    = "9999999999",
        AccountType      = "SAVINGS",
        Status           = "ACTIVE",
        AvailableBalance = new PassportBalance { Value = 1_500_000m, Currency = "COP" },
        PendingBalance   = new PassportBalance { Value = 0m, Currency = "COP" },
        CreatedAt        = "2026-01-01T00:00:00.000000Z",
        UpdatedAt        = "2026-01-01T00:00:00.000000Z",
    };

    [Fact]
    public void ToSanitizedResponse_MapeaEstadoCurrencyYSaldos()
    {
        var now = new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);

        var dto = CuentaOperativaResponseMapper.ToSanitizedResponse(AccountId, FullResponse(), now);

        Assert.Equal("ACTIVE", dto.Estado);
        Assert.Equal("COP", dto.Currency);
        Assert.Equal(1_500_000m, dto.SaldoDisponible);
        Assert.Equal(0m, dto.SaldoPendiente);
        Assert.Equal(now, dto.ConsultadoEnUtc);
    }

    [Fact]
    public void ToSanitizedResponse_NuncaExponeAccountIdEnClaro()
    {
        var dto = CuentaOperativaResponseMapper.ToSanitizedResponse(AccountId, FullResponse(), DateTime.UtcNow);

        Assert.DoesNotContain(AccountId, dto.AccountIdFingerprint);
        Assert.NotEqual(AccountId, dto.AccountIdFingerprint);
        Assert.NotEmpty(dto.AccountIdFingerprint);
    }

    [Fact]
    public void ToSanitizedResponse_MismoAccountId_ProduceSiempreElMismoFingerprint()
    {
        var dto1 = CuentaOperativaResponseMapper.ToSanitizedResponse(AccountId, FullResponse(), DateTime.UtcNow);
        var dto2 = CuentaOperativaResponseMapper.ToSanitizedResponse(AccountId, FullResponse(), DateTime.UtcNow);

        Assert.Equal(dto1.AccountIdFingerprint, dto2.AccountIdFingerprint);
    }

    [Fact]
    public void ToSanitizedResponse_SinBalances_DevuelveCamposNull()
    {
        var response = FullResponse();
        response.AvailableBalance = null;
        response.PendingBalance   = null;

        var dto = CuentaOperativaResponseMapper.ToSanitizedResponse(AccountId, response, DateTime.UtcNow);

        Assert.Null(dto.Currency);
        Assert.Null(dto.SaldoDisponible);
        Assert.Null(dto.SaldoPendiente);
    }

    // XPAY-372 — CustomerId/AccountNumber/CreatedAt/UpdatedAt de la
    // respuesta NUNCA deben aparecer literalmente reflejados en el DTO
    // (el DTO no tiene ninguna propiedad para ellos) — este test documenta
    // esa ausencia comparando el objeto DTO completo contra los valores
    // sensibles, no sólo inspeccionando su forma.
    [Fact]
    public void ToSanitizedResponse_DtoNoContieneCustomerIdNiAccountNumber()
    {
        var response = FullResponse();
        var dto = CuentaOperativaResponseMapper.ToSanitizedResponse(AccountId, response, DateTime.UtcNow);

        var serialized = System.Text.Json.JsonSerializer.Serialize(dto);
        Assert.DoesNotContain(response.CustomerId!, serialized);
        Assert.DoesNotContain(response.AccountNumber!, serialized);
        Assert.DoesNotContain(AccountId, serialized);
    }
}
