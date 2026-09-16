using Xpay.Api.DTOs;
using Xpay.Api.Integrations.Passport;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// XPAY-373 FASE 15 — tests offline/puros de BrebPaymentRequestBuilder. CERO
// I/O, CERO base de datos, CERO red.
public class BrebPaymentRequestBuilderTests
{
    private const string OperationalAccountId = "synthetic-operational-account-id-001";

    private static PassportBrebRetiro RetiroVigente(decimal valor = 5000m, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        return new PassportBrebRetiro
        {
            IdBrebRetiro                   = 1,
            IdWallet                       = 7,
            IdBrebLlave                    = 42,
            TipoSujeto                     = "USUARIO",
            Valor                          = valor,
            Moneda                         = "COP",
            Estado                         = "PENDIENTE_ENVIO_PASSPORT",
            PassportResolutionId           = "synthetic-resolution-id-001",
            PassportResolutionExpiresAtUtc = now.AddMinutes(10),
            ReferenciaInterna              = "REF001",
            IdempotencyKey                 = "IDEM001",
            FechaSolicitud                 = now,
        };
    }

    // FASE 15 test #1 — source account SIEMPRE server-side: el builder usa
    // exactamente el parámetro operationalAccountId, nunca ningún campo del
    // retiro (que no tiene ninguna columna de account_id de origen).
    [Fact]
    public void Build_UsaAccountIdDelParametro_NuncaDelRetiro()
    {
        var now = DateTime.UtcNow;
        var request = BrebPaymentRequestBuilder.Build(RetiroVigente(nowUtc: now), OperationalAccountId, now);

        Assert.Equal(OperationalAccountId, request.AccountId);
    }

    // FASE 15 test #2 — payment usa el resolution_id DEL RETIRO (snapshot
    // inmutable), no uno inventado ni el de otra fuente.
    [Fact]
    public void Build_UsaResolutionIdDelRetiro()
    {
        var now = DateTime.UtcNow;
        var retiro = RetiroVigente(nowUtc: now);
        var request = BrebPaymentRequestBuilder.Build(retiro, OperationalAccountId, now);

        Assert.Equal(retiro.PassportResolutionId, request.ResolutionId);
    }

    // FASE 15 test #3 — amount correcto (coincide con el valor del retiro).
    [Fact]
    public void Build_AmountValueCoincideConElValorDelRetiro()
    {
        var now = DateTime.UtcNow;
        var retiro = RetiroVigente(valor: 12345.67m, nowUtc: now);
        var request = BrebPaymentRequestBuilder.Build(retiro, OperationalAccountId, now);

        Assert.Equal("12345.67", request.Amount.Value);
    }

    // FASE 15 test #4 — currency siempre COP.
    [Fact]
    public void Build_CurrencySiempreCOP()
    {
        var now = DateTime.UtcNow;
        var request = BrebPaymentRequestBuilder.Build(RetiroVigente(nowUtc: now), OperationalAccountId, now);

        Assert.Equal("COP", request.Amount.Currency);
    }

    // FASE 15 test #16 — resolution expirada → NO payment.
    [Fact]
    public void Build_ResolutionExpirada_LanzaResolutionExpiradaException_SinDevolverRequest()
    {
        var now = DateTime.UtcNow;
        var retiro = RetiroVigente(nowUtc: now);
        retiro.PassportResolutionExpiresAtUtc = now.AddMinutes(-1); // vencida hace 1 minuto

        Assert.Throws<BrebPaymentRequestBuilder.ResolutionExpiradaException>(
            () => BrebPaymentRequestBuilder.Build(retiro, OperationalAccountId, now));
    }

    [Fact]
    public void Build_ResolutionAusente_LanzaResolutionExpiradaException()
    {
        var now = DateTime.UtcNow;
        var retiro = RetiroVigente(nowUtc: now);
        retiro.PassportResolutionId           = null;
        retiro.PassportResolutionExpiresAtUtc = null;

        Assert.Throws<BrebPaymentRequestBuilder.ResolutionExpiradaException>(
            () => BrebPaymentRequestBuilder.Build(retiro, OperationalAccountId, now));
    }

    [Fact]
    public void Build_ResolutionExactamenteEnElLimite_SeConsideraVencida()
    {
        var now = DateTime.UtcNow;
        var retiro = RetiroVigente(nowUtc: now);
        retiro.PassportResolutionExpiresAtUtc = now; // exactamente ahora — no "todavía vigente"

        Assert.Throws<BrebPaymentRequestBuilder.ResolutionExpiradaException>(
            () => BrebPaymentRequestBuilder.Build(retiro, OperationalAccountId, now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_OperationalAccountIdAusente_LanzaPassportConfigurationException(string? accountId)
    {
        var now = DateTime.UtcNow;

        Assert.Throws<PassportConfigurationException>(
            () => BrebPaymentRequestBuilder.Build(RetiroVigente(nowUtc: now), accountId, now));
    }

    [Fact]
    public void Build_MontoNoPositivo_LanzaInvalidOperationException()
    {
        var now = DateTime.UtcNow;
        var retiro = RetiroVigente(valor: 0m, nowUtc: now);

        Assert.Throws<InvalidOperationException>(
            () => BrebPaymentRequestBuilder.Build(retiro, OperationalAccountId, now));
    }

    // ── FASE 15 tests #17/#18/#19 — el contrato del request de usuario ───
    // estructuralmente no permite account_id/payment_id/resolution_id/
    // destination key arbitrarios: el DTO expuesto al frontend sólo tiene
    // Monto. Se verifica por reflexión para que este test falle
    // automáticamente si algún día se agrega un campo peligroso sin darse
    // cuenta.
    [Fact]
    public void SolicitarRetiroRealRequest_SoloExponeMonto_NuncaIdentificadoresSensibles()
    {
        var propiedades = typeof(SolicitarRetiroRealRequest).GetProperties()
            .Select(p => p.Name).ToArray();

        Assert.Single(propiedades);
        Assert.Equal("Monto", propiedades[0]);
    }
}
