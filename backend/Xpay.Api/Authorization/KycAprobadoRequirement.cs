using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;

namespace Xpay.Api.Authorization;

// KYC-GATING-001: guard centralizado para operaciones financieras de usuario
// final. A diferencia de ClaveVigenteRequirement, esta policy ("KycAprobado")
// es de opt-in explícito y NO se agrega a DefaultPolicy — la mayoría de
// endpoints (login, registro, mi-estado, iniciar Veriff, health, admin,
// COMERCIO) deben permanecer accesibles sin identidad verificada. Se aplica
// con [Authorize(Policy = "KycAprobado")] adicional al [Authorize]/
// [Authorize(Roles=...)] ya existente en cada action — ASP.NET Core combina
// (AND) todos los [Authorize] de una misma action, nunca se reemplaza el
// existente.
//
// Condición canónica (fail-safe: cualquier otra combinación bloquea):
//   usuario.EstadoKycActual == "APROBADO" AND persona.IdentidadVerificada == true
// Ambas son necesarias: en KycService.ProcesarDecisionVeriffAsync (Caso 3 —
// documento duplicado en otra Persona, Caso 6 — documento distinto al ya
// verificado) EstadoKycActual puede llegar a "APROBADO" sin que
// IdentidadVerificada se marque true. Verificar solo un campo sería inseguro.
public class KycAprobadoRequirement : IAuthorizationRequirement
{
}

public class KycAprobadoAuthorizationHandler : AuthorizationHandler<KycAprobadoRequirement>
{
    private readonly XpayDbContext _db;

    public KycAprobadoAuthorizationHandler(XpayDbContext db) => _db = db;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, KycAprobadoRequirement requirement)
    {
        var idUsuarioClaim = context.User.FindFirst("idUsuario")?.Value;
        if (!long.TryParse(idUsuarioClaim, out var idUsuario))
        {
            // A diferencia de ClaveVigenteAuthorizationHandler (que hace
            // Succeed aquí porque es DefaultPolicy y debe ser permisivo con
            // flujos no estándar), esta policy solo se aplica explícitamente
            // a endpoints financieros que ya exigen [Authorize] — llegar aquí
            // sin un claim idUsuario válido es una anomalía, no un caso
            // esperado. Fail-safe: no se llama Succeed.
            return;
        }

        var estado = await (
            from u in _db.Usuarios
            join p in _db.Personas on u.IdPersona equals p.IdPersona
            where u.IdUsuario == idUsuario
            select new { u.EstadoKycActual, p.IdentidadVerificada }
        ).FirstOrDefaultAsync();

        // Usuario inexistente en BD (p.ej. token de un usuario borrado) →
        // fail-safe: no se llama Succeed.
        if (estado is null) return;

        if (estado.EstadoKycActual == "APROBADO" && estado.IdentidadVerificada)
            context.Succeed(requirement);
        // Cualquier otra combinación (incluye APROBADO+IdentidadVerificada=false
        // e IdentidadVerificada=true+EstadoKycActual!=APROBADO) queda sin
        // Succeed → el requisito falla → bloqueado.
    }
}
