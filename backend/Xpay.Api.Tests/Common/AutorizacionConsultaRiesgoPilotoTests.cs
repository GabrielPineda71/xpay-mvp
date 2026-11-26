using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Common;
using Xpay.Api.Integrations.MiDecisor;
using Xunit;

namespace Xpay.Api.Tests.Common;

public sealed class AutorizacionConsultaRiesgoPilotoTests
{
    private sealed class Interna(bool permite) : IConsultaRiesgoAutorizacion
    {
        public int Llamadas { get; private set; }
        public Task<bool> TieneAutorizacionVigenteAsync(long usuario, long solicitud, CancellationToken ct = default)
        { Llamadas++; return Task.FromResult(permite && usuario == 7 && solicitud == 10); }
    }

    private static AutorizacionConsultaRiesgoPiloto Crear(string? flag, Interna interna,
        Func<long, CancellationToken, Task<bool>> elegible) => new(interna,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { [MiDecisorProduccionConfig.EnvProductionEnabled] = flag }).Build(), elegible,
        NullLogger<AutorizacionConsultaRiesgoPiloto>.Instance);

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("True")]
    [InlineData("true ")]
    public async Task Sin_bandera_exacta_no_lee_cohorte_ni_solicitud(string? flag)
    {
        var interna = new Interna(true);
        var llamadas = 0;
        var gate = Crear(flag, interna, (_, _) => { llamadas++; return Task.FromResult(true); });
        Assert.False(await gate.TieneAutorizacionVigenteAsync(7, 10));
        Assert.Equal(0, llamadas);
        Assert.Equal(0, interna.Llamadas);
    }

    [Fact]
    public async Task No_miembro_o_requisitos_revocados_no_llegan_a_autorizacion_interna()
    {
        var interna = new Interna(true);
        var gate = Crear("true", interna, (_, _) => Task.FromResult(false));
        Assert.False(await gate.TieneAutorizacionVigenteAsync(7, 10));
        Assert.Equal(0, interna.Llamadas);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Pertenencia_no_reemplaza_consentimiento_e_historial(bool permiteInterna, bool esperado)
    {
        var interna = new Interna(permiteInterna);
        var gate = Crear("true", interna, (id, _) => Task.FromResult(id == 7));
        Assert.Equal(esperado, await gate.TieneAutorizacionVigenteAsync(7, 10));
        Assert.Equal(1, interna.Llamadas);
    }

    [Fact]
    public async Task Error_tecnico_no_se_interpreta_como_habilitacion()
    {
        var interna = new Interna(true);
        var gate = Crear("true", interna, (_, _) => throw new InvalidOperationException());
        Assert.False(await gate.TieneAutorizacionVigenteAsync(7, 10));
        Assert.Equal(0, interna.Llamadas);
    }

    [Fact]
    public async Task Cancelacion_se_propaga_y_no_consulta()
    {
        var interna = new Interna(true);
        var gate = Crear("true", interna, (_, _) => throw new OperationCanceledException());
        await Assert.ThrowsAsync<OperationCanceledException>(() => gate.TieneAutorizacionVigenteAsync(7, 10));
        Assert.Equal(0, interna.Llamadas);
    }
}
