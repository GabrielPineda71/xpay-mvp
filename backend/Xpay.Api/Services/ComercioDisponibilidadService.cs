using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public class ComercioDisponibilidadService
{
    private readonly XpayDbContext _db;
    private readonly ILogger<ComercioDisponibilidadService> _logger;

    public ComercioDisponibilidadService(XpayDbContext db, ILogger<ComercioDisponibilidadService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── Condiciones de negociación ────────────────────────────────────────────

    public async Task<List<CondicionNegociacionResponse>> ListarCondicionesAsync(long idComercioAliado)
    {
        var items = await _db.ComercioCondicionesNegociacion
            .Where(c => c.IdComercioAliado == idComercioAliado)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        return items.Select(ToCondicionResponse).ToList();
    }

    public async Task<CondicionNegociacionResponse> CrearCondicionAsync(
        long idComercioAliado, CrearCondicionRequest req, long adminId)
    {
        _ = await _db.ComerciosAliados.FindAsync(idComercioAliado)
            ?? throw new KeyNotFoundException($"Comercio aliado {idComercioAliado} no encontrado.");

        ValidarCondicion(req.DiasDisponibilidad, req.PorcentajeDescuento, req.AplicaIva, req.PorcentajeIva);

        var activa = await _db.ComercioCondicionesNegociacion
            .FirstOrDefaultAsync(c => c.IdComercioAliado == idComercioAliado && c.Estado == "ACTIVO");
        if (activa != null)
        {
            activa.Estado    = "INACTIVO";
            activa.UpdatedAt = DateTime.UtcNow;
            activa.UpdatedByUsuario = adminId;
        }

        var condicion = new ComercioCondicionNegociacion
        {
            IdComercioAliado    = idComercioAliado,
            DiasDisponibilidad  = req.DiasDisponibilidad,
            PorcentajeDescuento = req.PorcentajeDescuento,
            AplicaIva           = req.AplicaIva,
            PorcentajeIva       = req.AplicaIva ? req.PorcentajeIva : null,
            Estado              = "ACTIVO",
            FechaInicio         = ParseDate(req.FechaInicio),
            FechaFin            = req.FechaFin != null ? ParseDate(req.FechaFin) : null,
            Observaciones       = req.Observaciones,
            CreatedByUsuario    = adminId,
            CreatedAt           = DateTime.UtcNow,
        };
        _db.ComercioCondicionesNegociacion.Add(condicion);
        await _db.SaveChangesAsync();
        return ToCondicionResponse(condicion);
    }

    public async Task<CondicionNegociacionResponse> ActualizarCondicionAsync(
        long idCondicion, ActualizarCondicionRequest req, long adminId)
    {
        var c = await _db.ComercioCondicionesNegociacion.FindAsync(idCondicion)
            ?? throw new KeyNotFoundException($"Condición {idCondicion} no encontrada.");

        if (req.DiasDisponibilidad.HasValue)
        {
            ValidarCondicion(req.DiasDisponibilidad.Value,
                req.PorcentajeDescuento ?? c.PorcentajeDescuento,
                req.AplicaIva ?? c.AplicaIva,
                req.PorcentajeIva ?? c.PorcentajeIva ?? 0);
            c.DiasDisponibilidad = req.DiasDisponibilidad.Value;
        }
        if (req.PorcentajeDescuento.HasValue) c.PorcentajeDescuento = req.PorcentajeDescuento.Value;
        if (req.AplicaIva.HasValue)           c.AplicaIva           = req.AplicaIva.Value;
        if (req.PorcentajeIva.HasValue)       c.PorcentajeIva       = c.AplicaIva ? req.PorcentajeIva : null;
        if (req.Estado != null)               c.Estado              = req.Estado;
        if (req.FechaInicio != null)          c.FechaInicio         = ParseDate(req.FechaInicio);
        if (req.FechaFin    != null)          c.FechaFin            = ParseDate(req.FechaFin);
        if (req.Observaciones != null)        c.Observaciones        = req.Observaciones;
        c.UpdatedAt = DateTime.UtcNow;
        c.UpdatedByUsuario = adminId;
        await _db.SaveChangesAsync();
        return ToCondicionResponse(c);
    }

    // ── Vinculación ──────────────────────────────────────────────────────────

    public async Task VincularComercioOperativoAsync(
        long idComercioAliado, long idComercioExistente, long adminId)
    {
        var aliado = await _db.ComerciosAliados.FindAsync(idComercioAliado)
            ?? throw new KeyNotFoundException($"Comercio aliado {idComercioAliado} no encontrado.");

        var comercio = await _db.Comercios.FindAsync(idComercioExistente)
            ?? throw new KeyNotFoundException($"Comercio operativo {idComercioExistente} no encontrado.");

        if (comercio.Estado != "ACTIVO")
            throw new InvalidOperationException($"El comercio operativo {idComercioExistente} no está ACTIVO.");

        aliado.IdComercioExistente = idComercioExistente;
        aliado.UpdatedAt           = DateTime.UtcNow;
        aliado.UpdatedByUsuario    = adminId;
        await _db.SaveChangesAsync();
    }

    // ── Parámetros liquidación anticipada ────────────────────────────────────

    public async Task<List<ParametroLiquidacionResponse>> ListarParametrosAsync(long? idComercioAliado = null)
    {
        var query = _db.XpayParametrosLiquidacionAnticipada.AsQueryable();
        query = idComercioAliado.HasValue
            ? query.Where(p => p.IdComercioAliado == idComercioAliado.Value)
            : query.Where(p => p.IdComercioAliado == null);
        var items = await query.OrderBy(p => p.DiasFaltantes).ToListAsync();
        return items.Select(ToParametroResponse).ToList();
    }

    public async Task<ParametroLiquidacionResponse> CrearParametroAsync(
        CrearParametroLiquidacionRequest req, long adminId)
    {
        if (req.DiasFaltantes < 0 || req.DiasFaltantes > 60)
            throw new InvalidOperationException("dias_faltantes debe ser entre 0 y 60.");
        if (req.PorcentajeDescuento < 0 || req.PorcentajeDescuento > 100)
            throw new InvalidOperationException("porcentaje_descuento debe ser entre 0 y 100.");
        if (req.AplicaIva && (req.PorcentajeIva <= 0 || req.PorcentajeIva > 100))
            throw new InvalidOperationException("porcentaje_iva debe ser > 0 y <= 100 cuando aplica_iva=true.");

        var existe = await _db.XpayParametrosLiquidacionAnticipada
            .AnyAsync(p => p.IdComercioAliado == req.IdComercioAliado
                        && p.DiasFaltantes == req.DiasFaltantes
                        && p.Estado == "ACTIVO");
        if (existe)
            throw new InvalidOperationException($"Ya existe un parámetro ACTIVO para {req.DiasFaltantes} días.");

        var param = new XpayParametroLiquidacionAnticipada
        {
            IdComercioAliado    = req.IdComercioAliado,
            DiasFaltantes       = req.DiasFaltantes,
            PorcentajeDescuento = req.PorcentajeDescuento,
            AplicaIva           = req.AplicaIva,
            PorcentajeIva       = req.AplicaIva ? req.PorcentajeIva : null,
            Estado              = "ACTIVO",
            CreatedAt           = DateTime.UtcNow,
            CreatedByUsuario    = adminId,
        };
        _db.XpayParametrosLiquidacionAnticipada.Add(param);
        await _db.SaveChangesAsync();
        return ToParametroResponse(param);
    }

    public async Task<ParametroLiquidacionResponse> ActualizarParametroAsync(
        long idParametro, ActualizarParametroLiquidacionRequest req, long adminId)
    {
        var p = await _db.XpayParametrosLiquidacionAnticipada.FindAsync(idParametro)
            ?? throw new KeyNotFoundException($"Parámetro {idParametro} no encontrado.");

        if (req.PorcentajeDescuento.HasValue)
        {
            if (req.PorcentajeDescuento < 0 || req.PorcentajeDescuento > 100)
                throw new InvalidOperationException("porcentaje_descuento debe ser entre 0 y 100.");
            p.PorcentajeDescuento = req.PorcentajeDescuento.Value;
        }
        if (req.AplicaIva.HasValue)  p.AplicaIva = req.AplicaIva.Value;
        if (req.PorcentajeIva.HasValue)
        {
            if (p.AplicaIva && (req.PorcentajeIva <= 0 || req.PorcentajeIva > 100))
                throw new InvalidOperationException("porcentaje_iva debe ser > 0 y <= 100 cuando aplica_iva=true.");
            p.PorcentajeIva = p.AplicaIva ? req.PorcentajeIva : null;
        }
        if (req.Estado != null) p.Estado = req.Estado;
        p.UpdatedAt = DateTime.UtcNow;
        p.UpdatedByUsuario = adminId;
        await _db.SaveChangesAsync();
        return ToParametroResponse(p);
    }

    // ── Disponibilidad ventas (admin) ────────────────────────────────────────

    public async Task<List<DisponibilidadVentaResponse>> ListarDisponibilidadAdminAsync(long idComercioAliado)
    {
        var items = await _db.ComercioVentasQrDisponibilidad
            .Where(d => d.IdComercioAliado == idComercioAliado)
            .OrderByDescending(d => d.FechaVenta)
            .ToListAsync();

        var now    = DateTime.UtcNow;
        var result = new List<DisponibilidadVentaResponse>();
        foreach (var d in items)
        {
            var calc = await CalcularDescuentoAnticipadoAsync(d, now);
            result.Add(ToDisponibilidadResponse(d, now, calc));
        }
        return result;
    }

    public async Task<LiquidarAhoraResponse> LiberarManualAsync(long idDisponibilidad, long adminId)
    {
        return await EjecutarLiberacionAsync(idDisponibilidad, adminId, esAnticipada: false);
    }

    public async Task<LiquidarAhoraResponse> LiquidarAutomaticaAsync(long idDisponibilidad, long? actorId = null)
    {
        return await EjecutarLiberacionAsync(idDisponibilidad, actorId, esAnticipada: false, tipoOverride: "AUTOMATICA");
    }

    // ── Disponibilidad ventas (comercio) ─────────────────────────────────────

    public async Task<MiDisponibilidadResponse> GetMiDisponibilidadAsync(
        long idComercio, DateTime? desde = null, DateTime? hasta = null)
    {
        var query = _db.ComercioVentasQrDisponibilidad
            .Where(d => d.IdComercioExistente == idComercio);
        if (desde.HasValue) query = query.Where(d => d.FechaVenta >= desde.Value);
        if (hasta.HasValue) query = query.Where(d => d.FechaVenta <= hasta.Value.AddDays(1));

        var disp = await query.ToListAsync();

        var noDisp     = disp.Where(d => d.Estado == "NO_DISPONIBLE").ToList();
        var liquidadas = disp.Where(d => d.Estado == "LIQUIDADA_ANTICIPADA" || d.Estado == "DISPONIBLE").ToList();

        var proxima = noDisp
            .OrderBy(d => d.FechaDisponibleProgramada)
            .FirstOrDefault();

        return new MiDisponibilidadResponse(
            TotalNoDisponibleBruto:          noDisp.Sum(d => d.ValorBruto),
            TotalDescuentoConvenio:          noDisp.Sum(d => d.ValorDescuento),
            TotalIvaConvenio:                noDisp.Sum(d => d.ValorIvaConvenio),
            TotalNoDisponibleNetoProgramado: noDisp.Sum(d => d.ValorNetoProgramado),
            TotalDisponibleBruto:            0,
            TotalLiquidado:                  liquidadas.Sum(d => d.ValorNetoLiberado ?? 0),
            CantidadNoDisponible:            noDisp.Count,
            ProximaFechaDisponibilidad:      proxima != null ? proxima.FechaDisponibleProgramada.ToString("yyyy-MM-dd HH:mm") : null,
            ValorEstimadoProximaLiberacion:  proxima?.ValorNetoProgramado ?? 0
        );
    }

    public async Task<List<DisponibilidadVentaResponse>> ListarVentasNoDisponiblesAsync(
        long idComercio, DateTime? desde = null, DateTime? hasta = null)
    {
        var query = _db.ComercioVentasQrDisponibilidad
            .Where(d => d.IdComercioExistente == idComercio && d.Estado == "NO_DISPONIBLE");
        if (desde.HasValue) query = query.Where(d => d.FechaVenta >= desde.Value);
        if (hasta.HasValue) query = query.Where(d => d.FechaVenta <= hasta.Value.AddDays(1));

        var items = await query.OrderBy(d => d.FechaDisponibleProgramada).ToListAsync();

        var now = DateTime.UtcNow;
        var result = new List<DisponibilidadVentaResponse>();
        foreach (var d in items)
        {
            var calc = await CalcularDescuentoAnticipadoAsync(d, now);
            result.Add(ToDisponibilidadResponse(d, now, calc));
        }
        return result;
    }

    public async Task<LiquidarAhoraResponse> LiquidarAhoraAsync(
        long idDisponibilidad, long idComercio)
    {
        var disp = await _db.ComercioVentasQrDisponibilidad.FindAsync(idDisponibilidad)
            ?? throw new KeyNotFoundException($"Disponibilidad {idDisponibilidad} no encontrada.");

        if (disp.IdComercioExistente != idComercio)
            throw new InvalidOperationException("No puede liquidar una venta de otro comercio.");

        return await EjecutarLiberacionAsync(idDisponibilidad, idComercio, esAnticipada: true);
    }

    // ── Implementación central de liberación ─────────────────────────────────

    private async Task<LiquidarAhoraResponse> EjecutarLiberacionAsync(
        long idDisponibilidad, long? actorId, bool esAnticipada, string? tipoOverride = null)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var disp = await _db.ComercioVentasQrDisponibilidad
                .FirstOrDefaultAsync(d => d.IdDisponibilidad == idDisponibilidad)
                ?? throw new KeyNotFoundException($"Disponibilidad {idDisponibilidad} no encontrada.");

            if (disp.Estado != "NO_DISPONIBLE")
                throw new InvalidOperationException(
                    $"La venta ya fue procesada (estado: {disp.Estado}). No se puede liquidar dos veces.");

            var ventaQr = await _db.VentasQr.FirstOrDefaultAsync(v => v.IdVentaQr == disp.IdVentaQr)
                ?? throw new InvalidOperationException("Venta QR no encontrada.");

            if (ventaQr.Estado != "CONTINGENCIA")
                throw new InvalidOperationException(
                    $"La venta QR subyacente no está en CONTINGENCIA (estado: {ventaQr.Estado}).");

            var comercio = await _db.Comercios
                .FirstOrDefaultAsync(c => c.IdComercio == disp.IdComercioExistente && c.Estado == "ACTIVO")
                ?? throw new InvalidOperationException("Comercio no activo.");

            if (comercio.IdWalletComercio == null)
                throw new InvalidOperationException("El comercio no tiene wallet asignada.");

            var saldo = await _db.WalletSaldos
                .FirstOrDefaultAsync(s => s.IdWallet == comercio.IdWalletComercio.Value)
                ?? throw new InvalidOperationException("No existe registro de saldo del comercio.");

            var now = DateTime.UtcNow;

            // Valores del convenio (ya calculados y guardados en disponibilidad)
            var valBruto        = disp.ValorBruto;
            var valDescConvenio = disp.ValorDescuento;
            var valIvaConvenio  = disp.ValorIvaConvenio;

            // Descuento anticipado (calculado en tiempo real desde parámetros)
            var calc = await CalcularDescuentoAnticipadoAsync(disp, now);

            var valDescAnticipado = esAnticipada ? calc.ValorDescuento : 0m;
            var valIvaAnticipado  = esAnticipada ? calc.ValorIva       : 0m;

            // Neto a acreditar en wallet
            var valNeto = esAnticipada
                ? disp.ValorNetoProgramado - valDescAnticipado - valIvaAnticipado
                : disp.ValorNetoProgramado;

            // Verificar balance (pre-check)
            var ivaTotal  = valIvaConvenio + (esAnticipada ? valIvaAnticipado : 0m);
            var crTotal   = valNeto + valDescConvenio + ivaTotal + (esAnticipada ? valDescAnticipado : 0m);
            if (Math.Abs(crTotal - valBruto) > 0.01m)
                throw new InvalidOperationException(
                    $"Error contable: DR={valBruto:0.00} ≠ CR={crTotal:0.00}. No se ejecutó la transacción.");

            // Cuentas ledger
            var idUnidad = comercio.IdUnidadNegocio;

            var ctaContingencia = await _db.LedgerCuentas
                .FirstOrDefaultAsync(c => c.IdUnidadNegocio == idUnidad && c.Codigo == "210201" && c.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("Cuenta 210201 no encontrada.");

            var ctaObligacion = await _db.LedgerCuentas
                .FirstOrDefaultAsync(c => c.IdUnidadNegocio == idUnidad && c.Codigo == "210202" && c.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("Cuenta 210202 no encontrada.");

            var ctaDescConvenio = await _db.LedgerCuentas
                .FirstOrDefaultAsync(c => c.IdUnidadNegocio == idUnidad && c.Codigo == "410201" && c.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("Cuenta 410201 no encontrada.");

            LedgerCuenta? ctaDescAnticipado = null;
            if (esAnticipada && valDescAnticipado > 0)
            {
                ctaDescAnticipado = await _db.LedgerCuentas
                    .FirstOrDefaultAsync(c => c.IdUnidadNegocio == idUnidad && c.Codigo == "410202" && c.Estado == "ACTIVA")
                    ?? throw new InvalidOperationException("Cuenta 410202 no encontrada.");
            }

            LedgerCuenta? ctaIva = null;
            if (ivaTotal > 0)
            {
                ctaIva = await _db.LedgerCuentas
                    .FirstOrDefaultAsync(c => c.IdUnidadNegocio == idUnidad && c.Codigo == "240802" && c.Estado == "ACTIVA")
                    ?? throw new InvalidOperationException("Cuenta 240802 no encontrada.");
            }

            var tipoLiberacion = tipoOverride ?? (esAnticipada ? "ANTICIPADA_COMERCIO" : "MANUAL_ADMIN");
            var desc = tipoLiberacion == "AUTOMATICA"
                ? $"Liquidación automática programada, venta #{disp.IdVentaQr}."
                : esAnticipada
                    ? $"Liquidación anticipada por comercio #{disp.IdComercioExistente}, venta #{disp.IdVentaQr}."
                    : $"Liberación manual admin, venta #{disp.IdVentaQr}.";

            var ledgerTx = new LedgerTransaccion
            {
                IdUnidadNegocio  = idUnidad,
                TipoTransaccion  = "LIBERACION_DISPONIBILIDAD_QR",
                ReferenciaTipo   = "comercio_ventas_qr_disponibilidad",
                ReferenciaId     = disp.IdDisponibilidad,
                Descripcion      = desc,
                ValorTotal       = valBruto,
                Estado           = "REGISTRADA",
                CreadoPor        = actorId,
                FechaTransaccion = now,
            };
            _db.LedgerTransacciones.Add(ledgerTx);
            await _db.SaveChangesAsync();

            var movimientos = new List<LedgerMovimiento>
            {
                // DR 210201 — liberar contingencia
                new() {
                    IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                    IdCuenta            = ctaContingencia.IdCuenta,
                    Naturaleza          = "D",
                    Valor               = valBruto,
                    Concepto            = "LIBERACION_CONTINGENCIA_QR",
                    ReferenciaTipo      = "ventas_qr",
                    ReferenciaId        = disp.IdVentaQr,
                    Descripcion         = $"Débito contingencia venta #{disp.IdVentaQr}.",
                    FechaMovimiento     = now,
                },
                // CR 210202 — acreditar neto a wallet comercio
                new() {
                    IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                    IdCuenta            = ctaObligacion.IdCuenta,
                    Naturaleza          = "C",
                    Valor               = valNeto,
                    Concepto            = "OBLIGACION_WALLET_COMERCIO",
                    ReferenciaTipo      = "comercios",
                    ReferenciaId        = disp.IdComercioExistente,
                    Descripcion         = $"Crédito wallet comercio #{disp.IdComercioExistente}.",
                    FechaMovimiento     = now,
                },
                // CR 410201 — ingreso descuento convenio
                new() {
                    IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                    IdCuenta            = ctaDescConvenio.IdCuenta,
                    Naturaleza          = "C",
                    Valor               = valDescConvenio,
                    Concepto            = "INGRESO_DESCUENTO_COMERCIO",
                    ReferenciaTipo      = "comercio_condiciones_negociacion",
                    ReferenciaId        = disp.IdComercioAliado,
                    Descripcion         = $"Descuento convenio {disp.PorcentajeDescuento}% venta #{disp.IdVentaQr}.",
                    FechaMovimiento     = now,
                },
            };

            // CR 240802 — IVA total (convenio + anticipado consolidado)
            if (ctaIva != null && ivaTotal > 0)
            {
                movimientos.Add(new LedgerMovimiento
                {
                    IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                    IdCuenta            = ctaIva.IdCuenta,
                    Naturaleza          = "C",
                    Valor               = ivaTotal,
                    Concepto            = "IVA_DESCUENTO_COMERCIO",
                    ReferenciaTipo      = "comercio_ventas_qr_disponibilidad",
                    ReferenciaId        = disp.IdDisponibilidad,
                    Descripcion         = $"IVA convenio {valIvaConvenio:0.00}" +
                                          (esAnticipada && valIvaAnticipado > 0
                                              ? $" + IVA anticipado {valIvaAnticipado:0.00}" : "") +
                                          $" venta #{disp.IdVentaQr}.",
                    FechaMovimiento     = now,
                });
            }

            // CR 410202 — ingreso descuento anticipado
            if (ctaDescAnticipado != null && valDescAnticipado > 0)
            {
                movimientos.Add(new LedgerMovimiento
                {
                    IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                    IdCuenta            = ctaDescAnticipado.IdCuenta,
                    Naturaleza          = "C",
                    Valor               = valDescAnticipado,
                    Concepto            = "INGRESO_DESCUENTO_ANTICIPADO",
                    ReferenciaTipo      = "comercio_ventas_qr_disponibilidad",
                    ReferenciaId        = disp.IdDisponibilidad,
                    Descripcion         = $"Descuento anticipado {calc.Tasa}% venta #{disp.IdVentaQr}.",
                    FechaMovimiento     = now,
                });
            }

            _db.LedgerMovimientos.AddRange(movimientos);

            // Wallet movimiento
            var saldoAntes   = saldo.SaldoDisponible;
            var saldoDespues = saldoAntes + valNeto;
            _db.WalletMovimientos.Add(new WalletMovimiento
            {
                IdWallet            = comercio.IdWalletComercio.Value,
                IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                TipoMovimiento      = "LIBERACION_QR_ENTRADA",
                Naturaleza          = "C",
                Valor               = valNeto,
                SaldoAntes          = saldoAntes,
                SaldoDespues        = saldoDespues,
                Descripcion         = desc,
                ReferenciaTipo      = "ventas_qr",
                ReferenciaId        = disp.IdVentaQr,
                Estado              = "APLICADO",
                CreadoPor           = actorId,
                FechaMovimiento     = now,
            });

            saldo.SaldoDisponible    = saldoDespues;
            saldo.FechaActualizacion = now;

            // Actualizar disponibilidad
            disp.Estado                        = tipoLiberacion == "AUTOMATICA" ? "DISPONIBLE" : "LIQUIDADA_ANTICIPADA";
            disp.TipoLiberacion                = tipoLiberacion;
            disp.FechaLiberacion               = now;
            disp.PorcentajeDescuentoAnticipado = esAnticipada ? calc.Tasa : 0;
            disp.ValorDescuentoAnticipado      = esAnticipada ? valDescAnticipado : 0;
            disp.ValorNetoLiberado             = valNeto;
            disp.IdTransaccionLedgerLiberacion = ledgerTx.IdTransaccionLedger;
            disp.UpdatedAt                     = now;

            ventaQr.Estado                   = "LIQUIDADA";
            ventaQr.IdTransaccionLiquidacion = ledgerTx.IdTransaccionLedger;

            await _db.SaveChangesAsync();

            // Verificar balance
            var sumDR = await _db.LedgerMovimientos
                .Where(m => m.IdTransaccionLedger == ledgerTx.IdTransaccionLedger && m.Naturaleza == "D")
                .SumAsync(m => m.Valor);
            var sumCR = await _db.LedgerMovimientos
                .Where(m => m.IdTransaccionLedger == ledgerTx.IdTransaccionLedger && m.Naturaleza == "C")
                .SumAsync(m => m.Valor);

            if (Math.Abs(sumDR - sumCR) > 0.01m)
                throw new InvalidOperationException(
                    $"Ledger no balanceado: DR={sumDR:0.00} CR={sumCR:0.00}. Rollback.");

            await tx.CommitAsync();

            _logger.LogInformation(
                "Liberación {Tipo}: disp={Disp} venta={Venta} bruto={Bruto} descConv={DC} ivaConv={IC} descAnt={DA} ivaAnt={IA} neto={Neto} saldoNuevo={Saldo}",
                tipoLiberacion, idDisponibilidad, disp.IdVentaQr,
                valBruto, valDescConvenio, valIvaConvenio,
                valDescAnticipado, valIvaAnticipado, valNeto, saldoDespues);

            return new LiquidarAhoraResponse(
                disp.IdDisponibilidad,
                disp.IdVentaQr,
                valBruto,
                valDescConvenio,
                valIvaConvenio,
                valDescAnticipado,
                valIvaAnticipado,
                valNeto,
                calc.Tasa,
                Math.Max(0, (int)(disp.FechaDisponibleProgramada - now).TotalDays),
                disp.Estado,
                tipoLiberacion,
                now.ToString("yyyy-MM-dd HH:mm:ss"),
                ledgerTx.IdTransaccionLedger,
                saldoDespues
            );
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private record AnticipadoCalc(
        decimal Tasa,
        decimal ValorDescuento,
        bool    AplicaIva,
        decimal PorcentajeIva,
        decimal ValorIva
    );

    private async Task<AnticipadoCalc> CalcularDescuentoAnticipadoAsync(
        ComercioVentaQrDisponibilidad disp, DateTime ahora)
    {
        var diasFaltantes = (int)Math.Max(0, Math.Ceiling((disp.FechaDisponibleProgramada - ahora).TotalDays));

        // 1. Específico del comercio aliado — exacto
        var param = await _db.XpayParametrosLiquidacionAnticipada
            .FirstOrDefaultAsync(p => p.IdComercioAliado == disp.IdComercioAliado
                                   && p.DiasFaltantes    == diasFaltantes
                                   && p.Estado           == "ACTIVO");

        // 2. Específico — más cercano <=
        if (param == null)
            param = await _db.XpayParametrosLiquidacionAnticipada
                .Where(p => p.IdComercioAliado == disp.IdComercioAliado
                         && p.DiasFaltantes    <= diasFaltantes
                         && p.Estado           == "ACTIVO")
                .OrderByDescending(p => p.DiasFaltantes)
                .FirstOrDefaultAsync();

        // 3. Global — exacto
        if (param == null)
            param = await _db.XpayParametrosLiquidacionAnticipada
                .FirstOrDefaultAsync(p => p.IdComercioAliado == null
                                       && p.DiasFaltantes    == diasFaltantes
                                       && p.Estado           == "ACTIVO");

        // 4. Global — más cercano <=
        if (param == null)
            param = await _db.XpayParametrosLiquidacionAnticipada
                .Where(p => p.IdComercioAliado == null
                         && p.DiasFaltantes    <= diasFaltantes
                         && p.Estado           == "ACTIVO")
                .OrderByDescending(p => p.DiasFaltantes)
                .FirstOrDefaultAsync();

        var tasa     = param?.PorcentajeDescuento ?? 0m;
        // CORRECTO: descuento anticipado sobre valor bruto, no sobre neto programado
        var valDesc  = Math.Round(disp.ValorBruto * tasa / 100m, 2);
        var aplicaIva = param?.AplicaIva ?? false;
        var pctIva    = (aplicaIva ? (param?.PorcentajeIva ?? 0m) : 0m);
        var valIva    = aplicaIva ? Math.Round(valDesc * pctIva / 100m, 2) : 0m;

        return new AnticipadoCalc(tasa, valDesc, aplicaIva, pctIva, valIva);
    }

    private static void ValidarCondicion(int dias, decimal pct, bool aplicaIva, decimal pctIva)
    {
        if (dias < 0)
            throw new InvalidOperationException("dias_disponibilidad debe ser >= 0.");
        if (pct < 0 || pct > 100)
            throw new InvalidOperationException("porcentaje_descuento debe ser entre 0 y 100.");
        if (aplicaIva && (pctIva <= 0 || pctIva > 100))
            throw new InvalidOperationException("porcentaje_iva debe ser > 0 y <= 100 cuando aplica_iva=true.");
    }

    private static DateOnly ParseDate(string? s)
    {
        if (DateOnly.TryParse(s, out var d)) return d;
        throw new InvalidOperationException($"Fecha inválida: '{s}'. Use formato YYYY-MM-DD.");
    }

    private static CondicionNegociacionResponse ToCondicionResponse(ComercioCondicionNegociacion c) =>
        new(c.IdCondicion, c.IdComercioAliado, c.DiasDisponibilidad,
            c.PorcentajeDescuento, c.AplicaIva, c.PorcentajeIva ?? 0m,
            c.Estado, c.FechaInicio.ToString("yyyy-MM-dd"),
            c.FechaFin?.ToString("yyyy-MM-dd"), c.Observaciones, c.CreatedAt.ToString("o"));

    private static ParametroLiquidacionResponse ToParametroResponse(XpayParametroLiquidacionAnticipada p) =>
        new(p.IdParametro, p.IdComercioAliado, p.DiasFaltantes,
            p.PorcentajeDescuento, p.AplicaIva, p.PorcentajeIva ?? 0m,
            p.Estado, p.CreatedAt.ToString("o"));

    private static DisponibilidadVentaResponse ToDisponibilidadResponse(
        ComercioVentaQrDisponibilidad d, DateTime now, AnticipadoCalc calc)
    {
        var netoSiLiquidaAhora = d.ValorNetoProgramado - calc.ValorDescuento - calc.ValorIva;
        return new DisponibilidadVentaResponse(
            d.IdDisponibilidad,
            d.IdVentaQr,
            d.ValorBruto,
            d.PorcentajeDescuento,
            d.ValorDescuento,
            d.AplicaIvaConvenio,
            d.PorcentajeIvaConvenio ?? 0m,
            d.ValorIvaConvenio,
            d.ValorNetoProgramado,
            d.DiasDisponibilidad,
            d.FechaVenta.ToString("o"),
            d.FechaDisponibleProgramada.ToString("o"),
            (int)Math.Max(0, Math.Ceiling((d.FechaDisponibleProgramada - now).TotalDays)),
            calc.Tasa,
            calc.ValorDescuento,
            calc.AplicaIva,
            calc.PorcentajeIva,
            calc.ValorIva,
            netoSiLiquidaAhora,
            d.Estado,
            d.TipoLiberacion,
            d.FechaLiberacion?.ToString("o"),
            d.ValorNetoLiberado,
            d.IdTransaccionLedgerLiberacion
        );
    }
}
