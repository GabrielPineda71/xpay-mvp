using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Integrations.MiDecisor;

namespace Xpay.Api.Services;

// XPAY-717 — lecturas del gate productivo de MiDecisor. Sólo lectura
// (AsNoTracking). No devuelve ni registra PII.
//
// El titular es el documento que realmente consulta MiDecisor
// (PersonaMiDecisorRequestMapper usa Persona.TipoDocumento/NumeroDocumento).
// La búsqueda es transversal: todas las personas con ese documento en
// cualquier unidad de negocio, sus usuarios y todas sus solicitudes. Así otro
// usuario, persona, solicitud o unidad no evade la ventana.
public sealed class RiesgoProduccionStore(XpayDbContext db) : IRiesgoProduccionStore
{
    public async Task<RiesgoProduccionSnapshot?> CargarAsync(
        long idSolicitud, CancellationToken cancellationToken = default)
    {
        var documento = await (
            from s in db.CarteraSolicitudesCupo.AsNoTracking()
            join p in db.Personas.AsNoTracking() on s.IdPersona equals p.IdPersona
            where s.IdSolicitud == idSolicitud
            select new { p.TipoDocumento, p.NumeroDocumento }
        ).FirstOrDefaultAsync(cancellationToken);

        // Identidad documental insuficiente: el gate falla cerrado.
        if (documento is null
            || string.IsNullOrWhiteSpace(documento.TipoDocumento)
            || string.IsNullOrWhiteSpace(documento.NumeroDocumento))
            return null;

        var tipo   = documento.TipoDocumento;
        var numero = documento.NumeroDocumento;

        var personas = db.Personas.AsNoTracking()
            .Where(p => p.TipoDocumento == tipo && p.NumeroDocumento == numero)
            .Select(p => p.IdPersona);

        var usuarios = db.Usuarios.AsNoTracking()
            .Where(u => personas.Contains(u.IdPersona))
            .Select(u => u.IdUsuario);

        var solicitudes = db.CarteraSolicitudesCupo.AsNoTracking()
            .Where(s => personas.Contains(s.IdPersona) || usuarios.Contains(s.IdUsuario))
            .Select(s => s.IdSolicitud);

        var finalizado = CarteraIntentoFases.Finalizado;
        var resultados = ReglaReconsultaMiDecisor.ResultadosConRespuesta.ToArray();

        var ultimaRespuesta = await db.CarteraSolicitudCupoIntentos.AsNoTracking()
            .Where(i => solicitudes.Contains(i.IdSolicitud))
            .Where(i => i.FaseIntento == finalizado
                     && i.FechaFin != null
                     && i.ResultadoTecnico != null
                     && resultados.Contains(i.ResultadoTecnico!))
            .MaxAsync(i => i.FechaFin, cancellationToken);

        var estadosQueBloquean = ReglaReconsultaMiDecisor.EstadosCupoQueBloquean.ToArray();

        var tieneCupoQueBloquea = await db.CarteraCuposOrdinarios.AsNoTracking()
            .AnyAsync(c => usuarios.Contains(c.IdUsuario) && estadosQueBloquean.Contains(c.Estado), cancellationToken);

        return new RiesgoProduccionSnapshot(ultimaRespuesta, tieneCupoQueBloquea);
    }
}
