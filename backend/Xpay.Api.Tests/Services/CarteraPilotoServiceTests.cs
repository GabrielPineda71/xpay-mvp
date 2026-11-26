using Xpay.Api.Services;
using Xpay.Api.Models;
using Xunit;

namespace Xpay.Api.Tests.Services;

public sealed class CarteraPilotoServiceTests
{
    private static CarteraPilotoService.Evidencia Lista => new(true, true, true, true, true, true, true, true, true, true, false, false, 0);

    [Theory]
    [InlineData(null, "true", "50")]
    [InlineData("True", "true", "50")]
    [InlineData("true", "TRUE", "50")]
    [InlineData("true", "true", "0")]
    [InlineData("true", "true", "-1")]
    [InlineData("true", "true", "1.5")]
    [InlineData("true", "true", "2147483648")]
    public void ConfiguracionIncompletaOAmbiguaNoHabilita(string? consultas, string alta, string maximo) =>
        Assert.Null(CarteraPilotoService.Maximo(consultas, alta, maximo));

    [Theory]
    [InlineData("1", 1)]
    [InlineData("50", 50)]
    [InlineData("500", 50)]
    public void ConfiguracionNuncaAdmiteMasDeCincuenta(string maximo, int esperado) =>
        Assert.Equal(esperado, CarteraPilotoService.Maximo("true", "true", maximo));

    [Fact]
    public void CuentaVerificadaPersonalObligatoriaAunqueYaEsteHabilitada()
    {
        foreach (var e in new[] { Lista with { CuentaActiva=false }, Lista with { CorreoVerificado=false },
            Lista with { ClaveVigente=false }, Lista with { RolPersonal=false } })
        {
            var r = CarteraPilotoService.Evaluar(e with { Habilitado=true }, 50);
            Assert.False(r.Disponible); Assert.True(r.Habilitado); Assert.Equal("NO_DISPONIBLE", r.Estado);
        }
    }

    [Fact]
    public void NoAdmiteIdentidadSimuladaNiBiometriaAusente()
    {
        foreach (var e in new[] { Lista with { IdentidadVeriff=false }, Lista with { Biometria=false } })
            Assert.Equal("IDENTIDAD_REQUERIDA", CarteraPilotoService.Evaluar(e, 50).Estado);
    }

    [Fact]
    public void WalletActivaYPasosPublicosNoSeOmiten()
    {
        foreach (var e in new[] { Lista with { WalletActiva=false }, Lista with { PreparacionPublica=false } })
            Assert.False(CarteraPilotoService.Evaluar(e, 50).Disponible);
    }

    [Fact]
    public void ApellidoExplicitoFaltanteSeDistingueDeIdentidadIncompatible()
    {
        var falta = CarteraPilotoService.Evaluar(Lista with { PrimerApellidoPresente=false }, 50);
        Assert.True(falta.RequierePrimerApellido); Assert.False(falta.Disponible); Assert.Equal("REQUIERE_PRIMER_APELLIDO", falta.Estado);
        var distinto = CarteraPilotoService.Evaluar(Lista with { PrimerApellidoCompatible=false }, 50);
        Assert.False(distinto.RequierePrimerApellido); Assert.Equal("IDENTIDAD_REQUERIDA", distinto.Estado);
    }

    [Fact]
    public void SegundoApellidoDeclaradoDiscordanteNoPermiteHabilitar()
    {
        var persona = new Persona { PrimerApellido="PINEDA", SegundoApellido="LOPEZ", ApellidoVerificadoCompleto="PINEDA GARCIA" };
        var compatible = CarteraPilotoService.ApellidoCompatible(persona);
        Assert.False(compatible);
        var estado = CarteraPilotoService.Evaluar(Lista with { PrimerApellidoCompatible=compatible, Plazas=49 }, 50);
        Assert.False(estado.Disponible); Assert.False(estado.Habilitado);
        Assert.Equal("IDENTIDAD_REQUERIDA", estado.Estado);
        Assert.False(estado.RequierePrimerApellido);
    }

    [Theory]
    [InlineData("De la Hoz", "García", "DE LA HOZ GARCÍA")]
    [InlineData("Pineda", null, "PINEDA GARCIA")]
    public void ApellidosCompatiblesConservanLaElegibilidad(string primero, string? segundo, string completo)
    {
        var persona = new Persona { PrimerApellido=primero, SegundoApellido=segundo, ApellidoVerificadoCompleto=completo };
        var compatible = CarteraPilotoService.ApellidoCompatible(persona);
        Assert.True(compatible);
        Assert.True(CarteraPilotoService.Evaluar(Lista with { PrimerApellidoCompatible=compatible }, 50).Disponible);
    }

    [Fact]
    public void FronteraCapacidadYaContadaNoConsumeSegundaPlaza()
    {
        Assert.True(CarteraPilotoService.Evaluar(Lista with { Plazas=49 }, 50).Disponible);
        Assert.Equal("PILOTO_LLENO", CarteraPilotoService.Evaluar(Lista with { Plazas=50 }, 50).Estado);
        Assert.True(CarteraPilotoService.Evaluar(Lista with { Plazas=50, OcupaPlaza=true }, 50).Disponible);
        var replay = CarteraPilotoService.Evaluar(Lista with { Plazas=50, OcupaPlaza=true, Habilitado=true }, 50);
        Assert.True(replay.Disponible && replay.Habilitado); Assert.Equal("HABILITADO", replay.Estado);
    }

    [Fact]
    public void LimiteInvalidoNoPermiteAltaNiReplay()
    {
        foreach (var max in new[] { 0, -1, 51 }) Assert.False(CarteraPilotoService.Evaluar(Lista with { Habilitado=true }, max).Disponible);
    }
}
