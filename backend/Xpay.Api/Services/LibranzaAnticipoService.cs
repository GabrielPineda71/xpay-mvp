using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public class LibranzaAnticipoService
{
    private readonly XpayDbContext                      _db;
    private readonly ILogger<LibranzaAnticipoService>  _logger;

    // Ledger account codes
    private const string CodAnticipoCartera = "130104"; // Cartera Anticipo Nómina (ACTIVO, D)
    private const string CodObligacion      = "210101"; // Obligación Wallet Usuarios (PASIVO, C)
    private const string CodBanco           = "110102"; // Banco Coopcentral XPAY (ACTIVO, D)
    private const string CodComisionIncome  = "310103"; // Ingreso Comisión Anticipo Nómina (INGRESO, C)
    private const string CodIva             = "230103"; // IVA Anticipo Nómina (PASIVO, C)

    private const long IdUnidadNegocio = 1;

    public LibranzaAnticipoService(XpayDbContext db, ILogger<LibranzaAnticipoService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    // ── Obtener corte vigente ─────────────────────────────────────────────────

    public async Task<CorteVigenteResponse?> ObtenerCorteVigenteAsync(
        long idEmpleado, DateOnly fechaSimulada)
    {
        var empleado = await _db.LibranzaEmpleados.FindAsync(idEmpleado)
            ?? throw new KeyNotFoundException($"Empleado {idEmpleado} no encontrado.");

        var convenio = await _db.LibranzaEmpresasConvenio.FindAsync(empleado.IdConvenio)
            ?? throw new InvalidOperationException("Convenio del empleado no encontrado.");

        var cortes = await _db.LibranzaEmpleadoCortesPago
            .Where(c => c.IdEmpleado == idEmpleado && c.Estado == "ACTIVO")
            .OrderBy(c => c.DiaPago)
            .ToListAsync();

        if (cortes.Count == 0) return null;

        var diaActual = fechaSimulada.Day;

        // Build period list: for each payment day, period is [prev+1..dayPago-1]
        // Days 0 = before first payment (virtual start)
        var diasPago = cortes.Select(c => c.DiaPago).OrderBy(d => d).ToList();

        for (int i = 0; i < diasPago.Count; i++)
        {
            var diaPago = diasPago[i];
            var periodoInicio = i == 0 ? 1 : diasPago[i - 1] + 1;
            var periodoFin    = diaPago - 1;

            if (diaActual >= periodoInicio && diaActual <= periodoFin)
            {
                var corte = cortes.First(c => c.DiaPago == diaPago);
                var param = await ObtenerParametrosAsync(empleado.IdConvenio);
                var pct   = param?.PorcentajeMaximoCupo ?? convenio.PorcentajeMaximoCupo;
                var cupoBase = corte.ValorPagoProgramado * pct / 100m;
                var fechaPago = new DateOnly(fechaSimulada.Year, fechaSimulada.Month, diaPago);
                if (diaActual > diaPago) fechaPago = fechaPago.AddMonths(1);
                var cupoUsado = await CalcularCupoUsadoAsync(idEmpleado, diaPago, fechaPago);

                return new CorteVigenteResponse
                {
                    DiaPago             = diaPago,
                    PeriodoInicio       = periodoInicio,
                    PeriodoFin          = periodoFin,
                    FechaPago           = fechaPago,
                    ValorPagoProgramado = corte.ValorPagoProgramado,
                    PorcentajeCupo      = pct,
                    CupoBase            = cupoBase,
                    CupoUsado           = cupoUsado,
                    CupoDisponible      = Math.Max(0, cupoBase - cupoUsado),
                    FechaSimulada       = fechaSimulada.ToString("yyyy-MM-dd"),
                    EsDiaPago           = false,
                };
            }

            if (diaActual == diaPago)
            {
                if (!convenio.PermiteAnticipodiaPago)
                    throw new InvalidOperationException(
                        $"No se permite solicitar anticipo el día de pago (día {diaPago}). " +
                        "Espere a que se cierre/recaude el corte.");

                var corte = cortes.First(c => c.DiaPago == diaPago);
                var param = await ObtenerParametrosAsync(empleado.IdConvenio);
                var pct   = param?.PorcentajeMaximoCupo ?? convenio.PorcentajeMaximoCupo;
                var cupoBase = corte.ValorPagoProgramado * pct / 100m;
                var fechaPago = new DateOnly(fechaSimulada.Year, fechaSimulada.Month, diaPago);
                var cupoUsado = await CalcularCupoUsadoAsync(idEmpleado, diaPago, fechaPago);

                return new CorteVigenteResponse
                {
                    DiaPago             = diaPago,
                    PeriodoInicio       = periodoInicio,
                    PeriodoFin          = periodoFin,
                    FechaPago           = fechaPago,
                    ValorPagoProgramado = corte.ValorPagoProgramado,
                    PorcentajeCupo      = pct,
                    CupoBase            = cupoBase,
                    CupoUsado           = cupoUsado,
                    CupoDisponible      = Math.Max(0, cupoBase - cupoUsado),
                    FechaSimulada       = fechaSimulada.ToString("yyyy-MM-dd"),
                    EsDiaPago           = true,
                };
            }
        }

        return null; // Day 31 or after last payment day with no next period
    }

    private async Task<decimal> CalcularCupoUsadoAsync(long idEmpleado, int diaPago, DateOnly fechaPago)
    {
        return await _db.LibranzaAnticipos
            .Where(a => a.IdEmpleado == idEmpleado
                     && a.DiaPagoCorte == diaPago
                     && a.FechaPagoProgramada == fechaPago
                     && (a.Estado == "DESEMBOLSADO"))
            .SumAsync(a => (decimal?)a.ValorSolicitado) ?? 0m;
    }

    private async Task<LibranzaParametrosEmpresa?> ObtenerParametrosAsync(long idConvenio) =>
        await _db.LibranzaParametrosEmpresas
            .Where(p => p.IdConvenio == idConvenio && p.Estado == "ACTIVO")
            .OrderByDescending(p => p.IdParametro)
            .FirstOrDefaultAsync();

    // ── Mi cupo (cliente) ─────────────────────────────────────────────────────

    public async Task<MiCupoResponse> GetMiCupoAsync(long idUsuario, DateOnly fechaSimulada)
    {
        var asoc = await _db.LibranzaEmpleadoUsuarios
            .FirstOrDefaultAsync(a => a.IdUsuario == idUsuario && a.Estado == "ACTIVO")
            ?? throw new UnauthorizedAccessException("No tiene un perfil de empleado de libranza asociado.");

        var empleado = await _db.LibranzaEmpleados.FindAsync(asoc.IdEmpleado)
            ?? throw new InvalidOperationException("Empleado no encontrado.");

        var convenio = await _db.LibranzaEmpresasConvenio.FindAsync(empleado.IdConvenio)
            ?? throw new InvalidOperationException("Convenio no encontrado.");

        CorteVigenteResponse? corte = null;
        try { corte = await ObtenerCorteVigenteAsync(empleado.IdEmpleado, fechaSimulada); }
        catch (InvalidOperationException) { /* día de pago bloqueado, corte null */ }

        var anticiposActivos = await _db.LibranzaAnticipos
            .Where(a => a.IdEmpleado == empleado.IdEmpleado && a.Estado == "DESEMBOLSADO")
            .OrderByDescending(a => a.FechaSolicitud)
            .ToListAsync();

        var historial = await _db.LibranzaAnticipos
            .Where(a => a.IdEmpleado == empleado.IdEmpleado && a.Estado != "DESEMBOLSADO")
            .OrderByDescending(a => a.FechaSolicitud)
            .Take(20)
            .ToListAsync();

        return new MiCupoResponse
        {
            IdConvenio         = convenio.IdConvenio,
            NombreEmpresa      = convenio.NombreEmpresa,
            IdEmpleado         = empleado.IdEmpleado,
            NombresEmpleado    = $"{empleado.Nombres} {empleado.Apellidos}".Trim(),
            NumeroDocumento    = empleado.NumeroDocumento,
            PeriodicidadPago   = empleado.PeriodicidadPago,
            CorteVigente       = corte,
            AnticiposActivos   = anticiposActivos.Select(ToAnticipoResponse).ToList(),
            HistorialAnticipos = historial.Select(ToAnticipoResponse).ToList(),
        };
    }

    // ── Solicitar anticipo ────────────────────────────────────────────────────

    public async Task<AnticipoResponse> SolicitarAnticipoAsync(long idUsuario, SolicitarAnticipoRequest req)
    {
        if (req.ValorSolicitado <= 0)
            throw new InvalidOperationException("El valor solicitado debe ser mayor a cero.");

        var fechaSimulada = req.FechaSimulada is not null
            ? DateOnly.Parse(req.FechaSimulada)
            : DateOnly.FromDateTime(DateTime.UtcNow);

        var asoc = await _db.LibranzaEmpleadoUsuarios
            .FirstOrDefaultAsync(a => a.IdUsuario == idUsuario && a.Estado == "ACTIVO")
            ?? throw new UnauthorizedAccessException("No tiene un perfil de empleado de libranza asociado.");

        if (asoc.IdWallet is null)
            throw new InvalidOperationException("El empleado no tiene wallet asociada para el desembolso.");

        var empleado = await _db.LibranzaEmpleados.FindAsync(asoc.IdEmpleado)
            ?? throw new InvalidOperationException("Empleado no encontrado.");
        if (empleado.Estado != "ACTIVO")
            throw new InvalidOperationException("El empleado no está activo.");

        var convenio = await _db.LibranzaEmpresasConvenio.FindAsync(empleado.IdConvenio)
            ?? throw new InvalidOperationException("Convenio no encontrado.");
        if (convenio.Estado != "ACTIVO")
            throw new InvalidOperationException("El convenio no está activo.");

        var param = await ObtenerParametrosAsync(empleado.IdConvenio)
            ?? throw new InvalidOperationException("El convenio no tiene parámetros activos.");

        // Verify anticipo limit
        var activosCount = await _db.LibranzaAnticipos
            .CountAsync(a => a.IdEmpleado == empleado.IdEmpleado && a.Estado == "DESEMBOLSADO");
        if (!param.PermiteAnticipoMultiple && activosCount >= param.MaxAnticipacionesActivos)
            throw new InvalidOperationException(
                $"Ya tiene {activosCount} anticipo(s) activo(s). El máximo permitido es {param.MaxAnticipacionesActivos}.");

        var corte = await ObtenerCorteVigenteAsync(empleado.IdEmpleado, fechaSimulada)
            ?? throw new InvalidOperationException(
                "No hay corte de pago vigente para la fecha simulada indicada.");

        if (corte.EsDiaPago && !convenio.PermiteAnticipodiaPago)
            throw new InvalidOperationException("No se permite anticipos el día de pago.");

        if (req.ValorSolicitado > corte.CupoDisponible)
            throw new InvalidOperationException(
                $"El valor solicitado ({req.ValorSolicitado:0.00}) supera el cupo disponible ({corte.CupoDisponible:0.00}).");

        // Calculate comisión
        var rango = await _db.LibranzaRangosCobro
            .Where(r => r.IdConvenio == convenio.IdConvenio
                     && r.Estado == "ACTIVO"
                     && r.ValorDesde <= req.ValorSolicitado
                     && r.ValorHasta >= req.ValorSolicitado)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException(
                $"No existe rango de cobro activo para el valor {req.ValorSolicitado:0.00}.");

        var comision = rango.TipoCobro == "FIJO"
            ? rango.ValorCobro
            : Math.Round(req.ValorSolicitado * rango.ValorCobro / 100m, 2);
        var iva = rango.AplicaIva ? Math.Round(comision * param.IvaPorcentaje / 100m, 2) : 0m;
        var totalACobrar = req.ValorSolicitado + comision + iva;

        decimal netoDesembolsado, drCartera, crObligacion, crComision, crIva;

        if (param.MomentoCobroComision == "VENCIDO")
        {
            // Desembolso total, cobra al vencer
            netoDesembolsado = req.ValorSolicitado;
            drCartera        = req.ValorSolicitado;
            crObligacion     = req.ValorSolicitado;
            crComision       = 0m;
            crIva            = 0m;
        }
        else // ANTICIPADO
        {
            netoDesembolsado = req.ValorSolicitado - comision - iva;
            if (netoDesembolsado <= 0)
                throw new InvalidOperationException(
                    "El valor neto desembolsado sería cero o negativo con comisión anticipada.");
            drCartera    = totalACobrar;
            crObligacion = netoDesembolsado;
            crComision   = comision;
            crIva        = iva;
        }

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // Load ledger accounts
            var cuentaCartera   = await GetCuentaAsync(CodAnticipoCartera);
            var cuentaObligacion = await GetCuentaAsync(CodObligacion);
            var cuentaComision  = param.MomentoCobroComision == "ANTICIPADO" ? await GetCuentaAsync(CodComisionIncome) : null;
            var cuentaIvaLedger = param.MomentoCobroComision == "ANTICIPADO" && iva > 0 ? await GetCuentaAsync(CodIva) : null;

            // Load wallet
            var idWallet = asoc.IdWallet.Value;
            var wallet  = await _db.Wallets.FirstOrDefaultAsync(w => w.IdWallet == idWallet && w.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("La wallet del empleado no está activa.");
            var saldo   = await _db.WalletSaldos.FirstOrDefaultAsync(s => s.IdWallet == idWallet)
                ?? throw new InvalidOperationException("La wallet no tiene registro de saldo.");

            var now = DateTime.UtcNow;

            // Create anticipo record
            var anticipo = new LibranzaAnticipo
            {
                IdConvenio             = convenio.IdConvenio,
                IdEmpleado             = empleado.IdEmpleado,
                IdUsuario              = idUsuario,
                IdWallet               = idWallet,
                FechaSolicitud         = now,
                FechaSimulada          = fechaSimulada,
                DiaPagoCorte           = corte.DiaPago,
                FechaPagoProgramada    = corte.FechaPago,
                ValorPagoProgramado    = corte.ValorPagoProgramado,
                PorcentajeCupo         = corte.PorcentajeCupo,
                ValorCupoBase          = corte.CupoBase,
                ValorSolicitado        = req.ValorSolicitado,
                ValorComision          = comision,
                ValorIva               = iva,
                ValorTotalACobrar      = totalACobrar,
                ValorNetoDesembolsado  = netoDesembolsado,
                MomentoCobroComision   = param.MomentoCobroComision,
                Estado                 = "CREADO",
                CreatedAt              = now,
                CreatedByUsuario       = idUsuario,
            };
            _db.LibranzaAnticipos.Add(anticipo);
            await _db.SaveChangesAsync();

            // Create ledger transaction
            var ledgerTx = new LedgerTransaccion
            {
                IdUnidadNegocio  = IdUnidadNegocio,
                TipoTransaccion  = "LIBRANZA_ANTICIPO_DESEMBOLSO",
                ReferenciaTipo   = "libranza_anticipos",
                ReferenciaId     = anticipo.IdAnticipo,
                Descripcion      = $"Desembolso anticipo nómina #{anticipo.IdAnticipo} empleado #{empleado.IdEmpleado}",
                ValorTotal       = req.ValorSolicitado,
                Estado           = "REGISTRADA",
                CreadoPor        = idUsuario,
                FechaTransaccion = now,
            };
            _db.LedgerTransacciones.Add(ledgerTx);
            await _db.SaveChangesAsync();

            // Ledger movements
            var movimientos = new List<LedgerMovimiento>
            {
                new() {
                    IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                    IdCuenta    = cuentaCartera.IdCuenta,
                    Naturaleza  = "D",
                    Valor       = drCartera,
                    Concepto    = "ANTICIPO_DESEMBOLSO",
                    ReferenciaTipo = "libranza_anticipos",
                    ReferenciaId   = anticipo.IdAnticipo,
                    Descripcion = "Anticipo nómina por cobrar.",
                    FechaMovimiento = now,
                },
                new() {
                    IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                    IdCuenta    = cuentaObligacion.IdCuenta,
                    Naturaleza  = "C",
                    Valor       = crObligacion,
                    Concepto    = "ANTICIPO_DESEMBOLSO",
                    ReferenciaTipo = "libranza_anticipos",
                    ReferenciaId   = anticipo.IdAnticipo,
                    Descripcion = "Obligación wallet usuario por anticipo.",
                    FechaMovimiento = now,
                },
            };

            if (param.MomentoCobroComision == "ANTICIPADO")
            {
                if (cuentaComision is not null && crComision > 0)
                    movimientos.Add(new() {
                        IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                        IdCuenta    = cuentaComision.IdCuenta,
                        Naturaleza  = "C",
                        Valor       = crComision,
                        Concepto    = "ANTICIPO_COMISION",
                        ReferenciaTipo = "libranza_anticipos",
                        ReferenciaId   = anticipo.IdAnticipo,
                        Descripcion = "Comisión anticipo nómina (cobro anticipado).",
                        FechaMovimiento = now,
                    });
                if (cuentaIvaLedger is not null && crIva > 0)
                    movimientos.Add(new() {
                        IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                        IdCuenta    = cuentaIvaLedger.IdCuenta,
                        Naturaleza  = "C",
                        Valor       = crIva,
                        Concepto    = "ANTICIPO_IVA",
                        ReferenciaTipo = "libranza_anticipos",
                        ReferenciaId   = anticipo.IdAnticipo,
                        Descripcion = "IVA comisión anticipo nómina (cobro anticipado).",
                        FechaMovimiento = now,
                    });
            }

            _db.LedgerMovimientos.AddRange(movimientos);

            // Wallet movement — credit to employee wallet
            var saldoAntes   = saldo.SaldoDisponible;
            var saldoDespues = saldoAntes + netoDesembolsado;
            _db.WalletMovimientos.Add(new WalletMovimiento
            {
                IdWallet            = idWallet,
                IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                TipoMovimiento      = "ANTICIPO_NOMINA",
                Naturaleza          = "C",
                Valor               = netoDesembolsado,
                SaldoAntes          = saldoAntes,
                SaldoDespues        = saldoDespues,
                Descripcion         = $"Anticipo nómina #{anticipo.IdAnticipo}",
                ReferenciaTipo      = "libranza_anticipos",
                ReferenciaId        = anticipo.IdAnticipo,
                Estado              = "APLICADO",
                CreadoPor           = idUsuario,
                FechaMovimiento     = now,
            });

            saldo.SaldoDisponible   = saldoDespues;
            saldo.FechaActualizacion = now;

            // Update anticipo state
            anticipo.Estado                        = "DESEMBOLSADO";
            anticipo.IdTransaccionLedgerDesembolso = ledgerTx.IdTransaccionLedger;
            anticipo.UpdatedAt                     = now;
            anticipo.UpdatedByUsuario              = idUsuario;

            await _db.SaveChangesAsync();

            // Verify ledger balance
            var totalD = movimientos.Where(m => m.Naturaleza == "D").Sum(m => m.Valor);
            var totalC = movimientos.Where(m => m.Naturaleza == "C").Sum(m => m.Valor);
            if (totalD != totalC)
                throw new InvalidOperationException($"Ledger desbalanceado: DR={totalD} CR={totalC}.");

            await tx.CommitAsync();

            _logger.LogInformation("LIBRANZA_ANTICIPO_DESEMBOLSO: id={Id} empleado={Emp} valor={Val} ledger={Led}",
                anticipo.IdAnticipo, empleado.IdEmpleado, netoDesembolsado, ledgerTx.IdTransaccionLedger);

            return ToAnticipoResponse(anticipo);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // ── Cobros de empresa ─────────────────────────────────────────────────────

    public async Task<List<CobroEmpresaItem>> GetCobrosAsync(long idConvenio, DateOnly fechaPago)
    {
        var anticipos = await _db.LibranzaAnticipos
            .Where(a => a.IdConvenio == idConvenio
                     && a.FechaPagoProgramada == fechaPago
                     && a.Estado == "DESEMBOLSADO")
            .OrderBy(a => a.IdAnticipo)
            .ToListAsync();

        var result = new List<CobroEmpresaItem>();
        foreach (var a in anticipos)
        {
            var emp = await _db.LibranzaEmpleados.FindAsync(a.IdEmpleado);
            result.Add(new CobroEmpresaItem
            {
                IdAnticipo           = a.IdAnticipo,
                IdEmpleado           = a.IdEmpleado,
                NombresEmpleado      = emp is null ? "?" : $"{emp.Nombres} {emp.Apellidos}".Trim(),
                NumeroDocumento      = emp?.NumeroDocumento ?? "?",
                TipoDocumento        = emp?.TipoDocumento ?? "?",
                DiaPagoCorte         = a.DiaPagoCorte,
                FechaPagoProgramada  = a.FechaPagoProgramada?.ToString("yyyy-MM-dd"),
                ValorSolicitado      = a.ValorSolicitado,
                ValorComision        = a.ValorComision,
                ValorIva             = a.ValorIva,
                ValorTotalACobrar    = a.ValorTotalACobrar,
                MomentoCobroComision = a.MomentoCobroComision,
                Estado               = a.Estado,
            });
        }
        return result;
    }

    public async Task<AplicarPagoResult> AplicarPagoEmpresaAsync(
        long idConvenio, AplicarPagoRequest req, long idUsuarioEmpresa)
    {
        if (string.IsNullOrWhiteSpace(req.FechaPago))
            throw new InvalidOperationException("fechaPago es obligatorio (YYYY-MM-DD).");
        if (string.IsNullOrWhiteSpace(req.ReferenciaPago))
            throw new InvalidOperationException("referenciaPago es obligatorio.");

        var fechaPago = DateOnly.Parse(req.FechaPago);

        // Verify no double-payment: check if any anticipo already pagado with same reference
        if (await _db.LibranzaAnticipos.AnyAsync(a =>
            a.IdConvenio == idConvenio &&
            a.ReferenciaPago == req.ReferenciaPago.Trim() &&
            a.Estado == "PAGADO"))
            throw new InvalidOperationException($"Ya se aplicó un pago con referencia '{req.ReferenciaPago}'.");

        var anticipos = await _db.LibranzaAnticipos
            .Where(a => a.IdConvenio == idConvenio
                     && a.FechaPagoProgramada == fechaPago
                     && a.Estado == "DESEMBOLSADO")
            .ToListAsync();

        if (anticipos.Count == 0)
            throw new InvalidOperationException(
                $"No hay anticipos DESEMBOLSADOS para el convenio {idConvenio} con fecha de pago {req.FechaPago}.");

        var convenio = await _db.LibranzaEmpresasConvenio.FindAsync(idConvenio)
            ?? throw new KeyNotFoundException($"Convenio {idConvenio} no encontrado.");
        if (convenio.Estado != "ACTIVO")
            throw new InvalidOperationException("El convenio no está activo.");

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var cuentaBanco         = await GetCuentaAsync(CodBanco);
            var cuentaCartera       = await GetCuentaAsync(CodAnticipoCartera);
            var cuentaComisionIncome = await GetCuentaAsync(CodComisionIncome);
            var cuentaIvaLedger     = await GetCuentaAsync(CodIva);

            var now          = DateTime.UtcNow;
            var totalCobrado = anticipos.Sum(a => a.ValorTotalACobrar);

            // Create single ledger transaction for the batch payment
            var ledgerTx = new LedgerTransaccion
            {
                IdUnidadNegocio  = IdUnidadNegocio,
                TipoTransaccion  = "LIBRANZA_PAGO_EMPRESA",
                ReferenciaTipo   = "libranza_empresas_convenio",
                ReferenciaId     = idConvenio,
                Descripcion      = $"Pago empresa convenio #{idConvenio} corte {req.FechaPago} ref={req.ReferenciaPago}",
                ValorTotal       = totalCobrado,
                Estado           = "REGISTRADA",
                CreadoPor        = idUsuarioEmpresa,
                FechaTransaccion = now,
            };
            _db.LedgerTransacciones.Add(ledgerTx);
            await _db.SaveChangesAsync();

            var movimientos = new List<LedgerMovimiento>();

            foreach (var anticipo in anticipos)
            {
                // DR Banco = valor_total_a_cobrar (simulated bank debit)
                movimientos.Add(new() {
                    IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                    IdCuenta    = cuentaBanco.IdCuenta,
                    Naturaleza  = "D",
                    Valor       = anticipo.ValorTotalACobrar,
                    Concepto    = "PAGO_EMPRESA_LIBRANZA",
                    ReferenciaTipo = "libranza_anticipos",
                    ReferenciaId   = anticipo.IdAnticipo,
                    Descripcion = $"Pago empresa anticipo #{anticipo.IdAnticipo} [simulado QA].",
                    FechaMovimiento = now,
                });

                if (anticipo.MomentoCobroComision == "VENCIDO")
                {
                    // CR Cartera = valor_solicitado, CR Comision, CR IVA
                    movimientos.Add(new() {
                        IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                        IdCuenta    = cuentaCartera.IdCuenta,
                        Naturaleza  = "C",
                        Valor       = anticipo.ValorSolicitado,
                        Concepto    = "PAGO_EMPRESA_LIBRANZA",
                        ReferenciaTipo = "libranza_anticipos",
                        ReferenciaId   = anticipo.IdAnticipo,
                        Descripcion = $"Cancelación anticipo #{anticipo.IdAnticipo}.",
                        FechaMovimiento = now,
                    });
                    if (anticipo.ValorComision > 0)
                        movimientos.Add(new() {
                            IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                            IdCuenta    = cuentaComisionIncome.IdCuenta,
                            Naturaleza  = "C",
                            Valor       = anticipo.ValorComision,
                            Concepto    = "PAGO_EMPRESA_COMISION",
                            ReferenciaTipo = "libranza_anticipos",
                            ReferenciaId   = anticipo.IdAnticipo,
                            Descripcion = $"Comisión anticipo #{anticipo.IdAnticipo} (vencida).",
                            FechaMovimiento = now,
                        });
                    if (anticipo.ValorIva > 0)
                        movimientos.Add(new() {
                            IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                            IdCuenta    = cuentaIvaLedger.IdCuenta,
                            Naturaleza  = "C",
                            Valor       = anticipo.ValorIva,
                            Concepto    = "PAGO_EMPRESA_IVA",
                            ReferenciaTipo = "libranza_anticipos",
                            ReferenciaId   = anticipo.IdAnticipo,
                            Descripcion = $"IVA comisión anticipo #{anticipo.IdAnticipo} (vencida).",
                            FechaMovimiento = now,
                        });
                }
                else // ANTICIPADO — comision already recorded at disbursement, only cancel cartera
                {
                    movimientos.Add(new() {
                        IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                        IdCuenta    = cuentaCartera.IdCuenta,
                        Naturaleza  = "C",
                        Valor       = anticipo.ValorTotalACobrar,
                        Concepto    = "PAGO_EMPRESA_LIBRANZA",
                        ReferenciaTipo = "libranza_anticipos",
                        ReferenciaId   = anticipo.IdAnticipo,
                        Descripcion = $"Cancelación anticipo #{anticipo.IdAnticipo} (comisión anticipada).",
                        FechaMovimiento = now,
                    });
                }

                // Mark anticipo as PAGADO
                anticipo.Estado                   = "PAGADO";
                anticipo.ReferenciaPago            = req.ReferenciaPago.Trim();
                anticipo.IdTransaccionLedgerPago   = ledgerTx.IdTransaccionLedger;
                anticipo.UpdatedAt                 = now;
                anticipo.UpdatedByUsuario          = idUsuarioEmpresa;
            }

            _db.LedgerMovimientos.AddRange(movimientos);
            await _db.SaveChangesAsync();

            // Verify balance
            var totalD = movimientos.Where(m => m.Naturaleza == "D").Sum(m => m.Valor);
            var totalC = movimientos.Where(m => m.Naturaleza == "C").Sum(m => m.Valor);
            if (totalD != totalC)
                throw new InvalidOperationException($"Ledger desbalanceado: DR={totalD} CR={totalC}.");

            await tx.CommitAsync();

            _logger.LogInformation("LIBRANZA_PAGO_EMPRESA: convenio={Conv} anticipos={N} total={T} ref={Ref} ledger={Led}",
                idConvenio, anticipos.Count, totalCobrado, req.ReferenciaPago, ledgerTx.IdTransaccionLedger);

            return new AplicarPagoResult
            {
                AnticiposAplicados  = anticipos.Count,
                TotalCobrado        = totalCobrado,
                ReferenciaPago      = req.ReferenciaPago.Trim(),
                IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
            };
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // ── Admin: listar anticipos por convenio ──────────────────────────────────

    public async Task<List<AnticipoResponse>> GetAnticiposConvenioAsync(long idConvenio)
    {
        var rows = await _db.LibranzaAnticipos
            .Where(a => a.IdConvenio == idConvenio)
            .OrderByDescending(a => a.IdAnticipo)
            .ToListAsync();
        return rows.Select(ToAnticipoResponse).ToList();
    }

    // ── Admin: ledger de un anticipo ──────────────────────────────────────────

    public async Task<AnticipoLedgerResponse> GetAnticipoLedgerAsync(long idAnticipo)
    {
        var anticipo = await _db.LibranzaAnticipos.FindAsync(idAnticipo)
            ?? throw new KeyNotFoundException($"Anticipo {idAnticipo} no encontrado.");

        var txIds = new List<long>();
        if (anticipo.IdTransaccionLedgerDesembolso.HasValue)
            txIds.Add(anticipo.IdTransaccionLedgerDesembolso.Value);
        if (anticipo.IdTransaccionLedgerPago.HasValue)
            txIds.Add(anticipo.IdTransaccionLedgerPago.Value);

        var movs = await _db.LedgerMovimientos
            .Where(m => txIds.Contains(m.IdTransaccionLedger))
            .ToListAsync();

        var cuentaIds = movs.Select(m => m.IdCuenta).Distinct().ToList();
        var cuentas   = await _db.LedgerCuentas
            .Where(c => cuentaIds.Contains(c.IdCuenta))
            .ToDictionaryAsync(c => c.IdCuenta);

        var txMap = await _db.LedgerTransacciones
            .Where(t => txIds.Contains(t.IdTransaccionLedger))
            .ToDictionaryAsync(t => t.IdTransaccionLedger);

        var dtos = movs.Select(m => new LedgerMovimientoDto
        {
            IdTransaccionLedger = m.IdTransaccionLedger,
            TipoTransaccion     = txMap.TryGetValue(m.IdTransaccionLedger, out var tx) ? tx.TipoTransaccion : "?",
            Naturaleza          = m.Naturaleza,
            CodigoCuenta        = cuentas.TryGetValue(m.IdCuenta, out var c) ? c.Codigo : "?",
            NombreCuenta        = cuentas.TryGetValue(m.IdCuenta, out var c2) ? c2.Nombre : "?",
            Valor               = m.Valor,
            Descripcion         = m.Descripcion,
            FechaMovimiento     = m.FechaMovimiento,
        }).OrderBy(m => m.FechaMovimiento).ToList();

        return new AnticipoLedgerResponse
        {
            Anticipo     = ToAnticipoResponse(anticipo),
            Movimientos  = dtos,
        };
    }

    // ── Admin: diagnóstico de corte por fecha ─────────────────────────────────

    public async Task<List<DiagnosticoCorteItem>> GetDiagnosticoCorteAsync(
        long idConvenio, DateOnly fecha)
    {
        var empleados = await _db.LibranzaEmpleados
            .Where(e => e.IdConvenio == idConvenio && e.Estado == "ACTIVO")
            .ToListAsync();

        var result = new List<DiagnosticoCorteItem>();
        foreach (var emp in empleados)
        {
            CorteVigenteResponse? corte = null;
            string? error = null;
            try { corte = await ObtenerCorteVigenteAsync(emp.IdEmpleado, fecha); }
            catch (Exception ex) { error = ex.Message; }

            result.Add(new DiagnosticoCorteItem
            {
                IdEmpleado       = emp.IdEmpleado,
                Nombres          = $"{emp.Nombres} {emp.Apellidos}".Trim(),
                NumeroDocumento  = emp.NumeroDocumento,
                PeriodicidadPago = emp.PeriodicidadPago,
                CorteVigente     = corte,
                Error            = error,
            });
        }
        return result;
    }

    // ── Admin: asociar empleado a usuario ─────────────────────────────────────

    public async Task AsociarEmpleadoUsuarioAsync(
        long idEmpleado, AsociarEmpleadoUsuarioRequest req, long adminId)
    {
        if (!await _db.LibranzaEmpleados.AnyAsync(e => e.IdEmpleado == idEmpleado))
            throw new KeyNotFoundException($"Empleado {idEmpleado} no encontrado.");

        var existing = await _db.LibranzaEmpleadoUsuarios
            .FirstOrDefaultAsync(a => a.IdEmpleado == idEmpleado && a.Estado == "ACTIVO");

        if (existing is not null)
        {
            // Check no other active employee has this user
            var conflicto = await _db.LibranzaEmpleadoUsuarios
                .AnyAsync(a => a.IdUsuario == req.IdUsuario && a.Estado == "ACTIVO"
                            && a.IdEmpleado != idEmpleado);
            if (conflicto)
                throw new InvalidOperationException(
                    $"El usuario {req.IdUsuario} ya está asociado a otro empleado activo.");
            existing.IdWallet  = req.IdWallet;
            await _db.SaveChangesAsync();
            return;
        }

        _db.LibranzaEmpleadoUsuarios.Add(new LibranzaEmpleadoUsuario
        {
            IdEmpleado        = idEmpleado,
            IdUsuario         = req.IdUsuario,
            IdWallet          = req.IdWallet,
            Estado            = "ACTIVO",
            CreatedAt         = DateTime.UtcNow,
            CreatedByUsuario  = adminId,
        });
        await _db.SaveChangesAsync();
    }

    // ── Lista anticipos del cliente ───────────────────────────────────────────

    public async Task<List<AnticipoResponse>> GetMisAnticiposAsync(long idUsuario)
    {
        var asoc = await _db.LibranzaEmpleadoUsuarios
            .FirstOrDefaultAsync(a => a.IdUsuario == idUsuario && a.Estado == "ACTIVO");
        if (asoc is null) return [];

        var rows = await _db.LibranzaAnticipos
            .Where(a => a.IdEmpleado == asoc.IdEmpleado)
            .OrderByDescending(a => a.IdAnticipo)
            .ToListAsync();
        return rows.Select(ToAnticipoResponse).ToList();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<LedgerCuenta> GetCuentaAsync(string codigo) =>
        await _db.LedgerCuentas.FirstOrDefaultAsync(c =>
            c.IdUnidadNegocio == IdUnidadNegocio && c.Codigo == codigo && c.Estado == "ACTIVA")
        ?? throw new InvalidOperationException($"Cuenta ledger '{codigo}' no encontrada.");

    private static AnticipoResponse ToAnticipoResponse(LibranzaAnticipo a) => new()
    {
        IdAnticipo              = a.IdAnticipo,
        IdConvenio              = a.IdConvenio,
        IdEmpleado              = a.IdEmpleado,
        FechaSimulada           = a.FechaSimulada?.ToString("yyyy-MM-dd"),
        DiaPagoCorte            = a.DiaPagoCorte,
        FechaPagoProgramada     = a.FechaPagoProgramada?.ToString("yyyy-MM-dd"),
        ValorPagoProgramado     = a.ValorPagoProgramado,
        PorcentajeCupo          = a.PorcentajeCupo,
        ValorCupoBase           = a.ValorCupoBase,
        ValorSolicitado         = a.ValorSolicitado,
        ValorComision           = a.ValorComision,
        ValorIva                = a.ValorIva,
        ValorTotalACobrar       = a.ValorTotalACobrar,
        ValorNetoDesembolsado   = a.ValorNetoDesembolsado,
        MomentoCobroComision    = a.MomentoCobroComision,
        Estado                  = a.Estado,
        IdTransaccionLedgerDesembolso = a.IdTransaccionLedgerDesembolso,
        IdTransaccionLedgerPago       = a.IdTransaccionLedgerPago,
        ReferenciaPago          = a.ReferenciaPago,
        FechaSolicitud          = a.FechaSolicitud,
        UpdatedAt               = a.UpdatedAt,
    };
}
