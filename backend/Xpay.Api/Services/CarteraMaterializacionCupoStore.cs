using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// M2.4c / TX2 — implementación EF Core de ICarteraMaterializacionCupo
// (infraestructura DORMIDA). Mismo patrón que CarteraConsultaRiesgoStore:
// BeginTransactionAsync → AppLockHelper.AdquirirAsync (owner=Transaction) →
// re-lectura autoritativa dentro de la transacción → SaveChangesAsync →
// CommitAsync, con rollback seguro.
//
// NO está registrada en DI. NO tiene ningún caller de runtime. Sólo la
// alcanzan los tests instanciándola explícitamente.
//
// APLICA EXCLUSIVAMENTE a Cartera Ordinaria: cero lectura/escritura sobre
// libranza_* / ledger_* / wallet_movimientos / cartera_utilizaciones. TX2
// materializa un LÍMITE de crédito; no realiza utilización ni movimiento
// financiero.
public sealed class CarteraMaterializacionCupoStore(XpayDbContext db)
    : ICarteraMaterializacionCupo
{
    public async Task<ResultadoMaterializacionCupo> MaterializarCupoAsync(
        long idSolicitud, CancellationToken cancellationToken = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // ── Primera lectura: SÓLO para derivar idUsuario y construir la
            // clave del AppLock. NO autoriza ninguna escritura ni guard final.
            var idUsuario = await db.CarteraSolicitudesCupo
                .AsNoTracking()
                .Where(s => s.IdSolicitud == idSolicitud)
                .Select(s => (long?)s.IdUsuario)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (idUsuario is null)
                return await NoMaterializarAsync(tx, ResultadoMaterializacionCupo.NoElegible).ConfigureAwait(false);

            ValidarResultadoLock(await AppLockHelper
                .AdquirirAsync(db, $"XPAY:CARTERA_CUPO:{idUsuario.Value}", cancellationToken)
                .ConfigureAwait(false));

            // ── Lectura AUTORITATIVA de la solicitud (tracked, bajo el lock).
            var solicitud = await db.CarteraSolicitudesCupo
                .FirstOrDefaultAsync(s => s.IdSolicitud == idSolicitud, cancellationToken)
                .ConfigureAwait(false);

            if (solicitud is null)
                return await NoMaterializarAsync(tx, ResultadoMaterializacionCupo.NoElegible).ConfigureAwait(false);

            // ── Marca durable de idempotencia (autoritativa, antes que
            // estado/decisión, sin auto-repair).
            if (solicitud.IdCupoOrdinario is not null)
            {
                if (solicitud.FechaMaterializacionCupo is null)
                    throw new CarteraMaterializacionInvarianteException(
                        "Solicitud con id_cupo_ordinario pero sin fecha_materializacion_cupo.");

                var cupoMarcado = await db.CarteraCuposOrdinarios
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.IdCupo == solicitud.IdCupoOrdinario.Value, cancellationToken)
                    .ConfigureAwait(false);

                if (cupoMarcado is null)
                    throw new CarteraMaterializacionInvarianteException(
                        "id_cupo_ordinario de la solicitud apunta a un cupo inexistente.");

                if (cupoMarcado.IdUsuario != solicitud.IdUsuario)
                    throw new CarteraMaterializacionInvarianteException(
                        "id_cupo_ordinario de la solicitud apunta a un cupo de otro usuario.");

                return await NoMaterializarAsync(tx, ResultadoMaterializacionCupo.YaMaterializado).ConfigureAwait(false);
            }

            if (solicitud.FechaMaterializacionCupo is not null)
                throw new CarteraMaterializacionInvarianteException(
                    "Solicitud con fecha_materializacion_cupo pero sin id_cupo_ordinario.");

            // ── Guards de elegibilidad.
            if (!string.Equals(solicitud.EstadoSolicitud, CarteraSolicitudCupoEstados.AprobadaPendienteCupo, StringComparison.Ordinal))
                return await NoMaterializarAsync(tx, ResultadoMaterializacionCupo.NoElegible).ConfigureAwait(false);

            if (!string.Equals(solicitud.DecisionCrediticia, CarteraDecisionCrediticia.Aprobada, StringComparison.Ordinal))
                throw new CarteraMaterializacionInvarianteException(
                    "Solicitud en APROBADA_PENDIENTE_CUPO pero decision_crediticia no es APROBADA.");

            if (solicitud.MontoAprobado is null)
                throw new CarteraMaterializacionInvarianteException(
                    "Solicitud aprobada sin monto_aprobado.");

            if (solicitud.MontoAprobado.Value <= 0m)
                throw new CarteraMaterializacionInvarianteException(
                    "Solicitud aprobada con monto_aprobado no positivo.");

            var montoAprobado = solicitud.MontoAprobado.Value;

            // ── Cupo existente del usuario, con lock pesimista de fila (mismo
            // patrón que ConfirmarAvanceWalletAsync / PagarQrConCupoAsync).
            var existente = await db.CarteraCuposOrdinarios
                .FromSqlInterpolated(
                    $"SELECT * FROM cartera_cupos_ordinarios WITH (UPDLOCK, ROWLOCK) WHERE id_usuario = {idUsuario.Value}")
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            var nowUtc = DateTime.UtcNow;
            long idCupo;

            if (existente is null)
            {
                // ── RAMA CREATE ────────────────────────────────────────────
                var wallet = await db.Wallets
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        w => w.IdPersona == solicitud.IdPersona
                          && w.TipoWallet == "PERSONA"
                          && w.Estado == "ACTIVA",
                        cancellationToken)
                    .ConfigureAwait(false);

                if (wallet is null)
                    return await NoMaterializarAsync(tx, ResultadoMaterializacionCupo.NoElegible).ConfigureAwait(false);

                var cupoNuevo = new CarteraCupoOrdinario
                {
                    IdUsuario          = solicitud.IdUsuario,
                    IdWallet           = wallet.IdWallet,
                    CupoAprobado       = montoAprobado,
                    CupoUsado          = 0m,
                    Estado             = "ACTIVO",
                    FechaAprobacion    = nowUtc,
                    FechaVencimiento   = null,
                    AprobadoPorUsuario = null,
                    Observaciones      = null,
                    CreatedAt          = nowUtc,
                    UpdatedAt          = null,
                };
                db.CarteraCuposOrdinarios.Add(cupoNuevo);
                // Primer SaveChanges: sólo para obtener el IDENTITY id_cupo
                // (mismo patrón parent-then-child que CrearSolicitudCupoAsync).
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                idCupo = cupoNuevo.IdCupo;
            }
            else
            {
                // ── RAMA UPDATE ────────────────────────────────────────────
                if (!string.Equals(existente.Estado, "ACTIVO", StringComparison.Ordinal))
                    return await NoMaterializarAsync(tx, ResultadoMaterializacionCupo.CupoNoActivo).ConfigureAwait(false);

                // Política autorizada: nunca reduce; cupo_usado inmutable;
                // id_wallet / estado / fecha_aprobacion / fecha_vencimiento /
                // aprobado_por_usuario / observaciones / created_at intactos.
                existente.CupoAprobado = Math.Max(existente.CupoAprobado, montoAprobado);
                existente.UpdatedAt    = nowUtc;
                idCupo = existente.IdCupo;
            }

            solicitud.IdCupoOrdinario          = idCupo;
            solicitud.FechaMaterializacionCupo = nowUtc;
            solicitud.EstadoSolicitud          = CarteraSolicitudCupoEstados.Aprobada;
            solicitud.FechaActualizacion       = nowUtc;

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return ResultadoMaterializacionCupo.Materializado;
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private static async Task<ResultadoMaterializacionCupo> NoMaterializarAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, ResultadoMaterializacionCupo resultado)
    {
        await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        return resultado;
    }

    // Mismo criterio que CarteraConsultaRiesgoStore.ValidarResultadoLock:
    // 0/1 → adquirido; -1/-2/-3 → contención transitoria; otro → error técnico.
    private static void ValidarResultadoLock(int resultado)
    {
        switch (resultado)
        {
            case 0:
            case 1:
                return;
            case -1:
            case -2:
            case -3:
                throw new InvalidOperationException(
                    "Hay otra materialización de cupo en curso para este usuario. Intenta de nuevo en unos segundos.");
            default:
                throw new Exception($"sp_getapplock devolvió un código inesperado: {resultado}.");
        }
    }
}
