using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// Excepción fail-closed de invariante de la evidencia de autorización: cualquier
// incoherencia de identidad/datos → rollback, sin auto-reparación.
public sealed class CarteraAutorizacionConsultaRiesgoInvarianteException(string message) : Exception(message);

// V1 (ACTA 001 §3 · XPAY-195/196/197) — implementación EF Core de
// ICarteraAutorizacionConsultaRiesgoStore. INSERT-ONLY. Sin AppLock nuevo: la
// serialización de la aceptación la da UNIQUE(id_solicitud_origen, version) +
// una transacción corta. NO llama a MiDecisor. NO registrada en DI.
public sealed class CarteraAutorizacionConsultaRiesgoStore(XpayDbContext db)
    : ICarteraAutorizacionConsultaRiesgoStore
{
    public async Task<ResultadoRegistroAutorizacion> RegistrarAceptacionAsync(
        long idSolicitud, long idUsuario, string versionRecibida,
        string? correlationId, CancellationToken cancellationToken = default)
    {
        // ── Precondiciones baratas antes de abrir transacción ──────────────
        if (idSolicitud <= 0 || idUsuario <= 0)
            return ResultadoRegistroAutorizacion.NoElegible;

        // La versión recibida debe ser EXACTAMENTE la vigente. El texto/hash
        // NUNCA vienen del cliente.
        if (!CarteraAutorizacionConsultaRiesgoTextos.EsVersionVigente(versionRecibida))
            return ResultadoRegistroAutorizacion.NoElegible;

        var version = CarteraAutorizacionConsultaRiesgoTextos.V1_Version;
        var texto   = CarteraAutorizacionConsultaRiesgoTextos.V1_Texto;
        var hash    = CarteraAutorizacionConsultaRiesgoTextos.V1_HashSha256;

        var solicitud = await db.CarteraSolicitudesCupo
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.IdSolicitud == idSolicitud, cancellationToken)
            .ConfigureAwait(false);

        if (solicitud is null)
            return ResultadoRegistroAutorizacion.NoElegible;

        // Ownership.
        if (solicitud.IdUsuario != idUsuario)
            return ResultadoRegistroAutorizacion.NoElegible;

        // Sólo se captura mientras la solicitud está RECIBIDA (única fase que
        // hoy precede a la consulta de riesgo).
        if (!string.Equals(solicitud.EstadoSolicitud, CarteraSolicitudCupoEstados.Recibida, StringComparison.Ordinal))
            return ResultadoRegistroAutorizacion.NoElegible;

        var usuario = await db.Usuarios
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.IdUsuario == idUsuario, cancellationToken)
            .ConfigureAwait(false);

        if (usuario is null)
            return ResultadoRegistroAutorizacion.NoElegible;

        // Invariante de identidad — fail-closed, sin auto-reparación.
        if (usuario.IdPersona != solicitud.IdPersona)
            throw new CarteraAutorizacionConsultaRiesgoInvarianteException(
                "La persona del usuario autenticado no coincide con la persona de la solicitud.");

        var idPersona = solicitud.IdPersona;
        var correlacion = string.IsNullOrWhiteSpace(correlationId) ? null
            : correlationId!.Length > 100 ? correlationId[..100] : correlationId;

        // ── INSERT en transacción corta ───────────────────────────────────
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            db.Set<CarteraAutorizacionConsultaRiesgo>().Add(new CarteraAutorizacionConsultaRiesgo
            {
                IdPersona          = idPersona,
                IdUsuario          = idUsuario,
                IdSolicitudOrigen  = idSolicitud,
                VersionTexto       = version,
                HashTexto          = hash,
                TextoSnapshot      = texto,
                FechaAceptacionUtc = DateTime.UtcNow,
                CorrelationId      = correlacion,
            });

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return ResultadoRegistroAutorizacion.Registrada;
        }
        catch (Exception ex) when (SqlExceptionHelper.IsUniqueViolation(ex))
        {
            // Carrera / doble AUTORIZO para la misma (solicitud, versión).
            await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            db.ChangeTracker.Clear();

            var existente = await db.Set<CarteraAutorizacionConsultaRiesgo>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    a => a.IdSolicitudOrigen == idSolicitud && a.VersionTexto == version,
                    cancellationToken)
                .ConfigureAwait(false);

            if (existente is null)
                // La violación fue de otra restricción UNIQUE / carrera rara —
                // no se puede afirmar idempotencia. Fail-closed.
                throw new CarteraAutorizacionConsultaRiesgoInvarianteException(
                    "Violación de unicidad al registrar la aceptación pero no se encontró la evidencia existente.");

            // La fila preexistente DEBE ser consistente con lo esperado.
            if (existente.IdPersona != idPersona
                || existente.IdUsuario != idUsuario
                || !string.Equals(existente.HashTexto, hash, StringComparison.Ordinal)
                || !string.Equals(existente.VersionTexto, version, StringComparison.Ordinal)
                || !string.Equals(existente.TextoSnapshot, texto, StringComparison.Ordinal))
                throw new CarteraAutorizacionConsultaRiesgoInvarianteException(
                    "Ya existe una aceptación para esta solicitud/versión pero es inconsistente con los valores esperados.");

            return ResultadoRegistroAutorizacion.YaRegistrada;
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
