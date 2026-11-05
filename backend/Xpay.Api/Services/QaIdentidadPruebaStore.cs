using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Exceptions;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// XPAY-669 — persistencia de la preparación QA de identidad de prueba.
// Mismo patrón transaccional que los stores de Cartera: transacción explícita
// + sp_getapplock (owner=Transaction) + re-lectura con tracking + guardas
// fail-closed + SaveChanges único + commit; ante excepción, rollback completo.
// No escribe ningún campo crudo de Veriff ni modifica filas kyc del proveedor
// VERIFF (el servicio y la revalidación aquí rechazan esos casos).
public sealed class QaIdentidadPruebaStore(XpayDbContext db) : IQaIdentidadPruebaStore
{
    public async Task<QaIdentidadPruebaSnapshot?> CargarAsync(long idUsuario, CancellationToken cancellationToken)
    {
        var usuario = await db.Usuarios.AsNoTracking()
            .FirstOrDefaultAsync(u => u.IdUsuario == idUsuario, cancellationToken)
            .ConfigureAwait(false);
        if (usuario is null)
            return null;

        var persona = await db.Personas.AsNoTracking()
            .FirstOrDefaultAsync(p => p.IdPersona == usuario.IdPersona, cancellationToken)
            .ConfigureAwait(false);
        if (persona is null)
            return null;

        var kyc = await db.KycVerificaciones.AsNoTracking()
            .Where(k => k.IdUsuario == idUsuario)
            .Select(k => new { k.Proveedor, k.EsActual, k.EstadoKyc, k.Decision })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var actuales = kyc.Where(k => k.EsActual).ToList();

        return new QaIdentidadPruebaSnapshot
        {
            IdUsuario                    = usuario.IdUsuario,
            IdPersona                    = persona.IdPersona,
            UsuarioEstado                = usuario.Estado,
            EstadoKycActual              = usuario.EstadoKycActual,
            PersonaEstado                = persona.Estado,
            TipoDocumento                = persona.TipoDocumento,
            NumeroDocumento              = persona.NumeroDocumento,
            PrimerApellido               = persona.PrimerApellido,
            IdentidadVerificada          = persona.IdentidadVerificada,
            IdentidadVerificadaProveedor = persona.IdentidadVerificadaProveedor,
            TieneDatosVeriff             = TieneDatosVeriff(persona),
            TieneVerificacionVeriff      = kyc.Any(k => k.Proveedor == QaIdentidadPruebaConstantes.ProveedorVeriff),
            KycActuales                  = actuales.Count,
            KycActualEsPreparacionQa     = actuales.Count == 1
                && actuales[0].Proveedor == QaIdentidadPruebaConstantes.Proveedor
                && actuales[0].EstadoKyc == QaIdentidadPruebaConstantes.EstadoKyc
                && actuales[0].Decision  == QaIdentidadPruebaConstantes.Decision,
        };
    }

    public async Task<bool> AplicarPreparacionAsync(
        AplicacionPreparacionIdentidadQa aplicacion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(aplicacion);

        // XPAY-702 - si ya hay una transaccion abierta (alta QA atomica), se une
        // a ella y no hace commit/rollback propios; si no, comportamiento original.
        await using var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
            : null;
        try
        {
            ValidarResultadoLock(await AppLockHelper
                .AdquirirAsync(db, ClaveLock(aplicacion.IdUsuario), cancellationToken)
                .ConfigureAwait(false));

            var usuario = await db.Usuarios
                .FirstOrDefaultAsync(u => u.IdUsuario == aplicacion.IdUsuario, cancellationToken)
                .ConfigureAwait(false);
            var persona = usuario is null
                ? null
                : await db.Personas
                    .FirstOrDefaultAsync(p => p.IdPersona == usuario.IdPersona, cancellationToken)
                    .ConfigureAwait(false);
            var kyc = usuario is null
                ? new List<KycVerificacion>()
                : await db.KycVerificaciones
                    .Where(k => k.IdUsuario == aplicacion.IdUsuario)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

            // Revalidación dentro de la transacción: cualquier cambio desde la
            // lectura del servicio → no se escribe nada.
            var elegible =
                usuario is not null
                && persona is not null
                && persona.IdPersona == aplicacion.IdPersona
                && usuario.Estado == "ACTIVO"
                && persona.Estado == "ACTIVA"
                && !persona.IdentidadVerificada
                && persona.IdentidadVerificadaProveedor != QaIdentidadPruebaConstantes.ProveedorVeriff
                && !TieneDatosVeriff(persona)
                && !kyc.Any(k => k.Proveedor == QaIdentidadPruebaConstantes.ProveedorVeriff)
                && QaIdentidadPruebaService.DatosDeclaradosValidos(new QaIdentidadPruebaSnapshot
                {
                    TipoDocumento   = persona.TipoDocumento,
                    NumeroDocumento = persona.NumeroDocumento,
                    PrimerApellido  = persona.PrimerApellido,
                });

            if (!elegible)
            {
                if (tx is not null) await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                db.ChangeTracker.Clear();
                return false;
            }

            // Persona: identidad habilitada con procedencia SIMULACION_QA. Los
            // campos crudos de Veriff quedan intactos (null): no se fabrican.
            persona!.IdentidadVerificada          = true;
            persona.IdentidadVerificadaProveedor  = QaIdentidadPruebaConstantes.Proveedor;
            persona.IdentidadVerificadaFecha      = aplicacion.FechaUtc;
            persona.FechaActualizacion            = aplicacion.FechaUtc;

            // Estado KYC real previo (enum, sin PII) para la auditoría.
            var estadoKycAnterior = usuario!.EstadoKycActual;

            // Usuario: estado KYC aprobado.
            usuario!.EstadoKycActual       = QaIdentidadPruebaConstantes.EstadoKyc;
            usuario.FechaKycActualizacion  = aplicacion.FechaUtc;

            // KYC: una sola fila es_actual (invariante existente). Las filas
            // previas sólo pueden ser de simulación (VERIFF ya fue rechazado).
            foreach (var anterior in kyc.Where(k => k.EsActual))
            {
                anterior.EsActual           = false;
                anterior.FechaActualizacion = aplicacion.FechaUtc;
            }
            db.KycVerificaciones.Add(new KycVerificacion
            {
                IdUsuario          = usuario.IdUsuario,
                IdPersona          = persona.IdPersona,
                Proveedor          = QaIdentidadPruebaConstantes.Proveedor,
                EstadoKyc          = QaIdentidadPruebaConstantes.EstadoKyc,
                Decision           = QaIdentidadPruebaConstantes.Decision,
                Reason             = QaIdentidadPruebaConstantes.Reason,
                EsActual           = true,
                FechaCreacion      = aplicacion.FechaUtc,
                FechaActualizacion = aplicacion.FechaUtc,
                FechaDecision      = aplicacion.FechaUtc,
            });

            // Auditoría durable: sólo ids internos, flags y procedencia. Sin PII.
            db.Auditorias.Add(new Auditoria
            {
                IdUsuario     = aplicacion.IdAdmin,
                IdPersona     = persona.IdPersona,
                Modulo        = QaIdentidadPruebaConstantes.Modulo,
                Accion        = QaIdentidadPruebaConstantes.Accion,
                Entidad       = "personas",
                IdEntidad     = persona.IdPersona.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ValorAnterior = $"identidad_verificada=false;estado_kyc_actual={estadoKycAnterior}",
                ValorNuevo    = $"identidad_verificada=true;proveedor={QaIdentidadPruebaConstantes.Proveedor};estado_kyc_actual={QaIdentidadPruebaConstantes.EstadoKyc}",
                Resultado     = "EXITOSO",
                Observacion   = $"Preparación QA de persona de prueba (usuario destino {usuario.IdUsuario}); NO verificada por Veriff.",
                FechaEvento   = aplicacion.FechaUtc,
            });

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (tx is not null) await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private static bool TieneDatosVeriff(Persona p) =>
        p.NombreVerificadoCompleto is not null
        || p.ApellidoVerificadoCompleto is not null
        || p.TipoDocumentoVeriffRaw is not null
        || p.NumeroDocumentoVerificado is not null;

    // Recurso EXACTO de KycService (CreateVeriffSessionAsync /
    // ProcesarDecisionVeriffAsync): las tres operaciones se serializan por usuario.
    internal static string ClaveLock(long idUsuario) => $"XPAY:KYC_USUARIO:{idUsuario}";

    // Misma semántica que KycService.ValidarResultadoLockKycUsuario.
    internal static void ValidarResultadoLock(int resultado)
    {
        switch (resultado)
        {
            case 0:
            case 1:
                return;
            case -1:
                throw new KycUsuarioConcurrenteException(
                    "Tiempo de espera agotado esperando otra sincronización KYC concurrente de este usuario. Intenta de nuevo.");
            case -2:
                throw new KycUsuarioConcurrenteException(
                    "La solicitud de sincronización KYC del usuario fue cancelada. Intenta de nuevo.");
            case -3:
                throw new KycUsuarioConcurrenteException(
                    "Se detectó un interbloqueo con otra sincronización KYC concurrente de este usuario. Intenta de nuevo.");
            default:
                throw new Exception($"sp_getapplock devolvió un código inesperado: {resultado}.");
        }
    }
}
