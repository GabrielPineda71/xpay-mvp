using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public class PagoQrService
{
    private readonly XpayDbContext                          _db;
    private readonly ILogger<PagoQrService>                _logger;

    public PagoQrService(XpayDbContext db, ILogger<PagoQrService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task<VentaQr> PagarQrAsync(PagoQrRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CodigoQr))
            throw new InvalidOperationException("El código QR es requerido.");
        if (request.Valor <= 0)
            throw new InvalidOperationException("El valor del pago debe ser mayor a cero.");

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var qr = await _db.QrComercios.FirstOrDefaultAsync(q => q.CodigoQr == request.CodigoQr && q.Estado == "ACTIVO")
                ?? throw new InvalidOperationException("El QR no existe o no está activo.");

            var comercio = await _db.Comercios.FirstOrDefaultAsync(c => c.IdComercio == qr.IdComercio && c.Estado == "ACTIVO")
                ?? throw new InvalidOperationException("El comercio no existe o no está activo.");

            var tienda = await _db.ComercioTiendas.FirstOrDefaultAsync(t => t.IdTienda == qr.IdTienda && t.Estado == "ACTIVO")
                ?? throw new InvalidOperationException("La tienda no existe o no está activa.");

            var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.IdWallet == request.IdWalletUsuario && w.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("La wallet del usuario no existe o no está activa.");

            var saldo = await _db.WalletSaldos.FirstOrDefaultAsync(s => s.IdWallet == request.IdWalletUsuario)
                ?? throw new InvalidOperationException("La wallet del usuario no tiene registro de saldo.");

            if (saldo.SaldoDisponible < request.Valor)
                throw new InvalidOperationException("Saldo insuficiente para realizar el pago.");

            // DR 210101 Obligación Wallet Usuarios  — reduce la deuda hacia el usuario que paga
            // CR 210201 Ventas QR en Contingencia Comercios — registra el monto pendiente de liquidar al comercio
            var cuentaObligacion = await _db.LedgerCuentas.FirstOrDefaultAsync(c =>
                c.IdUnidadNegocio == comercio.IdUnidadNegocio && c.Codigo == "210101" && c.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("No existe la cuenta ledger 210101 (Obligación Wallet Usuarios).");

            var cuentaContingencia = await _db.LedgerCuentas.FirstOrDefaultAsync(c =>
                c.IdUnidadNegocio == comercio.IdUnidadNegocio && c.Codigo == "210201" && c.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("No existe la cuenta ledger 210201 (Ventas QR en Contingencia Comercios).");

            var saldoAntes   = saldo.SaldoDisponible;
            var saldoDespues = saldoAntes - request.Valor;
            var now          = DateTime.UtcNow;
            var descripcion  = request.Descripcion ?? "Pago QR a comercio.";

            var tx = new LedgerTransaccion
            {
                IdUnidadNegocio  = comercio.IdUnidadNegocio,
                TipoTransaccion  = "PAGO_QR",
                ReferenciaTipo   = "qr_comercios",
                ReferenciaId     = qr.IdQr,
                Descripcion      = descripcion,
                ValorTotal       = request.Valor,
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
                    IdCuenta            = cuentaObligacion.IdCuenta,
                    Naturaleza          = "D",
                    Valor               = request.Valor,
                    Concepto            = "PAGO_QR_USUARIO",
                    ReferenciaTipo      = "wallets",
                    ReferenciaId        = wallet.IdWallet,
                    Descripcion         = $"Débito obligación wallet usuario #{wallet.IdWallet} por pago QR.",
                    FechaMovimiento     = now
                },
                new LedgerMovimiento
                {
                    IdTransaccionLedger = tx.IdTransaccionLedger,
                    IdCuenta            = cuentaContingencia.IdCuenta,
                    Naturaleza          = "C",
                    Valor               = request.Valor,
                    Concepto            = "PAGO_QR_COMERCIO",
                    ReferenciaTipo      = "comercios",
                    ReferenciaId        = comercio.IdComercio,
                    Descripcion         = $"Crédito contingencia comercio #{comercio.IdComercio} por pago QR.",
                    FechaMovimiento     = now
                }
            );

            _db.WalletMovimientos.Add(new WalletMovimiento
            {
                IdWallet            = wallet.IdWallet,
                IdTransaccionLedger = tx.IdTransaccionLedger,
                TipoMovimiento      = "PAGO_QR",
                Naturaleza          = "D",
                Valor               = request.Valor,
                SaldoAntes          = saldoAntes,
                SaldoDespues        = saldoDespues,
                Descripcion         = descripcion,
                ReferenciaTipo      = "qr_comercios",
                ReferenciaId        = qr.IdQr,
                Estado              = "APLICADO",
                CreadoPor           = request.CreadoPor,
                FechaMovimiento     = now
            });

            saldo.SaldoDisponible    = saldoDespues;
            saldo.FechaActualizacion = now;

            var venta = new VentaQr
            {
                IdUnidadNegocio     = comercio.IdUnidadNegocio,
                IdComercio          = comercio.IdComercio,
                IdTienda            = tienda.IdTienda,
                IdQr                = qr.IdQr,
                IdWalletUsuario     = wallet.IdWallet,
                IdTransaccionLedger = tx.IdTransaccionLedger,
                ValorBruto          = request.Valor,
                ValorComision       = 0,
                ValorIvaComision    = 0,
                ValorNetoComercio   = request.Valor,
                Estado              = "CONTINGENCIA",
                Referencia          = request.CodigoQr,
                Descripcion         = descripcion,
                FechaVenta          = now
            };
            _db.VentasQr.Add(venta);
            await _db.SaveChangesAsync(); // persiste venta → asigna IdVentaQr

            // Registrar disponibilidad + contexto para comercios aliados (idempotente, best-effort)
            try
            {
                await TryRegistrarDisponibilidadAsync(comercio, venta, now);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Venta QR #{IdVenta}: no se pudo registrar disponibilidad de comercio aliado. El pago continúa.",
                    venta.IdVentaQr);
            }

            _db.Auditorias.Add(new Auditoria
            {
                IdUsuario     = request.CreadoPor,
                IdPersona     = wallet.IdPersona,
                Modulo        = "WALLET",
                Accion        = "PAGO_QR",
                Entidad       = "wallets",
                IdEntidad     = wallet.IdWallet.ToString(),
                ValorAnterior = saldoAntes.ToString("0.00"),
                ValorNuevo    = saldoDespues.ToString("0.00"),
                Resultado     = "EXITOSO",
                Observacion   = $"Pago QR de {request.Valor:0.00} a comercio #{comercio.IdComercio} ({request.CodigoQr}).",
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
                throw new InvalidOperationException("La transacción ledger del pago QR no está balanceada.");

            await transaction.CommitAsync();
            return venta;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    internal async Task TryRegistrarDisponibilidadAsync(Comercio comercio, VentaQr venta, DateTime now)
    {
        if (comercio.IdWalletComercio == null) return;

        var aliado = await _db.ComerciosAliados
            .FirstOrDefaultAsync(a => a.IdComercioExistente == comercio.IdComercio && a.Estado == "ACTIVO");
        if (aliado == null) return;

        // Idempotencia: no duplicar
        var existe = await _db.ComercioVentasQrDisponibilidad
            .AnyAsync(d => d.IdVentaQr == venta.IdVentaQr);
        if (existe) return;

        var condicion = await _db.ComercioCondicionesNegociacion
            .FirstOrDefaultAsync(c => c.IdComercioAliado == aliado.IdComercioAliado && c.Estado == "ACTIVO");
        if (condicion == null)
        {
            _logger.LogWarning(
                "Venta QR #{IdVenta}: comercio aliado {IdAliado} no tiene condición activa — sin disponibilidad.",
                venta.IdVentaQr, aliado.IdComercioAliado);
            return;
        }

        var descuento    = Math.Round(venta.ValorBruto * condicion.PorcentajeDescuento / 100m, 2);
        var aplIva       = condicion.AplicaIva;
        var pctIva       = aplIva ? (condicion.PorcentajeIva ?? 0m) : 0m;
        var ivaConvenio  = aplIva ? Math.Round(descuento * pctIva / 100m, 2) : 0m;
        var neto         = venta.ValorBruto - descuento - ivaConvenio;

        _db.ComercioVentasQrDisponibilidad.Add(new ComercioVentaQrDisponibilidad
        {
            IdVentaQr                 = venta.IdVentaQr,
            IdComercioAliado          = aliado.IdComercioAliado,
            IdComercioExistente       = comercio.IdComercio,
            IdWalletComercio          = comercio.IdWalletComercio.Value,
            ValorBruto                = venta.ValorBruto,
            DiasDisponibilidad        = condicion.DiasDisponibilidad,
            PorcentajeDescuento       = condicion.PorcentajeDescuento,
            ValorDescuento            = descuento,
            AplicaIvaConvenio         = aplIva,
            PorcentajeIvaConvenio     = aplIva ? pctIva : null,
            ValorIvaConvenio          = ivaConvenio,
            ValorNetoProgramado       = neto,
            FechaVenta                = now,
            FechaDisponibleProgramada = now.AddDays(condicion.DiasDisponibilidad),
            Estado                    = "NO_DISPONIBLE",
            CreatedAt                 = now,
        });

        _db.ComercioVentasQrContexto.Add(new ComercioVentaQrContexto
        {
            IdVentaQr           = venta.IdVentaQr,
            IdComercioAliado    = aliado.IdComercioAliado,
            IdComercioExistente = comercio.IdComercio,
            IdEstablecimiento   = null,
            IdCajeroUsuario     = null,
            CreatedAt           = now,
        });

        _logger.LogInformation(
            "Venta QR #{IdVenta}: disponibilidad registrada — aliado {IdAliado}, neto={Neto}, disp={Disp:d}",
            venta.IdVentaQr, aliado.IdComercioAliado, neto,
            now.AddDays(condicion.DiasDisponibilidad));
    }
}
