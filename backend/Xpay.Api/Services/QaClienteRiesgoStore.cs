using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Integrations.MiDecisor;

namespace Xpay.Api.Services;

// XPAY-702 - lecturas del gate QA en modo QA_CLIENT_FULL_FLOW.
// Solo lectura (AsNoTracking). No devuelve ni registra PII.
public sealed class QaClienteRiesgoStore(XpayDbContext db) : IQaClienteRiesgoStore
{
    public async Task<bool> EsClienteQaPreparadoAsync(
        long idUsuario, CancellationToken cancellationToken = default)
    {
        var usuario = await db.Usuarios.AsNoTracking()
            .FirstOrDefaultAsync(u => u.IdUsuario == idUsuario, cancellationToken);
        if (usuario is null)
            return false;
        if (usuario.EstadoKycActual != QaIdentidadPruebaConstantes.EstadoKyc)
            return false;

        var persona = await db.Personas.AsNoTracking()
            .FirstOrDefaultAsync(p => p.IdPersona == usuario.IdPersona, cancellationToken);
        if (persona is null)
            return false;
        if (!persona.IdentidadVerificada)
            return false;
        if (persona.IdentidadVerificadaProveedor != QaIdentidadPruebaConstantes.Proveedor)
            return false;

        var veriff = QaIdentidadPruebaConstantes.ProveedorVeriff;
        var tieneVeriff = await db.KycVerificaciones.AsNoTracking()
            .Where(k => k.IdUsuario == idUsuario)
            .AnyAsync(k => k.Proveedor == veriff, cancellationToken);
        if (tieneVeriff)
            return false;

        var qa = QaIdentidadPruebaConstantes.Proveedor;
        return await db.KycVerificaciones.AsNoTracking()
            .Where(k => k.IdUsuario == idUsuario)
            .Where(k => k.EsActual)
            .AnyAsync(k => k.Proveedor == qa, cancellationToken);
    }

    public async Task<int> ContarConsultasRealesAsync(
        long idUsuario, CancellationToken cancellationToken = default)
    {
        var solicitudes = db.CarteraSolicitudesCupo.AsNoTracking()
            .Where(s => s.IdUsuario == idUsuario)
            .Select(s => s.IdSolicitud);

        var envio = CarteraIntentoFases.EnvioIncierto;
        var fin = CarteraIntentoFases.Finalizado;
        return await db.CarteraSolicitudCupoIntentos.AsNoTracking()
            .Where(i => solicitudes.Contains(i.IdSolicitud))
            .Where(i => i.FaseIntento == envio || i.FaseIntento == fin)
            .CountAsync(cancellationToken);
    }
}
