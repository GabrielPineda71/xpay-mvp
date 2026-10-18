using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Xpay.Api.Authorization;
using Xunit;

namespace Xpay.Api.Tests.Common;

// P0-A — configuración central de autorización (deny-by-default). Sin host.
public class AutorizacionPoliticasTests
{
    private static AuthorizationOptions Configuradas()
    {
        var options = new AuthorizationOptions();
        AutorizacionPoliticas.Configurar(options);
        return options;
    }

    [Fact]
    public void FallbackPolicy_ExisteExigeAutenticacionYClaveVigente()
    {
        var o = Configuradas();

        Assert.NotNull(o.FallbackPolicy);
        Assert.Contains(o.FallbackPolicy!.Requirements, r => r is DenyAnonymousAuthorizationRequirement);
        Assert.Contains(o.FallbackPolicy.Requirements, r => r is ClaveVigenteRequirement);
        Assert.Same(o.DefaultPolicy, o.FallbackPolicy);
    }

    [Fact]
    public void DefaultPolicy_ConservaClaveVigente()
    {
        var o = Configuradas();

        Assert.Contains(o.DefaultPolicy.Requirements, r => r is DenyAnonymousAuthorizationRequirement);
        Assert.Contains(o.DefaultPolicy.Requirements, r => r is ClaveVigenteRequirement);
    }

    [Fact]
    public void SoloAutenticado_SeConservaSinClaveVigente()
    {
        var p = Configuradas().GetPolicy("SoloAutenticado");

        Assert.NotNull(p);
        Assert.Contains(p!.Requirements, r => r is DenyAnonymousAuthorizationRequirement);
        Assert.DoesNotContain(p.Requirements, r => r is ClaveVigenteRequirement);
    }

    [Fact]
    public void KycAprobado_SeConserva()
    {
        var p = Configuradas().GetPolicy("KycAprobado");

        Assert.NotNull(p);
        Assert.Contains(p!.Requirements, r => r is KycAprobadoRequirement);
    }
}
