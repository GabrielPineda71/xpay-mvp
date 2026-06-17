using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public class RetiroComercioService
{
    private readonly XpayDbContext _db;
    public RetiroComercioService(XpayDbContext db) => _db = db;

    public async Task<RetiroComercio> SolicitarRetiroAsync(SolicitarRetiroComercioRequest request)
    {
        if (request.IdComercio <= 0)
            throw new InvalidOperationException("El identificador del comercio debe ser mayor a cero.");
        if (request.Valor <= 0)
            throw new InvalidOperationException("El valor del retiro debe ser mayor a cero.");

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var comercio = await _db.Comercios.FirstOrDefaultAsync(c => c.IdComercio == request.IdComercio && c.Estado == "ACTIVO")
                ?? throw new InvalidOperationException("El comercio no existe o no está activo.");

            if (comercio.IdWalletComercio == null)
                throw new InvalidOperationException("El comercio no tiene wallet asignada.");

            var walletComercio = await _db.Wallets.FirstOrDefaultAsync(w => w.IdWallet == comercio.IdWalletComercio.Value && w.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("La wallet del comercio no existe o no está activa.");

            var saldoComercio = await _db.WalletSaldos.FirstOrDefaultAsync(s => s.IdWallet == comercio.IdWalletComercio.Value)
                ?? throw new InvalidOperationException("La wallet del comercio no tiene registro de saldo.");

            if (saldoComercio.SaldoDisponible < request.Valor)
                throw new InvalidOperationException($"Saldo insuficiente para el retiro. Disponible: {saldoComercio.SaldoDisponible:0.00}, solicitado: {request.Valor:0.00}.");

            // DR 210202 Obligación Wallet Comercios  — cancela obligación directa
            // CR 210203 Retiros Comercios Pendientes — registra el retiro en espera de pago
            var cuentaObligacion = await _db.LedgerCuentas.FirstOrDefaultAsync(c =>
                c.IdUnidadNegocio == comercio.IdUnidadNegocio && c.Codigo == "210202" && c.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("No existe la cuenta ledger 210202 (Obligación Wallet Comercios).");

            var cuentaPendientes = await _db.LedgerCuentas.FirstOrDefaultAsync(c =>
                c.IdUnidadNegocio == comercio.IdUnidadNegocio && c.Codigo == "210203" && c.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("No existe la cuenta ledger 210203 (Retiros Comercios Pendientes de Pago).");

            var saldoAntes   = saldoComercio.SaldoDisponible;
            var saldoDespues = saldoAntes - request.Valor;
            var now          = DateTime.UtcNow;
            var descripcion  = request.Observacion ?? "Solicitud de retiro del comercio.";

            var tx = new LedgerTransaccion
            {
                IdUnidadNegocio  = comercio.IdUnidadNegocio,
                TipoTransaccion  = "RETIRO_COMERCIO_SOLICITADO",
                ReferenciaTipo   = "comercios",
                ReferenciaId     = comercio.IdComercio,
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
                    Concepto            = "RETIRO_COMERCIO_OBLIGACION",
                    ReferenciaTipo      = "comercios",
                    ReferenciaId        = comercio.IdComercio,
                    Descripcion         = $"Débito obligación wallet comercio #{comercio.IdComercio} por solicitud de retiro.",
                    FechaMovimiento     = now
                },
                new LedgerMovimiento
                {
                    IdTransaccionLedger = tx.IdTransaccionLedger,
                    IdCuenta            = cuentaPendientes.IdCuenta,
                    Naturaleza          = "C",
                    Valor               = request.Valor,
                    Concepto            = "RETIRO_COMERCIO_PENDIENTE",
                    ReferenciaTipo      = "comercios",
                    ReferenciaId        = comercio.IdComercio,
                    Descripcion         = $"Crédito retiro pendiente comercio #{comercio.IdComercio}.",
                    FechaMovimiento     = now
                }
            );

            _db.WalletMovimientos.Add(new WalletMovimiento
            {
                IdWallet            = walletComercio.IdWallet,
                IdTransaccionLedger = tx.IdTransaccionLedger,
                TipoMovimiento      = "RETIRO_COMERCIO_SOLICITADO",
                Naturaleza          = "D",
                Valor               = request.Valor,
                SaldoAntes          = saldoAntes,
                SaldoDespues        = saldoDespues,
                Descripcion         = descripcion,
                ReferenciaTipo      = "comercios",
                ReferenciaId        = comercio.IdComercio,
                Estado              = "APLICADO",
                CreadoPor           = request.CreadoPor,
                FechaMovimiento     = now
            });

            saldoComercio.SaldoDisponible    = saldoDespues;
            saldoComercio.FechaActualizacion = now;

            var retiro = new RetiroComercio
            {
                IdUnidadNegocio     = comercio.IdUnidadNegocio,
                IdComercio          = comercio.IdComercio,
                IdWalletComercio    = walletComercio.IdWallet,
                IdTransaccionLedger = tx.IdTransaccionLedger,
                Valor               = request.Valor,
                Estado              = "PENDIENTE",
                MedioRetiro         = request.MedioRetiro,
                Banco               = request.Banco,
                TipoCuenta          = request.TipoCuenta,
                NumeroCuenta        = request.NumeroCuenta,
                TitularCuenta       = request.TitularCuenta,
                DocumentoTitular    = request.DocumentoTitular,
                Observacion         = descripcion,
                CreadoPor           = request.CreadoPor,
                FechaSolicitud      = now
            };
            _db.RetirosComercio.Add(retiro);

            _db.Auditorias.Add(new Auditoria
            {
                IdUsuario     = request.CreadoPor,
                IdPersona     = null,
                Modulo        = "COMERCIO",
                Accion        = "RETIRO_SOLICITADO",
                Entidad       = "retiros_comercio",
                IdEntidad     = comercio.IdComercio.ToString(),
                ValorAnterior = saldoAntes.ToString("0.00"),
                ValorNuevo    = saldoDespues.ToString("0.00"),
                Resultado     = "EXITOSO",
                Observacion   = $"Retiro de {request.Valor:0.00} solicitado para comercio #{comercio.IdComercio}.",
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
                throw new InvalidOperationException("La transacción ledger de retiro no está balanceada.");

            await transaction.CommitAsync();
            return retiro;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<RetiroComercio> ConfirmarRetiroPagadoAsync(ConfirmarRetiroComercioRequest request)
    {
        if (request.IdRetiro <= 0)
            throw new InvalidOperationException("El identificador del retiro debe ser mayor a cero.");

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var retiro = await _db.RetirosComercio.FirstOrDefaultAsync(r => r.IdRetiro == request.IdRetiro)
                ?? throw new InvalidOperationException("El retiro no existe.");

            if (retiro.Estado != "PENDIENTE")
                throw new InvalidOperationException($"El retiro no está en estado PENDIENTE (estado actual: {retiro.Estado}).");

            if (retiro.Valor <= 0)
                throw new InvalidOperationException("El valor del retiro debe ser mayor a cero.");

            // DR 210203 Retiros Comercios Pendientes — cancela la obligación pendiente
            // CR 110101 Efectivo en Bóveda           — reduce el activo (salida de fondos)
            var cuentaPendientes = await _db.LedgerCuentas.FirstOrDefaultAsync(c =>
                c.IdUnidadNegocio == retiro.IdUnidadNegocio && c.Codigo == "210203" && c.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("No existe la cuenta ledger 210203 (Retiros Comercios Pendientes de Pago).");

            var cuentaBoveda = await _db.LedgerCuentas.FirstOrDefaultAsync(c =>
                c.IdUnidadNegocio == retiro.IdUnidadNegocio && c.Codigo == "110101" && c.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("No existe la cuenta ledger 110101 (Efectivo en Bóveda).");

            var now         = DateTime.UtcNow;
            var descripcion = request.Observacion ?? "Confirmación de pago de retiro.";

            var tx = new LedgerTransaccion
            {
                IdUnidadNegocio  = retiro.IdUnidadNegocio,
                TipoTransaccion  = "RETIRO_COMERCIO_PAGADO",
                ReferenciaTipo   = "retiros_comercio",
                ReferenciaId     = retiro.IdRetiro,
                Descripcion      = descripcion,
                ValorTotal       = retiro.Valor,
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
                    IdCuenta            = cuentaPendientes.IdCuenta,
                    Naturaleza          = "D",
                    Valor               = retiro.Valor,
                    Concepto            = "RETIRO_PAGADO_CANCELACION",
                    ReferenciaTipo      = "retiros_comercio",
                    ReferenciaId        = retiro.IdRetiro,
                    Descripcion         = $"Débito retiro pendiente #{retiro.IdRetiro} al confirmar pago.",
                    FechaMovimiento     = now
                },
                new LedgerMovimiento
                {
                    IdTransaccionLedger = tx.IdTransaccionLedger,
                    IdCuenta            = cuentaBoveda.IdCuenta,
                    Naturaleza          = "C",
                    Valor               = retiro.Valor,
                    Concepto            = "RETIRO_PAGADO_BOVEDA",
                    ReferenciaTipo      = "retiros_comercio",
                    ReferenciaId        = retiro.IdRetiro,
                    Descripcion         = $"Crédito bóveda por pago retiro #{retiro.IdRetiro} a comercio #{retiro.IdComercio}.",
                    FechaMovimiento     = now
                }
            );

            retiro.Estado                 = "PAGADO";
            retiro.FechaPago              = now;
            retiro.ReferenciaPago         = request.ReferenciaPago;
            retiro.Observacion            = descripcion;
            retiro.IdTransaccionGestion   = tx.IdTransaccionLedger;

            _db.Auditorias.Add(new Auditoria
            {
                IdUsuario     = request.CreadoPor,
                IdPersona     = null,
                Modulo        = "COMERCIO",
                Accion        = "RETIRO_CONFIRMADO_PAGADO",
                Entidad       = "retiros_comercio",
                IdEntidad     = retiro.IdRetiro.ToString(),
                ValorAnterior = "PENDIENTE",
                ValorNuevo    = "PAGADO",
                Resultado     = "EXITOSO",
                Observacion   = $"Retiro #{retiro.IdRetiro} de {retiro.Valor:0.00} confirmado como PAGADO. Ref: {request.ReferenciaPago}.",
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
                throw new InvalidOperationException("La transacción ledger de confirmación de pago no está balanceada.");

            await transaction.CommitAsync();
            return retiro;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<RetiroComercio> RechazarRetiroAsync(RechazarRetiroComercioRequest request)
    {
        if (request.IdRetiro <= 0)
            throw new InvalidOperationException("El identificador del retiro debe ser mayor a cero.");

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var retiro = await _db.RetirosComercio.FirstOrDefaultAsync(r => r.IdRetiro == request.IdRetiro)
                ?? throw new InvalidOperationException("El retiro no existe.");

            if (retiro.Estado != "PENDIENTE")
                throw new InvalidOperationException($"El retiro no está en estado PENDIENTE (estado actual: {retiro.Estado}).");

            var walletComercio = await _db.Wallets.FirstOrDefaultAsync(w => w.IdWallet == retiro.IdWalletComercio && w.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("La wallet del comercio no existe o no está activa.");

            var saldoComercio = await _db.WalletSaldos.FirstOrDefaultAsync(s => s.IdWallet == retiro.IdWalletComercio)
                ?? throw new InvalidOperationException("La wallet del comercio no tiene registro de saldo.");

            // DR 210203 Retiros Comercios Pendientes — cancela el retiro pendiente
            // CR 210202 Obligación Wallet Comercios  — restaura la obligación con el comercio
            var cuentaPendientes = await _db.LedgerCuentas.FirstOrDefaultAsync(c =>
                c.IdUnidadNegocio == retiro.IdUnidadNegocio && c.Codigo == "210203" && c.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("No existe la cuenta ledger 210203 (Retiros Comercios Pendientes de Pago).");

            var cuentaObligacion = await _db.LedgerCuentas.FirstOrDefaultAsync(c =>
                c.IdUnidadNegocio == retiro.IdUnidadNegocio && c.Codigo == "210202" && c.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("No existe la cuenta ledger 210202 (Obligación Wallet Comercios).");

            var saldoAntes   = saldoComercio.SaldoDisponible;
            var saldoDespues = saldoAntes + retiro.Valor;
            var now          = DateTime.UtcNow;
            var descripcion  = request.Observacion ?? "Rechazo de retiro del comercio.";

            var tx = new LedgerTransaccion
            {
                IdUnidadNegocio  = retiro.IdUnidadNegocio,
                TipoTransaccion  = "RETIRO_COMERCIO_RECHAZADO",
                ReferenciaTipo   = "retiros_comercio",
                ReferenciaId     = retiro.IdRetiro,
                Descripcion      = descripcion,
                ValorTotal       = retiro.Valor,
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
                    IdCuenta            = cuentaPendientes.IdCuenta,
                    Naturaleza          = "D",
                    Valor               = retiro.Valor,
                    Concepto            = "RETIRO_RECHAZADO_CANCELACION",
                    ReferenciaTipo      = "retiros_comercio",
                    ReferenciaId        = retiro.IdRetiro,
                    Descripcion         = $"Débito retiro pendiente #{retiro.IdRetiro} al rechazar.",
                    FechaMovimiento     = now
                },
                new LedgerMovimiento
                {
                    IdTransaccionLedger = tx.IdTransaccionLedger,
                    IdCuenta            = cuentaObligacion.IdCuenta,
                    Naturaleza          = "C",
                    Valor               = retiro.Valor,
                    Concepto            = "RETIRO_RECHAZADO_DEVOLUCION",
                    ReferenciaTipo      = "retiros_comercio",
                    ReferenciaId        = retiro.IdRetiro,
                    Descripcion         = $"Crédito obligación restaurada comercio #{retiro.IdComercio} por rechazo retiro #{retiro.IdRetiro}.",
                    FechaMovimiento     = now
                }
            );

            _db.WalletMovimientos.Add(new WalletMovimiento
            {
                IdWallet            = walletComercio.IdWallet,
                IdTransaccionLedger = tx.IdTransaccionLedger,
                TipoMovimiento      = "RETIRO_COMERCIO_RECHAZADO",
                Naturaleza          = "C",
                Valor               = retiro.Valor,
                SaldoAntes          = saldoAntes,
                SaldoDespues        = saldoDespues,
                Descripcion         = descripcion,
                ReferenciaTipo      = "retiros_comercio",
                ReferenciaId        = retiro.IdRetiro,
                Estado              = "APLICADO",
                CreadoPor           = request.CreadoPor,
                FechaMovimiento     = now
            });

            saldoComercio.SaldoDisponible    = saldoDespues;
            saldoComercio.FechaActualizacion = now;

            retiro.Estado               = "RECHAZADO";
            retiro.FechaRechazo         = now;
            retiro.MotivoRechazo        = request.MotivoRechazo;
            retiro.Observacion          = descripcion;
            retiro.IdTransaccionGestion = tx.IdTransaccionLedger;

            _db.Auditorias.Add(new Auditoria
            {
                IdUsuario     = request.CreadoPor,
                IdPersona     = null,
                Modulo        = "COMERCIO",
                Accion        = "RETIRO_RECHAZADO",
                Entidad       = "retiros_comercio",
                IdEntidad     = retiro.IdRetiro.ToString(),
                ValorAnterior = saldoAntes.ToString("0.00"),
                ValorNuevo    = saldoDespues.ToString("0.00"),
                Resultado     = "EXITOSO",
                Observacion   = $"Retiro #{retiro.IdRetiro} de {retiro.Valor:0.00} rechazado. Motivo: {request.MotivoRechazo}.",
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
                throw new InvalidOperationException("La transacción ledger de rechazo no está balanceada.");

            await transaction.CommitAsync();
            return retiro;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
