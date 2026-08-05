using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;

namespace Xpay.Api.Authorization;

// Fase USUARIOS-ADMIN-5: requisito agregado a la DefaultPolicy (ver Program.cs)
// — se combina automáticamente con todo [Authorize]/[Authorize(Roles=...)] sin
// política explícita, en toda la aplicación, sin tocar esos controladores.
// Bloquea el acceso mientras usuarios.requiere_cambio_clave = true, consultado
// en vivo contra la base de datos en cada request (nunca confía en el claim
// del JWT, que solo se usa como ayuda de UX en el frontend — ver AuthService).
// El endpoint POST /api/auth/cambiar-clave-obligatoria queda exento porque usa
// [Authorize(Policy = "SoloAutenticado")], una política explícita que NO se
// combina con DefaultPolicy.
public class ClaveVigenteRequirement : IAuthorizationRequirement
{
}

public class ClaveVigenteAuthorizationHandler : AuthorizationHandler<ClaveVigenteRequirement>
{
    private readonly XpayDbContext _db;

    public ClaveVigenteAuthorizationHandler(XpayDbContext db) => _db = db;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ClaveVigenteRequirement requirement)
    {
        var idUsuarioClaim = context.User.FindFirst("idUsuario")?.Value;
        if (!long.TryParse(idUsuarioClaim, out var idUsuario))
        {
            // Sin claim idUsuario válido: no es un JWT de este sistema — este
            // requisito no le compete, no bloquea (otros requisitos de la
            // policy, como RequireAuthenticatedUser, ya se encargan de eso).
            context.Succeed(requirement);
            return;
        }

        var requiereCambioClave = await _db.Usuarios
            .Where(u => u.IdUsuario == idUsuario)
            .Select(u => u.RequiereCambioClave)
            .FirstOrDefaultAsync();

        if (!requiereCambioClave)
            context.Succeed(requirement);
        // Si requiereCambioClave es true, no se llama Succeed → el requisito
        // queda insatisfecho → 403 (mismo resultado que cualquier otro
        // requisito fallido de autorización).
    }
}
