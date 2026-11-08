using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
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
//
// XPAY-717: en ambiente productivo (IAmbienteXpay) además se exige identidad
// consolidada por VERIFF y exactamente una sesión kyc_verificaciones vigente
// de VERIFF y APROBADA (ver KycAprobadoRegla). En no productivo la condición
// canónica sigue bastando y no se consulta kyc_verificaciones.
public class KycAprobadoRequirement : IAuthorizationRequirement
{
}

public class KycAprobadoAuthorizationHandler : AuthorizationHandler<KycAprobadoRequirement>
{
    // (idUsuario, incluirSesionVigente) → evidencia, o null si el usuario no existe.
    private readonly Func<long, bool, Task<EvidenciaKyc?>> _cargarEvidencia;
    private readonly IAmbienteXpay                         _ambiente;

    public KycAprobadoAuthorizationHandler(XpayDbContext db, IAmbienteXpay ambiente)
        : this((idUsuario, incluirSesionVigente) => CargarEvidenciaAsync(db, idUsuario, incluirSesionVigente), ambiente)
    {
        ArgumentNullException.ThrowIfNull(db);
    }

    // Sólo para tests (InternalsVisibleTo): cargador de evidencia sin DB.
    internal KycAprobadoAuthorizationHandler(
        Func<long, bool, Task<EvidenciaKyc?>> cargarEvidencia, IAmbienteXpay ambiente)
    {
        ArgumentNullException.ThrowIfNull(cargarEvidencia);
        ArgumentNullException.ThrowIfNull(ambiente);
        _cargarEvidencia = cargarEvidencia;
        _ambiente        = ambiente;
    }

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

        // Sesión vigente sólo se lee en productivo. Una excepción del cargador
        // se propaga sin llamar Succeed (nunca autoriza por error).
        var evidencia = await _cargarEvidencia(idUsuario, !_ambiente.PermiteMecanismosNoProductivos);

        // Usuario inexistente en BD (p.ej. token de un usuario borrado) →
        // fail-safe: no se llama Succeed.
        if (evidencia is null) return;

        if (KycAprobadoRegla.Cumple(evidencia, _ambiente))
            context.Succeed(requirement);
        // Cualquier otra combinación (incluye APROBADO+IdentidadVerificada=false
        // e IdentidadVerificada=true+EstadoKycActual!=APROBADO, y en productivo
        // cualquier procedencia distinta de VERIFF) queda sin Succeed → el
        // requisito falla → bloqueado.
    }

    private static async Task<EvidenciaKyc?> CargarEvidenciaAsync(
        XpayDbContext db, long idUsuario, bool incluirSesionVigente)
    {
        var estado = await (
            from u in db.Usuarios
            join p in db.Personas on u.IdPersona equals p.IdPersona
            where u.IdUsuario == idUsuario
            select new { u.EstadoKycActual, p.IdentidadVerificada, p.IdentidadVerificadaProveedor }
        ).FirstOrDefaultAsync();

        if (estado is null) return null;

        var evidencia = new EvidenciaKyc
        {
            EstadoKycActual     = estado.EstadoKycActual,
            IdentidadVerificada = estado.IdentidadVerificada,
            ProveedorIdentidad  = estado.IdentidadVerificadaProveedor,
        };

        // No productivo o condición canónica ya fallida: kyc_verificaciones no
        // participa del veredicto y no se consulta.
        if (!incluirSesionVigente || !KycAprobadoRegla.CumpleCondicionCanonica(evidencia))
            return evidencia;

        // Hasta 2 filas vigentes: distingue 0, 1 y >1 sin Single() (una
        // anomalía nunca produce autorización ni excepción ambigua).
        var vigentes = await db.KycVerificaciones
            .AsNoTracking()
            .Where(k => k.IdUsuario == idUsuario && k.EsActual)
            .Select(k => new { k.Proveedor, k.EstadoKyc })
            .Take(2)
            .ToListAsync();

        return new EvidenciaKyc
        {
            EstadoKycActual     = evidencia.EstadoKycActual,
            IdentidadVerificada = evidencia.IdentidadVerificada,
            ProveedorIdentidad  = evidencia.ProveedorIdentidad,
            FilasKycActuales    = vigentes.Count,
            ProveedorKycActual  = vigentes.Count == 1 ? vigentes[0].Proveedor : null,
            EstadoKycFilaActual = vigentes.Count == 1 ? vigentes[0].EstadoKyc : null,
        };
    }
}
