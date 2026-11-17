using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Integrations.Passport;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public sealed record ResultadoRecargaBreb(string Resultado, long? IdRecarga);

public sealed class WalletRecargaBrebService(
    XpayDbContext db, IConfiguration config, ControlOperacionesFinancieras control,
    IPassportPaymentClient passport, ILogger<WalletRecargaBrebService> logger)
{
    private const decimal MontoMaximoSql = 9999999999999999.99m;

    public async Task<ResultadoRecargaBreb?> ProcesarEventoEntranteAsync(
        PassportInboundEventKind kind, string paymentId, bool signatureValid, long idEvento,
        CancellationToken ct = default)
    {
        if (!PassportInboundEventClassifier.IsInbound(kind)) return null;
        if (!signatureValid) return new ResultadoRecargaBreb("FIRMA_NO_VALIDA_SIN_EFECTO", null);
        if (!Guid.TryParseExact(paymentId, "D", out var guid) || guid == Guid.Empty) return null;
        paymentId = guid.ToString("D");

        var aplicada = await db.WalletRecargasBreb.AsNoTracking()
            .SingleOrDefaultAsync(r => r.PassportPaymentId == paymentId, ct);
        if (aplicada is not null) return new ResultadoRecargaBreb("YA_APLICADA", aplicada.IdRecarga);

        var datos = await RecolectarAsync(paymentId, ct);
        if (datos.Referencias.Count == 1 && !EsReferenciaWallet(datos.Referencias.Single())) return null;
        if (datos.TieneLiquidacion && !datos.Conflicto && !datos.Completos)
        {
            // Se consulta fuera de la transacción SQL. Un settled puede llegar
            // antes de los eventos que contienen la referencia o el monto.
            var pago = await passport.RetrievePaymentAsync(paymentId, ct);
            if (!string.Equals(pago.Id, paymentId, StringComparison.OrdinalIgnoreCase)
                || pago.Direction != "INBOUND")
                return new ResultadoRecargaBreb("REQUIERE_REVISION", null);
            if (pago.Status is "PENDING" or "PROCESSING")
            {
                var referenciaConsultada = LibranzaRecaudoReferencia.ExtraerDePassport(pago.QrCodeReference);
                if (datos.Referencias.Count == 0 && !string.IsNullOrWhiteSpace(pago.QrCodeReference)
                    && (referenciaConsultada is null || !EsReferenciaWallet(referenciaConsultada))) return null;
                throw new InvalidOperationException("Passport aún está completando los datos de la recarga liquidada. Se reintentará la notificación.");
            }
            if (pago.Status != "SETTLED") return new ResultadoRecargaBreb("REQUIERE_REVISION", null);
            datos.Agregar(pago.QrCodeReference, pago.AccountId,
                pago.Amount?.Value?.ToString(CultureInfo.InvariantCulture), pago.Amount?.Currency);
        }

        var referencia = datos.Referencias.Count == 1 ? datos.Referencias.Single() : null;
        if (referencia is null || !EsReferenciaWallet(referencia))
            return datos.Referencias.Any(EsReferenciaWallet)
                ? new ResultadoRecargaBreb("REQUIERE_REVISION", null) : null;
        var qr = await db.WalletRecargaBrebQrs.AsNoTracking().SingleOrDefaultAsync(q => q.Referencia == referencia, ct);
        if (qr is null) return null;
        if (datos.Conflicto) return new ResultadoRecargaBreb("REQUIERE_REVISION", null);
        if (!datos.TieneLiquidacion)
            return new ResultadoRecargaBreb("PENDIENTE_DATOS", null);
        if (!datos.Completos)
            throw new InvalidOperationException("Passport aún no entregó todos los datos de la recarga liquidada. Se reintentará la notificación.");

        var accountId = datos.Cuentas.Single();
        var moneda = datos.Monedas.Single();
        var monto = datos.Montos.Single();
        if (!Guid.TryParse(config[PassportOptions.EnvOperationalAccountId], out var configurada)
            || accountId != configurada.ToString("D") || accountId != qr.PassportAccountId
            || moneda != "COP" || monto <= 0 || monto > MontoMaximoSql || decimal.Round(monto, 2) != monto)
            return new ResultadoRecargaBreb("REQUIERE_REVISION", null);

        control.ExigirInterruptor();
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (await AppLockHelper.AdquirirAsync(db, $"XPAY:WALLET_RECARGA_PAGO:{paymentId}", ct) < 0)
            throw new InvalidOperationException("Otra solicitud está aplicando esta recarga. Intenta de nuevo.");

        aplicada = await db.WalletRecargasBreb.SingleOrDefaultAsync(r => r.PassportPaymentId == paymentId, ct);
        if (aplicada is not null) return new ResultadoRecargaBreb("YA_APLICADA", aplicada.IdRecarga);
        if (await db.LibranzaLotesRecaudo.AnyAsync(l => l.PassportPaymentId == paymentId, ct))
            return new ResultadoRecargaBreb("REQUIERE_REVISION", null);

        var wallet = await db.Wallets
            .FromSqlInterpolated($"SELECT * FROM wallets WITH (UPDLOCK, ROWLOCK) WHERE id_wallet = {qr.IdWallet}")
            .SingleOrDefaultAsync(ct);
        if (wallet is null || wallet.Estado != "ACTIVA" || wallet.TipoWallet != "PERSONA")
            throw new InvalidOperationException("La Wallet no está disponible para aplicar la recarga.");
        var saldo = await db.WalletSaldos
            .FromSqlInterpolated($"SELECT * FROM wallet_saldos WITH (UPDLOCK, ROWLOCK) WHERE id_wallet = {qr.IdWallet}")
            .SingleOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("La Wallet no tiene un saldo disponible.");
        if (saldo.SaldoDisponible > MontoMaximoSql - monto)
            throw new InvalidOperationException("El saldo de la Wallet requiere revisión para aplicar la recarga.");
        var banco = await CuentaAsync(wallet.IdUnidadNegocio, "110103", ct);
        var obligacion = await CuentaAsync(wallet.IdUnidadNegocio, "210101", ct);
        var ahora = DateTime.UtcNow;
        var ledger = new LedgerTransaccion
        {
            IdUnidadNegocio = wallet.IdUnidadNegocio, TipoTransaccion = "WALLET_RECARGA_BREB",
            ReferenciaTipo = "wallet_recargas_breb", Descripcion = "Recarga de Wallet desde un banco por Bre-B.",
            ValorTotal = monto, Estado = "REGISTRADA", FechaTransaccion = ahora,
        };
        db.LedgerTransacciones.Add(ledger);
        await db.SaveChangesAsync(ct);
        aplicada = new WalletRecargaBreb
        {
            IdRecargaQr = qr.IdRecargaQr, IdWallet = qr.IdWallet, PassportPaymentId = paymentId,
            Valor = monto, Moneda = moneda, Estado = "APLICADA", IdTransaccionLedger = ledger.IdTransaccionLedger,
            IdEventoSettled = datos.IdEventoSettled!.Value, Fecha = ahora,
        };
        db.WalletRecargasBreb.Add(aplicada);
        await db.SaveChangesAsync(ct);
        ledger.ReferenciaId = aplicada.IdRecarga;
        db.LedgerMovimientos.AddRange(
            Movimiento(banco.IdCuenta, "D", ledger.IdTransaccionLedger, aplicada, ahora),
            Movimiento(obligacion.IdCuenta, "C", ledger.IdTransaccionLedger, aplicada, ahora));
        db.WalletMovimientos.Add(new WalletMovimiento
        {
            IdWallet = qr.IdWallet, IdTransaccionLedger = ledger.IdTransaccionLedger,
            TipoMovimiento = "RECARGA_BREB", Naturaleza = "C", Valor = monto,
            SaldoAntes = saldo.SaldoDisponible, SaldoDespues = saldo.SaldoDisponible + monto,
            Descripcion = "Recarga desde un banco por Bre-B.", ReferenciaTipo = "wallet_recargas_breb",
            ReferenciaId = aplicada.IdRecarga, Estado = "APLICADO", FechaMovimiento = ahora,
        });
        saldo.SaldoDisponible += monto;
        saldo.FechaActualizacion = ahora;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        logger.LogInformation("WALLET_RECARGA_BREB_APLICADA recarga={Recarga} wallet={Wallet} ledger={Ledger}",
            aplicada.IdRecarga, wallet.IdWallet, ledger.IdTransaccionLedger);
        return new ResultadoRecargaBreb("APLICADA", aplicada.IdRecarga);
    }

    private async Task<DatosPago> RecolectarAsync(string paymentId, CancellationToken ct)
    {
        var eventos = await db.PassportWebhookEvents.AsNoTracking()
            .Where(e => e.PassportPaymentId == paymentId && e.SignatureValid)
            .OrderBy(e => e.IdEvent).ToListAsync(ct);
        var datos = new DatosPago();
        foreach (var evento in eventos)
        {
            var settled = string.Equals(evento.EventType, PassportInboundEventClassifier.EventInboundSettled,
                StringComparison.OrdinalIgnoreCase);
            if (settled) datos.IdEventoSettled ??= evento.IdEvent;
            if (string.IsNullOrWhiteSpace(evento.PayloadSanitizedJson)) continue;
            try
            {
                using var doc = JsonDocument.Parse(evento.PayloadSanitizedJson);
                var o = doc.RootElement;
                datos.Agregar(Str(o, "qr_code_reference"), Str(o, "account_id"),
                    Str(o, "amount_value"), Str(o, "amount_currency"));
                if (settled && Str(o, "status") is { } status && status != "SETTLED") datos.Invalido = true;
                if (string.Equals(evento.EventType, PassportInboundEventClassifier.EventInboundRejected,
                    StringComparison.OrdinalIgnoreCase)) datos.Rechazado = true;
            }
            catch (JsonException) { datos.Invalido = true; }
        }
        return datos;
    }

    private static string? Str(JsonElement root, string key) => root.TryGetProperty(key, out var value)
        && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString() : null;

    private static bool EsReferenciaWallet(string value) => value.Length == 17 && value[0] == 'W'
        && LibranzaRecaudoReferencia.EsValida(value);

    private async Task<LedgerCuenta> CuentaAsync(long unidad, string codigo, CancellationToken ct) =>
        await db.LedgerCuentas.SingleOrDefaultAsync(c => c.IdUnidadNegocio == unidad && c.Codigo == codigo && c.Estado == "ACTIVA", ct)
        ?? throw new InvalidOperationException("No se encontró la cuenta contable de recarga.");

    private static LedgerMovimiento Movimiento(long idCuenta, string naturaleza, long idLedger,
        WalletRecargaBreb recarga, DateTime fecha) => new()
    {
        IdCuenta = idCuenta, Naturaleza = naturaleza, IdTransaccionLedger = idLedger,
        Valor = recarga.Valor, Concepto = "RECARGA_BREB", ReferenciaTipo = "wallet_recargas_breb",
        ReferenciaId = recarga.IdRecarga, Descripcion = "Recarga de Wallet por Bre-B.", FechaMovimiento = fecha,
    };

    private sealed class DatosPago
    {
        public HashSet<string> Referencias { get; } = [];
        public HashSet<string> Cuentas { get; } = [];
        public HashSet<decimal> Montos { get; } = [];
        public HashSet<string> Monedas { get; } = [];
        public long? IdEventoSettled { get; set; }
        public bool Invalido { get; set; }
        public bool Rechazado { get; set; }
        public bool TieneLiquidacion => IdEventoSettled is not null;
        public bool Conflicto => Invalido || Rechazado && TieneLiquidacion
            || Referencias.Count > 1 || Cuentas.Count > 1 || Montos.Count > 1 || Monedas.Count > 1;
        public bool Completos => Referencias.Count == 1 && Cuentas.Count == 1 && Montos.Count == 1 && Monedas.Count == 1;

        public void Agregar(string? referencia, string? cuenta, string? monto, string? moneda)
        {
            if (!string.IsNullOrWhiteSpace(referencia))
                Referencias.Add(LibranzaRecaudoReferencia.ExtraerDePassport(referencia) ?? referencia);
            if (!string.IsNullOrWhiteSpace(cuenta))
            {
                if (Guid.TryParse(cuenta, out var id) && id != Guid.Empty) Cuentas.Add(id.ToString("D"));
                else Invalido = true;
            }
            if (!string.IsNullOrWhiteSpace(monto))
            {
                if (decimal.TryParse(monto, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var valor))
                    Montos.Add(valor);
                else Invalido = true;
            }
            if (!string.IsNullOrWhiteSpace(moneda)) Monedas.Add(moneda);
        }
    }
}
