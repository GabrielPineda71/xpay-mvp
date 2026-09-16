using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// XPAY-381 FASE 5 test #9 — BrebPaymentReconciliationSelector.EsCandidato
// es una función PURA: sin I/O, sin EF, probada contra objetos en memoria.
public class BrebPaymentReconciliationSelectorTests
{
    private static PassportBrebRetiro Retiro(string estado, string? paymentId) => new()
    {
        Estado = estado,
        PassportPaymentId = paymentId,
    };

    [Fact]
    public void EsCandidato_EnviadoPassportConPaymentId_EsTrue() =>
        Assert.True(BrebPaymentReconciliationSelector.EsCandidato(Retiro("ENVIADO_PASSPORT", "pay-123")));

    [Fact]
    public void EsCandidato_EnviadoPassportSinPaymentId_EsFalse() =>
        Assert.False(BrebPaymentReconciliationSelector.EsCandidato(Retiro("ENVIADO_PASSPORT", null)));

    [Fact]
    public void EsCandidato_EnviadoPassportConPaymentIdVacio_EsFalse() =>
        Assert.False(BrebPaymentReconciliationSelector.EsCandidato(Retiro("ENVIADO_PASSPORT", "   ")));

    [Theory]
    [InlineData("CREADO")]
    [InlineData("PENDIENTE_ENVIO_PASSPORT")]
    [InlineData("LIQUIDADO")]
    [InlineData("RECHAZADO")]
    [InlineData("CANCELADO")]
    public void EsCandidato_CualquierOtroEstadoConPaymentId_EsFalse(string estado) =>
        Assert.False(BrebPaymentReconciliationSelector.EsCandidato(Retiro(estado, "pay-123")));
}
