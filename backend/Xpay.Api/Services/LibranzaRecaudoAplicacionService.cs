using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Integrations.Passport;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// P0 anticipo - aplicación del pago entrante Passport a un lote de recaudo.
//
// REGLA ABSOLUTA: solo un payment.inbound.settled con firma válida puede
// disparar movimiento financiero. received/confirmed solo marcan el lote
// PAGO_EN_PROCESO; rejected lo devuelve a PENDIENTE_PAGO. Ninguno acredita.
//
// Correlación por payment_id: Passport puede entregar settled ANTES que
// received/confirmed (observado en Sandbox, XPAY-604), y su contrato no
// garantiza que settled repita amount/qr_code_reference/account_id. Por eso
// los datos de validación se toman del propio settled y, si faltan, de los
// eventos YA registrados del MISMO payment_id con firma válida. Si aún faltan,
// no se aplica nada (PENDIENTE_DATOS) y la aplicación se reintenta cuando llegue
// el evento que los trae.
//
// Antes de aplicar se valida, bajo lock del lote: firma, payment_id, account_id
// esperado (PASSPORT_ACCOUNT_ID), referencia del lote, monto EXACTO, moneda COP,
// estado del lote y que no exista aplicación previa. Cualquier discrepancia deja
// el lote REQUIERE_REVISION sin mover dinero.
//
// La aplicación es UNA transacción: lock del lote, verificación del total,
// lock de cada anticipo, asiento por anticipo, anticipos PAGADO, detalles
// APLICADO, lote APLICADO con referencias Passport. Cualquier fallo revierte
// todo. Un settled duplicado tras APLICADO es no-op financiero (además de la
// unicidad durable del evento en uq_pwh_evento_pago).
public sealed record ResultadoRecaudo(string Resultado, long? IdLote);

public class LibranzaRecaudoAplicacionService
{
    // Cuentas ledger (unidad 1).
    private const string CodBancoReal     = "110103"; // Banco Coopcentral XPAY REAL (Passport) - 044
    private const string CodCartera       = "130104"; // Cartera Anticipo Nómina
    private const string CodComision      = "310103"; // Ingreso Comisión Anticipo Nómina
    private const string CodIva           = "230103"; // IVA Anticipo Nómina
    private const long   IdUnidadNegocio  = 1;

    public const string ResFirmaNoValida        = "FIRMA_NO_VALIDA_SIN_EFECTO";
    public const string ResSinLote              = "SIN_LOTE";
    public const string ResPendienteDatos       = "PENDIENTE_DATOS";
    public const string ResPagoEnProceso        = "PAGO_EN_PROCESO";
    public const string ResSinCambio            = "SIN_CAMBIO";
    public const string ResRechazoRegistrado    = "RECHAZO_REGISTRADO";
    public const string ResAplicado             = "LOTE_APLICADO";
    public const string ResYaAplicado           = "YA_APLICADO";
    public const string ResRevision             = "REQUIERE_REVISION";
    public const string ResPagoAdicional        = "PAGO_ADICIONAL_SOBRE_LOTE_APLICADO";
    public const string ResRechazoTrasAplicacion = "RECHAZO_TRAS_APLICACION";

    private readonly XpayDbContext                             _db;
    private readonly IConfiguration                            _config;
    private readonly ILogger<LibranzaRecaudoAplicacionService> _logger;

    public LibranzaRecaudoAplicacionService(
        XpayDbContext db, IConfiguration config, ILogger<LibranzaRecaudoAplicacionService> logger)
    {
        _db     = db;
        _config = config;
        _logger = logger;
    }

    // Punto de entrada desde PassportWebhookService, DESPUÉS de registrar el
    // evento (que ya está persistido y forma parte de la correlación).
    public async Task<ResultadoRecaudo> ProcesarEventoEntranteAsync(
        PassportInboundEventKind kind, string paymentId, bool signatureValid, long idEvento,
        CancellationToken ct = default)
    {
        // OBSERVE (opt-in) puede registrar eventos con firma inválida: nunca
        // tienen efecto sobre lotes ni dinero.
        if (!signatureValid)
            return new ResultadoRecaudo(ResFirmaNoValida, null);

        var datos = await RecolectarDatosAsync(paymentId, ct);

        switch (kind)
        {
            case PassportInboundEventKind.Received:
            case PassportInboundEventKind.Confirmed:
            {
                var r = await MarcarPagoEnProcesoAsync(paymentId, datos, ct);
                // settled pudo llegar ANTES: si ya hay uno válido, este evento
                // pudo traer los datos que faltaban para aplicarlo.
                if (datos.IdEventoSettled is long idSettled && r.IdLote is not null)
                    return await IntentarAplicarAsync(paymentId, datos, idSettled, ct);
                return r;
            }
            case PassportInboundEventKind.Rejected:
                return await RegistrarRechazoAsync(paymentId, datos, ct);
            case PassportInboundEventKind.Settled:
                return await IntentarAplicarAsync(paymentId, datos, datos.IdEventoSettled ?? idEvento, ct);
            default:
                return new ResultadoRecaudo(ResSinCambio, null);
        }
    }

    // ── Correlación ───────────────────────────────────────────────────────────

    internal sealed record DatosPago(
        string? Referencia, string? AccountId, string? MontoRaw, string? Moneda,
        string? EndToEnd, string? SettledAt, long? IdEventoSettled);

    private async Task<DatosPago> RecolectarDatosAsync(string paymentId, CancellationToken ct)
    {
        var eventos = await _db.PassportWebhookEvents.AsNoTracking()
            .Where(e => e.PassportPaymentId == paymentId && e.SignatureValid)
            .OrderBy(e => e.IdEvent)
            .Select(e => new { e.IdEvent, e.EventType, e.PayloadSanitizedJson })
            .ToListAsync(ct);

        string? referencia = null, account = null, monto = null, moneda = null, e2e = null, settledAt = null;
        long? idSettled = null;
        foreach (var ev in eventos)
        {
            if (string.Equals(ev.EventType, PassportInboundEventClassifier.EventInboundSettled, StringComparison.OrdinalIgnoreCase))
                idSettled ??= ev.IdEvent;
            if (string.IsNullOrWhiteSpace(ev.PayloadSanitizedJson)) continue;
            using var doc = JsonDocument.Parse(ev.PayloadSanitizedJson);
            var root = doc.RootElement;
            referencia ??= Str(root, "qr_code_reference");
            account    ??= Str(root, "account_id");
            if (monto is null && Str(root, "amount_value") is { } mv)
            {
                monto  = mv;
                moneda = Str(root, "amount_currency");
            }
            e2e       ??= Str(root, "end_to_end_identification");
            settledAt ??= Str(root, "settled_at");
        }
        return new DatosPago(referencia, account, monto, moneda, e2e, settledAt, idSettled);
    }

    private static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()
            : null;

    private async Task<long?> BuscarIdLoteAsync(string paymentId, string? referencia, CancellationToken ct)
    {
        var porPago = await _db.LibranzaLotesRecaudo.AsNoTracking()
            .Where(l => l.PassportPaymentId == paymentId)
            .Select(l => (long?)l.IdLote)
            .FirstOrDefaultAsync(ct);
        if (porPago is not null || referencia is null) return porPago;
        return await _db.LibranzaLotesRecaudo.AsNoTracking()
            .Where(l => l.ReferenciaPublica == referencia)
            .Select(l => (long?)l.IdLote)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<LibranzaLoteRecaudo> BloquearLoteAsync(long idLote, CancellationToken ct)
    {
        _db.ChangeTracker.Clear();
        return await _db.LibranzaLotesRecaudo
            .FromSqlInterpolated($"SELECT * FROM libranza_lotes_recaudo WITH (UPDLOCK, ROWLOCK) WHERE id_lote = {idLote}")
            .FirstAsync(ct);
    }

    // ── received / confirmed: sin efecto financiero ───────────────────────────

    private async Task<ResultadoRecaudo> MarcarPagoEnProcesoAsync(string paymentId, DatosPago datos, CancellationToken ct)
    {
        var idLote = await BuscarIdLoteAsync(paymentId, datos.Referencia, ct);
        if (idLote is null) return new ResultadoRecaudo(ResSinLote, null);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var lote = await BloquearLoteAsync(idLote.Value, ct);
        var resultado = ResSinCambio;
        if (lote.Estado == LibranzaLoteEstados.PendientePago && lote.PassportPaymentId is null)
        {
            lote.Estado                 = LibranzaLoteEstados.PagoEnProceso;
            lote.PassportPaymentId      = paymentId;
            lote.FechaPagoRecibido      = DateTime.UtcNow;
            lote.EndToEndIdentification ??= datos.EndToEnd;
            await _db.SaveChangesAsync(ct);
            resultado = ResPagoEnProceso;
        }
        else if (lote.PassportPaymentId is not null && lote.PassportPaymentId != paymentId)
        {
            _logger.LogWarning("LIBRANZA_RECAUDO_PAGO_PARALELO: lote={Lote} estado={Estado}", lote.IdLote, lote.Estado);
        }
        await tx.CommitAsync(ct);
        return new ResultadoRecaudo(resultado, lote.IdLote);
    }

    // ── rejected: devuelve el lote a PENDIENTE_PAGO ───────────────────────────

    private async Task<ResultadoRecaudo> RegistrarRechazoAsync(string paymentId, DatosPago datos, CancellationToken ct)
    {
        var idLote = await BuscarIdLoteAsync(paymentId, datos.Referencia, ct);
        if (idLote is null) return new ResultadoRecaudo(ResSinLote, null);

        var codigo = await _db.PassportWebhookEvents.AsNoTracking()
            .Where(e => e.PassportPaymentId == paymentId && e.SignatureValid
                     && e.EventType == PassportInboundEventClassifier.EventInboundRejected)
            .Select(e => e.PayloadSanitizedJson)
            .FirstOrDefaultAsync(ct);
        string? errorCode = null;
        if (codigo is not null)
        {
            using var doc = JsonDocument.Parse(codigo);
            errorCode = Str(doc.RootElement, "error_code");
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var lote = await BloquearLoteAsync(idLote.Value, ct);
        string resultado;
        if (lote.Estado == LibranzaLoteEstados.Aplicado)
        {
            _logger.LogCritical("LIBRANZA_RECAUDO_RECHAZO_TRAS_APLICACION: lote={Lote}", lote.IdLote);
            resultado = ResRechazoTrasAplicacion;
        }
        else
        {
            if (lote.Estado == LibranzaLoteEstados.PagoEnProceso && lote.PassportPaymentId == paymentId)
            {
                lote.Estado            = LibranzaLoteEstados.PendientePago;
                lote.PassportPaymentId = null;
            }
            lote.UltimoRechazoCodigo = Truncar(errorCode ?? "SIN_CODIGO", 50);
            lote.FechaUltimoRechazo  = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            resultado = ResRechazoRegistrado;
        }
        await tx.CommitAsync(ct);
        return new ResultadoRecaudo(resultado, lote.IdLote);
    }

    // ── settled: única ruta con efecto financiero ─────────────────────────────

    private sealed class LoteInconsistenteException(string motivo) : Exception(motivo);

    private async Task<ResultadoRecaudo> IntentarAplicarAsync(
        string paymentId, DatosPago datos, long idEventoSettled, CancellationToken ct)
    {
        var idLote = await BuscarIdLoteAsync(paymentId, datos.Referencia, ct);
        if (idLote is null)
            return new ResultadoRecaudo(datos.Referencia is null ? ResPendienteDatos : ResSinLote, null);

        try
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            var lote = await BloquearLoteAsync(idLote.Value, ct);

            switch (lote.Estado)
            {
                case LibranzaLoteEstados.Aplicado:
                    await tx.CommitAsync(ct);
                    if (lote.PassportPaymentId == paymentId)
                        return new ResultadoRecaudo(ResYaAplicado, lote.IdLote);
                    _logger.LogCritical("LIBRANZA_RECAUDO_PAGO_ADICIONAL: lote={Lote}", lote.IdLote);
                    return new ResultadoRecaudo(ResPagoAdicional, lote.IdLote);

                case LibranzaLoteEstados.RequiereRevision:
                    await tx.CommitAsync(ct);
                    return new ResultadoRecaudo(ResRevision, lote.IdLote);

                case LibranzaLoteEstados.Cancelado:
                case LibranzaLoteEstados.Borrador:
                    return await MarcarRevisionAsync(tx, lote, $"FONDOS_SOBRE_LOTE_{lote.Estado}", ct);
            }

            // PENDIENTE_PAGO / PAGO_EN_PROCESO
            if (lote.PassportPaymentId is not null && lote.PassportPaymentId != paymentId)
                return await MarcarRevisionAsync(tx, lote, "PAGO_DISTINTO_AL_EN_PROCESO", ct);

            if (datos.MontoRaw is null || datos.AccountId is null)
            {
                await tx.CommitAsync(ct);
                return new ResultadoRecaudo(ResPendienteDatos, lote.IdLote);
            }

            var cuentaEsperada = _config[PassportOptions.EnvOperationalAccountId];
            if (string.IsNullOrWhiteSpace(cuentaEsperada))
                return await MarcarRevisionAsync(tx, lote, "CUENTA_OPERATIVA_NO_CONFIGURADA", ct);
            if (!string.Equals(datos.AccountId.Trim(), cuentaEsperada.Trim(), StringComparison.OrdinalIgnoreCase))
                return await MarcarRevisionAsync(tx, lote, "CUENTA_DESTINO_NO_COINCIDE", ct);
            if (datos.Referencia is not null && datos.Referencia != lote.ReferenciaPublica)
                return await MarcarRevisionAsync(tx, lote, "REFERENCIA_NO_COINCIDE", ct);
            if (!string.Equals(datos.Moneda, "COP", StringComparison.OrdinalIgnoreCase))
                return await MarcarRevisionAsync(tx, lote, "MONEDA_NO_COINCIDE", ct);
            var monto = LibranzaRecaudoQrRequestBuilder.ParsearMonto(datos.MontoRaw);
            if (monto is null || monto.Value != lote.MontoTotal)
                return await MarcarRevisionAsync(tx, lote, "MONTO_NO_COINCIDE", ct);

            await AplicarAsync(lote, paymentId, datos, idEventoSettled, ct);
            await tx.CommitAsync(ct);

            _logger.LogInformation("LIBRANZA_RECAUDO_APLICADO: lote={Lote} detalles={N} total={Total} ledger={Ledger}",
                lote.IdLote, lote.CantidadDetalles, lote.MontoTotal, lote.IdTransaccionLedgerAplicacion);
            return new ResultadoRecaudo(ResAplicado, lote.IdLote);
        }
        catch (LoteInconsistenteException ex)
        {
            // La transacción de aplicación ya se revirtió por completo (dispose).
            // Se deja constancia en una transacción propia, sin movimiento financiero.
            await using var tx2 = await _db.Database.BeginTransactionAsync(ct);
            var lote = await BloquearLoteAsync(idLote.Value, ct);
            if (lote.Estado is LibranzaLoteEstados.PendientePago or LibranzaLoteEstados.PagoEnProceso)
                return await MarcarRevisionAsync(tx2, lote, ex.Message, ct);
            await tx2.CommitAsync(ct);
            return new ResultadoRecaudo(ResRevision, lote.IdLote);
        }
    }

    private async Task<ResultadoRecaudo> MarcarRevisionAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, LibranzaLoteRecaudo lote, string motivo,
        CancellationToken ct)
    {
        lote.Estado         = LibranzaLoteEstados.RequiereRevision;
        lote.MotivoRevision = Truncar(motivo, 500);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        _logger.LogWarning("LIBRANZA_RECAUDO_REQUIERE_REVISION: lote={Lote} motivo={Motivo}", lote.IdLote, motivo);
        return new ResultadoRecaudo(ResRevision, lote.IdLote);
    }

    private async Task AplicarAsync(
        LibranzaLoteRecaudo lote, string paymentId, DatosPago datos, long idEventoSettled, CancellationToken ct)
    {
        var detalles = await _db.LibranzaLoteRecaudoDetalles
            .Where(d => d.IdLote == lote.IdLote && d.Estado == LibranzaLoteEstados.DetalleVigente)
            .OrderBy(d => d.IdAnticipo)
            .ToListAsync(ct);
        if (detalles.Count != lote.CantidadDetalles || detalles.Sum(d => d.MontoTotal) != lote.MontoTotal)
            throw new LoteInconsistenteException("TOTAL_LOTE_NO_COINCIDE_CON_DETALLES");

        // Lock de cada obligación en orden de id (orden estable: sin deadlocks
        // entre aplicaciones concurrentes).
        var anticipos = new List<LibranzaAnticipo>(detalles.Count);
        foreach (var d in detalles)
        {
            var a = await _db.LibranzaAnticipos
                .FromSqlInterpolated($"SELECT * FROM libranza_anticipos WITH (UPDLOCK, ROWLOCK) WHERE id_anticipo = {d.IdAnticipo}")
                .FirstOrDefaultAsync(ct)
                ?? throw new LoteInconsistenteException("ANTICIPO_INEXISTENTE");
            if (a.Estado != "DESEMBOLSADO" || a.IdConvenio != lote.IdConvenio
                || a.MomentoCobroComision != LibranzaAnticipoService.MomentoCobroHabilitado
                || a.ValorTotalACobrar != d.MontoTotal || a.ValorSolicitado != d.MontoCapital
                || a.ValorComision != d.MontoComision || a.ValorIva != d.MontoIva)
                throw new LoteInconsistenteException($"ANTICIPO_NO_APLICABLE:{a.IdAnticipo}");
            anticipos.Add(a);
        }

        var ctaBanco    = await CuentaAsync(CodBancoReal, ct);
        var ctaCartera  = await CuentaAsync(CodCartera, ct);
        var ctaComision = await CuentaAsync(CodComision, ct);
        var ctaIva      = await CuentaAsync(CodIva, ct);

        var ahora = DateTime.UtcNow;
        var ledgerTx = new LedgerTransaccion
        {
            IdUnidadNegocio  = IdUnidadNegocio,
            TipoTransaccion  = "LIBRANZA_RECAUDO_APLICACION",
            ReferenciaTipo   = "libranza_lotes_recaudo",
            ReferenciaId     = lote.IdLote,
            Descripcion      = $"Recaudo lote #{lote.IdLote} (pago Passport settled).",
            ValorTotal       = lote.MontoTotal,
            Estado           = "REGISTRADA",
            CreadoPor        = null, // actor: sistema (webhook Passport)
            FechaTransaccion = ahora,
        };
        _db.LedgerTransacciones.Add(ledgerTx);
        await _db.SaveChangesAsync(ct);

        var movimientos = new List<LedgerMovimiento>();
        LedgerMovimiento Mov(long idCuenta, string nat, decimal valor, string concepto, long idAnticipo, string desc) => new()
        {
            IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
            IdCuenta        = idCuenta,
            Naturaleza      = nat,
            Valor           = valor,
            Concepto        = concepto,
            ReferenciaTipo  = "libranza_anticipos",
            ReferenciaId    = idAnticipo,
            Descripcion     = desc,
            FechaMovimiento = ahora,
        };

        foreach (var (a, d) in anticipos.Zip(detalles))
        {
            movimientos.Add(Mov(ctaBanco.IdCuenta, "D", d.MontoTotal, "RECAUDO_LIBRANZA", a.IdAnticipo,
                $"DR 110103 recaudo anticipo #{a.IdAnticipo} lote #{lote.IdLote}."));
            movimientos.Add(Mov(ctaCartera.IdCuenta, "C", d.MontoCapital, "RECAUDO_LIBRANZA", a.IdAnticipo,
                $"CR 130104 cancelación capital anticipo #{a.IdAnticipo}."));
            if (d.MontoComision > 0)
                movimientos.Add(Mov(ctaComision.IdCuenta, "C", d.MontoComision, "RECAUDO_LIBRANZA_COMISION", a.IdAnticipo,
                    $"CR 310103 comisión anticipo #{a.IdAnticipo}."));
            if (d.MontoIva > 0)
                movimientos.Add(Mov(ctaIva.IdCuenta, "C", d.MontoIva, "RECAUDO_LIBRANZA_IVA", a.IdAnticipo,
                    $"CR 230103 IVA comisión anticipo #{a.IdAnticipo}."));

            a.Estado                  = "PAGADO";
            a.ReferenciaPago          = lote.ReferenciaPublica;
            a.IdTransaccionLedgerPago = ledgerTx.IdTransaccionLedger;
            a.UpdatedAt               = ahora;
            a.UpdatedByUsuario        = null;

            d.Estado          = LibranzaLoteEstados.DetalleAplicado;
            d.FechaAplicacion = ahora;
        }
        _db.LedgerMovimientos.AddRange(movimientos);

        var totalD = movimientos.Where(m => m.Naturaleza == "D").Sum(m => m.Valor);
        var totalC = movimientos.Where(m => m.Naturaleza == "C").Sum(m => m.Valor);
        if (totalD != totalC || totalD != lote.MontoTotal)
            throw new InvalidOperationException($"Ledger de recaudo desbalanceado: DR={totalD} CR={totalC} total={lote.MontoTotal}.");

        lote.Estado                        = LibranzaLoteEstados.Aplicado;
        lote.PassportPaymentId             = paymentId;
        lote.EndToEndIdentification      ??= datos.EndToEnd;
        lote.FechaPagoRecibido           ??= ahora;
        lote.FechaFondosConfirmados        = ParsearFecha(datos.SettledAt) ?? ahora;
        lote.FechaAplicacion               = ahora;
        lote.IdTransaccionLedgerAplicacion = ledgerTx.IdTransaccionLedger;
        lote.IdEventoSettled               = idEventoSettled;
        lote.FechaCierre                   = ahora;
        lote.MotivoRevision                = null;

        await _db.SaveChangesAsync(ct);
    }

    private async Task<LedgerCuenta> CuentaAsync(string codigo, CancellationToken ct) =>
        await _db.LedgerCuentas.FirstOrDefaultAsync(c =>
            c.IdUnidadNegocio == IdUnidadNegocio && c.Codigo == codigo && c.Estado == "ACTIVA", ct)
        ?? throw new InvalidOperationException($"Cuenta ledger '{codigo}' no encontrada.");

    private static DateTime? ParsearFecha(string? iso) =>
        DateTimeOffset.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var d)
            ? d.UtcDateTime
            : null;

    private static string Truncar(string s, int max) => s.Length <= max ? s : s[..max];
}
