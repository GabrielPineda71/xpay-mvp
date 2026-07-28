using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Exceptions;
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

    // Fase 71.2-E-D: idWalletUsuario y creadoPor llegan como parámetros propios,
    // resueltos por el controller desde los claims del solicitante — el DTO ya
    // no los transporta, mismo principio aplicado a TransferirWalletAsync en
    // 71.2-E-C. Nunca se confía en un idWalletUsuario recibido del cliente.
    // Fase 71.2-E-G: idempotencia de una sola transacción, mismo diseño que
    // WalletOperacionService.TransferirWalletAsync (ver
    // docs/security/FASE_71.2_E_B_AUTORIZACION_IDOR.md §16).
    public async Task<IdempotentOperationResult<PagoQrResultadoDto>> PagarQrAsync(
        long idWalletUsuario, long creadoPor, Guid idempotencyKey, PagoQrRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CodigoQr))
            throw new InvalidOperationException("El código QR es requerido.");
        if (request.Valor <= 0)
            throw new InvalidOperationException("El valor del pago debe ser mayor a cero.");

        const string endpoint = IdempotencyEndpoints.QrPagar;
        var requestHash = IdempotencyHashHelper.ComputePagoQrHash(
            creadoPor, idWalletUsuario, request.CodigoQr, request.Valor, request.Descripcion);

        await using var transaction = await _db.Database.BeginTransactionAsync();
        // Fase 71.2-E-G.1: ver comentario equivalente en
        // WalletOperacionService.TransferirWalletAsync — evita un doble
        // RollbackAsync() cuando ResolverReplayAsync lanza dentro del catch
        // interno (p.ej. IdempotencyConflictException, caso normal).
        var rolledBack = false;

        // Fase 71.2-E-G.1: el catch de violación UNIQUE se limita
        // EXCLUSIVAMENTE al primer SaveChangesAsync (INSERT de la reserva) —
        // nunca a la creación de VentaQr, ledger, movimientos, auditoría ni al
        // SaveChangesAsync final. Ver justificación completa en
        // WalletOperacionService.TransferirWalletAsync.
        try
        {
            var idem = IdempotencyStore.NuevaReserva(creadoPor, endpoint, idempotencyKey, requestHash);
            _db.WalletIdempotencias.Add(idem);

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (Exception ex) when (SqlExceptionHelper.IsUniqueViolation(ex))
            {
                await transaction.RollbackAsync();
                rolledBack = true;
                _db.ChangeTracker.Clear();
                return await IdempotencyStore.ResolverReplayAsync<PagoQrResultadoDto>(_db, creadoPor, endpoint, idempotencyKey, requestHash);
            }

            var qr = await _db.QrComercios.FirstOrDefaultAsync(q => q.CodigoQr == request.CodigoQr && q.Estado == "ACTIVO")
                ?? throw new InvalidOperationException("El QR no existe o no está activo.");

            var comercio = await _db.Comercios.FirstOrDefaultAsync(c => c.IdComercio == qr.IdComercio && c.Estado == "ACTIVO")
                ?? throw new InvalidOperationException("El comercio no existe o no está activo.");

            var tienda = await _db.ComercioTiendas.FirstOrDefaultAsync(t => t.IdTienda == qr.IdTienda && t.Estado == "ACTIVO")
                ?? throw new InvalidOperationException("La tienda no existe o no está activa.");

            var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.IdWallet == idWalletUsuario && w.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("La wallet del usuario no existe o no está activa.");

            // Fase 71.2-E-D: lock pesimista WITH (UPDLOCK, ROWLOCK) — mismo patrón
            // aplicado en TransferirWalletAsync y ya usado en el resto del proyecto
            // para wallet_saldos (evita la misma clase de actualización perdida
            // entre dos pagos QR concurrentes desde la misma wallet).
            var saldo = await _db.WalletSaldos
                .FromSqlInterpolated($"SELECT * FROM wallet_saldos WITH (UPDLOCK, ROWLOCK) WHERE id_wallet = {idWalletUsuario}")
                .FirstOrDefaultAsync()
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
                CreadoPor        = creadoPor,
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
                CreadoPor           = creadoPor,
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
                IdUsuario     = creadoPor,
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

            var resultado = new PagoQrResultadoDto(venta.IdVentaQr, tx.IdTransaccionLedger, comercio.IdComercio, tienda.IdTienda, wallet.IdWallet, venta.ValorBruto, venta.Estado);
            var respuestaDataJson = JsonSerializer.Serialize(resultado);
            if (respuestaDataJson.Length > 1000)
                throw new InvalidOperationException("La respuesta del pago QR excede el límite de almacenamiento de idempotencia.");
            IdempotencyStore.MarcarCompletada(idem, httpStatus: 200, idRecurso: venta.IdVentaQr, idTransaccionLedger: tx.IdTransaccionLedger, respuestaDataJson);

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
            return new IdempotentOperationResult<PagoQrResultadoDto>(resultado, Replayed: false);
        }
        // Fase 71.2-E-G.1: catch externos — cubren toda la operación (incluida
        // la inserción de la reserva, por eso IsTransient sigue detectando un
        // deadlock/timeout ocurrido ahí), pero deliberadamente sin
        // IsUniqueViolation, que vive únicamente en el catch interno de arriba.
        catch (Exception ex) when (SqlExceptionHelper.IsTransient(ex))
        {
            if (!rolledBack) await transaction.RollbackAsync();
            throw new TransientDatabaseException("Conflicto transitorio de base de datos al procesar el pago QR.", ex);
        }
        catch
        {
            if (!rolledBack) await transaction.RollbackAsync();
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
