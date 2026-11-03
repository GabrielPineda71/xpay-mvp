using Microsoft.Extensions.Configuration;
using Xpay.Api.Common;

namespace Xpay.Api.Tests.Common;

// Interruptores financieros para pruebas, construidos con el AmbienteXpay real.
internal static class ControlesFinancierosPrueba
{
    // Ambiente no productivo sin la clave: habilitado (comportamiento de QA y CI).
    internal static ControlOperacionesFinancieras Habilitado() =>
        new(Config(), AmbienteXpayTests.Ambiente("Development"));

    // Ambiente productivo sin la clave: cerrado (fase visual de PROD).
    internal static ControlOperacionesFinancieras CerradoEnProduccion() =>
        new(Config(), AmbienteXpayTests.Ambiente("Production"));

    // Ambiente productivo con el interruptor abierto explícitamente.
    internal static ControlOperacionesFinancieras AbiertoEnProduccion() =>
        new(Config((ControlOperacionesFinancieras.ClaveInterruptor, "true")), AmbienteXpayTests.Ambiente("Production"));

    private static IConfiguration Config(params (string Clave, string Valor)[] valores) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(valores.Select(v => new KeyValuePair<string, string?>(v.Clave, v.Valor)))
            .Build();
}
