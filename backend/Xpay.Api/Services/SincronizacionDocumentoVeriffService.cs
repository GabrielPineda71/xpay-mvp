using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// Acción expresa del titular, separada del webhook y de la lectura de Perfil.
// Revalida el GET real sin reabrir la máquina de decisiones A–E. Únicamente
// completa el documento legal vacío; no modifica evidencia raw, KYC ni Wallet.
public sealed class SincronizacionDocumentoVeriffService(
    XpayDbContext db, CondicionesWalletPiloto condiciones,
    ConsentimientoBiometricoService consentimiento, KycService kyc, TimeProvider reloj)
{
    internal const string AccionSincronizar = "KYC_DOCUMENTO_LEGAL_SINCRONIZADO";
    internal const string AccionRevision = "KYC_DOCUMENTO_SINCRONIZACION_REVISION";
    internal const string MensajeRevision = "Tu documento requiere una revisión de XPAY antes de continuar.";

    public async Task SincronizarAsync(long idUsuario, CancellationToken ct = default)
    {
        ExigirDisponible();
        // Un caller no puede mantener una transacción abierta durante el GET.
        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("La actualización requiere su propia transacción.");
        var usuario = await ExigirTitularAsync(idUsuario, ct);
        var persona = await db.Personas.AsNoTracking().SingleAsync(p => p.IdPersona == usuario.IdPersona, ct);
        var candidata = await SesionActualAsync(usuario, ct);
        if (!IdentidadConsolidada(usuario, persona) || candidata is null)
            throw new InvalidOperationException(MensajeRevision);
        var comparable = TipoDocumentoVeriffMapper.NumeroComparable(persona.NumeroDocumentoVerificado);
        if (comparable is null) throw new InvalidOperationException(MensajeRevision);

        // Una segunda pulsación no vuelve a consultar ni cambia un documento
        // ya establecido. Un documento parcial o discordante nunca se rellena.
        if (!string.IsNullOrWhiteSpace(persona.TipoDocumento) || !string.IsNullOrWhiteSpace(persona.NumeroDocumento))
        {
            if (DocumentoYaConsolidado(persona)) return;
            throw new InvalidOperationException(MensajeRevision);
        }

        // Único punto que consulta al proveedor: fuera de SQL y por acción POST.
        var fetch = await kyc.FetchVeriffDecisionAsync(candidata.SessionId!, ct);
        if (!fetch.Exito)
            throw new InvalidOperationException("No pudimos confirmar tu documento. Intenta de nuevo en un momento.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            if (await AppLockHelper.AdquirirAsync(db, $"XPAY:KYC_USUARIO:{idUsuario}", ct) < 0)
                throw new InvalidOperationException("Hay una actualización de identidad en curso. Intenta de nuevo.");
            var idPersonaActual = await db.Usuarios.AsNoTracking().Where(u => u.IdUsuario == idUsuario)
                .Select(u => u.IdPersona).SingleAsync(ct);
            if (idPersonaActual != persona.IdPersona) throw new InvalidOperationException(MensajeRevision);
            await ContactoVerificacionService.BloquearPersonaAsync(db, idPersonaActual, ct);
            // Se vuelven a exigir feature, correo, rol y última aceptación C.
            ExigirDisponible();
            var actualUsuario = await ExigirTitularAsync(idUsuario, ct);
            var unidad = await db.Personas.AsNoTracking().Where(p => p.IdPersona == idPersonaActual)
                .Select(p => p.IdUnidadNegocio).SingleAsync(ct);
            if (await AppLockHelper.AdquirirAsync(db, $"XPAY:IDENTIDAD_DOCUMENTO:{unidad}:{comparable}", ct) < 0
                || await AppLockHelper.AdquirirAsync(db, $"XPAY:IDENTIDAD_DOCUMENTO_GLOBAL:{comparable}", ct) < 0)
                throw new InvalidOperationException("La identidad está siendo revisada. Intenta de nuevo.");

            var actualPersona = await db.Personas.SingleAsync(p => p.IdPersona == idPersonaActual, ct);
            await db.Entry(actualPersona).ReloadAsync(ct);
            var actualSesion = await SesionActualAsync(actualUsuario, ct);
            var decision = fetch.SinDecision ? null : fetch.Decision;
            if (actualSesion is null || !MismaSesion(candidata, actualSesion)
                || !IdentidadConsolidada(actualUsuario, actualPersona)
                || decision is null || !DecisionCoincide(actualSesion, actualPersona, decision)
                || !MismaEvidenciaRaw(persona, actualPersona))
            {
                await RegistrarRevisionAsync(idUsuario, idPersonaActual, "EVIDENCIA_NO_COINCIDE", ct);
                await tx.CommitAsync(ct);
                throw new InvalidOperationException(MensajeRevision);
            }

            // El país se toma exclusivamente de esta decisión nueva y coherente,
            // nunca del país de residencia ni de valores por defecto de Perfil.
            var documento = TipoDocumentoVeriffMapper.Mapear(decision.DocumentType,
                decision.DocumentCountry, decision.DocumentNumber);
            if (documento.Resultado != TipoDocumentoVeriffMapper.Resultado.Mapeado
                || documento.NumeroDocumento != comparable)
            {
                await RegistrarRevisionAsync(idUsuario, idPersonaActual, "DOCUMENTO_NO_MAPEABLE", ct);
                await tx.CommitAsync(ct);
                throw new InvalidOperationException(MensajeRevision);
            }

            if (!string.IsNullOrWhiteSpace(actualPersona.TipoDocumento) || !string.IsNullOrWhiteSpace(actualPersona.NumeroDocumento))
            {
                if (actualPersona.TipoDocumento == documento.TipoDocumento
                    && actualPersona.NumeroDocumento == documento.NumeroDocumento)
                {
                    await tx.CommitAsync(ct);
                    return;
                }
                await RegistrarRevisionAsync(idUsuario, idPersonaActual, "DOCUMENTO_EXISTENTE", ct);
                await tx.CommitAsync(ct);
                throw new InvalidOperationException(MensajeRevision);
            }
            if (await KycService.ExisteDocumentoComparableAsync(db, idPersonaActual, comparable, ct)
                || await db.Usuarios.CountAsync(u => u.IdPersona == idPersonaActual, ct) != 1)
            {
                await RegistrarRevisionAsync(idUsuario, idPersonaActual, "IDENTIDAD_NO_UNIVOCA", ct);
                await tx.CommitAsync(ct);
                throw new InvalidOperationException(MensajeRevision);
            }

            actualPersona.TipoDocumento = documento.TipoDocumento;
            actualPersona.NumeroDocumento = documento.NumeroDocumento;
            actualPersona.FechaActualizacion = reloj.GetUtcNow().UtcDateTime;
            db.Auditorias.Add(Evidencia(idUsuario, idPersonaActual, AccionSincronizar, "DOCUMENTO_ESTABLECIDO", actualSesion.IdKycVerificacion));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();
            throw new InvalidOperationException(MensajeRevision);
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private void ExigirDisponible()
    {
        if (condiciones.Obtener() is null || !consentimiento.Habilitado)
            throw new PreparacionWalletNoDisponibleException();
    }

    private async Task<Usuario> ExigirTitularAsync(long idUsuario, CancellationToken ct)
    {
        var u = await db.Usuarios.AsNoTracking().SingleOrDefaultAsync(u => u.IdUsuario == idUsuario, ct);
        if (u is null || u.Estado != "ACTIVO" || !u.EmailVerificado || u.RequiereCambioClave
            || !await RegistroPublicoService.EsAutorregistroAsync(db, idUsuario, ct)
            || !await consentimiento.TieneConsentimientoVigenteAsync(idUsuario, ct))
            throw new InvalidOperationException(MensajeRevision);
        var roles = await (from ur in db.UsuarioRoles join r in db.Roles on ur.IdRol equals r.IdRol
            where ur.IdUsuario == idUsuario && ur.Estado == "ACTIVO" && r.Estado == "ACTIVO"
            select r.Codigo).ToListAsync(ct);
        if (roles.Count != 1 || roles[0] != "USUARIO_FINAL"
            || await db.LibranzaUsuariosEmpresa.AnyAsync(a => a.IdUsuario == idUsuario && a.Estado == "ACTIVO", ct))
            throw new InvalidOperationException(MensajeRevision);
        return u;
    }

    private async Task<KycVerificacion?> SesionActualAsync(Usuario u, CancellationToken ct)
    {
        var sesiones = await db.KycVerificaciones.AsNoTracking().Where(k => k.IdUsuario == u.IdUsuario && k.EsActual)
            .Take(2).ToListAsync(ct);
        if (sesiones.Count != 1) return null;
        var k = sesiones[0];
        return k.IdPersona == u.IdPersona && k.Proveedor == "VERIFF" && k.EstadoKyc == "APROBADO"
            && k.Decision == "approved" && !string.IsNullOrWhiteSpace(k.SessionId)
            && !string.IsNullOrWhiteSpace(k.AttemptIdVeriff) && k.DecisionTimeVeriff.HasValue ? k : null;
    }

    private static bool IdentidadConsolidada(Usuario u, Persona p) => u.IdPersona == p.IdPersona
        && u.EstadoKycActual == "APROBADO" && p.Estado == "ACTIVA" && p.IdentidadVerificada
        && p.IdentidadVerificadaProveedor == "VERIFF" && p.IdentidadVerificadaFecha.HasValue
        && !string.IsNullOrWhiteSpace(p.NombreVerificadoCompleto) && !string.IsNullOrWhiteSpace(p.ApellidoVerificadoCompleto)
        && !string.IsNullOrWhiteSpace(p.TipoDocumentoVeriffRaw) && !string.IsNullOrWhiteSpace(p.NumeroDocumentoVerificado);

    internal static bool DecisionCoincide(KycVerificacion sesion, Persona persona, VeriffDecisionParsed decision) =>
        sesion.EsActual && sesion.IdPersona == persona.IdPersona && sesion.Proveedor == "VERIFF"
        && sesion.EstadoKyc == "APROBADO" && sesion.Decision == "approved" && decision.Decision == "approved"
        && !string.IsNullOrWhiteSpace(sesion.AttemptIdVeriff) && sesion.AttemptIdVeriff == decision.AttemptId
        && sesion.DecisionTimeVeriff.HasValue && sesion.DecisionTimeVeriff == decision.DecisionTimeUtc
        && sesion.SessionId == decision.SessionId && !string.IsNullOrWhiteSpace(sesion.SessionId)
        && string.Equals(sesion.VendorData, decision.VendorData, StringComparison.Ordinal)
        && string.Equals(persona.NombreVerificadoCompleto, decision.FirstName, StringComparison.Ordinal)
        && string.Equals(persona.ApellidoVerificadoCompleto, decision.LastName, StringComparison.Ordinal)
        && string.Equals(persona.TipoDocumentoVeriffRaw, decision.DocumentType, StringComparison.Ordinal)
        && string.Equals(persona.NumeroDocumentoVerificado, decision.DocumentNumber, StringComparison.Ordinal)
        && (decision.DateOfBirth is null || persona.FechaNacimiento?.Date == decision.DateOfBirth.Value.ToDateTime(TimeOnly.MinValue));

    private static bool MismaSesion(KycVerificacion a, KycVerificacion b) => a.IdKycVerificacion == b.IdKycVerificacion
        && a.IdUsuario == b.IdUsuario && a.IdPersona == b.IdPersona && a.SessionId == b.SessionId
        && a.AttemptIdVeriff == b.AttemptIdVeriff && a.DecisionTimeVeriff == b.DecisionTimeVeriff;

    private static bool MismaEvidenciaRaw(Persona a, Persona b) => a.IdPersona == b.IdPersona
        && a.IdUnidadNegocio == b.IdUnidadNegocio && a.NombreVerificadoCompleto == b.NombreVerificadoCompleto
        && a.ApellidoVerificadoCompleto == b.ApellidoVerificadoCompleto && a.TipoDocumentoVeriffRaw == b.TipoDocumentoVeriffRaw
        && a.NumeroDocumentoVerificado == b.NumeroDocumentoVerificado && a.IdentidadVerificadaFecha == b.IdentidadVerificadaFecha;

    private static bool DocumentoYaConsolidado(Persona p) => p.TipoDocumento is "CC" or "PASAPORTE"
        && !string.IsNullOrWhiteSpace(p.NumeroDocumento)
        && p.NumeroDocumento == TipoDocumentoVeriffMapper.NumeroComparable(p.NumeroDocumentoVerificado)
        && ((p.TipoDocumento == "CC" && p.TipoDocumentoVeriffRaw == "ID_CARD" && p.NumeroDocumento.All(char.IsAsciiDigit))
            || (p.TipoDocumento == "PASAPORTE" && p.TipoDocumentoVeriffRaw == "PASSPORT" && p.NumeroDocumento.All(char.IsAsciiLetterOrDigit)));

    private async Task RegistrarRevisionAsync(long idUsuario, long idPersona, string motivo, CancellationToken ct)
    {
        db.Auditorias.Add(Evidencia(idUsuario, idPersona, AccionRevision, motivo));
        await db.SaveChangesAsync(ct);
    }

    private Auditoria Evidencia(long idUsuario, long idPersona, string accion, string resultado, long? idKycVerificacion = null) => new()
    {
        IdUsuario = idUsuario, IdPersona = idPersona, Modulo = "KYC", Accion = accion, Entidad = "personas",
        IdEntidad = idPersona.ToString(CultureInfo.InvariantCulture), Resultado = "EXITOSO", ValorNuevo = JsonSerializer.Serialize(new { motivo = resultado, idKycVerificacion }),
        Observacion = "Confirmación solicitada por el titular; no modifica la decisión KYC ni habilita operaciones.",
        FechaEvento = reloj.GetUtcNow().UtcDateTime
    };
}
