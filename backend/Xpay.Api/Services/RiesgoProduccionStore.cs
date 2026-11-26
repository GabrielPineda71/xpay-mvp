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
public sealed class RiesgoProduccionStore(XpayDbContext db, IMiDecisorIdentidadResolver identidadResolver,
    IMiDecisorConsultaExternaStore consultasExternas) : IRiesgoProduccionStore
{
    public async Task<RiesgoProduccionSnapshot?> CargarAsync(
        long idSolicitud, CancellationToken cancellationToken = default)
    {
        var solicitud = await db.CarteraSolicitudesCupo.AsNoTracking().Where(s => s.IdSolicitud == idSolicitud)
            .Select(s => new { s.IdUsuario, s.IdPersona }).SingleOrDefaultAsync(cancellationToken);
        if (solicitud is null) return null;
        var identidad = await identidadResolver.ResolverAsync(solicitud.IdUsuario, cancellationToken);
        if (identidad is null || identidad.IdPersona != solicitud.IdPersona) return null;
        var numero = identidad.NumeroDocumento;
        // SQL selects a superset; the strict common normalizer decides in RAM.
        // Contains also preserves candidates with external Unicode whitespace:
        // SQL TRIM does not match string.Trim(). Partial matches are discarded.
        var candidatos = await db.Personas.AsNoTracking().Where(p =>
                (p.NumeroDocumento != null && p.NumeroDocumento.Replace(".", "").Contains(numero)) ||
                (p.NumeroDocumentoVerificado != null && p.NumeroDocumentoVerificado.Replace(".", "").Contains(numero)))
            .Select(p => new { p.IdPersona, p.TipoDocumento, p.NumeroDocumento, p.TipoDocumentoVeriffRaw, p.NumeroDocumentoVerificado })
            .Take(201).ToListAsync(cancellationToken);
        if (candidatos.Count > 200) return null;
        var personas = candidatos.Where(p => DocumentoCoincide(identidad, p.TipoDocumento, p.NumeroDocumento,
                p.TipoDocumentoVeriffRaw, p.NumeroDocumentoVerificado))
            .Select(p => p.IdPersona).Distinct().ToArray();
        if (!personas.Contains(identidad.IdPersona)) return null;

        var usuarios = db.Usuarios.AsNoTracking()
            .Where(u => personas.Contains(u.IdPersona))
            .Select(u => u.IdUsuario);

        var solicitudesTitular = db.CarteraSolicitudesCupo.AsNoTracking()
            .Where(s => personas.Contains(s.IdPersona) || usuarios.Contains(s.IdUsuario));
        var solicitudes = solicitudesTitular.Select(s => s.IdSolicitud);

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

        var pendiente = await (from intento in db.CarteraSolicitudCupoIntentos.AsNoTracking()
            join s in solicitudesTitular on intento.IdSolicitud equals s.IdSolicitud
            where intento.FaseIntento == CarteraIntentoFases.EnvioIncierto
                || intento.ResultadoTecnico == CarteraConsultaRiesgoResultados.ResultadoIncierto
                || intento.ResultadoTecnico == CarteraConsultaRiesgoResultados.ErrorProtocolo
                || (intento.FaseIntento == CarteraIntentoFases.PreCall && s.EstadoSolicitud == CarteraSolicitudCupoEstados.ConsultandoRiesgo)
            select intento.IdIntento).AnyAsync(cancellationToken);
        var externa = await consultasExternas.UltimaReferenciaAsync(identidad.TipoDocumento, numero, cancellationToken);
        return new RiesgoProduccionSnapshot(ultimaRespuesta, tieneCupoQueBloquea)
        {
            UltimaConsultaExternaUtc = externa, TieneConsultaPendiente = pendiente,
        };
    }

    internal static bool DocumentoCoincide(MiDecisorIdentidad identidad, string? tipo, string? numero, string? tipoVeriff, string? numeroVerificado)
    {
        var legal = tipo?.Trim().ToUpperInvariant();
        if (legal == "PASAPORTE") legal = "PAS";
        return (legal == identidad.TipoDocumento && TipoDocumentoVeriffMapper.NumeroComparable(numero) == identidad.NumeroDocumento)
            || (string.Equals(tipoVeriff?.Trim(), identidad.TipoVeriff, StringComparison.OrdinalIgnoreCase)
                && TipoDocumentoVeriffMapper.NumeroComparable(numeroVerificado) == identidad.NumeroDocumento);
    }
}
