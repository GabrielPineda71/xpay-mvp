using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public class CarteraOrdinariaService(XpayDbContext db)
{
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
        var param = await db.CarteraParametrosUtilizacion
            .FirstOrDefaultAsync(x => x.TipoUtilizacion == req.TipoUtilizacion && x.Estado == "ACTIVO")
            ?? throw new KeyNotFoundException($"No hay parámetros activos para {req.TipoUtilizacion}");

        if (req.ValorCapital < param.MontoMin || req.ValorCapital > param.MontoMax)
            throw new ArgumentException($"Monto fuera de rango [{param.MontoMin:N0} – {param.MontoMax:N0}]");
        if (req.PlazoMeses < param.PlazoMin || req.PlazoMeses > param.PlazoMax)
            throw new ArgumentException($"Plazo fuera de rango [{param.PlazoMin} – {param.PlazoMax}] meses");

        var frecuencia = req.Frecuencia.ToUpperInvariant();
        // Total de cuotas: MENSUAL = plazo, QUINCENAL = plazo * 2
        int n = frecuencia == "QUINCENAL" ? req.PlazoMeses * 2 : req.PlazoMeses;
        // Tasa por periodo: EMV mensual; para quincenal dividir por 2 (approximación lineal simple)
        decimal tasaPeriodo = frecuencia == "QUINCENAL"
            ? param.TasaEmv / 2m / 100m
            : param.TasaEmv / 100m;

        // Cuota French: PV * (i*(1+i)^n) / ((1+i)^n - 1)
        double pv  = (double)req.ValorCapital;
        double i   = (double)tasaPeriodo;
        double pot = Math.Pow(1 + i, n);
        double cuotaDouble = pv * (i * pot) / (pot - 1);
        decimal cuota = Math.Round((decimal)cuotaDouble, 0); // round to pesos

        // Distribuir aval/admin/IVA proporcional a capital en cada cuota
        decimal totalAval  = Math.Round(req.ValorCapital * param.PorcAval  / 100m, 0);
        decimal totalAdmin = Math.Round(req.ValorCapital * param.PorcAdmin / 100m, 0);
        decimal baseIva    = totalAval + totalAdmin;
        decimal totalIva   = param.AplicaIva ? Math.Round(baseIva * param.PorcIva / 100m, 0) : 0m;

        decimal avalPorCuota  = Math.Round(totalAval  / n, 0);
        decimal adminPorCuota = Math.Round(totalAdmin / n, 0);
        decimal ivaPorCuota   = Math.Round(totalIva   / n, 0);

        // Build amortization table
        var cuotas   = new List<CuotaSimuladaDto>();
        decimal saldo = req.ValorCapital;
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

        decimal valorTotalPagar = req.ValorCapital + sumInteres + totalAval + totalAdmin + totalIva;

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
            ValorCuota:          cuota,
            ValorTotalIntereses: sumInteres,
            ValorTotalAval:      totalAval,
            ValorTotalAdmin:     totalAdmin,
            ValorTotalIva:       totalIva,
            ValorTotalPagar:     valorTotalPagar,
            Cuotas:              cuotas);
    }

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
