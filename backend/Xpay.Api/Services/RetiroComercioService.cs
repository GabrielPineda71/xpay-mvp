using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Exceptions;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public class RetiroComercioService
{
    private readonly XpayDbContext        _db;
    private readonly ComercioScopeService _scope;
    public RetiroComercioService(XpayDbContext db, ComercioScopeService scope) { _db = db; _scope = scope; }

    // Fase 71.2-E-B: ownership real para el caller COMERCIO — nunca se confía
    // en un idComercio recibido del cliente. esAdministrativo=true (ADMIN_XPAY/
    // SUPERUSUARIO/OPERADOR_XPAY, ya validado por [Authorize] en el
    // controller) omite la restricción; false fuerza el comercio del propio
    // scope del solicitante.
    public async Task<object> GetRetiroByIdAsync(long idRetiro, long idUsuario, bool esAdministrativo)
    {
        if (idRetiro <= 0)
            throw new InvalidOperationException("El identificador del retiro debe ser mayor a cero.");

        var retiro = await _db.RetirosComercio.FirstOrDefaultAsync(r => r.IdRetiro == idRetiro)
            ?? throw new InvalidOperationException($"No existe el retiro con id {idRetiro}.");

        if (!esAdministrativo)
        {
            var s = await _scope.RequireScopeAsync(idUsuario);
            // Mismo mensaje que "no existe" — no revela que el retiro existe
            // pero pertenece a otro comercio.
            if (s.IdComercioExistente != retiro.IdComercio)
                throw new InvalidOperationException($"No existe el retiro con id {idRetiro}.");
        }

        return new
        {
            idRetiro         = retiro.IdRetiro,
            idComercio       = retiro.IdComercio,
            idWalletComercio = retiro.IdWalletComercio,
            valor            = retiro.Valor,
            estado           = retiro.Estado,
            medioRetiro      = retiro.MedioRetiro,
            banco            = retiro.Banco,
            tipoCuenta       = retiro.TipoCuenta,
            numeroCuenta     = retiro.NumeroCuenta,
            titularCuenta    = retiro.TitularCuenta,
            documentoTitular = retiro.DocumentoTitular,
            observacion      = retiro.Observacion,
            fechaSolicitud   = retiro.FechaSolicitud,
            fechaPago        = retiro.FechaPago,
            referenciaPago   = retiro.ReferenciaPago,
            fechaRechazo     = retiro.FechaRechazo,
            motivoRechazo    = retiro.MotivoRechazo
        };
    }

    // Fase 71.2-E-B: si el solicitante no es administrativo, el idComercio
    // recibido del cliente se ignora por completo y se fuerza el de su propio
    // scope — mismo criterio ya aprobado en AbrirAsync (Fase 70.4) para
    // ADMIN_SEDE_COMERCIO/CAJERO.
    public async Task<object> ListarRetirosAsync(
        string? estado,
        long?   idComercio,
        DateTime? desde,
        DateTime? hasta,
        int page,
        int pageSize,
        long idUsuario,
        bool esAdministrativo)
    {
        if (page < 1)      page     = 1;
        if (pageSize < 1)  pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        if (!esAdministrativo)
        {
            var s = await _scope.RequireScopeAsync(idUsuario);
            idComercio = s.IdComercioExistente;
        }

        var query = _db.RetirosComercio.AsQueryable();

        if (!string.IsNullOrWhiteSpace(estado))
            query = query.Where(r => r.Estado == estado);
        if (idComercio.HasValue)
            query = query.Where(r => r.IdComercio == idComercio.Value);
        if (desde.HasValue)
            query = query.Where(r => r.FechaSolicitud >= desde.Value.Date);
        if (hasta.HasValue)
            query = query.Where(r => r.FechaSolicitud < hasta.Value.Date.AddDays(1));

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(r => r.FechaSolicitud)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new
            {
                idRetiro         = r.IdRetiro,
                idComercio       = r.IdComercio,
                idWalletComercio = r.IdWalletComercio,
                valor            = r.Valor,
                estado           = r.Estado,
                medioRetiro      = r.MedioRetiro,
                banco            = r.Banco,
                titularCuenta    = r.TitularCuenta,
                fechaSolicitud   = r.FechaSolicitud,
                fechaPago        = r.FechaPago,
                fechaRechazo     = r.FechaRechazo
            })
            .ToListAsync();

        return new { items, total, page, pageSize };
    }

    // Fase 71.2-E-B: request.IdComercio del cliente se ignora para
    // solicitantes COMERCIO (se fuerza el de su propio scope, igual que
    // ListarRetirosAsync); request.CreadoPor siempre se sobrescribe con el
    // idUsuario autenticado, nunca se confía en el valor recibido.
    public async Task<RetiroComercio> SolicitarRetiroAsync(SolicitarRetiroComercioRequest request, long idUsuario, bool esAdministrativo)
    {
        if (!esAdministrativo)
        {
            var s = await _scope.RequireScopeAsync(idUsuario);
            request.IdComercio = s.IdComercioExistente
                ?? throw new InvalidOperationException("Tu comercio operativo no tiene un comercio existente asociado.");
        }
        request.CreadoPor = idUsuario;

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

            // Fase 71.2-E-E: lock pesimista sobre wallet_saldos del comercio — sin
            // este lock, dos SolicitarRetiroAsync concurrentes (o uno concurrente
            // con un RechazarRetiroAsync de otro retiro) sobre la misma wallet de
            // comercio pueden leer el mismo SaldoDisponible y perder una
            // actualización, igual que el escenario ya corregido en
            // TransferirWalletAsync (ver docs/security/FASE_71.2_E_B_AUTORIZACION_IDOR.md
            // §14.2). Este método no bloquea ninguna fila existente de
            // retiros_comercio (el retiro se inserta como fila nueva más abajo, sin
            // contención posible), así que el único orden relevante aquí es este
            // único lock — no hay riesgo de deadlock por orden cruzado con
            // RechazarRetiroAsync, que bloquea retiros_comercio antes que
            // wallet_saldos (mismo orden relativo: nunca al revés).
            var saldoComercio = await _db.WalletSaldos
                .FromSqlInterpolated($"SELECT * FROM wallet_saldos WITH (UPDLOCK, ROWLOCK) WHERE id_wallet = {comercio.IdWalletComercio.Value}")
                .FirstOrDefaultAsync()
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

    // Fase 71.2-E-B: solo accesible a roles administrativos (validado por
    // [Authorize] en el controller) — un comercio nunca puede auto-aprobar su
    // propio retiro. CreadoPor se sobrescribe con el idUsuario autenticado.
    public async Task<RetiroComercio> ConfirmarRetiroPagadoAsync(ConfirmarRetiroComercioRequest request, long idUsuario)
    {
        request.CreadoPor = idUsuario;

        if (request.IdRetiro <= 0)
            throw new InvalidOperationException("El identificador del retiro debe ser mayor a cero.");

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            // Fase 71.2-E-D: lock pesimista WITH (UPDLOCK, ROWLOCK) — mismo patrón
            // usado para wallet_saldos. Sin este lock, dos solicitudes concurrentes
            // (doble clic en "confirmar pago", o "confirmar" y "rechazar" casi
            // simultáneos) pueden ambas leer Estado == "PENDIENTE" antes de que
            // cualquiera confirme su cambio de estado, y ambas pasar el guard de
            // abajo. El lock serializa la lectura+validación+escritura del estado.
            var retiro = await _db.RetirosComercio
                .FromSqlInterpolated($"SELECT * FROM retiros_comercio WITH (UPDLOCK, ROWLOCK) WHERE id_retiro = {request.IdRetiro}")
                .FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("El retiro no existe.");

            if (retiro.Estado != "PENDIENTE")
                throw new TransicionRetiroInvalidaException($"El retiro no está en estado PENDIENTE (estado actual: {retiro.Estado}).");

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

    // Fase 71.2-E-B: mismo criterio que ConfirmarRetiroPagadoAsync.
    public async Task<RetiroComercio> RechazarRetiroAsync(RechazarRetiroComercioRequest request, long idUsuario)
    {
        request.CreadoPor = idUsuario;

        if (request.IdRetiro <= 0)
            throw new InvalidOperationException("El identificador del retiro debe ser mayor a cero.");

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            // Fase 71.2-E-D: mismo lock pesimista que ConfirmarRetiroPagadoAsync —
            // ver comentario ahí para el escenario de carrera que evita.
            var retiro = await _db.RetirosComercio
                .FromSqlInterpolated($"SELECT * FROM retiros_comercio WITH (UPDLOCK, ROWLOCK) WHERE id_retiro = {request.IdRetiro}")
                .FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("El retiro no existe.");

            if (retiro.Estado != "PENDIENTE")
                throw new TransicionRetiroInvalidaException($"El retiro no está en estado PENDIENTE (estado actual: {retiro.Estado}).");

            var walletComercio = await _db.Wallets.FirstOrDefaultAsync(w => w.IdWallet == retiro.IdWalletComercio && w.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("La wallet del comercio no existe o no está activa.");

            // Fase 71.2-E-E: lock pesimista sobre wallet_saldos del comercio —
            // adquirido DESPUÉS del lock ya tomado arriba sobre retiros_comercio
            // (orden: retiro específico primero, wallet compartida después),
            // mismo orden relativo que SolicitarRetiroAsync respeta. Sin este lock,
            // dos rechazos de retiros distintos sobre la misma wallet de comercio
            // (o un rechazo concurrente con una nueva solicitud) podían perder una
            // actualización sobre SaldoDisponible.
            var saldoComercio = await _db.WalletSaldos
                .FromSqlInterpolated($"SELECT * FROM wallet_saldos WITH (UPDLOCK, ROWLOCK) WHERE id_wallet = {retiro.IdWalletComercio}")
                .FirstOrDefaultAsync()
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
