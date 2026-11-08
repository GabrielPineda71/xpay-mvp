using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// XPAY-702 - revision manual de solicitudes PENDIENTE_REVISION_MANUAL.
// La resolucion se inserta UNA vez (UNIQUE id_solicitud) bajo el mismo AppLock
// que consumo/decision; conserva el resultado automatico original; el monto de
// una aprobacion lo calcula CarteraRevisionManualPolicy (nunca el request) y el
// cupo se materializa con la primitiva existente.
public enum ResultadoResolucionManual
{
    Resuelta,
    NoEncontrada,
    NoElegible,
    YaResuelta,
    NoAprobable,
    DatosInvalidos,
}

public sealed record ResolucionManualResultado(
    ResultadoResolucionManual Resultado, string? EstadoSolicitud, decimal? MontoAprobado);

public static class CarteraRevisionManualMotivos
{
    public const string AprobacionPolitica            = "APROBACION_MANUAL_POLITICA";
    public const string RechazoInformacionInsuficiente = "RECHAZO_MANUAL_INFORMACION_INSUFICIENTE";
    public const string RechazoRiesgo                 = "RECHAZO_MANUAL_RIESGO";
    public const string RechazoOtro                   = "RECHAZO_MANUAL_OTRO";
    public const string ReglaRechazoManual            = "RECHAZO_MANUAL";

    public static readonly IReadOnlyList<string> Rechazo = new[]
    {
        RechazoInformacionInsuficiente, RechazoRiesgo, RechazoOtro,
    };
}

public interface ICarteraRevisionManualStore
{
    Task<IReadOnlyList<RevisionManualItemDto>> ListarPendientesAsync(CancellationToken ct = default);
    Task<RevisionManualDetalleDto?> ObtenerDetalleAsync(long idSolicitud, CancellationToken ct = default);
    Task<ResolucionManualResultado> ResolverAsync(
        long idSolicitud, string decision, string motivoCodigo, string? observacion,
        long idAdmin, CancellationToken ct = default);
}

public sealed class CarteraRevisionManualStore(
    XpayDbContext db, ICarteraMaterializacionCupo materializacion) : ICarteraRevisionManualStore
{
    public async Task<IReadOnlyList<RevisionManualItemDto>> ListarPendientesAsync(CancellationToken ct = default)
    {
        var pendiente = CarteraSolicitudCupoEstados.PendienteRevisionManual;
        var filas = await db.CarteraSolicitudesCupo.AsNoTracking()
            .Where(s => s.EstadoSolicitud == pendiente)
            .OrderBy(s => s.FechaSolicitud)
            .Join(db.Personas.AsNoTracking(), s => s.IdPersona, p => p.IdPersona,
                  (s, p) => new { s, p.NumeroDocumento })
            .ToListAsync(ct);

        return filas.Select(f => new RevisionManualItemDto(
                f.s.IdSolicitud, f.s.IdUsuario, Enmascarar(f.NumeroDocumento),
                f.s.FechaSolicitud, f.s.FechaDecision, f.s.CodigoMotivoDecision))
            .ToList();
    }

    internal static string Enmascarar(string? documento)
    {
        var d = (documento ?? string.Empty).Trim();
        return d.Length <= 3 ? "***" : new string('*', d.Length - 3) + d[^3..];
    }

    // Mismo snapshot que CarteraDecisionCrediticiaStore entrega al motor.
    internal static CarteraDecisionSnapshot Snapshot(CarteraSolicitudCupo s) => new(
        ConInformacionObservado:           s.ConInformacionObservado,
        ScoreObservado:                    s.ScoreObservado,
        EstadoScore:                       s.EstadoScore,
        ViabilidadObservada:               s.ViabilidadObservada,
        RatingRecaudosObservado:           s.RatingRecaudosObservado,
        TipoDocumentoObservado:            s.TipoDocumentoObservado,
        EstadoDocumentoDatosBasicosRaw:    s.EstadoDocumentoDatosBasicosRaw,
        EstadoDocumentoInfoDemograficaRaw: s.EstadoDocumentoInfoDemograficaRaw,
        EstadoDocumentoCaptura:            s.EstadoDocumentoCaptura,
        RangoEdadDatosBasicosRaw:          s.RangoEdadDatosBasicosRaw,
        RangoEdadInfoDemograficaRaw:       s.RangoEdadInfoDemograficaRaw,
        RangoEdadCaptura:                  s.RangoEdadCaptura,
        ComportamientoVectorJson:          s.ComportamientoVectorJson,
        ComportamientoVectorCount:         s.ComportamientoVectorCount);

    internal static CarteraEvaluationContext Contexto(CarteraSolicitudCupo s)
        => CarteraEvaluationContext.DesdeConsultaRaw(s.ConsultaAnioRaw, s.ConsultaMesRaw);

    public async Task<RevisionManualDetalleDto?> ObtenerDetalleAsync(long idSolicitud, CancellationToken ct = default)
    {
        var s = await db.CarteraSolicitudesCupo.AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdSolicitud == idSolicitud, ct);
        if (s is null)
            return null;

        var documento = await db.Personas.AsNoTracking()
            .Where(p => p.IdPersona == s.IdPersona)
            .Select(p => p.NumeroDocumento)
            .FirstOrDefaultAsync(ct);

        var motivos = await db.CarteraSolicitudCupoMotivosDecision.AsNoTracking()
            .Where(m => m.IdSolicitud == idSolicitud)
            .OrderBy(m => m.Orden)
            .Select(m => m.CodigoMotivo)
            .ToListAsync(ct);

        var rev = await db.CarteraSolicitudCupoRevisiones.AsNoTracking()
            .FirstOrDefaultAsync(r => r.IdSolicitud == idSolicitud, ct);

        var propuesta = CarteraRevisionManualPolicy.Proponer(
            Snapshot(s), CarteraPolicyParameters.Vigente(), Contexto(s));

        var rangoEdad = string.IsNullOrWhiteSpace(s.RangoEdadDatosBasicosRaw)
            ? s.RangoEdadInfoDemograficaRaw
            : s.RangoEdadDatosBasicosRaw;

        var resolucion = rev is null ? null : new RevisionManualResueltaDto(
            rev.DecisionManual, rev.MontoAprobadoManual, rev.ReglaAplicada, rev.MotivoCodigo,
            rev.Observacion, rev.IdAdmin, rev.FechaResolucionUtc, rev.DecisionAutomaticaOriginal,
            rev.EstadoAutomaticoOriginal, rev.MotivoAutomaticoOriginal);

        return new RevisionManualDetalleDto(
            s.IdSolicitud, s.IdUsuario, Enmascarar(documento), s.EstadoSolicitud,
            s.DecisionCrediticia, s.CodigoMotivoDecision, motivos, s.FechaSolicitud,
            s.FechaDecision, s.ConInformacionObservado, s.ScoreObservado, s.EstadoScore,
            s.ViabilidadObservada, s.RatingRecaudosObservado, s.MontoSugeridoObservado,
            s.AlertasCountObservado, s.TipoDocumentoObservado, s.EstadoDocumentoCaptura,
            rangoEdad, s.ConsultaAnioRaw, s.ConsultaMesRaw,
            Comportamiento(s.ComportamientoVectorJson), propuesta, resolucion);
    }

    private static IReadOnlyList<ComportamientoMesDto> Comportamiento(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return Array.Empty<ComportamientoMesDto>();
        try
        {
            var items = System.Text.Json.JsonSerializer
                .Deserialize<List<CarteraComportamientoVectorItemRaw>>(json);
            if (items is null)
                return Array.Empty<ComportamientoMesDto>();
            return items.Select(i => new ComportamientoMesDto(i?.AnioMes, i?.Comportamiento)).ToList();
        }
        catch (System.Text.Json.JsonException)
        {
            return Array.Empty<ComportamientoMesDto>();
        }
    }

    public async Task<ResolucionManualResultado> ResolverAsync(
        long idSolicitud, string decision, string motivoCodigo, string? observacion,
        long idAdmin, CancellationToken ct = default)
    {
        var aprobar  = decision == CarteraDecisionCrediticia.Aprobada;
        var rechazar = decision == CarteraDecisionCrediticia.Rechazada;
        var motivoOk = aprobar
            ? motivoCodigo == CarteraRevisionManualMotivos.AprobacionPolitica
            : CarteraRevisionManualMotivos.Rechazo.Contains(motivoCodigo);

        if ((!aprobar && !rechazar) || !motivoOk || idAdmin <= 0
            || (observacion is not null && observacion.Length > 500))
            return new(ResultadoResolucionManual.DatosInvalidos, null, null);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var clave = $"XPAY:CARTERA_RIESGO:{idSolicitud}";
            ValidarLock(await AppLockHelper.AdquirirAsync(db, clave, ct));

            var s = await db.CarteraSolicitudesCupo
                .FirstOrDefaultAsync(x => x.IdSolicitud == idSolicitud, ct);
            if (s is null)
                return await NoAplicar(tx, ResultadoResolucionManual.NoEncontrada, null);

            var yaResuelta = await db.CarteraSolicitudCupoRevisiones
                .AnyAsync(r => r.IdSolicitud == idSolicitud, ct);
            if (yaResuelta)
                return await NoAplicar(tx, ResultadoResolucionManual.YaResuelta, s);

            if (s.EstadoSolicitud != CarteraSolicitudCupoEstados.PendienteRevisionManual
                || s.DecisionCrediticia != CarteraDecisionCrediticia.NoDecidible)
                return await NoAplicar(tx, ResultadoResolucionManual.NoElegible, s);

            var propuesta = CarteraRevisionManualPolicy.Proponer(
                Snapshot(s), CarteraPolicyParameters.Vigente(), Contexto(s));
            if (aprobar && (!propuesta.PermiteAprobar || propuesta.MontoPropuesto is not > 0m))
                return await NoAplicar(tx, ResultadoResolucionManual.NoAprobable, s);

            var now = DateTime.UtcNow;
            db.CarteraSolicitudCupoRevisiones.Add(new CarteraSolicitudCupoRevision
            {
                IdSolicitud                     = idSolicitud,
                DecisionManual                  = decision,
                MontoAprobadoManual             = aprobar ? propuesta.MontoPropuesto : null,
                ReglaAplicada                   = aprobar
                    ? propuesta.Regla
                    : CarteraRevisionManualMotivos.ReglaRechazoManual,
                MotivoCodigo                    = motivoCodigo,
                Observacion                     = observacion,
                IdAdmin                         = idAdmin,
                FechaResolucionUtc              = now,
                DecisionAutomaticaOriginal      = s.DecisionCrediticia,
                EstadoAutomaticoOriginal        = s.EstadoSolicitud,
                MotivoAutomaticoOriginal        = s.CodigoMotivoDecision,
                FechaDecisionAutomaticaOriginal = s.FechaDecision,
            });

            if (aprobar)
            {
                s.EstadoSolicitud      = CarteraSolicitudCupoEstados.AprobadaPendienteCupo;
                s.DecisionCrediticia   = CarteraDecisionCrediticia.Aprobada;
                s.MontoAprobado        = propuesta.MontoPropuesto;
                s.CodigoMotivoDecision = null;
            }
            else
            {
                s.EstadoSolicitud      = CarteraSolicitudCupoEstados.Rechazada;
                s.DecisionCrediticia   = CarteraDecisionCrediticia.Rechazada;
                s.MontoAprobado        = 0m;
                s.CodigoMotivoDecision = motivoCodigo;
            }
            s.FechaDecision      = now;
            s.FechaActualizacion = now;

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }

        db.ChangeTracker.Clear();
        if (aprobar)
            await materializacion.MaterializarCupoAsync(idSolicitud, ct);

        var final = await db.CarteraSolicitudesCupo.AsNoTracking()
            .FirstAsync(x => x.IdSolicitud == idSolicitud, ct);
        return new(ResultadoResolucionManual.Resuelta, final.EstadoSolicitud, final.MontoAprobado);
    }

    private static async Task<ResolucionManualResultado> NoAplicar(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx,
        ResultadoResolucionManual resultado, CarteraSolicitudCupo? s)
    {
        await tx.RollbackAsync(CancellationToken.None);
        return new(resultado, s?.EstadoSolicitud, s?.MontoAprobado);
    }

    // Mismo criterio que los demas stores: 0/1 adquirido; -1/-2/-3 contencion.
    private static void ValidarLock(int resultado)
    {
        if (resultado is 0 or 1)
            return;
        if (resultado is -1 or -2 or -3)
            throw new InvalidOperationException(
                "Hay otra operación en curso para esta solicitud. Intenta de nuevo en unos segundos.");
        throw new Exception($"sp_getapplock devolvió un código inesperado: {resultado}.");
    }
}
