using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public class LiquidacionComercioService
{
    private readonly XpayDbContext _db;
    public LiquidacionComercioService(XpayDbContext db) => _db = db;

    public async Task<LiquidacionComercio> LiquidarVentaQrAsync(LiquidarVentaQrRequest request)
    {
        if (request.IdVentaQr <= 0)
            throw new InvalidOperationException("El identificador de la venta QR debe ser mayor a cero.");

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var ventaQr = await _db.VentasQr.FirstOrDefaultAsync(v => v.IdVentaQr == request.IdVentaQr)
                ?? throw new InvalidOperationException("La venta QR no existe.");

            if (ventaQr.Estado != "CONTINGENCIA")
                throw new InvalidOperationException($"La venta QR no está en estado CONTINGENCIA (estado actual: {ventaQr.Estado}).");

            var comercio = await _db.Comercios.FirstOrDefaultAsync(c => c.IdComercio == ventaQr.IdComercio && c.Estado == "ACTIVO")
                ?? throw new InvalidOperationException("El comercio de la venta no existe o no está activo.");

            if (comercio.IdWalletComercio == null)
                throw new InvalidOperationException("El comercio no tiene wallet asignada.");

            var walletComercio = await _db.Wallets.FirstOrDefaultAsync(w => w.IdWallet == comercio.IdWalletComercio.Value && w.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("La wallet del comercio no existe o no está activa.");

            var saldoComercio = await _db.WalletSaldos.FirstOrDefaultAsync(s => s.IdWallet == comercio.IdWalletComercio.Value)
                ?? throw new InvalidOperationException("La wallet del comercio no tiene registro de saldo.");

            if (ventaQr.ValorNetoComercio <= 0)
                throw new InvalidOperationException("El valor neto de la venta QR debe ser mayor a cero.");

            // DR 210201 Ventas QR en Contingencia Comercios → el pasivo contingente se cancela
            // CR 210202 Obligación Wallet Comercios         → se crea la obligación directa con el comercio
            var cuentaContingencia = await _db.LedgerCuentas.FirstOrDefaultAsync(c =>
                c.IdUnidadNegocio == comercio.IdUnidadNegocio && c.Codigo == "210201" && c.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("No existe la cuenta ledger 210201 (Ventas QR en Contingencia Comercios).");

            var cuentaObligacionComercio = await _db.LedgerCuentas.FirstOrDefaultAsync(c =>
                c.IdUnidadNegocio == comercio.IdUnidadNegocio && c.Codigo == "210202" && c.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("No existe la cuenta ledger 210202 (Obligación Wallet Comercios).");

            var valorNeto            = ventaQr.ValorNetoComercio;
            var saldoComercioAntes   = saldoComercio.SaldoDisponible;
            var saldoComercioDespues = saldoComercioAntes + valorNeto;
            var now                  = DateTime.UtcNow;
            var descripcion          = request.Observacion ?? "Liquidación QR al comercio.";

            var tx = new LedgerTransaccion
            {
                IdUnidadNegocio  = comercio.IdUnidadNegocio,
                TipoTransaccion  = "LIQUIDACION_QR",
                ReferenciaTipo   = "ventas_qr",
                ReferenciaId     = ventaQr.IdVentaQr,
                Descripcion      = descripcion,
                ValorTotal       = valorNeto,
                Estado           = "REGISTRADA",
                CreadoPor        = request.CreadoPor,
                FechaTransaccion = now
            };
            _db.LedgerTransacciones.Add(tx);
            await _db.SaveChangesAsync();

            _db.LedgerMovimientos.AddRange(
                new LedgerMovimiento
                {
                    IdTransaccionLedger = tx.IdTransaccionLedger,
                    IdCuenta            = cuentaContingencia.IdCuenta,
                    Naturaleza          = "D",
                    Valor               = valorNeto,
                    Concepto            = "LIQUIDACION_QR_CONTINGENCIA",
                    ReferenciaTipo      = "ventas_qr",
                    ReferenciaId        = ventaQr.IdVentaQr,
                    Descripcion         = $"Débito contingencia QR venta #{ventaQr.IdVentaQr} al liquidar.",
                    FechaMovimiento     = now
                },
                new LedgerMovimiento
                {
                    IdTransaccionLedger = tx.IdTransaccionLedger,
                    IdCuenta            = cuentaObligacionComercio.IdCuenta,
                    Naturaleza          = "C",
                    Valor               = valorNeto,
                    Concepto            = "LIQUIDACION_QR_COMERCIO",
                    ReferenciaTipo      = "comercios",
                    ReferenciaId        = comercio.IdComercio,
                    Descripcion         = $"Crédito obligación wallet comercio #{comercio.IdComercio} por liquidación QR.",
                    FechaMovimiento     = now
                }
            );

            _db.WalletMovimientos.Add(new WalletMovimiento
            {
                IdWallet            = walletComercio.IdWallet,
                IdTransaccionLedger = tx.IdTransaccionLedger,
                TipoMovimiento      = "LIQUIDACION_QR_ENTRADA",
                Naturaleza          = "C",
                Valor               = valorNeto,
                SaldoAntes          = saldoComercioAntes,
                SaldoDespues        = saldoComercioDespues,
                Descripcion         = descripcion,
                ReferenciaTipo      = "ventas_qr",
                ReferenciaId        = ventaQr.IdVentaQr,
                Estado              = "APLICADO",
                CreadoPor           = request.CreadoPor,
                FechaMovimiento     = now
            });

            saldoComercio.SaldoDisponible    = saldoComercioDespues;
            saldoComercio.FechaActualizacion = now;

            var liquidacion = new LiquidacionComercio
            {
                IdUnidadNegocio     = comercio.IdUnidadNegocio,
                IdComercio          = comercio.IdComercio,
                IdWalletComercio    = walletComercio.IdWallet,
                IdTransaccionLedger = tx.IdTransaccionLedger,
                ValorBruto          = ventaQr.ValorBruto,
                ValorComision       = ventaQr.ValorComision,
                ValorIvaComision    = ventaQr.ValorIvaComision,
                ValorNeto           = valorNeto,
                Estado              = "APLICADA",
                FechaLiquidacion    = now,
                CreadoPor           = request.CreadoPor
            };
            _db.LiquidacionesComercios.Add(liquidacion);
            await _db.SaveChangesAsync();

            _db.LiquidacionComercioDetalles.Add(new LiquidacionComercioDetalle
            {
                IdLiquidacion    = liquidacion.IdLiquidacion,
                IdVentaQr        = ventaQr.IdVentaQr,
                ValorBruto       = ventaQr.ValorBruto,
                ValorComision    = ventaQr.ValorComision,
                ValorIvaComision = ventaQr.ValorIvaComision,
                ValorNeto        = valorNeto
            });

            ventaQr.Estado                   = "LIQUIDADA";
            ventaQr.IdTransaccionLiquidacion = tx.IdTransaccionLedger;

            _db.Auditorias.Add(new Auditoria
            {
                IdUsuario     = request.CreadoPor,
                IdPersona     = null,
                Modulo        = "COMERCIO",
                Accion        = "LIQUIDACION_QR",
                Entidad       = "ventas_qr",
                IdEntidad     = ventaQr.IdVentaQr.ToString(),
                ValorAnterior = saldoComercioAntes.ToString("0.00"),
                ValorNuevo    = saldoComercioDespues.ToString("0.00"),
                Resultado     = "EXITOSO",
                Observacion   = $"Liquidación QR venta #{ventaQr.IdVentaQr} por {valorNeto:0.00} a comercio #{comercio.IdComercio}.",
                FechaEvento   = now
            });

            await _db.SaveChangesAsync();

            var totalDebitos = await _db.LedgerMovimientos
                .Where(m => m.IdTransaccionLedger == tx.IdTransaccionLedger && m.Naturaleza == "D")
                .SumAsync(m => m.Valor);
            var totalCreditos = await _db.LedgerMovimientos
                .Where(m => m.IdTransaccionLedger == tx.IdTransaccionLedger && m.Naturaleza == "C")
                .SumAsync(m => m.Valor);
            if (totalDebitos != totalCreditos)
                throw new InvalidOperationException("La transacción ledger de liquidación QR no está balanceada.");

            await transaction.CommitAsync();
            return liquidacion;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
