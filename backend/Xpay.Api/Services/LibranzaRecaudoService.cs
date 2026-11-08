using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Integrations.Passport;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// P0 anticipo - lote durable de recaudo empresarial y su QR dinámico Passport.
//
// Ciclo del lote (estados en LibranzaLoteEstados / CHECK ck_llr_estado):
//   BORRADOR        creado: detalles y total congelados; sin QR todavía.
//   PENDIENTE_PAGO  QR dinámico generado (amount = total, reference = lote).
//   PAGO_EN_PROCESO Passport notificó received/confirmed del pago.
//   APLICADO        settled validado y aplicado en UNA transacción (terminal).
//   REQUIERE_REVISION settled no conciliable (monto/cuenta/estado): fondos
//                   posiblemente recibidos, sin movimiento financiero.
//   CANCELADO       cancelado por la empresa antes de cualquier pago (terminal;
//                   detalles LIBERADO, anticipos re-agrupables).
// Este servicio NUNCA mueve dinero: la aplicación financiera vive solo en
// LibranzaRecaudoAplicacionService y solo se dispara con un settled validado.
public class LibranzaRecaudoService
{
    // Un QR EN_CURSO más reciente que esto se considera en vuelo (otra
    // solicitud lo está generando); pasado este tiempo se permite reintentar
    // con la MISMA referencia (nunca un lote nuevo ni una operación financiera).
    public static readonly TimeSpan VentanaReintentoQr = TimeSpan.FromSeconds(60);

    private readonly XpayDbContext                   _db;
    private readonly IPassportQrClient               _qr;
    private readonly IConfiguration                  _config;
    private readonly ILogger<LibranzaRecaudoService> _logger;

    public LibranzaRecaudoService(
        XpayDbContext db, IPassportQrClient qr, IConfiguration config, ILogger<LibranzaRecaudoService> logger)
    {
        _db     = db;
        _qr     = qr;
        _config = config;
        _logger = logger;
    }

    // ── Crear lote (idempotente por convenio + corte) ─────────────────────────

    public async Task<LoteRecaudoResponse> CrearLoteAsync(long idConvenio, DateOnly fechaCorte, long idUsuario)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            ValidarLock(await AppLockHelper.AdquirirAsync(_db, $"XPAY:LIBRANZA_LOTE:{idConvenio}:{fechaCorte:yyyy-MM-dd}"));

            if (!await _db.LibranzaEmpresasConvenio.AnyAsync(c => c.IdConvenio == idConvenio))
                throw new KeyNotFoundException($"Convenio {idConvenio} no encontrado.");

            // Idempotencia funcional: un solo lote abierto por convenio + corte
            // (uq_llr_convenio_corte_abierto). Repetir la creación lo devuelve.
            var abierto = await _db.LibranzaLotesRecaudo.FirstOrDefaultAsync(l =>
                l.IdConvenio == idConvenio && l.FechaCorte == fechaCorte
                && (l.Estado == LibranzaLoteEstados.Borrador || l.Estado == LibranzaLoteEstados.PendientePago
                    || l.Estado == LibranzaLoteEstados.PagoEnProceso || l.Estado == LibranzaLoteEstados.RequiereRevision));
            if (abierto is not null)
            {
                await tx.CommitAsync();
                return await ToResponseAsync(abierto);
            }

            var candidatos = await _db.LibranzaAnticipos
                .FromSqlInterpolated($"SELECT * FROM libranza_anticipos WITH (UPDLOCK, ROWLOCK) WHERE id_convenio = {idConvenio} AND fecha_pago_programada = {fechaCorte} AND estado = 'DESEMBOLSADO'")
                .OrderBy(a => a.IdAnticipo)
                .ToListAsync();
            var ids = candidatos.Select(a => a.IdAnticipo).ToList();
            var yaAgrupados = await _db.LibranzaLoteRecaudoDetalles
                .Where(d => ids.Contains(d.IdAnticipo)
                         && (d.Estado == LibranzaLoteEstados.DetalleVigente || d.Estado == LibranzaLoteEstados.DetalleAplicado))
                .Select(d => d.IdAnticipo)
                .ToListAsync();
            var anticipos = candidatos.Where(a => !yaAgrupados.Contains(a.IdAnticipo)).ToList();

            if (anticipos.Count == 0)
                throw new InvalidOperationException("No hay anticipos pendientes de recaudo para el corte indicado.");
            if (anticipos.Any(a => a.MomentoCobroComision != LibranzaAnticipoService.MomentoCobroHabilitado))
                throw new InvalidOperationException("El corte contiene anticipos con modalidad distinta de VENCIDO; no se puede agrupar.");
            if (anticipos.Any(a => a.ValorTotalACobrar != a.ValorSolicitado + a.ValorComision + a.ValorIva))
                throw new InvalidOperationException("Inconsistencia de montos en un anticipo del corte; revisar antes de recaudar.");

            string referencia;
            var intentos = 0;
            do
            {
                referencia = LibranzaRecaudoReferencia.Generar();
                if (++intentos > 5)
                    throw new InvalidOperationException("No fue posible generar una referencia única para el lote.");
            }
            while (await _db.LibranzaLotesRecaudo.AnyAsync(l => l.ReferenciaPublica == referencia));

            var ahora = DateTime.UtcNow;
            var lote = new LibranzaLoteRecaudo
            {
                IdConvenio        = idConvenio,
                FechaCorte        = fechaCorte,
                Estado            = LibranzaLoteEstados.Borrador,
                ReferenciaPublica = referencia,
                MontoTotal        = anticipos.Sum(a => a.ValorTotalACobrar),
                Moneda            = "COP",
                CantidadDetalles  = anticipos.Count,
                FechaCreacion     = ahora,
                CreadoPorUsuario  = idUsuario,
            };
            _db.LibranzaLotesRecaudo.Add(lote);
            await _db.SaveChangesAsync();

            _db.LibranzaLoteRecaudoDetalles.AddRange(anticipos.Select(a => new LibranzaLoteRecaudoDetalle
            {
                IdLote        = lote.IdLote,
                IdAnticipo    = a.IdAnticipo,
                IdEmpleado    = a.IdEmpleado,
                MontoCapital  = a.ValorSolicitado,
                MontoComision = a.ValorComision,
                MontoIva      = a.ValorIva,
                MontoTotal    = a.ValorTotalACobrar,
                Estado        = LibranzaLoteEstados.DetalleVigente,
                FechaCreacion = ahora,
            }));
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation("LIBRANZA_LOTE_CREADO: lote={Lote} convenio={Conv} corte={Corte} detalles={N} total={Total}",
                lote.IdLote, idConvenio, fechaCorte, lote.CantidadDetalles, lote.MontoTotal);
            return await ToResponseAsync(lote);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // ── Generar QR dinámico (idempotente por lote) ────────────────────────────

    public async Task<LoteRecaudoResponse> GenerarQrAsync(long idLote, long idConvenio, long idUsuario)
    {
        // Fase 1 (tx corta): marca EN_CURSO bajo lock ANTES de la llamada HTTP,
        // para que dos solicitudes no generen dos QR en paralelo.
        LibranzaLoteRecaudo lote;
        await using (var tx = await _db.Database.BeginTransactionAsync())
        {
            lote = await BloquearLoteAsync(idLote, idConvenio);

            if (lote.Estado == LibranzaLoteEstados.PendientePago && lote.QrEstado == LibranzaLoteEstados.QrGenerado)
            {
                await tx.CommitAsync();
                return await ToResponseAsync(lote); // ya generado: misma respuesta, sin HTTP
            }
            if (lote.Estado != LibranzaLoteEstados.Borrador)
                throw new InvalidOperationException($"El lote no admite generar QR en estado {lote.Estado}.");
            if (lote.QrEstado == LibranzaLoteEstados.QrEnCurso
                && lote.FechaQrIntento > DateTime.UtcNow - VentanaReintentoQr)
                throw new InvalidOperationException("La generación del QR de este lote está en curso. Intenta de nuevo en unos segundos.");

            lote.QrEstado       = LibranzaLoteEstados.QrEnCurso;
            lote.FechaQrIntento = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        var detalles = await _db.LibranzaLoteRecaudoDetalles.AsNoTracking()
            .Where(d => d.IdLote == idLote && d.Estado == LibranzaLoteEstados.DetalleVigente)
            .ToListAsync();

        PassportCreateQrCodeRequest request;
        try
        {
            request = LibranzaRecaudoQrRequestBuilder.Build(
                lote.ReferenciaPublica, lote.MontoTotal,
                detalles.Sum(d => d.MontoComision), detalles.Sum(d => d.MontoIva),
                _config[PassportOptions.EnvCollectionKeyId], _config[PassportOptions.EnvOperationalCustomerId]);
            if (detalles.Count != lote.CantidadDetalles || detalles.Sum(d => d.MontoTotal) != lote.MontoTotal)
                throw new InvalidOperationException("El total del lote no coincide con sus detalles.");
        }
        catch (Exception ex) when (ex is PassportConfigurationException or InvalidOperationException)
        {
            // Fallo LOCAL: Passport no recibió nada; se libera la marca.
            await LiberarMarcaQrAsync(idLote);
            throw;
        }

        PassportQrCodeResponse response;
        try
        {
            response = await _qr.CreateQrCodeAsync(request);
        }
        catch (PassportException ex)
        {
            // Incierto: la marca EN_CURSO se conserva; pasada la ventana se
            // permite reintentar con la MISMA referencia y el MISMO monto.
            _logger.LogWarning("LIBRANZA_LOTE_QR_INCIERTO: lote={Lote} tipo={Tipo}", idLote, ex.GetType().Name);
            throw;
        }

        var montoRespuesta = LibranzaRecaudoQrRequestBuilder.ParsearMonto(response.Amount?.Value);
        if (string.IsNullOrWhiteSpace(response.Id) || string.IsNullOrWhiteSpace(response.QrCodeData)
            || LibranzaRecaudoReferencia.ExtraerDePassport(response.QrCodeReference) != lote.ReferenciaPublica
            || (response.Amount?.Value is not null && montoRespuesta != lote.MontoTotal)
            || (response.Amount?.Currency is not null && response.Amount.Currency != "COP"))
        {
            await LiberarMarcaQrAsync(idLote);
            throw new PassportProtocolException("La respuesta de Passport no coincide con el lote (referencia, monto o datos del QR).");
        }

        // Relectura autoritativa: sin el Clear, EF devolvería la instancia ya
        // rastreada con los valores de la fase 1.
        _db.ChangeTracker.Clear();
        await using (var tx = await _db.Database.BeginTransactionAsync())
        {
            lote = await BloquearLoteAsync(idLote, idConvenio);
            if (lote.QrEstado == LibranzaLoteEstados.QrEnCurso && lote.Estado == LibranzaLoteEstados.Borrador)
            {
                lote.QrEstado        = LibranzaLoteEstados.QrGenerado;
                lote.PassportQrId    = response.Id;
                lote.QrCodeData      = response.QrCodeData;
                lote.FechaQrGenerado = DateTime.UtcNow;
                lote.Estado          = LibranzaLoteEstados.PendientePago;
                await _db.SaveChangesAsync();
            }
            await tx.CommitAsync();
        }

        _logger.LogInformation("LIBRANZA_LOTE_QR_GENERADO: lote={Lote} usuario={Usr}", idLote, idUsuario);
        return await ToResponseAsync(lote);
    }

    // ── Cancelar lote (solo antes de cualquier pago) ─────────────────────────

    public async Task<LoteRecaudoResponse> CancelarLoteAsync(long idLote, long idConvenio, long idUsuario)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var lote = await BloquearLoteAsync(idLote, idConvenio);
            if (lote.Estado == LibranzaLoteEstados.Cancelado)
            {
                await tx.CommitAsync();
                return await ToResponseAsync(lote);
            }
            if (lote.Estado is not (LibranzaLoteEstados.Borrador or LibranzaLoteEstados.PendientePago)
                || lote.PassportPaymentId is not null)
                throw new InvalidOperationException($"El lote no se puede cancelar en estado {lote.Estado}.");
            if (lote.QrEstado == LibranzaLoteEstados.QrEnCurso)
                throw new InvalidOperationException("La generación del QR de este lote está en curso; no se puede cancelar todavía.");

            var detalles = await _db.LibranzaLoteRecaudoDetalles
                .Where(d => d.IdLote == idLote && d.Estado == LibranzaLoteEstados.DetalleVigente)
                .ToListAsync();
            foreach (var d in detalles) d.Estado = LibranzaLoteEstados.DetalleLiberado;

            var ahora = DateTime.UtcNow;
            lote.Estado            = LibranzaLoteEstados.Cancelado;
            lote.FechaCierre       = ahora;
            lote.CerradoPorUsuario = idUsuario;
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation("LIBRANZA_LOTE_CANCELADO: lote={Lote} usuario={Usr}", idLote, idUsuario);
            return await ToResponseAsync(lote);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // ── Lecturas ──────────────────────────────────────────────────────────────

    public async Task<List<LoteRecaudoResponse>> ListarLotesAsync(long idConvenio)
    {
        var lotes = await _db.LibranzaLotesRecaudo.AsNoTracking()
            .Where(l => l.IdConvenio == idConvenio)
            .OrderByDescending(l => l.IdLote)
            .Take(100)
            .ToListAsync();
        var result = new List<LoteRecaudoResponse>(lotes.Count);
        foreach (var l in lotes) result.Add(ToResponse(l, new List<LoteRecaudoDetalleResponse>()));
        return result;
    }

    public async Task<LoteRecaudoResponse> ObtenerLoteAsync(long idLote, long idConvenio)
    {
        var lote = await _db.LibranzaLotesRecaudo.AsNoTracking()
            .FirstOrDefaultAsync(l => l.IdLote == idLote && l.IdConvenio == idConvenio)
            ?? throw new KeyNotFoundException($"Lote {idLote} no encontrado.");
        return await ToResponseAsync(lote);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<LibranzaLoteRecaudo> BloquearLoteAsync(long idLote, long idConvenio) =>
        await _db.LibranzaLotesRecaudo
            .FromSqlInterpolated($"SELECT * FROM libranza_lotes_recaudo WITH (UPDLOCK, ROWLOCK) WHERE id_lote = {idLote} AND id_convenio = {idConvenio}")
            .FirstOrDefaultAsync()
        ?? throw new KeyNotFoundException($"Lote {idLote} no encontrado.");

    private async Task LiberarMarcaQrAsync(long idLote)
    {
        _db.ChangeTracker.Clear();
        await using var tx = await _db.Database.BeginTransactionAsync();
        var lote = await _db.LibranzaLotesRecaudo
            .FromSqlInterpolated($"SELECT * FROM libranza_lotes_recaudo WITH (UPDLOCK, ROWLOCK) WHERE id_lote = {idLote}")
            .FirstOrDefaultAsync();
        if (lote is not null && lote.QrEstado == LibranzaLoteEstados.QrEnCurso)
        {
            lote.QrEstado       = null;
            lote.FechaQrIntento = null;
            await _db.SaveChangesAsync();
        }
        await tx.CommitAsync();
    }

    internal static void ValidarLock(int resultado)
    {
        switch (resultado)
        {
            case 0:
            case 1:
                return;
            case -1:
            case -2:
            case -3:
                throw new InvalidOperationException("Otra operación sobre este lote está en curso. Intenta de nuevo.");
            default:
                throw new Exception($"sp_getapplock devolvió un código no reconocido ({resultado}).");
        }
    }

    private async Task<LoteRecaudoResponse> ToResponseAsync(LibranzaLoteRecaudo lote)
    {
        var detalles = await (
            from d in _db.LibranzaLoteRecaudoDetalles.AsNoTracking()
            join e in _db.LibranzaEmpleados.AsNoTracking() on d.IdEmpleado equals e.IdEmpleado
            where d.IdLote == lote.IdLote
            orderby d.IdDetalle
            select new LoteRecaudoDetalleResponse
            {
                IdDetalle       = d.IdDetalle,
                IdAnticipo      = d.IdAnticipo,
                IdEmpleado      = d.IdEmpleado,
                NombresEmpleado = (e.Nombres + " " + e.Apellidos).Trim(),
                MontoCapital    = d.MontoCapital,
                MontoComision   = d.MontoComision,
                MontoIva        = d.MontoIva,
                MontoTotal      = d.MontoTotal,
                Estado          = d.Estado,
            }).ToListAsync();
        return ToResponse(lote, detalles);
    }

    private static LoteRecaudoResponse ToResponse(LibranzaLoteRecaudo l, List<LoteRecaudoDetalleResponse> detalles) => new()
    {
        IdLote                 = l.IdLote,
        IdConvenio             = l.IdConvenio,
        FechaCorte             = l.FechaCorte.ToString("yyyy-MM-dd"),
        Estado                 = l.Estado,
        ReferenciaPublica      = l.ReferenciaPublica,
        MontoTotal             = l.MontoTotal,
        Moneda                 = l.Moneda,
        CantidadDetalles       = l.CantidadDetalles,
        FechaCreacion          = l.FechaCreacion,
        FechaCierre            = l.FechaCierre,
        QrEstado               = l.QrEstado,
        QrCodeData             = l.QrCodeData,
        FechaQrGenerado        = l.FechaQrGenerado,
        FechaPagoRecibido      = l.FechaPagoRecibido,
        FechaFondosConfirmados = l.FechaFondosConfirmados,
        FechaAplicacion        = l.FechaAplicacion,
        MotivoRevision         = l.MotivoRevision,
        Detalles               = detalles,
    };
}
