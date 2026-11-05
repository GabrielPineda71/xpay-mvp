using Microsoft.AspNetCore.Authorization;

namespace Xpay.Api.Authorization;

// Configuración central de autorización, extraída de Program.cs para poder
// probarla sin levantar el host.
public static class AutorizacionPoliticas
{
    public static void Configurar(AuthorizationOptions options)
    {
        options.DefaultPolicy = new AuthorizationPolicyBuilder(options.DefaultPolicy)
            .AddRequirements(new ClaveVigenteRequirement())
            .Build();

        // P0-A deny-by-default: toda acción sin [Authorize] ni [AllowAnonymous]
        // exige usuario autenticado con clave vigente (misma política que
        // DefaultPolicy). Los endpoints públicos llevan [AllowAnonymous]
        // explícito, lista cerrada verificada por test.
        options.FallbackPolicy = options.DefaultPolicy;

        options.AddPolicy("SoloAutenticado", policy => policy.RequireAuthenticatedUser());
        options.AddPolicy("KycAprobado", policy => policy.AddRequirements(new KycAprobadoRequirement()));
    }
}
