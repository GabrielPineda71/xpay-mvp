using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Models;

namespace Xpay.Api.Integrations.MiDecisor;

// V1 (ACTA 001 §3 · XPAY-195/196/197) — implementación de IConsultaRiesgoAutorizacion
// respaldada por la evidencia durable (cartera_autorizacion_consulta_riesgo).
//
// NO ESTÁ REGISTRADA EN DI: Program.cs sigue registrando
// AutorizacionConsultaRiesgoNoDisponible (devuelve false siempre). El swap del
// registro es una decisión de wiring de M2.4d, revisada aparte. Aquí sólo se
// deja la lógica de lectura lista y probada.
//
// Regla V1 ESTRICTA (XPAY-197): la aceptación autoriza EXCLUSIVAMENTE la consulta
// de la MISMA solicitud (id_solicitud_origen == idSolicitud). Sin reutilización
// cross-request. Sin relacionKey / id_cupo_ordinario. Sin compatibilidad v2.
//
// Lectura pura, fail-closed: cualquier ausencia o inconsistencia → false. NO
// lanza excepción por "no autorizado". NUNCA llama a MiDecisor.
//
// XPAY-199 (P2-2): la cancelación de la operación se PROPAGA (no se convierte en
// false) ; un error operacional inesperado se registra de forma segura (mensaje
// genérico, sin identificadores de cliente) y sigue produciendo false fail-closed.
public sealed class AutorizacionConsultaRiesgoDurable(
    XpayDbContext db, ILogger<AutorizacionConsultaRiesgoDurable> logger)
    : IConsultaRiesgoAutorizacion
{
    public async Task<bool> TieneAutorizacionVigenteAsync(
        long idUsuario, long idSolicitud, CancellationToken cancellationToken = default)
    {
        if (idUsuario <= 0 || idSolicitud <= 0)
            return false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var solicitud = await db.CarteraSolicitudesCupo
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.IdSolicitud == idSolicitud, cancellationToken)
                .ConfigureAwait(false);

            if (solicitud is null || solicitud.IdUsuario != idUsuario)
                return false;

            var usuario = await db.Usuarios
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.IdUsuario == idUsuario, cancellationToken)
                .ConfigureAwait(false);

            if (usuario is null || !string.Equals(usuario.Estado, "ACTIVO", StringComparison.Ordinal))
                return false;

            if (usuario.IdPersona != solicitud.IdPersona)
                return false;

            var persona = await db.Personas
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.IdPersona == solicitud.IdPersona, cancellationToken)
                .ConfigureAwait(false);

            if (persona is null || !string.Equals(persona.Estado, "ACTIVA", StringComparison.Ordinal))
                return false;

            var version = CarteraAutorizacionConsultaRiesgoTextos.V1_Version;
            var hashVigente = CarteraAutorizacionConsultaRiesgoTextos.V1_HashSha256;
            var textoVigente = CarteraAutorizacionConsultaRiesgoTextos.V1_Texto;

            // Regla V1 estricta: la aceptación debe ser de ESTA solicitud.
            var aceptacion = await db.Set<CarteraAutorizacionConsultaRiesgo>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    a => a.IdSolicitudOrigen == idSolicitud
                      && a.IdUsuario == idUsuario
                      && a.IdPersona == solicitud.IdPersona
                      && a.VersionTexto == version
                      && a.HashTexto == hashVigente,
                    cancellationToken)
                .ConfigureAwait(false);

            if (aceptacion is null)
                return false;

            // Integridad del snapshot persistido.
            if (!string.Equals(aceptacion.TextoSnapshot, textoVigente, StringComparison.Ordinal))
                return false;
            if (!string.Equals(
                    CarteraAutorizacionConsultaRiesgoTextos.HashSha256Hex(aceptacion.TextoSnapshot),
                    aceptacion.HashTexto, StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }
        catch (OperationCanceledException)
        {
            // La cancelación de la operación NO es un veredicto de negocio: se propaga.
            throw;
        }
        catch (Exception ex)
        {
            // Fail-closed ante un error operacional inesperado (conexión, timeout,
            // datos corruptos): se devuelve false, pero se registra para diagnóstico.
            // Mensaje genérico — SIN idUsuario / idSolicitud / idPersona / snapshot /
            // texto legal / hash / claims / secretos / PII.
            logger.LogWarning(ex, "Error al validar autorización durable de consulta de riesgo.");
            return false;
        }
    }
}
