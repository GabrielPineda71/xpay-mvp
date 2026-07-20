using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public class WalletLiquidacionRecaudoComercioService(XpayDbContext db, ILogger<WalletLiquidacionRecaudoComercioService> logger)
{
    private const string CodEfectivoBoveda   = "110101"; // Efectivo en Bóveda (ACTIVO, D)
    private const string CodBancoCoopcentral = "110102"; // Banco Coopcentral XPAY (ACTIVO, D)
    private const string CodEfectivoRecaudar = "130107"; // Efectivo por Recaudar en Comercios (ACTIVO, D)
    private static readonly string[] MetodosValidos = { "EFECTIVO_BOVEDA", "CONSIGNACION_BANCO" };
    private const long IdUnidadNegocio = 1;

    // ── Recaudos pendientes ──────────────────────────────────────────────
    public async Task<List<RecaudoPendienteComercioDto>> ListarPendientesAsync(
        long? idComercio, long? idTienda, long? idUsuarioCajero, DateTime? desde, DateTime? hasta)
    {
        var q = db.WalletRecargasComercio
            .Where(r => r.Estado == "APLICADA" && r.IdLiquidacionRecaudo == null);

        if (idComercio.HasValue)      q = q.Where(r => r.IdComercio == idComercio.Value);
        if (idTienda.HasValue)        q = q.Where(r => r.IdTienda == idTienda.Value);
        if (idUsuarioCajero.HasValue) q = q.Where(r => r.IdUsuarioCajero == idUsuarioCajero.Value);
        if (desde.HasValue)           q = q.Where(r => r.FechaRecarga >= desde.Value);
        if (hasta.HasValue)           q = q.Where(r => r.FechaRecarga <= hasta.Value);

        var recargas = await q.OrderBy(r => r.FechaRecarga).ToListAsync();
        return await ProyectarAsync(recargas);
    }

    public async Task<List<ResumenRecaudosPendientesDto>> ResumenPendientesAsync(
        long? idComercio, long? idTienda, long? idUsuarioCajero, DateTime? desde, DateTime? hasta)
    {
        var pendientes = await ListarPendientesAsync(idComercio, idTienda, idUsuarioCajero, desde, hasta);

        return pendientes
            .GroupBy(r => new { r.IdComercio, r.IdTienda })
            .Select(g => new ResumenRecaudosPendientesDto(
                g.Key.IdComercio,
                g.First().NombreComercio,
                g.Key.IdTienda,
                g.First().NombreTienda,
                g.Count(),
                g.Sum(r => r.Valor)))
            .OrderBy(r => r.IdComercio).ThenBy(r => r.IdTienda)
            .ToList();
    }

    // ── Liquidar recaudos ─────────────────────────────────────────────────
    public async Task<LiquidarRecaudosComercioResultDto> LiquidarAsync(LiquidarRecaudosComercioRequest req, long idUsuarioAdmin)
    {
        if (req.IdsRecarga is null || req.IdsRecarga.Length == 0)
            throw new ArgumentException("Debes seleccionar al menos una recarga para liquidar.");

        var metodo = (req.MetodoLiquidacion ?? string.Empty).Trim().ToUpperInvariant();
        if (!MetodosValidos.Contains(metodo))
            throw new ArgumentException("Método de liquidación inválido. Use EFECTIVO_BOVEDA o CONSIGNACION_BANCO.");

        var idsUnicos = req.IdsRecarga.Distinct().ToArray();

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            // Lock pesimista sobre cada recarga seleccionada — mismo patrón de
            // WalletRecargaComercioService (una fila a la vez, parametrizado).
            var recargas = new List<WalletRecargaComercio>();
            foreach (var id in idsUnicos)
            {
                var r = await db.WalletRecargasComercio
                    .FromSqlInterpolated($"SELECT * FROM wallet_recargas_comercio WITH (UPDLOCK, ROWLOCK) WHERE id_recarga = {id}")
                    .FirstOrDefaultAsync();
                if (r is not null) recargas.Add(r);
            }

            if (recargas.Count != idsUnicos.Length)
            {
                var encontrados = recargas.Select(r => r.IdRecarga).ToHashSet();
                var faltantes = idsUnicos.Where(id => !encontrados.Contains(id));
                throw new KeyNotFoundException($"No existen las recargas: {string.Join(", ", faltantes)}.");
            }

            var noAplicadas = recargas.Where(r => r.Estado != "APLICADA" || r.IdLiquidacionRecaudo != null).ToList();
            if (noAplicadas.Count > 0)
                throw new InvalidOperationException(
                    $"Las siguientes recargas no están pendientes de liquidar: {string.Join(", ", noAplicadas.Select(r => r.IdRecarga))}.");

            // El comercio solo puede recaudar en EFECTIVO (Fase 70.1). El método de
            // liquidación (EFECTIVO_BOVEDA/CONSIGNACION_BANCO) describe cómo el comercio
            // entrega ese efectivo a XPAY después, no cómo el usuario le pagó al comercio —
            // así que no hay "mezcla de método de recaudo" que resolver, solo una
            // inconsistencia de datos que debe rechazar la liquidación completa.
            var metodoInconsistente = recargas.Where(r => r.MetodoRecaudo != "EFECTIVO").ToList();
            if (metodoInconsistente.Count > 0)
                throw new InvalidOperationException(
                    $"Inconsistencia de datos: las siguientes recargas no tienen metodo_recaudo=EFECTIVO: {string.Join(", ", metodoInconsistente.Select(r => r.IdRecarga))}.");

            var comerciosDistintos = recargas.Select(r => r.IdComercio).Distinct().ToList();
            if (comerciosDistintos.Count > 1)
                throw new InvalidOperationException(
                    "Las recargas seleccionadas pertenecen a más de un comercio. Liquide un comercio por operación.");

            var valorTotal = recargas.Sum(r => r.Valor);
            if (valorTotal <= 0)
                throw new InvalidOperationException("El valor total a liquidar debe ser mayor a cero.");

            var idComercio        = recargas[0].IdComercio;
            var aliadosDistintos  = recargas.Select(r => r.IdComercioAliado).Distinct().ToList();
            var idComercioAliado  = aliadosDistintos.Count == 1 ? aliadosDistintos[0] : null;
            var tiendasDistintas  = recargas.Select(r => r.IdTienda).Distinct().ToList();
            var idTienda          = tiendasDistintas.Count == 1 ? tiendasDistintas[0] : null;

            var cuentaDestino  = await GetCuentaLedgerAsync(metodo == "EFECTIVO_BOVEDA" ? CodEfectivoBoveda : CodBancoCoopcentral);
            var cuentaRecaudar = await GetCuentaLedgerAsync(CodEfectivoRecaudar);

            var now = DateTime.UtcNow;

            var ledgerTx = new LedgerTransaccion
            {
                IdUnidadNegocio  = IdUnidadNegocio,
                TipoTransaccion  = "WALLET_LIQUIDACION_RECAUDO_COMERCIO",
                ReferenciaTipo   = "wallet_liquidaciones_recaudo_comercio",
                ReferenciaId     = null,
                Descripcion      = $"Liquidación de recaudo comercio #{idComercio} — {metodo}",
                ValorTotal       = valorTotal,
                Estado           = "REGISTRADA",
                CreadoPor        = idUsuarioAdmin,
                FechaTransaccion = now,
            };
            db.LedgerTransacciones.Add(ledgerTx);
            await db.SaveChangesAsync();

            var movimientos = new List<LedgerMovimiento>
            {
                new() {
                    IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                    IdCuenta       = cuentaDestino.IdCuenta,
                    Naturaleza     = "D",
                    Valor          = valorTotal,
                    Concepto       = "LIQUIDACION_RECAUDO_COMERCIO",
                    ReferenciaTipo = "wallet_liquidaciones_recaudo_comercio",
                    ReferenciaId   = null,
                    Descripcion    = metodo == "EFECTIVO_BOVEDA"
                        ? "Efectivo recibido en bóveda por liquidación de recaudo de comercio."
                        : "Consignación bancaria recibida por liquidación de recaudo de comercio.",
                    FechaMovimiento = now,
                },
                new() {
                    IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                    IdCuenta       = cuentaRecaudar.IdCuenta,
                    Naturaleza     = "C",
                    Valor          = valorTotal,
                    Concepto       = "LIQUIDACION_RECAUDO_COMERCIO",
                    ReferenciaTipo = "wallet_liquidaciones_recaudo_comercio",
                    ReferenciaId   = null,
                    Descripcion    = "Cancelación de efectivo por recaudar en comercio.",
                    FechaMovimiento = now,
                },
            };
            db.LedgerMovimientos.AddRange(movimientos);

            var liquidacion = new WalletLiquidacionRecaudoComercio
            {
                IdUnidadNegocio     = IdUnidadNegocio,
                IdComercio          = idComercio,
                IdComercioAliado    = idComercioAliado,
                IdTienda            = idTienda,
                IdUsuarioAdmin      = idUsuarioAdmin,
                IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                MetodoLiquidacion   = metodo,
                ValorTotal          = valorTotal,
                CantidadRecargas    = recargas.Count,
                Estado              = "APLICADA",
                ReferenciaExterna   = req.ReferenciaExterna,
                Observaciones       = req.Observaciones,
                FechaLiquidacion    = now,
                CreatedAt           = now,
            };
            db.WalletLiquidacionesRecaudoComercio.Add(liquidacion);
            await db.SaveChangesAsync();

            var detalles = recargas.Select(r => new WalletLiquidacionRecaudoComercioDetalle
            {
                IdLiquidacion = liquidacion.IdLiquidacion,
                IdRecarga     = r.IdRecarga,
                Valor         = r.Valor,
                CreatedAt     = now,
            }).ToList();
            db.WalletLiquidacionesRecaudoComercioDetalle.AddRange(detalles);

            foreach (var r in recargas)
            {
                r.Estado               = "LIQUIDADA";
                r.IdLiquidacionRecaudo = liquidacion.IdLiquidacion;
                r.FechaLiquidacion     = now;
                r.LiquidadoPorUsuario  = idUsuarioAdmin;
            }

            ledgerTx.ReferenciaId = liquidacion.IdLiquidacion;
            foreach (var m in movimientos) m.ReferenciaId = liquidacion.IdLiquidacion;
            await db.SaveChangesAsync();

            var totalD = movimientos.Where(m => m.Naturaleza == "D").Sum(m => m.Valor);
            var totalC = movimientos.Where(m => m.Naturaleza == "C").Sum(m => m.Valor);
            if (totalD != totalC)
                throw new InvalidOperationException($"Ledger desbalanceado: DR={totalD} CR={totalC}.");

            await tx.CommitAsync();

            var comprobante =
                $"Liquidación de {recargas.Count} recarga(s) por {valorTotal:N0} del comercio #{idComercio} " +
                $"— método {metodo}. {now:yyyy-MM-dd HH:mm}. Liquidación #{liquidacion.IdLiquidacion}.";

            logger.LogInformation(
                "WALLET_LIQUIDACION_RECAUDO_COMERCIO: idLiquidacion={IdLiquidacion} idComercio={IdComercio} metodo={Metodo} valorTotal={ValorTotal} cantidadRecargas={Cantidad}",
                liquidacion.IdLiquidacion, idComercio, metodo, valorTotal, recargas.Count);

            return new LiquidarRecaudosComercioResultDto(
                IdLiquidacion:       liquidacion.IdLiquidacion,
                IdTransaccionLedger: ledgerTx.IdTransaccionLedger,
                IdComercio:          idComercio,
                IdTienda:            idTienda,
                MetodoLiquidacion:   metodo,
                ValorTotal:          valorTotal,
                CantidadRecargas:    recargas.Count,
                Estado:              liquidacion.Estado,
                IdUsuarioAdmin:      idUsuarioAdmin,
                FechaLiquidacion:    liquidacion.FechaLiquidacion,
                ComprobanteTexto:    comprobante);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private async Task<List<RecaudoPendienteComercioDto>> ProyectarAsync(List<WalletRecargaComercio> recargas)
    {
        if (recargas.Count == 0) return new List<RecaudoPendienteComercioDto>();

        var idsComercio = recargas.Select(r => r.IdComercio).Distinct().ToList();
        var idsTienda   = recargas.Where(r => r.IdTienda.HasValue).Select(r => r.IdTienda!.Value).Distinct().ToList();
        var idsUsuario  = recargas.Select(r => r.IdUsuarioCajero).Concat(recargas.Select(r => r.IdUsuarioWallet)).Distinct().ToList();

        var comercios = await db.Comercios
            .Where(c => idsComercio.Contains(c.IdComercio))
            .ToDictionaryAsync(c => c.IdComercio, c => c.NombreComercial);
        var tiendas = await db.ComercioEstablecimientos
            .Where(e => idsTienda.Contains(e.IdEstablecimiento))
            .ToDictionaryAsync(e => e.IdEstablecimiento, e => e.NombreEstablecimiento);
        var usuarios = await db.Usuarios
            .Where(u => idsUsuario.Contains(u.IdUsuario))
            .ToDictionaryAsync(u => u.IdUsuario, u => u.NombreUsuario);

        return recargas.Select(r => new RecaudoPendienteComercioDto(
            IdRecarga:           r.IdRecarga,
            IdComercio:          r.IdComercio,
            NombreComercio:      comercios.GetValueOrDefault(r.IdComercio),
            IdComercioAliado:    r.IdComercioAliado,
            IdTienda:            r.IdTienda,
            NombreTienda:        r.IdTienda.HasValue ? tiendas.GetValueOrDefault(r.IdTienda.Value) : null,
            IdUsuarioCajero:     r.IdUsuarioCajero,
            NombreUsuarioCajero: usuarios.GetValueOrDefault(r.IdUsuarioCajero),
            IdUsuarioWallet:     r.IdUsuarioWallet,
            NombreUsuarioWallet: usuarios.GetValueOrDefault(r.IdUsuarioWallet),
            IdWallet:            r.IdWallet,
            Valor:               r.Valor,
            Estado:              r.Estado,
            FechaRecarga:        r.FechaRecarga,
            Observaciones:       r.Observaciones
        )).ToList();
    }

    private async Task<LedgerCuenta> GetCuentaLedgerAsync(string codigo) =>
        await db.LedgerCuentas.FirstOrDefaultAsync(c => c.IdUnidadNegocio == IdUnidadNegocio && c.Codigo == codigo && c.Estado == "ACTIVA")
        ?? throw new InvalidOperationException($"Cuenta ledger {codigo} no encontrada o inactiva");
}
