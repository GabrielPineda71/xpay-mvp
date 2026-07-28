using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Exceptions;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public class WalletOperacionService
{
    private readonly XpayDbContext _db;
    public WalletOperacionService(XpayDbContext db) => _db = db;

    public async Task<long> RecargarWalletManualAsync(long idWallet, RecargaWalletRequest request)
    {
        if (request.Valor <= 0) throw new InvalidOperationException("El valor de la recarga debe ser mayor a cero.");
        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.IdWallet == idWallet && w.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("La wallet no existe o no está activa.");
            if (wallet.TipoWallet != "PERSONA") throw new InvalidOperationException("La recarga manual inicial solo está permitida para wallets de persona.");

            // Fase 71.2-E-E: mismo lock pesimista aplicado en 71.2-E-D a
            // TransferirWalletAsync/PagarQrAsync — antes de esta corrección,
            // RecargarWalletManualAsync leía WalletSaldo con FirstOrDefaultAsync
            // (sin lock), sumaba en memoria y escribía en SaveChangesAsync, con el
            // mismo riesgo de actualización perdida entre dos recargas concurrentes
            // sobre la misma wallet (dos administradores recargando "al mismo
            // tiempo" perderían una de las dos sumas).
            var saldo = await _db.WalletSaldos
                .FromSqlInterpolated($"SELECT * FROM wallet_saldos WITH (UPDLOCK, ROWLOCK) WHERE id_wallet = {idWallet}")
                .FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("La wallet no tiene registro de saldo.");

            var banco = await _db.LedgerCuentas.FirstOrDefaultAsync(c => c.IdUnidadNegocio == wallet.IdUnidadNegocio && c.Codigo == "110202" && c.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("No existe la cuenta ledger Banco Liquidez Usuarios.");
            var obligacion = await _db.LedgerCuentas.FirstOrDefaultAsync(c => c.IdUnidadNegocio == wallet.IdUnidadNegocio && c.Codigo == "210101" && c.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("No existe la cuenta ledger Obligación Wallet Usuarios.");

            var saldoAntes = saldo.SaldoDisponible;
            var saldoDespues = saldoAntes + request.Valor;
            var now = DateTime.UtcNow;

            var tx = new LedgerTransaccion
            {
                IdUnidadNegocio = wallet.IdUnidadNegocio,
                TipoTransaccion = "RECARGA_WALLET",
                ReferenciaTipo = "wallets",
                ReferenciaId = wallet.IdWallet,
                Descripcion = request.Observacion ?? "Recarga manual de wallet.",
                ValorTotal = request.Valor,
                Estado = "REGISTRADA",
                CreadoPor = request.CreadoPor,
                FechaTransaccion = now
            };
            _db.LedgerTransacciones.Add(tx);
            await _db.SaveChangesAsync();

            _db.LedgerMovimientos.AddRange(
                new LedgerMovimiento { IdTransaccionLedger = tx.IdTransaccionLedger, IdCuenta = banco.IdCuenta, Naturaleza = "D", Valor = request.Valor, Concepto = "RECARGA_WALLET", ReferenciaTipo = "wallets", ReferenciaId = wallet.IdWallet, Descripcion = "Entrada de dinero al fondo de liquidez de usuarios.", FechaMovimiento = now },
                new LedgerMovimiento { IdTransaccionLedger = tx.IdTransaccionLedger, IdCuenta = obligacion.IdCuenta, Naturaleza = "C", Valor = request.Valor, Concepto = "RECARGA_WALLET", ReferenciaTipo = "wallets", ReferenciaId = wallet.IdWallet, Descripcion = "Aumento de obligación wallet usuarios.", FechaMovimiento = now }
            );

            var wm = new WalletMovimiento
            {
                IdWallet = wallet.IdWallet,
                IdTransaccionLedger = tx.IdTransaccionLedger,
                TipoMovimiento = "RECARGA",
                Naturaleza = "C",
                Valor = request.Valor,
                SaldoAntes = saldoAntes,
                SaldoDespues = saldoDespues,
                Descripcion = request.Observacion ?? "Recarga manual de wallet.",
                ReferenciaTipo = "ledger_transacciones",
                ReferenciaId = tx.IdTransaccionLedger,
                Estado = "APLICADO",
                CreadoPor = request.CreadoPor,
                FechaMovimiento = now
            };
            _db.WalletMovimientos.Add(wm);

            saldo.SaldoDisponible = saldoDespues;
            saldo.FechaActualizacion = now;

            _db.Auditorias.Add(new Auditoria
            {
                IdUsuario = request.CreadoPor,
                IdPersona = wallet.IdPersona,
                Modulo = "WALLET",
                Accion = "RECARGA_MANUAL",
                Entidad = "wallets",
                IdEntidad = wallet.IdWallet.ToString(),
                ValorAnterior = saldoAntes.ToString("0.00"),
                ValorNuevo = saldoDespues.ToString("0.00"),
                Resultado = "EXITOSO",
                Observacion = $"Recarga manual por valor {request.Valor:0.00}. Referencia: {request.ReferenciaExterna}",
                FechaEvento = now
            });

            await _db.SaveChangesAsync();
            var totalDebitos = await _db.LedgerMovimientos.Where(m => m.IdTransaccionLedger == tx.IdTransaccionLedger && m.Naturaleza == "D").SumAsync(m => m.Valor);
            var totalCreditos = await _db.LedgerMovimientos.Where(m => m.IdTransaccionLedger == tx.IdTransaccionLedger && m.Naturaleza == "C").SumAsync(m => m.Valor);
            if (totalDebitos != totalCreditos) throw new InvalidOperationException("La transacción ledger no está balanceada.");

            await transaction.CommitAsync();
            return wm.IdMovimientoWallet;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    // Fase 71.2-E-C: idWalletOrigen y creadoPor llegan como parámetros propios,
    // resueltos por el controller desde los claims del solicitante — el DTO ya
    // no los transporta, así que no hay valor del cliente que ignorar.
    // Fase 71.2-E-G: idempotencia de una sola transacción (diseño aprobado en
    // docs/security/FASE_71.2_E_B_AUTORIZACION_IDOR.md §16). idempotencyKey
    // llega ya validada como GUID desde el controller.
    public async Task<IdempotentOperationResult<TransferenciaResultadoDto>> TransferirWalletAsync(
        long idWalletOrigen, long creadoPor, Guid idempotencyKey, TransferenciaWalletRequest request)
    {
        if (request.Valor <= 0)
            throw new InvalidOperationException("El valor de la transferencia debe ser mayor a cero.");
        if (idWalletOrigen == request.IdWalletDestino)
            throw new InvalidOperationException("La wallet origen y destino no pueden ser la misma.");

        const string endpoint = IdempotencyEndpoints.WalletsTransferencia;
        var requestHash = IdempotencyHashHelper.ComputeTransferenciaHash(
            creadoPor, idWalletOrigen, request.IdWalletDestino, request.Valor, request.Descripcion);

        await using var transaction = await _db.Database.BeginTransactionAsync();
        // Fase 71.2-E-G.1: evita un doble RollbackAsync() — si ResolverReplayAsync
        // (invocado dentro del catch interno de abajo) lanza una excepción
        // (p.ej. IdempotencyConflictException por hash distinto — caso normal,
        // no raro), esa excepción es atrapada por los catch externos porque el
        // catch interno está anidado dentro del try externo. Sin esta bandera,
        // los catch externos intentarían RollbackAsync() sobre una transacción
        // que el catch interno ya revirtió, lo que lanza una excepción propia
        // de ADO.NET y enmascararía el 409/503 real con un 500 genérico.
        var rolledBack = false;

        // Fase 71.2-E-G.1: el catch de violación UNIQUE (IsUniqueViolation) debe
        // limitarse EXCLUSIVAMENTE al primer SaveChangesAsync (el INSERT de la
        // reserva EN_PROCESO) — es la única sentencia que compite por la
        // restricción UNIQUE de wallet_idempotencia. IsUniqueViolation() no
        // identifica CUÁL constraint falló (ver SqlExceptionHelper) — si el
        // catch envolviera también el ledger/movimientos/auditoría/el
        // SaveChangesAsync final, una violación UNIQUE real en cualquiera de
        // esas tablas (un problema de integridad genuino, no una colisión de
        // idempotencia) se interpretaría incorrectamente como "esta operación
        // ya se procesó" y devolvería una respuesta de reproducción (replay)
        // en vez de un error 500 — silenciando una corrupción de datos real.
        // Por eso el try/catch de la reserva está anidado y aislado del resto.
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
                _db.ChangeTracker.Clear(); // la entidad Added fallida no debe reenviarse en un SaveChangesAsync posterior
                return await IdempotencyStore.ResolverReplayAsync<TransferenciaResultadoDto>(_db, creadoPor, endpoint, idempotencyKey, requestHash);
            }

            var walletOrigen = await _db.Wallets.FirstOrDefaultAsync(w => w.IdWallet == idWalletOrigen && w.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("La wallet origen no existe o no está activa.");
            var walletDestino = await _db.Wallets.FirstOrDefaultAsync(w => w.IdWallet == request.IdWalletDestino && w.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("La wallet destino no existe o no está activa.");

            if (walletOrigen.TipoWallet != "PERSONA")
                throw new InvalidOperationException("La wallet origen debe ser de tipo PERSONA.");
            if (walletDestino.TipoWallet != "PERSONA")
                throw new InvalidOperationException("La wallet destino debe ser de tipo PERSONA.");

            // Fase 71.2-E-D: lock pesimista WITH (UPDLOCK, ROWLOCK) — mismo patrón ya
            // aprobado y usado en CarteraOrdinariaService/WalletRecargaComercioService
            // para wallet_saldos. Sin este lock, dos transferencias concurrentes desde
            // la misma wallet origen pueden leer el mismo SaldoDisponible antes de que
            // cualquiera confirme, validar ambas "saldo suficiente" contra ese valor
            // obsoleto, y al escribir producir una actualización perdida (la segunda
            // UPDATE, calculada en memoria sobre el valor obsoleto, sobreescribe el
            // resultado de la primera) — el saldo final queda inconsistente con el
            // ledger, que sí registra ambos movimientos.
            // UPDLOCK es el elemento que retiene el lock de actualización hasta el fin
            // de la transacción (commit/rollback) — es lo que realmente serializa el
            // ciclo leer-validar-escribir. ROWLOCK es solo un hint que le pide al motor
            // que intente aplicar el lock a nivel de fila en vez de escalarlo a página/
            // tabla bajo presión de memoria; no es una garantía absoluta de granularidad.
            // Se bloquean ambas filas (origen y destino) en orden ascendente de IdWallet,
            // sin importar cuál es origen y cuál destino: esto reduce el riesgo de
            // deadlock específicamente entre dos transferencias que cruzan las mismas dos
            // wallets en sentido opuesto (A→B y B→A simultáneas), pero no elimina todos
            // los deadlocks posibles del sistema — otras operaciones dentro de la misma
            // transacción (ledger, movimientos, auditoría) e índices/constraints propios
            // de SQL Server también pueden intervenir en un deadlock no relacionado con
            // este orden específico.
            var idWalletMenor = idWalletOrigen < request.IdWalletDestino ? idWalletOrigen : request.IdWalletDestino;
            var idWalletMayor = idWalletOrigen < request.IdWalletDestino ? request.IdWalletDestino : idWalletOrigen;

            var saldoMenor = await _db.WalletSaldos
                .FromSqlInterpolated($"SELECT * FROM wallet_saldos WITH (UPDLOCK, ROWLOCK) WHERE id_wallet = {idWalletMenor}")
                .FirstOrDefaultAsync();
            var saldoMayor = await _db.WalletSaldos
                .FromSqlInterpolated($"SELECT * FROM wallet_saldos WITH (UPDLOCK, ROWLOCK) WHERE id_wallet = {idWalletMayor}")
                .FirstOrDefaultAsync();

            var saldoOrigen  = idWalletOrigen == idWalletMenor ? saldoMenor : saldoMayor;
            var saldoDestino = request.IdWalletDestino == idWalletMenor ? saldoMenor : saldoMayor;

            if (saldoOrigen == null)
                throw new InvalidOperationException("La wallet origen no tiene registro de saldo.");
            if (saldoDestino == null)
                throw new InvalidOperationException("La wallet destino no tiene registro de saldo.");

            if (saldoOrigen.SaldoDisponible < request.Valor)
                throw new InvalidOperationException("Saldo insuficiente en la wallet origen.");

            // Cuenta 210101 = Obligación Wallet Usuarios (PASIVO).
            // La transferencia reasigna la obligación: se debita a origen y se acredita a destino.
            var obligacionOrigen = await _db.LedgerCuentas.FirstOrDefaultAsync(c =>
                c.IdUnidadNegocio == walletOrigen.IdUnidadNegocio && c.Codigo == "210101" && c.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("No existe la cuenta ledger 210101 para la unidad de negocio origen.");
            var obligacionDestino = await _db.LedgerCuentas.FirstOrDefaultAsync(c =>
                c.IdUnidadNegocio == walletDestino.IdUnidadNegocio && c.Codigo == "210101" && c.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("No existe la cuenta ledger 210101 para la unidad de negocio destino.");

            var saldoOrigenAntes   = saldoOrigen.SaldoDisponible;
            var saldoOrigenDespues = saldoOrigenAntes - request.Valor;
            var saldoDestinoAntes   = saldoDestino.SaldoDisponible;
            var saldoDestinoDespues = saldoDestinoAntes + request.Valor;
            var now        = DateTime.UtcNow;
            var descripcion = request.Descripcion ?? "Transferencia XPAY a XPAY.";

            var tx = new LedgerTransaccion
            {
                IdUnidadNegocio  = walletOrigen.IdUnidadNegocio,
                TipoTransaccion  = "TRANSFERENCIA_WALLET",
                ReferenciaTipo   = "wallets",
                ReferenciaId     = walletOrigen.IdWallet,
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
                    IdCuenta            = obligacionOrigen.IdCuenta,
                    Naturaleza          = "D",
                    Valor               = request.Valor,
                    Concepto            = "TRANSFERENCIA_SALIDA",
                    ReferenciaTipo      = "wallets",
                    ReferenciaId        = walletOrigen.IdWallet,
                    Descripcion         = $"Débito obligación wallet origen #{walletOrigen.IdWallet}.",
                    FechaMovimiento     = now
                },
                new LedgerMovimiento
                {
                    IdTransaccionLedger = tx.IdTransaccionLedger,
                    IdCuenta            = obligacionDestino.IdCuenta,
                    Naturaleza          = "C",
                    Valor               = request.Valor,
                    Concepto            = "TRANSFERENCIA_ENTRADA",
                    ReferenciaTipo      = "wallets",
                    ReferenciaId        = walletDestino.IdWallet,
                    Descripcion         = $"Crédito obligación wallet destino #{walletDestino.IdWallet}.",
                    FechaMovimiento     = now
                }
            );

            _db.WalletMovimientos.AddRange(
                new WalletMovimiento
                {
                    IdWallet            = walletOrigen.IdWallet,
                    IdTransaccionLedger = tx.IdTransaccionLedger,
                    TipoMovimiento      = "TRANSFERENCIA_SALIDA",
                    Naturaleza          = "D",
                    Valor               = request.Valor,
                    SaldoAntes          = saldoOrigenAntes,
                    SaldoDespues        = saldoOrigenDespues,
                    Descripcion         = descripcion,
                    ReferenciaTipo      = "wallets",
                    ReferenciaId        = walletDestino.IdWallet,
                    Estado              = "APLICADO",
                    CreadoPor           = creadoPor,
                    FechaMovimiento     = now
                },
                new WalletMovimiento
                {
                    IdWallet            = walletDestino.IdWallet,
                    IdTransaccionLedger = tx.IdTransaccionLedger,
                    TipoMovimiento      = "TRANSFERENCIA_ENTRADA",
                    Naturaleza          = "C",
                    Valor               = request.Valor,
                    SaldoAntes          = saldoDestinoAntes,
                    SaldoDespues        = saldoDestinoDespues,
                    Descripcion         = descripcion,
                    ReferenciaTipo      = "wallets",
                    ReferenciaId        = walletOrigen.IdWallet,
                    Estado              = "APLICADO",
                    CreadoPor           = creadoPor,
                    FechaMovimiento     = now
                }
            );

            saldoOrigen.SaldoDisponible  = saldoOrigenDespues;
            saldoOrigen.FechaActualizacion = now;
            saldoDestino.SaldoDisponible  = saldoDestinoDespues;
            saldoDestino.FechaActualizacion = now;

            _db.Auditorias.Add(new Auditoria
            {
                IdUsuario    = creadoPor,
                IdPersona    = walletOrigen.IdPersona,
                Modulo       = "WALLET",
                Accion       = "TRANSFERENCIA",
                Entidad      = "wallets",
                IdEntidad    = walletOrigen.IdWallet.ToString(),
                ValorAnterior = saldoOrigenAntes.ToString("0.00"),
                ValorNuevo   = saldoOrigenDespues.ToString("0.00"),
                Resultado    = "EXITOSO",
                Observacion  = $"Transferencia de {request.Valor:0.00} hacia wallet #{walletDestino.IdWallet}.",
                FechaEvento  = now
            });

            // Cierra el registro de idempotencia con el mismo resultado que se
            // devuelve al cliente — se confirma en el MISMO commit que el resto
            // de la operación (diseño de una sola transacción, §16 del
            // documento de seguridad). respuesta_data_json es exactamente el
            // objeto "data" ya público de la respuesta, nunca información
            // adicional.
            var resultado = new TransferenciaResultadoDto(tx.IdTransaccionLedger, walletOrigen.IdWallet, walletDestino.IdWallet, request.Valor);
            var respuestaDataJson = JsonSerializer.Serialize(resultado);
            if (respuestaDataJson.Length > 1000)
                throw new InvalidOperationException("La respuesta de la transferencia excede el límite de almacenamiento de idempotencia.");
            IdempotencyStore.MarcarCompletada(idem, httpStatus: 200, idRecurso: tx.IdTransaccionLedger, idTransaccionLedger: tx.IdTransaccionLedger, respuestaDataJson);

            await _db.SaveChangesAsync();

            var totalDebitos = await _db.LedgerMovimientos
                .Where(m => m.IdTransaccionLedger == tx.IdTransaccionLedger && m.Naturaleza == "D")
                .SumAsync(m => m.Valor);
            var totalCreditos = await _db.LedgerMovimientos
                .Where(m => m.IdTransaccionLedger == tx.IdTransaccionLedger && m.Naturaleza == "C")
                .SumAsync(m => m.Valor);
            if (totalDebitos != totalCreditos)
                throw new InvalidOperationException("La transacción ledger de transferencia no está balanceada.");

            await transaction.CommitAsync();
            return new IdempotentOperationResult<TransferenciaResultadoDto>(resultado, Replayed: false);
        }
        // Fase 71.2-E-G.1: estos dos catch son EXTERNOS — cubren toda la
        // operación (incluida la inserción de la reserva, por eso IsTransient
        // sigue detectando un deadlock/timeout ocurrido ahí), pero
        // deliberadamente NO incluyen IsUniqueViolation — esa clasificación
        // vive únicamente en el catch interno de arriba, acotada al primer
        // SaveChangesAsync.
        catch (Exception ex) when (SqlExceptionHelper.IsTransient(ex))
        {
            if (!rolledBack) await transaction.RollbackAsync();
            throw new TransientDatabaseException("Conflicto transitorio de base de datos al procesar la transferencia.", ex);
        }
        catch
        {
            if (!rolledBack) await transaction.RollbackAsync();
            throw;
        }
    }
}
