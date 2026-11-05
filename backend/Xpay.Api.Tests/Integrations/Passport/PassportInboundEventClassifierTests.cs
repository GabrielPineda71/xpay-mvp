using Xpay.Api.Integrations.Passport;
using Xunit;

namespace Xpay.Api.Tests.Integrations.Passport;

// XPAY-594 — clasificador AISLADO del flujo entrante. Verifica también que el
// ciclo saliente y lo desconocido tengan un tratamiento explícito y seguro.
public class PassportInboundEventClassifierTests
{
    [Theory]
    [InlineData("payment.inbound.received",  PassportInboundEventKind.Received)]
    [InlineData("payment.inbound.confirmed", PassportInboundEventKind.Confirmed)]
    [InlineData("payment.inbound.settled",   PassportInboundEventKind.Settled)]
    [InlineData("payment.inbound.rejected",  PassportInboundEventKind.Rejected)]
    public void LosCuatroEventosEntrantes_SeReconocen(string nombre, PassportInboundEventKind esperado)
    {
        Assert.Equal(esperado, PassportInboundEventClassifier.Classify(nombre));
        Assert.True(PassportInboundEventClassifier.IsInbound(esperado));
    }

    [Theory]
    [InlineData("payment.outbound.confirmed")]
    [InlineData("payment.outbound.settled")]
    [InlineData("payment.outbound.rejected")]
    public void ElCicloSaliente_NoSeMezclaConElEntrante(string nombre)
    {
        var kind = PassportInboundEventClassifier.Classify(nombre);
        Assert.Equal(PassportInboundEventKind.NotInbound, kind);
        Assert.False(PassportInboundEventClassifier.IsInbound(kind));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("payment.inbound")]
    [InlineData("payment.inbound.unknown")]
    [InlineData("key.updated")]
    public void EventoDesconocido_SeManejaDeFormaExplicitaYSegura(string? nombre)
    {
        var kind = PassportInboundEventClassifier.Classify(nombre);
        Assert.Equal(PassportInboundEventKind.Unknown, kind);
        Assert.False(PassportInboundEventClassifier.IsInbound(kind));
    }

    [Fact]
    public void ClasificacionEsInsensibleAMayusculasYEspacios()
    {
        Assert.Equal(
            PassportInboundEventKind.Settled,
            PassportInboundEventClassifier.Classify("  Payment.Inbound.Settled  "));
    }

    [Fact]
    public void EstadosTerminales_SonSettledYRejected()
    {
        Assert.True(PassportInboundEventClassifier.IsTerminal(PassportInboundEventKind.Settled));
        Assert.True(PassportInboundEventClassifier.IsTerminal(PassportInboundEventKind.Rejected));
        Assert.False(PassportInboundEventClassifier.IsTerminal(PassportInboundEventKind.Received));
        Assert.False(PassportInboundEventClassifier.IsTerminal(PassportInboundEventKind.Confirmed));
    }
}
