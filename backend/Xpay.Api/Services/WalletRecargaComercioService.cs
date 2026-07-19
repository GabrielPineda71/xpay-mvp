using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public class WalletRecargaComercioService(XpayDbContext db, ComercioScopeService scope, ILogger<WalletRecargaComercioService> logger)
{
    private const string CodEfectivoRecaudar  = "130107"; // Efectivo por Recaudar en Comercios (ACTIVO, D)
    private const string CodObligacionWallet  = "210101"; // Obligación Wallet Usuarios (PASIVO, C)
    private const decimal ValorMinimo = 1_000m;
    private const decimal ValorMaximo = 2_000_000m;
    private const long IdUnidadNegocio = 1;

    // ── Búsqueda de usuario destino ──────────────────────────────────────
    public async Task<List<BuscarUsuarioWalletDto>> BuscarUsuariosAsync(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return new List<BuscarUsuarioWalletDto>();
        var q = query.Trim();

        var candidatos = await (
            from u in db.Usuarios
            join p in db.Personas on u.IdPersona equals p.IdPersona
            where u.NombreUsuario.Contains(q)
               || p.NumeroDocumento.Contains(q)
               || p.Celular.Contains(q)
               || (p.Email != null && p.Email.Contains(q))
            select new { u, p })
            .Take(10)
            .ToListAsync();

        if (candidatos.Count == 0) return new List<BuscarUsuarioWalletDto>();

        var idsPersona = candidatos.Select(c => c.p.IdPersona).ToList();
        var wallets = await db.Wallets
            .Where(w => idsPersona.Contains(w.IdPersona!.Value) && w.TipoWallet == "PERSONA" && w.Estado == "ACTIVA")
            .ToListAsync();
        var idsWallet = wallets.Select(w => w.IdWallet).ToList();
        var saldos = await db.WalletSaldos
            .Where(s => idsWallet.Contains(s.IdWallet))
            .ToDictionaryAsync(s => s.IdWallet, s => s.SaldoDisponible);

        var result = new List<BuscarUsuarioWalletDto>();
        foreach (var c in candidatos)
        {
            var wallet = wallets.FirstOrDefault(w => w.IdPersona == c.p.IdPersona);
            if (wallet is null) continue; // sin wallet PERSONA activa — no puede recibir recarga

            var nombreCompleto = string.Join(" ", new[] { c.p.PrimerNombre, c.p.SegundoNombre, c.p.PrimerApellido, c.p.SegundoApellido }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            result.Add(new BuscarUsuarioWalletDto(
                IdUsuario:     c.u.IdUsuario,
                NombreUsuario: c.u.NombreUsuario,
                NombreCompleto: nombreCompleto,
                Documento:     c.p.NumeroDocumento,
                Celular:       c.p.Celular,
                Correo:        c.p.Email,
                IdWallet:      wallet.IdWallet,
                SaldoActual:   saldos.GetValueOrDefault(wallet.IdWallet, 0m),
                EstadoWallet:  wallet.Estado));
        }
        return result;
    }

    // ── Recarga de Wallet en efectivo ────────────────────────────────────
    public async Task<RecargarWalletComercioResultDto> RecargarWalletAsync(RecargarWalletComercioRequest req, long idUsuarioCajero)
    {
        if (string.IsNullOrEmpty(req.Pin) || req.Pin.Length != 7 || !req.Pin.All(char.IsDigit))
            throw new ArgumentException("El PIN debe ser exactamente 7 dígitos numéricos");
        if (req.Valor < ValorMinimo)
            throw new ArgumentException($"El valor mínimo de recarga es {ValorMinimo:N0}");
        if (req.Valor > ValorMaximo)
            throw new ArgumentException($"El valor máximo de recarga por operación es {ValorMaximo:N0}");

        // Scope del cajero/comercio — fuera de la transacción, no depende de datos mutables.
        var cajeroScope = await scope.RequireScopeAsync(idUsuarioCajero);
        var idComercio = cajeroScope.IdComercioExistente
            ?? throw new InvalidOperationException("Tu comercio operativo no tiene un comercio existente asociado.");
        // Chequeo defensivo: CAJERO/ADMIN_SEDE_COMERCIO siempre deben tener sede asignada
        // (ya lo exige la creación del usuario operativo, pero se revalida aquí para que un
        // futuro cambio en esa validación no deje recargas sin atribución de sede en silencio).
        if ((cajeroScope.RolComercio == "CAJERO" || cajeroScope.RolComercio == "ADMIN_SEDE_COMERCIO")
            && !cajeroScope.IdEstablecimiento.HasValue)
            throw new InvalidOperationException("Tu usuario operativo no tiene una sede asignada.");

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            var usuarioDestino = await db.Usuarios
                .FirstOrDefaultAsync(u => u.IdUsuario == req.IdUsuarioWallet && u.Estado == "ACTIVO")
                ?? throw new KeyNotFoundException("El usuario destino no existe o no está activo.");

            var wallet = await db.Wallets
                .FirstOrDefaultAsync(w => w.IdPersona == usuarioDestino.IdPersona && w.TipoWallet == "PERSONA" && w.Estado == "ACTIVA")
                ?? throw new KeyNotFoundException("El usuario destino no tiene una Wallet activa.");

            // Lock pesimista sobre el saldo destino — mismo patrón que las fases de Cartera Ordinaria.
            var saldo = await db.WalletSaldos
                .FromSqlInterpolated($"SELECT * FROM wallet_saldos WITH (UPDLOCK, ROWLOCK) WHERE id_wallet = {wallet.IdWallet}")
                .FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("La wallet destino no tiene registro de saldo.");

            var now = DateTime.UtcNow;
            var saldoAntes = saldo.SaldoDisponible;

            var ledgerTx = new LedgerTransaccion
            {
                IdUnidadNegocio  = IdUnidadNegocio,
                TipoTransaccion  = "WALLET_RECARGA_EFECTIVO_COMERCIO",
                ReferenciaTipo   = "wallet_recargas_comercio",
                ReferenciaId     = null,
                Descripcion      = $"Recarga en efectivo comercio #{idComercio} a wallet #{wallet.IdWallet}",
                ValorTotal       = req.Valor,
                Estado           = "REGISTRADA",
                CreadoPor        = idUsuarioCajero,
                FechaTransaccion = now,
            };
            db.LedgerTransacciones.Add(ledgerTx);
            await db.SaveChangesAsync();

            var cuentaEfectivo    = await GetCuentaLedgerAsync(CodEfectivoRecaudar);
            var cuentaObligacion  = await GetCuentaLedgerAsync(CodObligacionWallet);

            var movimientos = new List<LedgerMovimiento>
            {
                new() {
                    IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                    IdCuenta       = cuentaEfectivo.IdCuenta,
                    Naturaleza     = "D",
                    Valor          = req.Valor,
                    Concepto       = "RECARGA_EFECTIVO_COMERCIO",
                    ReferenciaTipo = "wallet_recargas_comercio",
                    ReferenciaId   = null,
                    Descripcion    = "Efectivo por recaudar en comercio — recarga de wallet.",
                    FechaMovimiento = now,
                },
                new() {
                    IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                    IdCuenta       = cuentaObligacion.IdCuenta,
                    Naturaleza     = "C",
                    Valor          = req.Valor,
                    Concepto       = "RECARGA_EFECTIVO_COMERCIO",
                    ReferenciaTipo = "wallet_recargas_comercio",
                    ReferenciaId   = null,
                    Descripcion    = "Obligación wallet usuario por recarga en efectivo.",
                    FechaMovimiento = now,
                },
            };
            db.LedgerMovimientos.AddRange(movimientos);

            var saldoDespues = saldoAntes + req.Valor;
            saldo.SaldoDisponible    = saldoDespues;
            saldo.FechaActualizacion = now;

            db.WalletMovimientos.Add(new WalletMovimiento
            {
                IdWallet            = wallet.IdWallet,
                IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                TipoMovimiento      = "RECARGA_EFECTIVO_COMERCIO",
                Naturaleza          = "C",
                Valor               = req.Valor,
                SaldoAntes          = saldoAntes,
                SaldoDespues        = saldoDespues,
                Descripcion         = $"Recarga en efectivo — comercio #{idComercio}",
                ReferenciaTipo      = "wallet_recargas_comercio",
                ReferenciaId        = null,
                Estado              = "APLICADO",
                CreadoPor           = idUsuarioCajero,
                FechaMovimiento     = now,
            });

            var recarga = new WalletRecargaComercio
            {
                IdUnidadNegocio      = IdUnidadNegocio,
                IdComercio           = idComercio,
                IdComercioAliado     = cajeroScope.IdComercioAliado,
                IdTienda             = cajeroScope.IdEstablecimiento,
                IdUsuarioCajero      = idUsuarioCajero,
                IdUsuarioWallet      = req.IdUsuarioWallet,
                IdWallet             = wallet.IdWallet,
                IdTransaccionLedger  = ledgerTx.IdTransaccionLedger,
                Valor                = req.Valor,
                Estado               = "APLICADA",
                MetodoRecaudo        = "EFECTIVO",
                PinValidadoQa        = true,
                SaldoWalletAntes     = saldoAntes,
                SaldoWalletDespues   = saldoDespues,
                Observaciones        = req.Observaciones,
                FechaRecarga         = now,
                CreatedAt            = now,
            };
            db.WalletRecargasComercio.Add(recarga);
            await db.SaveChangesAsync();

            ledgerTx.ReferenciaId = recarga.IdRecarga;
            foreach (var m in movimientos) m.ReferenciaId = recarga.IdRecarga;
            await db.SaveChangesAsync();

            var totalD = movimientos.Where(m => m.Naturaleza == "D").Sum(m => m.Valor);
            var totalC = movimientos.Where(m => m.Naturaleza == "C").Sum(m => m.Valor);
            if (totalD != totalC)
                throw new InvalidOperationException($"Ledger desbalanceado: DR={totalD} CR={totalC}.");

            await tx.CommitAsync();

            var comprobante =
                $"Recarga de {req.Valor:N0} a {usuarioDestino.NombreUsuario} (Wallet #{wallet.IdWallet}). " +
                $"Saldo anterior: {saldoAntes:N0}. Saldo nuevo: {saldoDespues:N0}. " +
                $"Comercio #{idComercio}{(recarga.IdTienda.HasValue ? $", sede #{recarga.IdTienda}" : "")}, cajero #{idUsuarioCajero}. " +
                $"{now:yyyy-MM-dd HH:mm}. Recarga #{recarga.IdRecarga}.";

            logger.LogInformation(
                "WALLET_RECARGA_EFECTIVO_COMERCIO: idRecarga={IdRecarga} idComercio={IdComercio} idWallet={IdWallet} valor={Valor}",
                recarga.IdRecarga, idComercio, wallet.IdWallet, req.Valor);

            return new RecargarWalletComercioResultDto(
                IdRecarga:           recarga.IdRecarga,
                IdTransaccionLedger: ledgerTx.IdTransaccionLedger,
                IdWallet:            wallet.IdWallet,
                IdUsuarioWallet:     req.IdUsuarioWallet,
                Valor:               req.Valor,
                SaldoWalletAntes:    saldoAntes,
                SaldoWalletDespues:  saldoDespues,
                IdComercio:          idComercio,
                IdTienda:            recarga.IdTienda,
                IdUsuarioCajero:     idUsuarioCajero,
                Estado:              recarga.Estado,
                FechaRecarga:        recarga.FechaRecarga,
                ComprobanteTexto:    comprobante);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // ── Mis recargas ──────────────────────────────────────────────────────
    public async Task<List<RecargaComercioResumenDto>> GetMisRecargasAsync(long idUsuarioCajero, DateTime? desde, DateTime? hasta)
    {
        var s = await scope.RequireScopeAsync(idUsuarioCajero);

        var query = db.WalletRecargasComercio.AsQueryable();
        query = s.RolComercio switch
        {
            "ADMIN_COMERCIO"      => query.Where(r => r.IdComercioAliado == s.IdComercioAliado),
            "ADMIN_SEDE_COMERCIO" => query.Where(r => r.IdComercioAliado == s.IdComercioAliado && r.IdTienda == s.IdEstablecimiento),
            "CAJERO"              => query.Where(r => r.IdUsuarioCajero == idUsuarioCajero),
            _                     => query.Where(r => false),
        };

        if (desde.HasValue) query = query.Where(r => r.FechaRecarga >= desde.Value);
        if (hasta.HasValue) query = query.Where(r => r.FechaRecarga <= hasta.Value);

        var recargas = await query.OrderByDescending(r => r.FechaRecarga).ToListAsync();
        if (recargas.Count == 0) return new List<RecargaComercioResumenDto>();

        var idsUsuario = recargas.Select(r => r.IdUsuarioWallet).Distinct().ToList();
        var nombres = await db.Usuarios
            .Where(u => idsUsuario.Contains(u.IdUsuario))
            .ToDictionaryAsync(u => u.IdUsuario, u => u.NombreUsuario);

        return recargas.Select(r => new RecargaComercioResumenDto(
            IdRecarga:           r.IdRecarga,
            IdUsuarioWallet:     r.IdUsuarioWallet,
            NombreUsuarioWallet: nombres.GetValueOrDefault(r.IdUsuarioWallet, ""),
            IdWallet:            r.IdWallet,
            Valor:               r.Valor,
            Estado:              r.Estado,
            IdTienda:            r.IdTienda,
            IdUsuarioCajero:     r.IdUsuarioCajero,
            FechaRecarga:        r.FechaRecarga)).ToList();
    }

    private async Task<LedgerCuenta> GetCuentaLedgerAsync(string codigo) =>
        await db.LedgerCuentas.FirstOrDefaultAsync(c => c.IdUnidadNegocio == IdUnidadNegocio && c.Codigo == codigo && c.Estado == "ACTIVA")
        ?? throw new InvalidOperationException($"Cuenta ledger {codigo} no encontrada o inactiva");
}
