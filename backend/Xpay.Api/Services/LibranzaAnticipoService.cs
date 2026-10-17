using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Exceptions;
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

        if (cortes.Count == 0) throw new InvalidOperationException("El empleado no tiene cortes de pago configurados.");

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

        // After last payment day of the month: next period is for corte 1 of next month
        var primerCorte    = cortes.First(c => c.DiaPago == diasPago[0]);
        var paramFinal     = await ObtenerParametrosAsync(empleado.IdConvenio);
        var pctFinal       = paramFinal?.PorcentajeMaximoCupo ?? convenio.PorcentajeMaximoCupo;
        var cupoBaseFinal  = primerCorte.ValorPagoProgramado * pctFinal / 100m;
        var ultimoDia      = diasPago[^1];
        var mesBase        = fechaSimulada.Month == 12
            ? new DateOnly(fechaSimulada.Year + 1, 1, 1)
            : new DateOnly(fechaSimulada.Year, fechaSimulada.Month + 1, 1);
        var fechaPagoFinal = new DateOnly(mesBase.Year, mesBase.Month, diasPago[0]);
        var cupoUsadoFinal = await CalcularCupoUsadoAsync(idEmpleado, diasPago[0], fechaPagoFinal);

        return new CorteVigenteResponse
        {
            DiaPago             = diasPago[0],
            PeriodoInicio       = ultimoDia + 1,
            PeriodoFin          = diasPago[0] - 1,
            FechaPago           = fechaPagoFinal,
            ValorPagoProgramado = primerCorte.ValorPagoProgramado,
            PorcentajeCupo      = pctFinal,
            CupoBase            = cupoBaseFinal,
            CupoUsado           = cupoUsadoFinal,
            CupoDisponible      = Math.Max(0, cupoBaseFinal - cupoUsadoFinal),
            FechaSimulada       = fechaSimulada.ToString("yyyy-MM-dd"),
            EsDiaPago           = false,
        };
    }

    private static readonly HashSet<string> EstadosActivos =
        ["DESEMBOLSADO", "PENDIENTE", "APROBADO", "EN_PROCESO", "CREADO"];

    private async Task<decimal> CalcularCupoUsadoAsync(long idEmpleado, int diaPago, DateOnly fechaPago)
    {
        return await _db.LibranzaAnticipos
            .Where(a => a.IdEmpleado         == idEmpleado
                     && a.DiaPagoCorte        == diaPago
                     && a.FechaPagoProgramada == fechaPago
                     && EstadosActivos.Contains(a.Estado))
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

        // Anticipos del período vigente (todos los estados activos que consumen cupo)
        var anticiposActivos = corte != null
            ? await _db.LibranzaAnticipos
                .Where(a => a.IdEmpleado         == empleado.IdEmpleado
                         && a.DiaPagoCorte        == corte.DiaPago
                         && a.FechaPagoProgramada == corte.FechaPago
                         && EstadosActivos.Contains(a.Estado))
                .OrderByDescending(a => a.FechaSolicitud)
                .ToListAsync()
            : await _db.LibranzaAnticipos
                .Where(a => a.IdEmpleado == empleado.IdEmpleado && EstadosActivos.Contains(a.Estado))
                .OrderByDescending(a => a.FechaSolicitud)
                .ToListAsync();

        var historial = await _db.LibranzaAnticipos
            .Where(a => a.IdEmpleado == empleado.IdEmpleado && !EstadosActivos.Contains(a.Estado))
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

    // P0 anticipo - la modalidad ANTICIPADO produce un asiento descuadrado
    // (DR = S+C+I, CR = (S-C-I)+C+I = S) y cobra la comisión dos veces. Queda
    // FAIL-CLOSED en todo ambiente hasta que exista una definición contable y
    // de producto aprobada: ningún desembolso se ejecuta con ella.
    internal const string MomentoCobroHabilitado = "VENCIDO";
    internal const string MensajeAnticipadoDeshabilitado =
        "La modalidad de cobro ANTICIPADO está deshabilitada. Solo se permite VENCIDO.";

    // P0 anticipo - patrón XPAY-712 adaptado a libranza: Idempotency-Key
    // durable (wallet_idempotencia), lock pesimista del empleado (serializa
    // el cálculo de cupo de TODAS las solicitudes del mismo empleado) y de
    // wallet_saldos (mismo lock que la reserva Bre-B: sin lost update), una
    // sola transacción y asiento balanceado.
    public async Task<IdempotentOperationResult<AnticipoResponse>> SolicitarAnticipoAsync(
        long idUsuario, SolicitarAnticipoRequest req, Guid idempotencyKey)
    {
        if (idempotencyKey == Guid.Empty)
            throw new ArgumentException("Idempotency-Key debe ser un identificador válido.");
        if (req.ValorSolicitado <= 0)
            throw new InvalidOperationException("El valor solicitado debe ser mayor a cero.");

        var fechaSimulada = req.FechaSimulada is not null
            ? DateOnly.Parse(req.FechaSimulada)
            : DateOnly.FromDateTime(DateTime.UtcNow);

        const string endpoint = IdempotencyEndpoints.LibranzaAnticipoDesembolso;
        var requestHash = IdempotencyHashHelper.ComputeAnticipoLibranzaHash(
            idUsuario, req.ValorSolicitado, req.FechaSimulada);

        await using var tx = await _db.Database.BeginTransactionAsync();
        var rolledBack = false;
        try
        {
            // Reserva EN_PROCESO: primera escritura de la transacción. La
            // restricción UNIQUE (id_usuario, endpoint, idempotency_key) arbitra
            // la concurrencia de la misma clave.
            var idem = IdempotencyStore.NuevaReserva(idUsuario, endpoint, idempotencyKey, requestHash);
            _db.WalletIdempotencias.Add(idem);
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (Exception ex) when (SqlExceptionHelper.IsUniqueViolation(ex))
            {
                await tx.RollbackAsync();
                rolledBack = true;
                _db.ChangeTracker.Clear();
                try
                {
                    return await IdempotencyStore.ResolverReplayAsync<AnticipoResponse>(
                        _db, idUsuario, endpoint, idempotencyKey, requestHash);
                }
                catch (InvalidOperationException exReplay)
                {
                    // P0 F.3 - replay de clave existente cuya respuesta no puede
                    // reconstruirse: resultado INCIERTO (503), nunca rechazo
                    // definitivo (400). El conflicto de hash es
                    // IdempotencyConflictException (no capturada aqui -> 409).
                    _logger.LogError(
                        exReplay,
                        "LIBRANZA_ANTICIPO_REPLAY_IRRECUPERABLE idUsuario={Id}",
                        idUsuario);

                    throw new IdempotencyUnavailableException(
                        "No fue posible confirmar el resultado de la operación con esta clave de idempotencia.");
                }
            }

            var asoc = await _db.LibranzaEmpleadoUsuarios
                .FirstOrDefaultAsync(a => a.IdUsuario == idUsuario && a.Estado == "ACTIVO")
                ?? throw new UnauthorizedAccessException("No tiene un perfil de empleado de libranza asociado.");

            if (asoc.IdWallet is null)
                throw new InvalidOperationException("El empleado no tiene wallet asociada para el desembolso.");

            // Lock pesimista del empleado: dos solicitudes concurrentes del mismo
            // empleado se serializan aquí; la segunda recalcula el cupo usado YA
            // incluyendo el anticipo confirmado por la primera.
            var empleado = await _db.LibranzaEmpleados
                .FromSqlInterpolated($"SELECT * FROM libranza_empleados WITH (UPDLOCK, ROWLOCK) WHERE id_empleado = {asoc.IdEmpleado}")
                .FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("Empleado no encontrado.");
            if (empleado.Estado != "ACTIVO")
                throw new InvalidOperationException("El empleado no está activo.");

            var convenio = await _db.LibranzaEmpresasConvenio.FindAsync(empleado.IdConvenio)
                ?? throw new InvalidOperationException("Convenio no encontrado.");
            if (convenio.Estado != "ACTIVO")
                throw new InvalidOperationException("El convenio no está activo.");

            var param = await ObtenerParametrosAsync(empleado.IdConvenio)
                ?? throw new InvalidOperationException("El convenio no tiene parámetros activos.");

            if (!string.Equals(param.MomentoCobroComision, MomentoCobroHabilitado, StringComparison.Ordinal))
                throw new InvalidOperationException(MensajeAnticipadoDeshabilitado);

            if (req.ValorSolicitado < param.MontoMinimoAnticipo)
                throw new InvalidOperationException(
                    $"El valor solicitado ({req.ValorSolicitado:0.00}) es inferior al mínimo de anticipo ({param.MontoMinimoAnticipo:0.00}).");

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

            // VENCIDO: se desembolsa el capital completo; comisión e IVA se
            // reconocen al recaudar (ver LibranzaRecaudoAplicacionService).
            var netoDesembolsado = req.ValorSolicitado;

            var cuentaCartera    = await GetCuentaAsync(CodAnticipoCartera);
            var cuentaObligacion = await GetCuentaAsync(CodObligacion);

            // La wallet debe pertenecer a la persona del usuario autenticado
            // (defensa en profundidad sobre el vínculo empleado ↔ usuario).
            var idPersonaUsuario = await _db.Usuarios
                .Where(u => u.IdUsuario == idUsuario)
                .Select(u => (long?)u.IdPersona)
                .FirstOrDefaultAsync();
            var idWallet = asoc.IdWallet.Value;
            var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.IdWallet == idWallet && w.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("La wallet del empleado no está activa.");
            if (idPersonaUsuario is null || wallet.IdPersona != idPersonaUsuario || wallet.TipoWallet != "PERSONA")
                throw new InvalidOperationException("La wallet asociada no pertenece al usuario.");

            // Mismo lock que BrebPaymentService (reserva/liquidación/rechazo) y
            // que Cartera: ninguna escritura concurrente del saldo se pierde.
            var saldo = await _db.WalletSaldos
                .FromSqlInterpolated($"SELECT * FROM wallet_saldos WITH (UPDLOCK, ROWLOCK) WHERE id_wallet = {idWallet}")
                .FirstOrDefaultAsync()
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
                MomentoCobroComision   = MomentoCobroHabilitado,
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

            // Ledger (VENCIDO): DR 130104 cartera anticipo / CR 210101 obligación
            // wallet, ambos por el capital desembolsado.
            var movimientos = new List<LedgerMovimiento>
            {
                new() {
                    IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                    IdCuenta    = cuentaCartera.IdCuenta,
                    Naturaleza  = "D",
                    Valor       = netoDesembolsado,
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
                    Valor       = netoDesembolsado,
                    Concepto    = "ANTICIPO_DESEMBOLSO",
                    ReferenciaTipo = "libranza_anticipos",
                    ReferenciaId   = anticipo.IdAnticipo,
                    Descripcion = "Obligación wallet usuario por anticipo.",
                    FechaMovimiento = now,
                },
            };

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

            var totalD = movimientos.Where(m => m.Naturaleza == "D").Sum(m => m.Valor);
            var totalC = movimientos.Where(m => m.Naturaleza == "C").Sum(m => m.Valor);
            if (totalD != totalC)
                throw new InvalidOperationException($"Ledger desbalanceado: DR={totalD} CR={totalC}.");

            // Respuesta ORIGINAL persistida para el replay de la misma clave.
            var resultado     = ToAnticipoResponse(anticipo);
            var respuestaJson = System.Text.Json.JsonSerializer.Serialize(resultado);
            if (respuestaJson.Length > 1000)
                throw new InvalidOperationException(
                    "La respuesta del desembolso excede el límite de almacenamiento de idempotencia.");
            IdempotencyStore.MarcarCompletada(idem, httpStatus: 200,
                idRecurso: anticipo.IdAnticipo,
                idTransaccionLedger: ledgerTx.IdTransaccionLedger,
                respuestaJson);

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation("LIBRANZA_ANTICIPO_DESEMBOLSO: id={Id} empleado={Emp} valor={Val} ledger={Led}",
                anticipo.IdAnticipo, empleado.IdEmpleado, netoDesembolsado, ledgerTx.IdTransaccionLedger);

            return new IdempotentOperationResult<AnticipoResponse>(resultado, Replayed: false);
        }
        catch
        {
            if (!rolledBack)
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

        // P0 anticipo - herramienta SOLO QA (el controller la bloquea en
        // productivo). Nunca compite con el recaudo real: si algún anticipo
        // del corte ya está en un lote vigente o aplicado, no se simula nada.
        var idsAnticipos = anticipos.Select(a => a.IdAnticipo).ToList();
        if (await _db.LibranzaLoteRecaudoDetalles.AnyAsync(d =>
                idsAnticipos.Contains(d.IdAnticipo)
                && (d.Estado == LibranzaLoteEstados.DetalleVigente || d.Estado == LibranzaLoteEstados.DetalleAplicado)))
            throw new InvalidOperationException(
                "Hay anticipos de este corte incluidos en un lote de recaudo; el pago simulado no aplica.");

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

    // P0 anticipo - el vínculo determina a QUIÉN se acredita dinero real, así
    // que se valida antes de crearlo o cambiarlo:
    //   · el empleado existe, está ACTIVO y pertenece al convenio esperado;
    //   · el documento del empleado corresponde a la persona del usuario
    //     (y al documento verificado por KYC, si existe);
    //   · la wallet es la wallet PERSONA ACTIVA de esa misma persona;
    //   · sin vínculos cruzados: un empleado ↔ un usuario, en ambos sentidos.
    // Los mensajes y logs nunca incluyen documentos.
    public async Task AsociarEmpleadoUsuarioAsync(
        long idEmpleado, AsociarEmpleadoUsuarioRequest req, long adminId)
    {
        if (req.IdConvenio is null or <= 0)
            throw new InvalidOperationException("idConvenio es obligatorio para vincular un empleado.");
        if (req.IdUsuario <= 0)
            throw new InvalidOperationException("idUsuario es obligatorio.");

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var empleado = await _db.LibranzaEmpleados
                .FromSqlInterpolated($"SELECT * FROM libranza_empleados WITH (UPDLOCK, ROWLOCK) WHERE id_empleado = {idEmpleado}")
                .FirstOrDefaultAsync()
                ?? throw new KeyNotFoundException($"Empleado {idEmpleado} no encontrado.");
            if (empleado.IdConvenio != req.IdConvenio.Value)
                throw new InvalidOperationException("El empleado no pertenece al convenio indicado.");
            if (empleado.Estado != "ACTIVO")
                throw new InvalidOperationException("El empleado no está activo.");

            var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.IdUsuario == req.IdUsuario)
                ?? throw new KeyNotFoundException($"Usuario {req.IdUsuario} no encontrado.");
            if (usuario.Estado != "ACTIVO")
                throw new InvalidOperationException("El usuario no está activo.");

            var persona = await _db.Personas.FirstOrDefaultAsync(p => p.IdPersona == usuario.IdPersona)
                ?? throw new InvalidOperationException("El usuario no tiene persona asociada.");
            if (!DocumentoCoincide(empleado.TipoDocumento, empleado.NumeroDocumento, persona.TipoDocumento, persona.NumeroDocumento))
                throw new InvalidOperationException("El documento del empleado no corresponde al usuario indicado.");
            if (!string.IsNullOrWhiteSpace(persona.NumeroDocumentoVerificado)
                && NormalizarDocumento(persona.NumeroDocumentoVerificado) != NormalizarDocumento(empleado.NumeroDocumento))
                throw new InvalidOperationException("El documento del empleado no corresponde al documento verificado del usuario.");

            var wallet = req.IdWallet is long idWallet
                ? await _db.Wallets.FirstOrDefaultAsync(w => w.IdWallet == idWallet)
                : await _db.Wallets.FirstOrDefaultAsync(w =>
                    w.IdPersona == persona.IdPersona && w.TipoWallet == "PERSONA" && w.Estado == "ACTIVA");
            if (wallet is null || wallet.IdPersona != persona.IdPersona
                || wallet.TipoWallet != "PERSONA" || wallet.Estado != "ACTIVA")
                throw new InvalidOperationException("La wallet indicada no es una wallet activa del usuario.");

            var existing = await _db.LibranzaEmpleadoUsuarios
                .FirstOrDefaultAsync(a => a.IdEmpleado == idEmpleado && a.Estado == "ACTIVO");
            if (existing is not null && existing.IdUsuario != req.IdUsuario)
                throw new InvalidOperationException(
                    "El empleado ya está vinculado a otro usuario. Debe desvincularse antes de vincular uno nuevo.");

            var usuarioEnOtroEmpleado = await _db.LibranzaEmpleadoUsuarios
                .AnyAsync(a => a.IdUsuario == req.IdUsuario && a.Estado == "ACTIVO" && a.IdEmpleado != idEmpleado);
            if (usuarioEnOtroEmpleado)
                throw new InvalidOperationException("El usuario ya está vinculado a otro empleado activo.");

            if (existing is not null)
            {
                existing.IdWallet = wallet.IdWallet;
            }
            else
            {
                _db.LibranzaEmpleadoUsuarios.Add(new LibranzaEmpleadoUsuario
                {
                    IdEmpleado        = idEmpleado,
                    IdUsuario         = req.IdUsuario,
                    IdWallet          = wallet.IdWallet,
                    Estado            = "ACTIVO",
                    CreatedAt         = DateTime.UtcNow,
                    CreatedByUsuario  = adminId,
                });
            }

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (Exception ex) when (SqlExceptionHelper.IsUniqueViolation(ex))
            {
                throw new InvalidOperationException("El empleado o el usuario ya tienen un vínculo activo.");
            }
            await tx.CommitAsync();

            _logger.LogInformation("LIBRANZA_EMPLEADO_VINCULADO: empleado={Emp} usuario={Usr} wallet={Wal} admin={Admin}",
                idEmpleado, req.IdUsuario, wallet.IdWallet, adminId);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    internal static string NormalizarDocumento(string? numero) =>
        new string((numero ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    internal static bool DocumentoCoincide(string? tipoEmpleado, string? numeroEmpleado, string? tipoPersona, string? numeroPersona)
    {
        var tE = (tipoEmpleado ?? string.Empty).Trim().ToUpperInvariant();
        var tP = (tipoPersona ?? string.Empty).Trim().ToUpperInvariant();
        var nE = NormalizarDocumento(numeroEmpleado);
        var nP = NormalizarDocumento(numeroPersona);
        return tE.Length > 0 && nE.Length > 0 && tE == tP && nE == nP;
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
