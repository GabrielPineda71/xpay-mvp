using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public class BrebService
{
    private readonly XpayDbContext          _db;
    private readonly ILogger<BrebService>   _logger;

    private static readonly HashSet<string> ValidKeyTypes =
        new(StringComparer.OrdinalIgnoreCase) { "ID", "PHONE", "EMAIL", "ALPHA", "BCODE" };

    private static readonly HashSet<string> ValidLlaveStates =
        new(StringComparer.OrdinalIgnoreCase) { "VALIDADA", "RECHAZADA" };

    public BrebService(XpayDbContext db, ILogger<BrebService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    // ── Passport health-config ────────────────────────────────────────────
    public PassportHealthResponse GetHealthConfig(IConfiguration config) => new()
    {
        PassportBaseUrl       = !string.IsNullOrWhiteSpace(config["PASSPORT_BASE_URL"]),
        PassportApiKey        = !string.IsNullOrWhiteSpace(config["PASSPORT_API_KEY"]),
        PassportApiSecret     = !string.IsNullOrWhiteSpace(config["PASSPORT_API_SECRET"]),
        PassportWebhookSecret = !string.IsNullOrWhiteSpace(config["PASSPORT_WEBHOOK_SECRET"]),
    };

    // ── Llave Bre-B — USUARIO ─────────────────────────────────────────────
    public async Task<MiLlaveResponse?> GetMiLlaveAsync(long idPersona)
    {
        var wallet = await _db.Wallets.FirstOrDefaultAsync(
            w => w.IdPersona == idPersona && w.TipoWallet == "PERSONA" && w.Estado == "ACTIVA");
        if (wallet is null) return null;

        var llave = await _db.PassportBrebLlaves.FirstOrDefaultAsync(
            l => l.IdWallet == wallet.IdWallet && l.EsActiva);
        return llave is null ? null : ToLlaveResponse(llave);
    }

    public async Task<MiLlaveResponse> RegistrarLlaveAsync(long idPersona, long idUsuario, RegistrarLlaveRequest req)
    {
        ValidateKeyType(req.KeyType);
        ValidateKeyValue(req.KeyType, req.KeyValue);

        var wallet = await _db.Wallets.FirstOrDefaultAsync(
            w => w.IdPersona == idPersona && w.TipoWallet == "PERSONA" && w.Estado == "ACTIVA")
            ?? throw new InvalidOperationException("No se encontró wallet activa para este usuario.");

        return await UpsertLlave(wallet.IdWallet, "USUARIO", idUsuario, null, req, idUsuario);
    }

    // ── Llave Bre-B — COMERCIO ────────────────────────────────────────────
    public async Task<MiLlaveResponse?> GetLlaveComercioAsync(long idComercio)
    {
        var comercio = await _db.Comercios.FirstOrDefaultAsync(
            c => c.IdComercio == idComercio && c.Estado == "ACTIVO")
            ?? throw new InvalidOperationException("Comercio no encontrado o inactivo.");

        if (comercio.IdWalletComercio is null)
            throw new InvalidOperationException("El comercio no tiene wallet asignada.");

        var llave = await _db.PassportBrebLlaves.FirstOrDefaultAsync(
            l => l.IdWallet == comercio.IdWalletComercio.Value && l.EsActiva);
        return llave is null ? null : ToLlaveResponse(llave);
    }

    public async Task<MiLlaveResponse> RegistrarLlaveComercioAsync(long idComercio, long idUsuario, RegistrarLlaveRequest req)
    {
        ValidateKeyType(req.KeyType);
        ValidateKeyValue(req.KeyType, req.KeyValue);

        var comercio = await _db.Comercios.FirstOrDefaultAsync(
            c => c.IdComercio == idComercio && c.Estado == "ACTIVO")
            ?? throw new InvalidOperationException("Comercio no encontrado o inactivo.");

        if (comercio.IdWalletComercio is null)
            throw new InvalidOperationException("El comercio no tiene wallet asignada.");

        return await UpsertLlave(comercio.IdWalletComercio.Value, "COMERCIO", null, idComercio, req, idUsuario);
    }

    // ── Listar todos los retiros (Admin) ─────────────────────────────────
    public async Task<List<AdminRetiroResponse>> GetAdminRetirosAsync()
    {
        var retiros = await _db.PassportBrebRetiros
            .OrderByDescending(r => r.FechaSolicitud)
            .ToListAsync();

        if (retiros.Count == 0) return [];

        var llaveIds = retiros.Select(r => r.IdBrebLlave).Distinct().ToList();
        var llaves   = await _db.PassportBrebLlaves
            .Where(l => llaveIds.Contains(l.IdBrebLlave))
            .ToDictionaryAsync(l => l.IdBrebLlave, l => l.KeyValueMasked);

        return retiros.Select(r => new AdminRetiroResponse
        {
            IdBrebRetiro        = r.IdBrebRetiro,
            TipoSujeto          = r.TipoSujeto,
            IdUsuario           = r.IdUsuario,
            IdComercio          = r.IdComercio,
            IdWallet            = r.IdWallet,
            Valor               = r.Valor,
            Moneda              = r.Moneda,
            Estado              = r.Estado,
            ReferenciaInterna   = r.ReferenciaInterna,
            KeyValueMasked      = llaves.TryGetValue(r.IdBrebLlave, out var m) ? m : "***",
            MotivoRechazo       = r.MotivoRechazo,
            IdTransaccionLedger = r.IdTransaccionLedger,
            FechaSolicitud      = r.FechaSolicitud,
            FechaConfirmacion   = r.FechaConfirmacion,
            FechaLiquidacion    = r.FechaLiquidacion,
            FechaRechazo        = r.FechaRechazo,
        }).ToList();
    }

    // ── Confirmar retiro (CREADO → CONFIRMADO) ────────────────────────────
    public async Task<string> ConfirmarRetiroAsync(long idRetiro, long adminId)
    {
        await using var dbTx = await _db.Database.BeginTransactionAsync();
        try
        {
            var retiro = await _db.PassportBrebRetiros.FindAsync(idRetiro)
                ?? throw new InvalidOperationException($"Retiro {idRetiro} no encontrado.");

            if (retiro.Estado != "CREADO")
                throw new InvalidOperationException($"Solo se puede confirmar un retiro CREADO. Estado actual: {retiro.Estado}.");

            if (retiro.IdTransaccionLedger is not null)
                throw new InvalidOperationException("Este retiro ya tiene transacción ledger (no doble contabilización).");

            var llave = await _db.PassportBrebLlaves.FindAsync(retiro.IdBrebLlave)
                ?? throw new InvalidOperationException("Llave Bre-B no encontrada.");
            if (llave.Estado != "VALIDADA")
                throw new InvalidOperationException("La llave Bre-B no está validada.");

            var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.IdWallet == retiro.IdWallet)
                ?? throw new InvalidOperationException("Wallet no encontrada.");
            var saldo = await _db.WalletSaldos.FirstOrDefaultAsync(s => s.IdWallet == retiro.IdWallet)
                ?? throw new InvalidOperationException("Saldo de wallet no encontrado.");
            if (saldo.SaldoDisponible < retiro.Valor)
                throw new InvalidOperationException($"Saldo insuficiente. Disponible: {saldo.SaldoDisponible:0.00}, requerido: {retiro.Valor:0.00}.");

            var cta210101 = await _db.LedgerCuentas.FirstOrDefaultAsync(c =>
                c.IdUnidadNegocio == wallet.IdUnidadNegocio && c.Codigo == "210101" && c.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("No existe cuenta 210101 (Obligación Wallet Usuarios).");
            var cta210204 = await _db.LedgerCuentas.FirstOrDefaultAsync(c =>
                c.IdUnidadNegocio == wallet.IdUnidadNegocio && c.Codigo == "210204" && c.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("No existe cuenta 210204 (Retiros Bre-B Pendientes).");

            var saldoAntes   = saldo.SaldoDisponible;
            var saldoDespues = saldoAntes - retiro.Valor;
            var now          = DateTime.UtcNow;

            var ledgerTx = new LedgerTransaccion
            {
                IdUnidadNegocio  = wallet.IdUnidadNegocio,
                TipoTransaccion  = "BREB_RETIRO_CONFIRMAR",
                ReferenciaTipo   = "passport_breb_retiros",
                ReferenciaId     = retiro.IdBrebRetiro,
                Descripcion      = $"Confirmación retiro Bre-B #{retiro.IdBrebRetiro}",
                ValorTotal       = retiro.Valor,
                Estado           = "REGISTRADA",
                CreadoPor        = adminId,
                FechaTransaccion = now,
            };
            _db.LedgerTransacciones.Add(ledgerTx);
            await _db.SaveChangesAsync();

            _db.LedgerMovimientos.AddRange(
                new LedgerMovimiento { IdTransaccionLedger = ledgerTx.IdTransaccionLedger, IdCuenta = cta210101.IdCuenta, Naturaleza = "D", Valor = retiro.Valor, Concepto = "BREB_RETIRO_CONFIRMAR", ReferenciaTipo = "passport_breb_retiros", ReferenciaId = retiro.IdBrebRetiro, Descripcion = $"DR 210101 — salida obligación wallet #{retiro.IdWallet}", FechaMovimiento = now },
                new LedgerMovimiento { IdTransaccionLedger = ledgerTx.IdTransaccionLedger, IdCuenta = cta210204.IdCuenta, Naturaleza = "C", Valor = retiro.Valor, Concepto = "BREB_RETIRO_CONFIRMAR", ReferenciaTipo = "passport_breb_retiros", ReferenciaId = retiro.IdBrebRetiro, Descripcion = $"CR 210204 — retiro Bre-B pendiente wallet #{retiro.IdWallet}", FechaMovimiento = now }
            );
            _db.WalletMovimientos.Add(new WalletMovimiento
            {
                IdWallet = retiro.IdWallet, IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                TipoMovimiento = "RETIRO_BREB", Naturaleza = "D", Valor = retiro.Valor,
                SaldoAntes = saldoAntes, SaldoDespues = saldoDespues,
                Descripcion = $"Retiro Bre-B #{retiro.IdBrebRetiro} confirmado.",
                ReferenciaTipo = "passport_breb_retiros", ReferenciaId = retiro.IdBrebRetiro,
                Estado = "APLICADO", CreadoPor = adminId, FechaMovimiento = now,
            });

            saldo.SaldoDisponible    = saldoDespues;
            saldo.FechaActualizacion = now;
            retiro.Estado              = "CONFIRMADO";
            retiro.FechaConfirmacion   = now;
            retiro.IdTransaccionLedger = ledgerTx.IdTransaccionLedger;
            retiro.UpdatedByUsuario    = adminId;

            await _db.SaveChangesAsync();

            var dr = await _db.LedgerMovimientos.Where(m => m.IdTransaccionLedger == ledgerTx.IdTransaccionLedger && m.Naturaleza == "D").SumAsync(m => m.Valor);
            var cr = await _db.LedgerMovimientos.Where(m => m.IdTransaccionLedger == ledgerTx.IdTransaccionLedger && m.Naturaleza == "C").SumAsync(m => m.Valor);
            if (dr != cr) throw new InvalidOperationException("Transacción ledger no balanceada.");

            await dbTx.CommitAsync();
            _logger.LogInformation("BREB_RETIRO_CONFIRMAR: id={Id} admin={Admin} ledger={Ledger} saldo_antes={Antes} saldo_despues={Despues}", idRetiro, adminId, ledgerTx.IdTransaccionLedger, saldoAntes, saldoDespues);
            return $"Retiro {idRetiro} CONFIRMADO. Ledger #{ledgerTx.IdTransaccionLedger}. Saldo wallet −{retiro.Valor:0.00} COP.";
        }
        catch { await dbTx.RollbackAsync(); throw; }
    }

    // ── Liquidar retiro (CONFIRMADO → LIQUIDADO) ──────────────────────────
    public async Task<string> LiquidarRetiroAsync(long idRetiro, long adminId)
    {
        await using var dbTx = await _db.Database.BeginTransactionAsync();
        try
        {
            var retiro = await _db.PassportBrebRetiros.FindAsync(idRetiro)
                ?? throw new InvalidOperationException($"Retiro {idRetiro} no encontrado.");
            if (retiro.Estado != "CONFIRMADO")
                throw new InvalidOperationException($"Solo se puede liquidar un retiro CONFIRMADO. Estado actual: {retiro.Estado}.");

            var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.IdWallet == retiro.IdWallet)
                ?? throw new InvalidOperationException("Wallet no encontrada.");
            var cta210204 = await _db.LedgerCuentas.FirstOrDefaultAsync(c =>
                c.IdUnidadNegocio == wallet.IdUnidadNegocio && c.Codigo == "210204" && c.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("No existe cuenta 210204 (Retiros Bre-B Pendientes).");
            var cta110102 = await _db.LedgerCuentas.FirstOrDefaultAsync(c =>
                c.IdUnidadNegocio == wallet.IdUnidadNegocio && c.Codigo == "110102" && c.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("No existe cuenta 110102 (Banco Coopcentral XPAY).");

            var now = DateTime.UtcNow;
            var ledgerTx = new LedgerTransaccion
            {
                IdUnidadNegocio  = wallet.IdUnidadNegocio,
                TipoTransaccion  = "BREB_RETIRO_LIQUIDAR",
                ReferenciaTipo   = "passport_breb_retiros",
                ReferenciaId     = retiro.IdBrebRetiro,
                Descripcion      = $"Liquidación retiro Bre-B #{retiro.IdBrebRetiro}",
                ValorTotal       = retiro.Valor,
                Estado           = "REGISTRADA",
                CreadoPor        = adminId,
                FechaTransaccion = now,
            };
            _db.LedgerTransacciones.Add(ledgerTx);
            await _db.SaveChangesAsync();

            _db.LedgerMovimientos.AddRange(
                new LedgerMovimiento { IdTransaccionLedger = ledgerTx.IdTransaccionLedger, IdCuenta = cta210204.IdCuenta, Naturaleza = "D", Valor = retiro.Valor, Concepto = "BREB_RETIRO_LIQUIDAR", ReferenciaTipo = "passport_breb_retiros", ReferenciaId = retiro.IdBrebRetiro, Descripcion = $"DR 210204 — liquidación retiro Bre-B #{retiro.IdBrebRetiro}", FechaMovimiento = now },
                new LedgerMovimiento { IdTransaccionLedger = ledgerTx.IdTransaccionLedger, IdCuenta = cta110102.IdCuenta, Naturaleza = "C", Valor = retiro.Valor, Concepto = "BREB_RETIRO_LIQUIDAR", ReferenciaTipo = "passport_breb_retiros", ReferenciaId = retiro.IdBrebRetiro, Descripcion = $"CR 110102 — pago Bre-B Banco Coopcentral XPAY", FechaMovimiento = now }
            );

            retiro.Estado           = "LIQUIDADO";
            retiro.FechaLiquidacion = now;
            retiro.UpdatedByUsuario = adminId;
            await _db.SaveChangesAsync();

            var dr = await _db.LedgerMovimientos.Where(m => m.IdTransaccionLedger == ledgerTx.IdTransaccionLedger && m.Naturaleza == "D").SumAsync(m => m.Valor);
            var cr = await _db.LedgerMovimientos.Where(m => m.IdTransaccionLedger == ledgerTx.IdTransaccionLedger && m.Naturaleza == "C").SumAsync(m => m.Valor);
            if (dr != cr) throw new InvalidOperationException("Transacción ledger de liquidación no balanceada.");

            await dbTx.CommitAsync();
            _logger.LogInformation("BREB_RETIRO_LIQUIDAR: id={Id} admin={Admin} ledger={Ledger}", idRetiro, adminId, ledgerTx.IdTransaccionLedger);
            return $"Retiro {idRetiro} LIQUIDADO. Ledger #{ledgerTx.IdTransaccionLedger}. DR 210204 / CR 110102 = {retiro.Valor:0.00} COP.";
        }
        catch { await dbTx.RollbackAsync(); throw; }
    }

    // ── Rechazar retiro (CREADO o CONFIRMADO → RECHAZADO) ─────────────────
    public async Task<string> RechazarRetiroAsync(long idRetiro, string? motivo, long adminId)
    {
        await using var dbTx = await _db.Database.BeginTransactionAsync();
        try
        {
            var retiro = await _db.PassportBrebRetiros.FindAsync(idRetiro)
                ?? throw new InvalidOperationException($"Retiro {idRetiro} no encontrado.");

            if (retiro.Estado == "LIQUIDADO")
                throw new InvalidOperationException("No se puede rechazar un retiro ya LIQUIDADO.");
            if (retiro.Estado == "RECHAZADO")
                throw new InvalidOperationException("El retiro ya está RECHAZADO.");
            if (retiro.Estado != "CREADO" && retiro.Estado != "CONFIRMADO")
                throw new InvalidOperationException($"Estado no permitido para rechazo: {retiro.Estado}.");

            var now    = DateTime.UtcNow;
            var motFin = string.IsNullOrWhiteSpace(motivo) ? "Rechazado por admin QA." : motivo.Trim();

            if (retiro.Estado == "CREADO")
            {
                retiro.Estado           = "RECHAZADO";
                retiro.FechaRechazo     = now;
                retiro.MotivoRechazo    = motFin;
                retiro.UpdatedByUsuario = adminId;
                await _db.SaveChangesAsync();
                await dbTx.CommitAsync();
                _logger.LogInformation("BREB_RETIRO_RECHAZAR: id={Id} desde CREADO admin={Admin} (sin ledger)", idRetiro, adminId);
                return $"Retiro {idRetiro} RECHAZADO (estaba CREADO). Sin movimiento de saldo ni ledger.";
            }

            // CONFIRMADO → RECHAZADO: reverso contable
            var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.IdWallet == retiro.IdWallet)
                ?? throw new InvalidOperationException("Wallet no encontrada.");
            var saldo = await _db.WalletSaldos.FirstOrDefaultAsync(s => s.IdWallet == retiro.IdWallet)
                ?? throw new InvalidOperationException("Saldo de wallet no encontrado.");
            var cta210204 = await _db.LedgerCuentas.FirstOrDefaultAsync(c =>
                c.IdUnidadNegocio == wallet.IdUnidadNegocio && c.Codigo == "210204" && c.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("No existe cuenta 210204 (Retiros Bre-B Pendientes).");
            var cta210101 = await _db.LedgerCuentas.FirstOrDefaultAsync(c =>
                c.IdUnidadNegocio == wallet.IdUnidadNegocio && c.Codigo == "210101" && c.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("No existe cuenta 210101 (Obligación Wallet Usuarios).");

            var saldoAntes   = saldo.SaldoDisponible;
            var saldoDespues = saldoAntes + retiro.Valor;

            var ledgerTx = new LedgerTransaccion
            {
                IdUnidadNegocio  = wallet.IdUnidadNegocio,
                TipoTransaccion  = "BREB_RETIRO_RECHAZAR",
                ReferenciaTipo   = "passport_breb_retiros",
                ReferenciaId     = retiro.IdBrebRetiro,
                Descripcion      = $"Rechazo/reversión retiro Bre-B #{retiro.IdBrebRetiro}",
                ValorTotal       = retiro.Valor,
                Estado           = "REGISTRADA",
                CreadoPor        = adminId,
                FechaTransaccion = now,
            };
            _db.LedgerTransacciones.Add(ledgerTx);
            await _db.SaveChangesAsync();

            _db.LedgerMovimientos.AddRange(
                new LedgerMovimiento { IdTransaccionLedger = ledgerTx.IdTransaccionLedger, IdCuenta = cta210204.IdCuenta, Naturaleza = "D", Valor = retiro.Valor, Concepto = "BREB_RETIRO_RECHAZAR", ReferenciaTipo = "passport_breb_retiros", ReferenciaId = retiro.IdBrebRetiro, Descripcion = $"DR 210204 — reverso retiro Bre-B pendiente #{retiro.IdBrebRetiro}", FechaMovimiento = now },
                new LedgerMovimiento { IdTransaccionLedger = ledgerTx.IdTransaccionLedger, IdCuenta = cta210101.IdCuenta, Naturaleza = "C", Valor = retiro.Valor, Concepto = "BREB_RETIRO_RECHAZAR", ReferenciaTipo = "passport_breb_retiros", ReferenciaId = retiro.IdBrebRetiro, Descripcion = $"CR 210101 — devolución obligación wallet #{retiro.IdWallet}", FechaMovimiento = now }
            );
            _db.WalletMovimientos.Add(new WalletMovimiento
            {
                IdWallet = retiro.IdWallet, IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                TipoMovimiento = "RETIRO_BREB_DEVOLUCION", Naturaleza = "C", Valor = retiro.Valor,
                SaldoAntes = saldoAntes, SaldoDespues = saldoDespues,
                Descripcion = $"Devolución retiro Bre-B #{retiro.IdBrebRetiro} rechazado.",
                ReferenciaTipo = "passport_breb_retiros", ReferenciaId = retiro.IdBrebRetiro,
                Estado = "APLICADO", CreadoPor = adminId, FechaMovimiento = now,
            });

            saldo.SaldoDisponible    = saldoDespues;
            saldo.FechaActualizacion = now;
            retiro.Estado           = "RECHAZADO";
            retiro.FechaRechazo     = now;
            retiro.MotivoRechazo    = motFin;
            retiro.UpdatedByUsuario = adminId;
            await _db.SaveChangesAsync();

            var dr = await _db.LedgerMovimientos.Where(m => m.IdTransaccionLedger == ledgerTx.IdTransaccionLedger && m.Naturaleza == "D").SumAsync(m => m.Valor);
            var cr = await _db.LedgerMovimientos.Where(m => m.IdTransaccionLedger == ledgerTx.IdTransaccionLedger && m.Naturaleza == "C").SumAsync(m => m.Valor);
            if (dr != cr) throw new InvalidOperationException("Transacción ledger de rechazo no balanceada.");

            await dbTx.CommitAsync();
            _logger.LogInformation("BREB_RETIRO_RECHAZAR: id={Id} desde CONFIRMADO admin={Admin} ledger={Ledger} saldo_devuelto={Val}", idRetiro, adminId, ledgerTx.IdTransaccionLedger, retiro.Valor);
            return $"Retiro {idRetiro} RECHAZADO. Saldo +{retiro.Valor:0.00} COP devuelto. Ledger #{ledgerTx.IdTransaccionLedger}.";
        }
        catch { await dbTx.RollbackAsync(); throw; }
    }

    // ── Listar todas las llaves (Admin) ──────────────────────────────────
    public async Task<List<AdminLlaveResponse>> GetAdminLlavesAsync() =>
        await _db.PassportBrebLlaves
            .OrderByDescending(l => l.FechaRegistro)
            .Select(l => new AdminLlaveResponse
            {
                IdBrebLlave     = l.IdBrebLlave,
                TipoSujeto      = l.TipoSujeto,
                IdUsuario       = l.IdUsuario,
                IdComercio      = l.IdComercio,
                IdWallet        = l.IdWallet,
                KeyType         = l.KeyType,
                KeyValueMasked  = l.KeyValueMasked,
                Estado          = l.Estado,
                FechaRegistro   = l.FechaRegistro,
                FechaValidacion = l.FechaValidacion,
                EsActiva        = l.EsActiva,
            })
            .ToListAsync();

    // ── Simular validación (Admin/QA) ─────────────────────────────────────
    public async Task<string> SimularValidacionAsync(SimularValidacionLlaveRequest req, long adminId)
    {
        if (!ValidLlaveStates.Contains(req.Estado))
            throw new InvalidOperationException($"Estado inválido: '{req.Estado}'. Permitidos: VALIDADA, RECHAZADA.");

        var llave = await _db.PassportBrebLlaves.FindAsync(req.IdBrebLlave)
            ?? throw new InvalidOperationException($"Llave {req.IdBrebLlave} no encontrada.");

        llave.Estado              = req.Estado.ToUpperInvariant();
        llave.FechaValidacion     = DateTime.UtcNow;
        llave.FechaActualizacion  = DateTime.UtcNow;
        llave.UpdatedByUsuario    = adminId;

        await _db.SaveChangesAsync();
        _logger.LogInformation("Bre-B llave {IdLlave} simulada a estado {Estado} por admin {Admin}",
            llave.IdBrebLlave, llave.Estado, adminId);
        return $"Llave {llave.IdBrebLlave} actualizada a {llave.Estado}.";
    }

    // ── Retiros — USUARIO ─────────────────────────────────────────────────
    public async Task<List<BrebRetiroResponse>> GetMisRetirosAsync(long idPersona)
    {
        var wallet = await _db.Wallets.FirstOrDefaultAsync(
            w => w.IdPersona == idPersona && w.TipoWallet == "PERSONA" && w.Estado == "ACTIVA");
        if (wallet is null) return [];

        return await BuildRetirosResponse(wallet.IdWallet);
    }

    public async Task<BrebRetiroResponse> SimularRetiroAsync(long idPersona, long idUsuario, SimularRetiroRequest req)
    {
        if (req.Valor <= 0)
            throw new InvalidOperationException("El valor del retiro debe ser mayor a cero.");

        var wallet = await _db.Wallets.FirstOrDefaultAsync(
            w => w.IdPersona == idPersona && w.TipoWallet == "PERSONA" && w.Estado == "ACTIVA")
            ?? throw new InvalidOperationException("No se encontró wallet activa para este usuario.");

        var llave = await _db.PassportBrebLlaves.FirstOrDefaultAsync(
            l => l.IdWallet == wallet.IdWallet && l.EsActiva && l.Estado == "VALIDADA")
            ?? throw new InvalidOperationException("Debes tener una llave Bre-B validada para solicitar un retiro.");

        var saldo = await _db.WalletSaldos.FirstOrDefaultAsync(s => s.IdWallet == wallet.IdWallet);
        if (saldo is null || saldo.SaldoDisponible < req.Valor)
            throw new InvalidOperationException(
                $"Saldo insuficiente. Disponible: {saldo?.SaldoDisponible ?? 0:0.00}, solicitado: {req.Valor:0.00}.");

        return await CreateRetiroSimulado(wallet.IdWallet, "USUARIO", idUsuario, null, llave, req.Valor, idUsuario);
    }

    // ── Retiros — COMERCIO ────────────────────────────────────────────────
    public async Task<List<BrebRetiroResponse>> GetRetirosComercioAsync(long idComercio)
    {
        var comercio = await _db.Comercios.FirstOrDefaultAsync(
            c => c.IdComercio == idComercio && c.Estado == "ACTIVO")
            ?? throw new InvalidOperationException("Comercio no encontrado o inactivo.");

        if (comercio.IdWalletComercio is null) return [];
        return await BuildRetirosResponse(comercio.IdWalletComercio.Value);
    }

    public async Task<BrebRetiroResponse> SimularRetiroComercioAsync(long idComercio, long idUsuario, SimularRetiroRequest req)
    {
        if (req.Valor <= 0)
            throw new InvalidOperationException("El valor del retiro debe ser mayor a cero.");

        var comercio = await _db.Comercios.FirstOrDefaultAsync(
            c => c.IdComercio == idComercio && c.Estado == "ACTIVO")
            ?? throw new InvalidOperationException("Comercio no encontrado o inactivo.");

        if (comercio.IdWalletComercio is null)
            throw new InvalidOperationException("El comercio no tiene wallet asignada.");

        var idWallet = comercio.IdWalletComercio.Value;

        var llave = await _db.PassportBrebLlaves.FirstOrDefaultAsync(
            l => l.IdWallet == idWallet && l.EsActiva && l.Estado == "VALIDADA")
            ?? throw new InvalidOperationException("El comercio debe tener una llave Bre-B validada para solicitar un retiro.");

        var saldo = await _db.WalletSaldos.FirstOrDefaultAsync(s => s.IdWallet == idWallet);
        if (saldo is null || saldo.SaldoDisponible < req.Valor)
            throw new InvalidOperationException(
                $"Saldo insuficiente. Disponible: {saldo?.SaldoDisponible ?? 0:0.00}, solicitado: {req.Valor:0.00}.");

        return await CreateRetiroSimulado(idWallet, "COMERCIO", null, idComercio, llave, req.Valor, idUsuario);
    }

    // ── Helpers privados ──────────────────────────────────────────────────

    private async Task<MiLlaveResponse> UpsertLlave(
        long idWallet, string tipoSujeto, long? idUsuario, long? idComercio,
        RegistrarLlaveRequest req, long creadoPor)
    {
        var hash = ComputeKeyHash(req.KeyValue);

        await using var tx = await _db.Database.BeginTransactionAsync();
        // Desactivar llave anterior activa si existe
        var anterior = await _db.PassportBrebLlaves.FirstOrDefaultAsync(
            l => l.IdWallet == idWallet && l.EsActiva);
        if (anterior is not null)
        {
            anterior.EsActiva           = false;
            anterior.FechaActualizacion = DateTime.UtcNow;
            anterior.UpdatedByUsuario   = creadoPor;
        }

        var llave = new PassportBrebLlave
        {
            TipoSujeto       = tipoSujeto,
            IdUsuario        = idUsuario,
            IdComercio       = idComercio,
            IdWallet         = idWallet,
            KeyType          = req.KeyType.ToUpperInvariant(),
            KeyValueMasked   = MaskKeyValue(req.KeyType, req.KeyValue),
            KeyValueHash     = hash,
            Estado           = "PENDIENTE_VALIDACION",
            FechaRegistro    = DateTime.UtcNow,
            EsActiva         = true,
            CreatedByUsuario = creadoPor,
        };

        _db.PassportBrebLlaves.Add(llave);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        _logger.LogInformation("Bre-B llave registrada: wallet={Wallet} tipoSujeto={Tipo} keyType={KT}",
            idWallet, tipoSujeto, llave.KeyType);

        return ToLlaveResponse(llave);
    }

    private async Task<BrebRetiroResponse> CreateRetiroSimulado(
        long idWallet, string tipoSujeto, long? idUsuario, long? idComercio,
        PassportBrebLlave llave, decimal valor, long creadoPor)
    {
        var referencia    = Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
        var idempotency   = $"QA-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..50];

        var retiro = new PassportBrebRetiro
        {
            TipoSujeto        = tipoSujeto,
            IdUsuario         = idUsuario,
            IdComercio        = idComercio,
            IdWallet          = idWallet,
            IdBrebLlave       = llave.IdBrebLlave,
            Valor             = valor,
            Moneda            = "COP",
            Estado            = "CREADO",
            ReferenciaInterna = referencia,
            IdempotencyKey    = idempotency,
            FechaSolicitud    = DateTime.UtcNow,
            CreatedByUsuario  = creadoPor,
        };

        _db.PassportBrebRetiros.Add(retiro);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Bre-B retiro simulado creado: id={Id} wallet={Wallet} tipoSujeto={Tipo} estado=CREADO",
            retiro.IdBrebRetiro, idWallet, tipoSujeto);

        return ToRetiroResponse(retiro, llave.KeyValueMasked);
    }

    private async Task<List<BrebRetiroResponse>> BuildRetirosResponse(long idWallet)
    {
        var retiros = await _db.PassportBrebRetiros
            .Where(r => r.IdWallet == idWallet)
            .OrderByDescending(r => r.FechaSolicitud)
            .Take(50)
            .ToListAsync();

        if (retiros.Count == 0) return [];

        var llaveIds = retiros.Select(r => r.IdBrebLlave).Distinct().ToList();
        var llaves   = await _db.PassportBrebLlaves
            .Where(l => llaveIds.Contains(l.IdBrebLlave))
            .ToDictionaryAsync(l => l.IdBrebLlave, l => l.KeyValueMasked);

        return retiros.Select(r => ToRetiroResponse(r,
            llaves.TryGetValue(r.IdBrebLlave, out var m) ? m : "***")).ToList();
    }

    private static MiLlaveResponse ToLlaveResponse(PassportBrebLlave l) => new()
    {
        IdBrebLlave    = l.IdBrebLlave,
        TipoSujeto     = l.TipoSujeto,
        KeyType        = l.KeyType,
        KeyValueMasked = l.KeyValueMasked,
        Estado         = l.Estado,
        FechaRegistro  = l.FechaRegistro,
        FechaValidacion = l.FechaValidacion,
    };

    private static BrebRetiroResponse ToRetiroResponse(PassportBrebRetiro r, string keyMasked) => new()
    {
        IdBrebRetiro      = r.IdBrebRetiro,
        TipoSujeto        = r.TipoSujeto,
        Valor             = r.Valor,
        Moneda            = r.Moneda,
        Estado            = r.Estado,
        ReferenciaInterna = r.ReferenciaInterna,
        KeyValueMasked    = keyMasked,
        FechaSolicitud    = r.FechaSolicitud,
        MotivoRechazo     = r.MotivoRechazo,
    };

    private static string ComputeKeyHash(string keyValue)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(keyValue.Trim().ToLowerInvariant()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string MaskKeyValue(string keyType, string keyValue)
    {
        var v = keyValue.Trim();
        return keyType.ToUpperInvariant() switch
        {
            "EMAIL" => MaskEmail(v),
            "PHONE" => MaskPhone(v),
            _       => MaskGeneric(v),
        };
    }

    private static string MaskEmail(string email)
    {
        var atIdx = email.IndexOf('@');
        if (atIdx <= 1) return "***@***";
        var local  = email[..atIdx];
        var domain = email[atIdx..];
        var visible = local.Length > 2 ? local[..2] : local[..1];
        return $"{visible}***{domain}";
    }

    private static string MaskPhone(string phone)
    {
        var digits = phone.Where(char.IsDigit).ToArray();
        if (digits.Length < 4) return "***";
        var last4 = new string(digits[^4..]);
        return $"***{last4}";
    }

    private static string MaskGeneric(string value)
    {
        if (value.Length <= 4) return "***";
        return $"***{value[^Math.Min(4, value.Length)..]}";
    }

    private static void ValidateKeyType(string keyType)
    {
        if (!ValidKeyTypes.Contains(keyType))
            throw new InvalidOperationException(
                $"Tipo de llave inválido: '{keyType}'. Permitidos: ID, PHONE, EMAIL, ALPHA, BCODE.");
    }

    private static void ValidateKeyValue(string keyType, string keyValue)
    {
        if (string.IsNullOrWhiteSpace(keyValue))
            throw new InvalidOperationException("El valor de la llave no puede estar vacío.");

        var v = keyValue.Trim();
        var valid = keyType.ToUpperInvariant() switch
        {
            "ID"    => v.Length >= 5 && v.Length <= 20 && v.All(char.IsDigit),
            "PHONE" => v.Length >= 7 && v.Length <= 15,
            "EMAIL" => v.Contains('@') && v.Contains('.') && v.Length <= 100,
            "ALPHA" => v.Length >= 3 && v.Length <= 30 && v.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'),
            "BCODE" => v.Length >= 8 && v.Length <= 50 && v.All(c => char.IsLetterOrDigit(c) || c == '-'),
            _       => false,
        };

        if (!valid)
            throw new InvalidOperationException(
                $"Formato de llave inválido para tipo {keyType}. Revisa la longitud y caracteres permitidos.");
    }
}
