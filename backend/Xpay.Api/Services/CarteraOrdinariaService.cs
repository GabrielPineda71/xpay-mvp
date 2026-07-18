using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public class CarteraOrdinariaService(XpayDbContext db, PagoQrService pagoQrService, ILogger<CarteraOrdinariaService> logger)
{
    private const string CodCarteraOrdinaria      = "130105"; // Cartera Ordinaria - Avance Wallet (ACTIVO, D)
    private const string CodCarteraCompraComercio = "130106"; // Cartera Ordinaria - Compra Comercio (ACTIVO, D)
    private const string CodObligacionWallet      = "210101"; // Obligación Wallet Usuarios (PASIVO, C)
    private const string CodContingenciaQr        = "210201"; // Ventas QR en Contingencia Comercios (PASIVO, C)
    private const string CodIngresoInteres        = "410301"; // Ingreso Intereses Cartera Ordinaria (INGRESO, C)
    private const string CodIngresoAval           = "410302"; // Ingreso Aval Cartera Ordinaria (INGRESO, C)
    private const string CodIngresoAdmin          = "410303"; // Ingreso Administración Cartera Ordinaria (INGRESO, C)
    private const string CodIvaCarteraPagar       = "240803"; // IVA Cartera Ordinaria por Pagar (PASIVO, C)
    private const long IdUnidadNegocio = 1;

    // ── Parámetros de utilización ──────────────────────────────────────
    public async Task<List<ParametroUtilizacionDto>> GetParametrosAsync()
    {
        var rows = await db.CarteraParametrosUtilizacion
            .OrderBy(x => x.TipoUtilizacion)
            .ToListAsync();
        return rows.Select(ToDto).ToList();
    }

    public async Task<ParametroUtilizacionDto?> GetParametroByTipoAsync(string tipo)
    {
        var row = await db.CarteraParametrosUtilizacion
            .FirstOrDefaultAsync(x => x.TipoUtilizacion == tipo && x.Estado == "ACTIVO");
        return row is null ? null : ToDto(row);
    }

    public async Task<ParametroUtilizacionDto> UpsertParametroAsync(string tipo, UpsertParametroUtilizacionRequest req, long idUsuario)
    {
        var row = await db.CarteraParametrosUtilizacion
            .FirstOrDefaultAsync(x => x.TipoUtilizacion == tipo);
        if (row is null)
        {
            row = new CarteraParametroUtilizacion { TipoUtilizacion = tipo, CreatedAt = DateTime.UtcNow, CreatedByUsuario = idUsuario };
            db.CarteraParametrosUtilizacion.Add(row);
        }
        row.TasaEmv          = req.TasaEmv;
        row.PorcAval         = req.PorcAval;
        row.PorcAdmin        = req.PorcAdmin;
        row.AplicaIva        = req.AplicaIva;
        row.PorcIva          = req.PorcIva;
        row.PlazoMin         = req.PlazoMin;
        row.PlazoMax         = req.PlazoMax;
        row.Frecuencia       = req.Frecuencia;
        row.MontoMin         = req.MontoMin;
        row.MontoMax         = req.MontoMax;
        row.UpdatedAt        = DateTime.UtcNow;
        row.UpdatedByUsuario = idUsuario;
        await db.SaveChangesAsync();
        return ToDto(row);
    }

    // ── Gastos de cobranza ─────────────────────────────────────────────
    public async Task<List<GastosCobranzaDto>> GetGastosCobranzaAsync()
    {
        var rows = await db.CarteraParametrosGastosCobranza
            .OrderBy(x => x.DiasDesde)
            .ToListAsync();
        return rows.Select(ToGastoDto).ToList();
    }

    public async Task<GastosCobranzaDto> UpsertGastoCobranzaAsync(long? id, UpsertGastosCobranzaRequest req)
    {
        CarteraParametroGastosCobranza row;
        if (id.HasValue)
        {
            row = await db.CarteraParametrosGastosCobranza.FindAsync(id.Value)
                  ?? throw new KeyNotFoundException("Gasto no encontrado");
        }
        else
        {
            row = new CarteraParametroGastosCobranza { CreatedAt = DateTime.UtcNow };
            db.CarteraParametrosGastosCobranza.Add(row);
        }
        row.DiasDesde   = req.DiasDesde;
        row.DiasHasta   = req.DiasHasta;
        row.TipoCobro   = req.TipoCobro;
        row.ValorCobro  = req.ValorCobro;
        row.Descripcion = req.Descripcion;
        row.UpdatedAt   = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return ToGastoDto(row);
    }

    // ── Política de crédito ────────────────────────────────────────────
    public async Task<PoliticaCreditoDto?> GetPoliticaVigenteAsync()
    {
        var row = await db.CarteraPoliticasCredito
            .Where(x => x.Estado == "ACTIVO")
            .OrderByDescending(x => x.VigenteDesde)
            .FirstOrDefaultAsync();
        return row is null ? null : ToPoliticaDto(row);
    }

    public async Task<PoliticaCreditoDto> UpsertPoliticaAsync(UpsertPoliticaCreditoRequest req, long idUsuario)
    {
        var row = await db.CarteraPoliticasCredito
            .Where(x => x.Estado == "ACTIVO")
            .OrderByDescending(x => x.VigenteDesde)
            .FirstOrDefaultAsync();
        if (row is null)
        {
            row = new CarteraPoliticaCredito { CreatedAt = DateTime.UtcNow, CreatedByUsuario = idUsuario, VigenteDesde = DateTime.UtcNow };
            db.CarteraPoliticasCredito.Add(row);
        }
        row.ScoreDatacreditoMinimo = req.ScoreDatacreditoMinimo;
        row.RequiereVeriff         = req.RequiereVeriff;
        row.CupoMinimo             = req.CupoMinimo;
        row.CupoMaximo             = req.CupoMaximo;
        row.EdadMinima             = req.EdadMinima;
        row.EdadMaxima             = req.EdadMaxima;
        row.UpdatedAt              = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return ToPoliticaDto(row);
    }

    // ── Cupos ordinarios (admin) ────────────────────────────────────────
    public async Task<List<CupoOrdinarioDto>> GetCuposAsync()
    {
        var cupos = await db.CarteraCuposOrdinarios.ToListAsync();
        var uIds  = cupos.Select(x => x.IdUsuario).Distinct().ToList();
        var users = await db.Usuarios
            .Where(u => uIds.Contains(u.IdUsuario))
            .ToDictionaryAsync(u => u.IdUsuario, u => u.NombreUsuario);
        return cupos.Select(c => ToCupoDto(c, users.GetValueOrDefault(c.IdUsuario, ""))).ToList();
    }

    public async Task<CupoOrdinarioDto> AsignarCupoAsync(AsignarCupoRequest req, long idAdmin)
    {
        var user = await db.Usuarios.FindAsync(req.IdUsuario)
                   ?? throw new KeyNotFoundException("Usuario no encontrado");
        var wallet = await db.Wallets
            .FirstOrDefaultAsync(w => w.IdPersona == user.IdPersona && w.TipoWallet == "PERSONA")
            ?? throw new InvalidOperationException("Wallet no encontrada");

        var row = await db.CarteraCuposOrdinarios.FirstOrDefaultAsync(x => x.IdUsuario == req.IdUsuario);
        if (row is null)
        {
            row = new CarteraCupoOrdinario { IdUsuario = req.IdUsuario, IdWallet = wallet.IdWallet, CreatedAt = DateTime.UtcNow, FechaAprobacion = DateTime.UtcNow };
            db.CarteraCuposOrdinarios.Add(row);
        }
        row.CupoAprobado       = req.CupoAprobado;
        row.FechaVencimiento   = req.FechaVencimiento;
        row.AprobadoPorUsuario = idAdmin;
        row.Observaciones      = req.Observaciones;
        row.UpdatedAt          = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return ToCupoDto(row, user.NombreUsuario);
    }

    // ── Mi cupo (vista usuario) ────────────────────────────────────────
    public async Task<MiCupoOrdinarioDto?> GetMiCupoAsync(long idUsuario)
    {
        var cupo = await db.CarteraCuposOrdinarios
            .FirstOrDefaultAsync(x => x.IdUsuario == idUsuario && x.Estado == "ACTIVO");
        if (cupo is null) return null;
        return new MiCupoOrdinarioDto(
            cupo.IdCupo,
            cupo.IdWallet,
            cupo.CupoAprobado,
            cupo.CupoUsado,
            cupo.CupoAprobado - cupo.CupoUsado,
            cupo.Estado,
            cupo.FechaAprobacion,
            cupo.FechaVencimiento);
    }

    // ── Simulador de amortización (French) ─────────────────────────────
    public async Task<SimulacionResultDto> SimularUtilizacionAsync(SimularUtilizacionRequest req, long idUsuario)
    {
        var param = await GetParametroValidadoAsync(req);
        var (frecuencia, n, cuotas, sumInteres, totalAval, totalAdmin, totalIva, valorCuota, valorTotalPagar) =
            CalcularAmortizacion(param, req.ValorCapital, req.PlazoMeses, req.Frecuencia);

        return new SimulacionResultDto(
            TipoUtilizacion:     req.TipoUtilizacion,
            ValorCapital:        req.ValorCapital,
            TasaEmv:             param.TasaEmv,
            PorcAval:            param.PorcAval,
            PorcAdmin:           param.PorcAdmin,
            AplicaIva:           param.AplicaIva,
            PorcIva:             param.PorcIva,
            PlazoMeses:          req.PlazoMeses,
            Frecuencia:          frecuencia,
            TotalCuotas:         n,
            ValorCuota:          valorCuota,
            ValorTotalIntereses: sumInteres,
            ValorTotalAval:      totalAval,
            ValorTotalAdmin:     totalAdmin,
            ValorTotalIva:       totalIva,
            ValorTotalPagar:     valorTotalPagar,
            Cuotas:              cuotas);
    }

    // ── Confirmación real: AVANCE_WALLET ────────────────────────────────
    public async Task<ConfirmacionUtilizacionDto> ConfirmarAvanceWalletAsync(SimularUtilizacionRequest req, long idUsuario)
    {
        if (!string.Equals(req.TipoUtilizacion, "AVANCE_WALLET", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Solo se puede confirmar utilización de tipo AVANCE_WALLET en esta fase");

        // Todo lo que sigue se lee y revalida dentro de la transacción — nunca se confía en
        // valores ya calculados por el cliente (simulación previa), solo en TipoUtilizacion/
        // ValorCapital/PlazoMeses/Frecuencia como entrada cruda a recalcular en el servidor.
        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            var param = await GetParametroValidadoAsync(req);

            // ── Lock pesimista sobre el cupo del usuario ────────────────────
            // WITH (UPDLOCK, ROWLOCK) toma un lock exclusivo de actualización sobre esa fila
            // hasta que esta transacción haga COMMIT o ROLLBACK. Si una segunda confirmación
            // concurrente del mismo usuario intenta leer el mismo cupo, SQL Server la bloquea
            // hasta que esta termine; al continuar, esa segunda lectura ve el cupo_usado ya
            // actualizado por la primera, por lo que la validación de cupo disponible que sigue
            // no puede ser burlada por una carrera entre dos requests concurrentes.
            var cupo = await db.CarteraCuposOrdinarios
                .FromSqlInterpolated($"SELECT * FROM cartera_cupos_ordinarios WITH (UPDLOCK, ROWLOCK) WHERE id_usuario = {idUsuario}")
                .FirstOrDefaultAsync()
                ?? throw new KeyNotFoundException("No tienes un cupo ordinario asignado");
            if (cupo.Estado != "ACTIVO")
                throw new InvalidOperationException("Tu cupo ordinario no está activo");
            if (cupo.FechaVencimiento.HasValue && cupo.FechaVencimiento.Value < DateTime.UtcNow)
                throw new InvalidOperationException("Tu cupo ordinario está vencido");

            decimal cupoDisponible = cupo.CupoAprobado - cupo.CupoUsado;
            if (req.ValorCapital > cupoDisponible)
                throw new InvalidOperationException($"El valor solicitado supera tu cupo disponible ({cupoDisponible:N0})");

            var wallet = await db.Wallets
                .FirstOrDefaultAsync(w => w.IdWallet == cupo.IdWallet && w.Estado == "ACTIVA")
                ?? throw new InvalidOperationException("La wallet asociada al cupo no está activa");

            // ── Lock pesimista sobre el saldo de la wallet ──────────────────
            // Misma razón que el cupo: serializa desembolsos concurrentes sobre la misma wallet
            // para que "SaldoAntes"/"SaldoDespues" y el crédito aplicado sean siempre exactos,
            // sin condición de carrera "leer-calcular-escribir" entre dos transacciones.
            var saldo = await db.WalletSaldos
                .FromSqlInterpolated($"SELECT * FROM wallet_saldos WITH (UPDLOCK, ROWLOCK) WHERE id_wallet = {wallet.IdWallet}")
                .FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("La wallet no tiene registro de saldo");

            var (frecuencia, n, cuotasSimuladas, sumInteres, totalAval, totalAdmin, totalIva, valorCuota, valorTotalPagar) =
                CalcularAmortizacion(param, req.ValorCapital, req.PlazoMeses, req.Frecuencia);

            var cuentaCartera    = await GetCuentaLedgerAsync(CodCarteraOrdinaria);
            var cuentaObligacion = await GetCuentaLedgerAsync(CodObligacionWallet);

            var now = DateTime.UtcNow;

            var utilizacion = new CarteraUtilizacion
            {
                IdCupo               = cupo.IdCupo,
                IdUsuario            = idUsuario,
                IdWallet             = wallet.IdWallet,
                TipoUtilizacion      = "AVANCE_WALLET",
                ValorCapital         = req.ValorCapital,
                TasaEmv              = param.TasaEmv,
                PorcAval             = param.PorcAval,
                PorcAdmin            = param.PorcAdmin,
                AplicaIva            = param.AplicaIva,
                PorcIva              = param.PorcIva,
                PlazoMeses           = req.PlazoMeses,
                Frecuencia           = frecuencia,
                TotalCuotas          = n,
                ValorCuota           = valorCuota,
                ValorTotalAval       = totalAval,
                ValorTotalAdmin      = totalAdmin,
                ValorTotalIva        = totalIva,
                ValorTotalIntereses  = sumInteres,
                ValorTotalPagar      = valorTotalPagar,
                Estado               = "DESEMBOLSADO",
                FechaSolicitud       = now,
                FechaDesembolso      = now,
                CreatedAt            = now,
                CreatedByUsuario     = idUsuario,
            };
            db.CarteraUtilizaciones.Add(utilizacion);
            await db.SaveChangesAsync();

            var cuotas = cuotasSimuladas.Select(c => new CarteraCuota
            {
                IdUtilizacion        = utilizacion.IdUtilizacion,
                NumeroCuota          = c.NumeroCuota,
                FechaVencimiento     = DateOnly.Parse(c.FechaVencimiento),
                ValorCapital         = c.ValorCapital,
                ValorInteres         = c.ValorInteres,
                ValorAval            = c.ValorAval,
                ValorAdmin           = c.ValorAdmin,
                ValorIva             = c.ValorIva,
                ValorTotal           = c.ValorTotal,
                SaldoCapitalAntes    = c.SaldoCapitalAntes,
                SaldoCapitalDespues  = c.SaldoCapitalDespues,
                SaldoCuota           = c.ValorTotal,
                Estado               = "PENDIENTE",
                CreatedAt            = now,
            }).ToList();
            db.CarteraCuotas.AddRange(cuotas);

            var ledgerTx = new LedgerTransaccion
            {
                IdUnidadNegocio  = IdUnidadNegocio,
                TipoTransaccion  = "CARTERA_AVANCE_WALLET_DESEMBOLSO",
                ReferenciaTipo   = "cartera_utilizaciones",
                ReferenciaId     = utilizacion.IdUtilizacion,
                Descripcion      = $"Desembolso avance wallet #{utilizacion.IdUtilizacion} usuario #{idUsuario}",
                ValorTotal       = req.ValorCapital,
                Estado           = "REGISTRADA",
                CreadoPor        = idUsuario,
                FechaTransaccion = now,
            };
            db.LedgerTransacciones.Add(ledgerTx);
            await db.SaveChangesAsync();

            var movimientos = new List<LedgerMovimiento>
            {
                new() {
                    IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                    IdCuenta       = cuentaCartera.IdCuenta,
                    Naturaleza     = "D",
                    Valor          = req.ValorCapital,
                    Concepto       = "CARTERA_AVANCE_WALLET",
                    ReferenciaTipo = "cartera_utilizaciones",
                    ReferenciaId   = utilizacion.IdUtilizacion,
                    Descripcion    = "Cartera ordinaria — avance a wallet por cobrar.",
                    FechaMovimiento = now,
                },
                new() {
                    IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                    IdCuenta       = cuentaObligacion.IdCuenta,
                    Naturaleza     = "C",
                    Valor          = req.ValorCapital,
                    Concepto       = "CARTERA_AVANCE_WALLET",
                    ReferenciaTipo = "cartera_utilizaciones",
                    ReferenciaId   = utilizacion.IdUtilizacion,
                    Descripcion    = "Obligación wallet usuario por avance de cartera ordinaria.",
                    FechaMovimiento = now,
                },
            };
            db.LedgerMovimientos.AddRange(movimientos);

            var saldoAntes   = saldo.SaldoDisponible;
            var saldoDespues = saldoAntes + req.ValorCapital;
            db.WalletMovimientos.Add(new WalletMovimiento
            {
                IdWallet            = wallet.IdWallet,
                IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                TipoMovimiento      = "CARTERA_AVANCE_WALLET",
                Naturaleza          = "C",
                Valor               = req.ValorCapital,
                SaldoAntes          = saldoAntes,
                SaldoDespues        = saldoDespues,
                Descripcion         = $"Avance de cartera ordinaria #{utilizacion.IdUtilizacion}",
                ReferenciaTipo      = "cartera_utilizaciones",
                ReferenciaId        = utilizacion.IdUtilizacion,
                Estado              = "APLICADO",
                CreadoPor           = idUsuario,
                FechaMovimiento     = now,
            });

            saldo.SaldoDisponible    = saldoDespues;
            saldo.FechaActualizacion = now;

            cupo.CupoUsado = cupo.CupoUsado + req.ValorCapital;
            cupo.UpdatedAt = now;

            await db.SaveChangesAsync();

            var totalD = movimientos.Where(m => m.Naturaleza == "D").Sum(m => m.Valor);
            var totalC = movimientos.Where(m => m.Naturaleza == "C").Sum(m => m.Valor);
            if (totalD != totalC)
                throw new InvalidOperationException($"Ledger desbalanceado: DR={totalD} CR={totalC}.");

            await tx.CommitAsync();

            return new ConfirmacionUtilizacionDto(
                IdUtilizacion:       utilizacion.IdUtilizacion,
                TipoUtilizacion:     utilizacion.TipoUtilizacion,
                ValorCapital:        utilizacion.ValorCapital,
                Estado:              utilizacion.Estado,
                FechaDesembolso:     utilizacion.FechaDesembolso!.Value,
                NuevoSaldoWallet:    saldoDespues,
                NuevoCupoDisponible: cupo.CupoAprobado - cupo.CupoUsado,
                Cuotas:              cuotasSimuladas);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // ── Mis créditos (vista usuario) ─────────────────────────────────────
    public async Task<List<MiCreditoDto>> GetMisCreditosAsync(long idUsuario)
    {
        var utilizaciones = await db.CarteraUtilizaciones
            .Where(u => u.IdUsuario == idUsuario)
            .OrderByDescending(u => u.IdUtilizacion)
            .ToListAsync();
        if (utilizaciones.Count == 0) return new List<MiCreditoDto>();

        var idsUtilizacion = utilizaciones.Select(u => u.IdUtilizacion).ToList();
        var cuotas = await db.CarteraCuotas
            .Where(c => idsUtilizacion.Contains(c.IdUtilizacion))
            .ToListAsync();

        var result = new List<MiCreditoDto>();
        foreach (var u in utilizaciones)
        {
            var cuotasCredito = cuotas.Where(c => c.IdUtilizacion == u.IdUtilizacion)
                .OrderBy(c => c.FechaVencimiento).ThenBy(c => c.NumeroCuota).ToList();
            var saldoPendiente = cuotasCredito.Sum(c => c.SaldoCuota);
            var cuotasPagadas  = cuotasCredito.Count(c => c.Estado == "PAGADA");
            var proxima        = cuotasCredito.FirstOrDefault(c => c.SaldoCuota > 0);

            result.Add(new MiCreditoDto(
                IdUtilizacion:     u.IdUtilizacion,
                NroCredito:        u.IdUtilizacion,
                TipoUtilizacion:   u.TipoUtilizacion,
                ValorCapital:      u.ValorCapital,
                Estado:            u.Estado,
                FechaDesembolso:   u.FechaDesembolso,
                TotalCuotas:       u.TotalCuotas,
                CuotasPagadas:     cuotasPagadas,
                SaldoPendiente:    saldoPendiente,
                ProximaCuota:      proxima?.NumeroCuota,
                ValorProximaCuota: proxima?.SaldoCuota));
        }
        return result;
    }

    public async Task<List<CuotaDetalleDto>> GetCuotasCreditoAsync(long idUtilizacion, long idUsuario)
    {
        // WHERE por IdUsuario en la misma consulta — evita revelar si el crédito existe para otro usuario.
        var utilizacion = await db.CarteraUtilizaciones
            .FirstOrDefaultAsync(u => u.IdUtilizacion == idUtilizacion && u.IdUsuario == idUsuario)
            ?? throw new KeyNotFoundException("Crédito no encontrado");

        var cuotas = await db.CarteraCuotas
            .Where(c => c.IdUtilizacion == utilizacion.IdUtilizacion)
            .OrderBy(c => c.FechaVencimiento).ThenBy(c => c.NumeroCuota)
            .ToListAsync();

        return cuotas.Select(c => new CuotaDetalleDto(
            IdCuota:             c.IdCuota,
            NumeroCuota:         c.NumeroCuota,
            FechaVencimiento:    c.FechaVencimiento.ToString("yyyy-MM-dd"),
            ValorCapital:        c.ValorCapital,
            ValorInteres:        c.ValorInteres,
            ValorAval:           c.ValorAval,
            ValorAdmin:          c.ValorAdmin,
            ValorIva:            c.ValorIva,
            ValorGastosCobranza: 0m, // sin gastos de cobranza automáticos en esta fase
            ValorTotal:          c.ValorTotal,
            PagadoCapital:       c.PagadoCapital,
            PagadoInteres:       c.PagadoInteres,
            PagadoAval:          c.PagadoAval,
            PagadoAdmin:         c.PagadoAdmin,
            PagadoIva:           c.PagadoIva,
            SaldoCuota:          c.SaldoCuota,
            Estado:              c.Estado)).ToList();
    }

    // ── Pago manual de cuotas desde Wallet ──────────────────────────────
    public async Task<PagoCuotaResultDto> PagarCuotaWalletAsync(PagarCuotaWalletRequest req, long idUsuario)
    {
        if (string.IsNullOrEmpty(req.Pin) || req.Pin.Length != 7 || !req.Pin.All(char.IsDigit))
            throw new ArgumentException("El PIN debe ser exactamente 7 dígitos numéricos");
        if (req.ValorPago <= 0)
            throw new ArgumentException("El valor a pagar debe ser mayor a cero");

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            var utilizacion = await db.CarteraUtilizaciones
                .FirstOrDefaultAsync(u => u.IdUtilizacion == req.IdUtilizacion && u.IdUsuario == idUsuario)
                ?? throw new KeyNotFoundException("Crédito no encontrado");
            if (utilizacion.Estado == "ANULADA")
                throw new InvalidOperationException("Este crédito fue anulado y no admite pagos");
            if (utilizacion.Estado == "PAGADA")
                throw new InvalidOperationException("Este crédito ya está pagado en su totalidad");

            // Lock pesimista sobre cupo y wallet — mismo patrón que ConfirmarAvanceWalletAsync.
            var cupo = await db.CarteraCuposOrdinarios
                .FromSqlInterpolated($"SELECT * FROM cartera_cupos_ordinarios WITH (UPDLOCK, ROWLOCK) WHERE id_usuario = {idUsuario}")
                .FirstOrDefaultAsync()
                ?? throw new KeyNotFoundException("No tienes un cupo ordinario asignado");

            var saldo = await db.WalletSaldos
                .FromSqlInterpolated($"SELECT * FROM wallet_saldos WITH (UPDLOCK, ROWLOCK) WHERE id_wallet = {cupo.IdWallet}")
                .FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("La wallet no tiene registro de saldo");

            // Lock pesimista sobre las cuotas pendientes/parciales de este crédito, ordenadas por
            // vencimiento — evita que dos pagos concurrentes sobre el mismo crédito se pisen.
            var cuotas = await db.CarteraCuotas
                .FromSqlInterpolated($"SELECT * FROM cartera_cuotas WITH (UPDLOCK, ROWLOCK) WHERE id_utilizacion = {req.IdUtilizacion} AND estado IN ('PENDIENTE','PARCIAL') ORDER BY fecha_vencimiento, numero_cuota")
                .ToListAsync();
            if (cuotas.Count == 0)
                throw new InvalidOperationException("No hay saldo pendiente en este crédito");

            decimal saldoPendienteCredito = cuotas.Sum(c => c.SaldoCuota);
            if (req.ValorPago > saldoPendienteCredito)
                throw new InvalidOperationException($"El valor a pagar ({req.ValorPago:N0}) supera el saldo pendiente del crédito ({saldoPendienteCredito:N0})");
            if (req.ValorPago > saldo.SaldoDisponible)
                throw new InvalidOperationException($"El valor a pagar ({req.ValorPago:N0}) supera tu saldo disponible en Wallet ({saldo.SaldoDisponible:N0})");

            var now = DateTime.UtcNow;
            decimal montoRestante = req.ValorPago;
            decimal totalCapital = 0, totalInteres = 0, totalAval = 0, totalAdmin = 0, totalIva = 0;
            var detalles         = new List<CarteraPagoDetalle>();
            var cuotasAfectadas  = new List<CuotaAfectadaDto>();

            foreach (var cuota in cuotas)
            {
                if (montoRestante <= 0) break;
                decimal aplicadoCuota   = Math.Min(montoRestante, cuota.SaldoCuota);
                decimal disponibleCuota = aplicadoCuota;

                decimal AplicarConcepto(decimal valorTotalConcepto, decimal yaPagado)
                {
                    decimal pendiente = valorTotalConcepto - yaPagado;
                    decimal aplicado  = Math.Min(disponibleCuota, pendiente);
                    disponibleCuota  -= aplicado;
                    return aplicado;
                }

                // Orden de aplicación: IVA, IVA gastos cobranza, gastos cobranza, aval,
                // administración, intereses, capital. Los gastos de cobranza no están
                // implementados todavía en esta fase (siempre 0 pendiente, pasos no-op).
                decimal ivaAplicado               = AplicarConcepto(cuota.ValorIva, cuota.PagadoIva);
                decimal ivaGastosCobranzaAplicado = 0m;
                decimal gastosCobranzaAplicado    = 0m;
                decimal avalAplicado              = AplicarConcepto(cuota.ValorAval, cuota.PagadoAval);
                decimal adminAplicado             = AplicarConcepto(cuota.ValorAdmin, cuota.PagadoAdmin);
                decimal interesAplicado           = AplicarConcepto(cuota.ValorInteres, cuota.PagadoInteres);
                decimal capitalAplicado           = AplicarConcepto(cuota.ValorCapital, cuota.PagadoCapital);

                cuota.PagadoIva               += ivaAplicado;
                cuota.PagadoIvaGastosCobranza += ivaGastosCobranzaAplicado;
                cuota.PagadoGastosCobranza    += gastosCobranzaAplicado;
                cuota.PagadoAval              += avalAplicado;
                cuota.PagadoAdmin             += adminAplicado;
                cuota.PagadoInteres           += interesAplicado;
                cuota.PagadoCapital           += capitalAplicado;
                cuota.SaldoCuota              -= aplicadoCuota;
                cuota.UpdatedAt                = now;

                if (cuota.SaldoCuota <= 0)
                {
                    cuota.SaldoCuota = 0;
                    cuota.Estado     = "PAGADA";
                    cuota.FechaPago  = now;
                }
                else
                {
                    cuota.Estado = "PARCIAL";
                }

                totalCapital += capitalAplicado;
                totalInteres += interesAplicado;
                totalAval    += avalAplicado;
                totalAdmin   += adminAplicado;
                totalIva     += ivaAplicado;

                detalles.Add(new CarteraPagoDetalle
                {
                    IdCuota                        = cuota.IdCuota,
                    ValorCapital                   = capitalAplicado,
                    ValorInteres                   = interesAplicado,
                    ValorAval                      = avalAplicado,
                    ValorAdmin                     = adminAplicado,
                    ValorIva                       = ivaAplicado,
                    ValorTotal                     = aplicadoCuota,
                    ValorAplicadoAdmin             = adminAplicado,
                    ValorAplicadoIva               = ivaAplicado,
                    ValorAplicadoGastosCobranza    = gastosCobranzaAplicado,
                    ValorAplicadoIvaGastosCobranza = ivaGastosCobranzaAplicado,
                    CreatedAt                      = now,
                });

                cuotasAfectadas.Add(new CuotaAfectadaDto(
                    IdCuota:           cuota.IdCuota,
                    NumeroCuota:       cuota.NumeroCuota,
                    CapitalPagado:     capitalAplicado,
                    InteresPagado:     interesAplicado,
                    AvalPagado:        avalAplicado,
                    AdminPagado:       adminAplicado,
                    IvaPagado:         ivaAplicado,
                    ValorPagado:       aplicadoCuota,
                    SaldoCuotaDespues: cuota.SaldoCuota,
                    Estado:            cuota.Estado));

                montoRestante -= aplicadoCuota;
            }

            var ledgerTx = new LedgerTransaccion
            {
                IdUnidadNegocio  = IdUnidadNegocio,
                TipoTransaccion  = "CARTERA_PAGO_CUOTA_WALLET",
                ReferenciaTipo   = "cartera_utilizaciones",
                ReferenciaId     = utilizacion.IdUtilizacion,
                Descripcion      = $"Pago cartera ordinaria crédito #{utilizacion.IdUtilizacion} usuario #{idUsuario}",
                ValorTotal       = req.ValorPago,
                Estado           = "REGISTRADA",
                CreadoPor        = idUsuario,
                FechaTransaccion = now,
            };
            db.LedgerTransacciones.Add(ledgerTx);
            await db.SaveChangesAsync();

            var movimientos = new List<LedgerMovimiento>
            {
                new() {
                    IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                    IdCuenta       = (await GetCuentaLedgerAsync(CodObligacionWallet)).IdCuenta,
                    Naturaleza     = "D",
                    Valor          = req.ValorPago,
                    Concepto       = "CARTERA_PAGO_CUOTA",
                    ReferenciaTipo = "cartera_utilizaciones",
                    ReferenciaId   = utilizacion.IdUtilizacion,
                    Descripcion    = $"Pago cartera ordinaria crédito #{utilizacion.IdUtilizacion} — total.",
                    FechaMovimiento = now,
                },
            };

            async Task AgregarCredito(string codigo, decimal valor, string concepto, string descripcion)
            {
                if (valor <= 0) return;
                var cuenta = await GetCuentaLedgerAsync(codigo);
                movimientos.Add(new LedgerMovimiento
                {
                    IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                    IdCuenta       = cuenta.IdCuenta,
                    Naturaleza     = "C",
                    Valor          = valor,
                    Concepto       = concepto,
                    ReferenciaTipo = "cartera_utilizaciones",
                    ReferenciaId   = utilizacion.IdUtilizacion,
                    Descripcion    = descripcion,
                    FechaMovimiento = now,
                });
            }

            await AgregarCredito(CodCarteraOrdinaria, totalCapital, "CARTERA_PAGO_CAPITAL", "Abono a capital cartera ordinaria.");
            await AgregarCredito(CodIngresoInteres,   totalInteres, "CARTERA_PAGO_INTERES", "Interés cartera ordinaria pagado.");
            await AgregarCredito(CodIngresoAval,      totalAval,    "CARTERA_PAGO_AVAL",    "Aval cartera ordinaria pagado.");
            await AgregarCredito(CodIngresoAdmin,     totalAdmin,   "CARTERA_PAGO_ADMIN",   "Administración cartera ordinaria pagada.");
            await AgregarCredito(CodIvaCarteraPagar,  totalIva,     "CARTERA_PAGO_IVA",     "IVA cartera ordinaria pagado.");

            db.LedgerMovimientos.AddRange(movimientos);

            var cupoUsadoAntes      = cupo.CupoUsado;
            var cupoDisponibleAntes = cupo.CupoAprobado - cupo.CupoUsado;
            // El capital pagado nunca debería exceder el cupo_usado registrado — si ocurre,
            // es una inconsistencia real de datos (no un caso normal a esconder con un clamp).
            if (totalCapital > cupo.CupoUsado)
                throw new InvalidOperationException(
                    $"Inconsistencia de cupo: capital pagado ({totalCapital:N0}) supera el cupo usado registrado ({cupo.CupoUsado:N0}).");
            cupo.CupoUsado = cupo.CupoUsado - totalCapital;
            cupo.UpdatedAt = now;
            var cupoUsadoDespues      = cupo.CupoUsado;
            var cupoDisponibleDespues = cupo.CupoAprobado - cupo.CupoUsado;

            var saldoAntes = saldo.SaldoDisponible;
            saldo.SaldoDisponible   -= req.ValorPago;
            saldo.FechaActualizacion = now;
            var saldoDespues = saldo.SaldoDisponible;

            db.WalletMovimientos.Add(new WalletMovimiento
            {
                IdWallet            = cupo.IdWallet,
                IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                TipoMovimiento      = "CARTERA_PAGO_CUOTA",
                Naturaleza          = "D",
                Valor               = req.ValorPago,
                SaldoAntes          = saldoAntes,
                SaldoDespues        = saldoDespues,
                Descripcion         = $"Pago cartera ordinaria crédito {utilizacion.IdUtilizacion}",
                ReferenciaTipo      = "cartera_utilizaciones",
                ReferenciaId        = utilizacion.IdUtilizacion,
                Estado              = "APLICADO",
                CreadoPor           = idUsuario,
                FechaMovimiento     = now,
            });

            bool quedanPendientes = cuotas.Any(c => c.Estado == "PENDIENTE" || c.Estado == "PARCIAL");
            if (!quedanPendientes)
            {
                utilizacion.Estado    = "PAGADA";
                utilizacion.UpdatedAt = now;
            }

            var pago = new CarteraPago
            {
                IdUtilizacion         = utilizacion.IdUtilizacion,
                IdUsuario             = idUsuario,
                IdWallet              = cupo.IdWallet,
                ValorPago             = req.ValorPago,
                FechaPago             = now,
                TipoPago              = "CUOTA_NORMAL",
                Estado                = "REGISTRADO",
                CreatedAt             = now,
                CreatedByUsuario      = idUsuario,
                IdTransaccionLedger   = ledgerTx.IdTransaccionLedger,
                SaldoWalletAntes      = saldoAntes,
                SaldoWalletDespues    = saldoDespues,
                CupoUsadoAntes        = cupoUsadoAntes,
                CupoUsadoDespues      = cupoUsadoDespues,
                CupoDisponibleAntes   = cupoDisponibleAntes,
                CupoDisponibleDespues = cupoDisponibleDespues,
                MetodoPago            = "WALLET",
                PinValidadoQa         = true,
            };
            db.CarteraPagos.Add(pago);
            await db.SaveChangesAsync();

            foreach (var d in detalles) d.IdPago = pago.IdPago;
            db.CarteraPagosDetalle.AddRange(detalles);
            await db.SaveChangesAsync();

            var totalD = movimientos.Where(m => m.Naturaleza == "D").Sum(m => m.Valor);
            var totalC = movimientos.Where(m => m.Naturaleza == "C").Sum(m => m.Valor);
            if (totalD != totalC)
                throw new InvalidOperationException($"Ledger desbalanceado: DR={totalD} CR={totalC}.");

            await tx.CommitAsync();

            return new PagoCuotaResultDto(
                IdPago:                pago.IdPago,
                IdTransaccionLedger:   ledgerTx.IdTransaccionLedger,
                ValorPago:             req.ValorPago,
                SaldoWalletAntes:      saldoAntes,
                SaldoWalletDespues:    saldoDespues,
                CupoUsadoAntes:        cupoUsadoAntes,
                CupoUsadoDespues:      cupoUsadoDespues,
                CupoDisponibleAntes:   cupoDisponibleAntes,
                CupoDisponibleDespues: cupoDisponibleDespues,
                CapitalPagado:         totalCapital,
                InteresesPagados:      totalInteres,
                AvalPagado:            totalAval,
                AdminPagado:           totalAdmin,
                IvaPagado:             totalIva,
                CuotasAfectadas:       cuotasAfectadas);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // ── Compra QR con Cupo Ordinario ─────────────────────────────────────
    public async Task<PagarQrConCupoResultDto> PagarQrConCupoAsync(PagarQrConCupoRequest req, long idUsuario)
    {
        if (string.IsNullOrEmpty(req.Pin) || req.Pin.Length != 7 || !req.Pin.All(char.IsDigit))
            throw new ArgumentException("El PIN debe ser exactamente 7 dígitos numéricos");
        if (req.ValorCompra <= 0)
            throw new ArgumentException("El valor de la compra debe ser mayor a cero");

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            // Resolución QR → comercio → tienda: misma consulta que PagoQrService.PagarQrAsync,
            // sin modificarla, para no divergir del flujo de pago con Wallet ya existente.
            var qr = await db.QrComercios.FirstOrDefaultAsync(q => q.CodigoQr == req.QrCode && q.Estado == "ACTIVO")
                ?? throw new InvalidOperationException("El QR no existe o no está activo.");

            var comercio = await db.Comercios.FirstOrDefaultAsync(c => c.IdComercio == qr.IdComercio && c.Estado == "ACTIVO")
                ?? throw new InvalidOperationException("El comercio no existe o no está activo.");

            var tienda = await db.ComercioTiendas.FirstOrDefaultAsync(t => t.IdTienda == qr.IdTienda && t.Estado == "ACTIVO")
                ?? throw new InvalidOperationException("La tienda no existe o no está activa.");

            var param = await GetParametroValidadoAsync(
                new SimularUtilizacionRequest("COMPRA_COMERCIO", req.ValorCompra, req.PlazoMeses, req.Frecuencia));

            // Lock pesimista sobre el cupo del usuario — mismo patrón que ConfirmarAvanceWalletAsync
            // y PagarCuotaWalletAsync: serializa compras concurrentes contra el mismo cupo.
            var cupo = await db.CarteraCuposOrdinarios
                .FromSqlInterpolated($"SELECT * FROM cartera_cupos_ordinarios WITH (UPDLOCK, ROWLOCK) WHERE id_usuario = {idUsuario}")
                .FirstOrDefaultAsync()
                ?? throw new KeyNotFoundException("No tienes un cupo ordinario asignado");
            if (cupo.Estado != "ACTIVO")
                throw new InvalidOperationException("Tu cupo ordinario no está activo");
            if (cupo.FechaVencimiento.HasValue && cupo.FechaVencimiento.Value < DateTime.UtcNow)
                throw new InvalidOperationException("Tu cupo ordinario está vencido");

            decimal cupoDisponible = cupo.CupoAprobado - cupo.CupoUsado;
            if (req.ValorCompra > cupoDisponible)
                throw new InvalidOperationException($"El valor de la compra supera tu cupo disponible ({cupoDisponible:N0})");

            var (frecuencia, n, cuotasSimuladas, sumInteres, totalAval, totalAdmin, totalIva, valorCuota, valorTotalPagar) =
                CalcularAmortizacion(param, req.ValorCompra, req.PlazoMeses, req.Frecuencia);

            // Aliado (si el comercio lo es) — mismo criterio que TryRegistrarDisponibilidadAsync,
            // para poblar IdComercioAliado en la utilización de forma consistente.
            var aliado = await db.ComerciosAliados
                .FirstOrDefaultAsync(a => a.IdComercioExistente == comercio.IdComercio && a.Estado == "ACTIVO");

            var now = DateTime.UtcNow;

            // Venta QR — misma forma que el pago con Wallet (Estado="CONTINGENCIA", comisión/IVA en
            // 0), salvo que no hay débito de Wallet: IdWalletUsuario apunta a la wallet real del
            // usuario solo por trazabilidad/auditoría (es NOT NULL en el modelo existente).
            var venta = new VentaQr
            {
                IdUnidadNegocio   = comercio.IdUnidadNegocio,
                IdComercio        = comercio.IdComercio,
                IdTienda          = tienda.IdTienda,
                IdQr              = qr.IdQr,
                IdWalletUsuario   = cupo.IdWallet,
                ValorBruto        = req.ValorCompra,
                ValorComision     = 0,
                ValorIvaComision  = 0,
                ValorNetoComercio = req.ValorCompra,
                Estado            = "CONTINGENCIA",
                Referencia        = req.QrCode,
                Descripcion       = "Compra QR financiada con Cupo Ordinario.",
                FechaVenta        = now,
            };
            db.VentasQr.Add(venta);
            await db.SaveChangesAsync();

            var ledgerTx = new LedgerTransaccion
            {
                IdUnidadNegocio  = comercio.IdUnidadNegocio,
                TipoTransaccion  = "COMPRA_QR_CUPO_ORDINARIO",
                ReferenciaTipo   = "ventas_qr",
                ReferenciaId     = venta.IdVentaQr,
                Descripcion      = $"Compra QR #{venta.IdVentaQr} financiada con cupo ordinario, usuario #{idUsuario}",
                ValorTotal       = req.ValorCompra,
                Estado           = "REGISTRADA",
                CreadoPor        = idUsuario,
                FechaTransaccion = now,
            };
            db.LedgerTransacciones.Add(ledgerTx);
            await db.SaveChangesAsync();

            var cuentaCartera      = await GetCuentaLedgerAsync(CodCarteraCompraComercio);
            var cuentaContingencia = await GetCuentaLedgerAsync(CodContingenciaQr);

            var movimientos = new List<LedgerMovimiento>
            {
                new() {
                    IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                    IdCuenta       = cuentaCartera.IdCuenta,
                    Naturaleza     = "D",
                    Valor          = req.ValorCompra,
                    Concepto       = "COMPRA_QR_CUPO",
                    ReferenciaTipo = "ventas_qr",
                    ReferenciaId   = venta.IdVentaQr,
                    Descripcion    = "Cartera ordinaria — compra en comercio por cobrar.",
                    FechaMovimiento = now,
                },
                new() {
                    IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                    IdCuenta       = cuentaContingencia.IdCuenta,
                    Naturaleza     = "C",
                    Valor          = req.ValorCompra,
                    Concepto       = "COMPRA_QR_CUPO",
                    ReferenciaTipo = "ventas_qr",
                    ReferenciaId   = venta.IdVentaQr,
                    Descripcion    = "Contingencia comercio por compra QR financiada con cupo.",
                    FechaMovimiento = now,
                },
            };
            db.LedgerMovimientos.AddRange(movimientos);

            var utilizacion = new CarteraUtilizacion
            {
                IdCupo               = cupo.IdCupo,
                IdUsuario            = idUsuario,
                IdWallet             = cupo.IdWallet,
                TipoUtilizacion      = "COMPRA_COMERCIO",
                IdComercioAliado     = aliado?.IdComercioAliado,
                IdVentaQr            = venta.IdVentaQr,
                ValorCapital         = req.ValorCompra,
                TasaEmv              = param.TasaEmv,
                PorcAval             = param.PorcAval,
                PorcAdmin            = param.PorcAdmin,
                AplicaIva            = param.AplicaIva,
                PorcIva              = param.PorcIva,
                PlazoMeses           = req.PlazoMeses,
                Frecuencia           = frecuencia,
                TotalCuotas          = n,
                ValorCuota           = valorCuota,
                ValorTotalAval       = totalAval,
                ValorTotalAdmin      = totalAdmin,
                ValorTotalIva        = totalIva,
                ValorTotalIntereses  = sumInteres,
                ValorTotalPagar      = valorTotalPagar,
                Estado               = "DESEMBOLSADO",
                FechaSolicitud       = now,
                FechaDesembolso      = now,
                CreatedAt            = now,
                CreatedByUsuario     = idUsuario,
            };
            db.CarteraUtilizaciones.Add(utilizacion);
            await db.SaveChangesAsync();

            var cuotas = cuotasSimuladas.Select(c => new CarteraCuota
            {
                IdUtilizacion        = utilizacion.IdUtilizacion,
                NumeroCuota          = c.NumeroCuota,
                FechaVencimiento     = DateOnly.Parse(c.FechaVencimiento),
                ValorCapital         = c.ValorCapital,
                ValorInteres         = c.ValorInteres,
                ValorAval            = c.ValorAval,
                ValorAdmin           = c.ValorAdmin,
                ValorIva             = c.ValorIva,
                ValorTotal           = c.ValorTotal,
                SaldoCapitalAntes    = c.SaldoCapitalAntes,
                SaldoCapitalDespues  = c.SaldoCapitalDespues,
                SaldoCuota           = c.ValorTotal,
                Estado               = "PENDIENTE",
                CreatedAt            = now,
            }).ToList();
            db.CarteraCuotas.AddRange(cuotas);

            venta.IdUtilizacionCartera = utilizacion.IdUtilizacion;
            venta.IdTransaccionLedger  = ledgerTx.IdTransaccionLedger;

            cupo.CupoUsado = cupo.CupoUsado + req.ValorCompra;
            cupo.UpdatedAt = now;

            await db.SaveChangesAsync();

            // Disponibilidad del comercio aliado — reutiliza exactamente la misma lógica que el
            // pago QR con Wallet (idempotente, best-effort: un fallo aquí no revierte la compra).
            try
            {
                await pagoQrService.TryRegistrarDisponibilidadAsync(comercio, venta, now);
            }
            catch (Exception ex)
            {
                // best-effort — igual que PagoQrService.PagarQrAsync: un fallo aquí no revierte la compra.
                logger.LogWarning(ex,
                    "Venta QR #{IdVenta}: no se pudo registrar disponibilidad de comercio aliado (compra con cupo). La compra continúa.",
                    venta.IdVentaQr);
            }

            // TryRegistrarDisponibilidadAsync solo hace db.Add(...) internamente, sin guardar —
            // igual que en PagoQrService.PagarQrAsync, hace falta este SaveChangesAsync para que
            // las filas de disponibilidad/contexto realmente se persistan antes del commit.
            await db.SaveChangesAsync();

            var totalD = movimientos.Where(m => m.Naturaleza == "D").Sum(m => m.Valor);
            var totalC = movimientos.Where(m => m.Naturaleza == "C").Sum(m => m.Valor);
            if (totalD != totalC)
                throw new InvalidOperationException($"Ledger desbalanceado: DR={totalD} CR={totalC}.");

            await tx.CommitAsync();

            return new PagarQrConCupoResultDto(
                IdUtilizacion:        utilizacion.IdUtilizacion,
                IdVentaQr:            venta.IdVentaQr,
                IdTransaccionLedger:  ledgerTx.IdTransaccionLedger,
                ValorCompra:          req.ValorCompra,
                NuevoCupoUsado:       cupo.CupoUsado,
                NuevoCupoDisponible:  cupo.CupoAprobado - cupo.CupoUsado,
                EstadoUtilizacion:    utilizacion.Estado,
                EstadoVentaQr:        venta.Estado,
                Cuotas:               cuotasSimuladas);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // ── Helpers de simulación/confirmación ──────────────────────────────
    private async Task<CarteraParametroUtilizacion> GetParametroValidadoAsync(SimularUtilizacionRequest req)
    {
        var param = await db.CarteraParametrosUtilizacion
            .FirstOrDefaultAsync(x => x.TipoUtilizacion == req.TipoUtilizacion && x.Estado == "ACTIVO")
            ?? throw new KeyNotFoundException($"No hay parámetros activos para {req.TipoUtilizacion}");

        if (req.ValorCapital < param.MontoMin || req.ValorCapital > param.MontoMax)
            throw new ArgumentException($"Monto fuera de rango [{param.MontoMin:N0} – {param.MontoMax:N0}]");
        if (req.PlazoMeses < param.PlazoMin || req.PlazoMeses > param.PlazoMax)
            throw new ArgumentException($"Plazo fuera de rango [{param.PlazoMin} – {param.PlazoMax}] meses");

        return param;
    }

    private static (string Frecuencia, int N, List<CuotaSimuladaDto> Cuotas, decimal SumInteres,
        decimal TotalAval, decimal TotalAdmin, decimal TotalIva, decimal ValorCuota, decimal ValorTotalPagar)
        CalcularAmortizacion(CarteraParametroUtilizacion param, decimal valorCapital, int plazoMeses, string frecuenciaReq)
    {
        var frecuencia = frecuenciaReq.ToUpperInvariant();
        // Total de cuotas: MENSUAL = plazo, QUINCENAL = plazo * 2
        int n = frecuencia == "QUINCENAL" ? plazoMeses * 2 : plazoMeses;
        // Tasa por periodo: EMV mensual; para quincenal dividir por 2 (approximación lineal simple)
        decimal tasaPeriodo = frecuencia == "QUINCENAL"
            ? param.TasaEmv / 2m / 100m
            : param.TasaEmv / 100m;

        // Cuota French: PV * (i*(1+i)^n) / ((1+i)^n - 1)
        double pv  = (double)valorCapital;
        double i   = (double)tasaPeriodo;
        double pot = Math.Pow(1 + i, n);
        double cuotaDouble = pv * (i * pot) / (pot - 1);
        decimal cuota = Math.Round((decimal)cuotaDouble, 0); // round to pesos

        // Distribuir aval/admin/IVA proporcional a capital en cada cuota
        decimal totalAval  = Math.Round(valorCapital * param.PorcAval  / 100m, 0);
        decimal totalAdmin = Math.Round(valorCapital * param.PorcAdmin / 100m, 0);
        decimal baseIva    = totalAval + totalAdmin;
        decimal totalIva   = param.AplicaIva ? Math.Round(baseIva * param.PorcIva / 100m, 0) : 0m;

        decimal avalPorCuota  = Math.Round(totalAval  / n, 0);
        decimal adminPorCuota = Math.Round(totalAdmin / n, 0);
        decimal ivaPorCuota   = Math.Round(totalIva   / n, 0);

        // Build amortization table
        var cuotas   = new List<CuotaSimuladaDto>();
        decimal saldo = valorCapital;
        decimal sumInteres = 0m;

        var fechaBase = DateOnly.FromDateTime(DateTime.Today);

        for (int k = 1; k <= n; k++)
        {
            // Interest for this period
            decimal interes = Math.Round(saldo * tasaPeriodo, 0);
            decimal capitalCuota;

            if (k < n)
            {
                capitalCuota = cuota - interes;
            }
            else
            {
                // last cuota: absorbs rounding difference
                capitalCuota = saldo;
                interes      = cuota - capitalCuota;
                if (interes < 0) { capitalCuota = cuota; interes = 0; }
            }

            // Adjust last period rounding for aval/admin/iva
            decimal avalK  = (k == n) ? totalAval  - avalPorCuota  * (n - 1) : avalPorCuota;
            decimal adminK = (k == n) ? totalAdmin - adminPorCuota * (n - 1) : adminPorCuota;
            decimal ivaK   = (k == n) ? totalIva   - ivaPorCuota   * (n - 1) : ivaPorCuota;

            decimal saldoAntes    = saldo;
            decimal saldoDespues  = saldo - capitalCuota;
            decimal valorTotalCuota = capitalCuota + interes + avalK + adminK + ivaK;

            // Date: MENSUAL +k months, QUINCENAL +k*15 days from base
            DateOnly fecha = frecuencia == "QUINCENAL"
                ? fechaBase.AddDays(k * 15)
                : fechaBase.AddMonths(k);

            cuotas.Add(new CuotaSimuladaDto(
                NumeroCuota:        k,
                FechaVencimiento:   fecha.ToString("yyyy-MM-dd"),
                ValorCapital:       capitalCuota,
                ValorInteres:       interes,
                ValorAval:          avalK,
                ValorAdmin:         adminK,
                ValorIva:           ivaK,
                ValorTotal:         valorTotalCuota,
                SaldoCapitalAntes:  saldoAntes,
                SaldoCapitalDespues: Math.Max(0, saldoDespues)));

            sumInteres += interes;
            saldo = Math.Max(0, saldoDespues);
        }

        decimal valorTotalPagar = valorCapital + sumInteres + totalAval + totalAdmin + totalIva;
        return (frecuencia, n, cuotas, sumInteres, totalAval, totalAdmin, totalIva, cuota, valorTotalPagar);
    }

    private async Task<LedgerCuenta> GetCuentaLedgerAsync(string codigo) =>
        await db.LedgerCuentas.FirstOrDefaultAsync(c => c.IdUnidadNegocio == IdUnidadNegocio && c.Codigo == codigo && c.Estado == "ACTIVA")
        ?? throw new InvalidOperationException($"Cuenta ledger {codigo} no encontrada o inactiva");

    // ── Helpers ────────────────────────────────────────────────────────
    private static ParametroUtilizacionDto ToDto(CarteraParametroUtilizacion x) => new(
        x.IdParametro, x.TipoUtilizacion, x.TasaEmv, x.PorcAval, x.PorcAdmin,
        x.AplicaIva, x.PorcIva, x.PlazoMin, x.PlazoMax, x.Frecuencia, x.MontoMin, x.MontoMax, x.Estado);

    private static GastosCobranzaDto ToGastoDto(CarteraParametroGastosCobranza x) => new(
        x.IdGasto, x.DiasDesde, x.DiasHasta, x.TipoCobro, x.ValorCobro, x.Descripcion, x.Estado);

    private static PoliticaCreditoDto ToPoliticaDto(CarteraPoliticaCredito x) => new(
        x.IdPolitica, x.ScoreDatacreditoMinimo, x.RequiereVeriff,
        x.CupoMinimo, x.CupoMaximo, x.EdadMinima, x.EdadMaxima,
        x.Estado, x.VigenteDesde, x.VigenteHasta);

    private static CupoOrdinarioDto ToCupoDto(CarteraCupoOrdinario c, string nombreUsuario) => new(
        c.IdCupo, c.IdUsuario, nombreUsuario, c.IdWallet,
        c.CupoAprobado, c.CupoUsado, c.CupoAprobado - c.CupoUsado,
        c.Estado, c.FechaAprobacion, c.FechaVencimiento, c.Observaciones);
}
