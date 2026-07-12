using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public class ComercioScopeService
{
    private readonly XpayDbContext _db;
    private readonly ILogger<ComercioScopeService> _logger;

    public ComercioScopeService(XpayDbContext db, ILogger<ComercioScopeService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    // ── Scope ─────────────────────────────────────────────────────────────────

    public async Task<ComercioScope?> GetScopeAsync(long idUsuario)
    {
        var cu = await _db.ComercioUsuarios
            .FirstOrDefaultAsync(u => u.IdUsuario == idUsuario && u.Estado == "ACTIVO");

        if (cu == null) return null;

        return BuildScope(cu);
    }

    public async Task<ComercioScope> RequireScopeAsync(long idUsuario)
    {
        var scope = await GetScopeAsync(idUsuario);
        if (scope == null)
            throw new UnauthorizedAccessException(
                "El usuario no tiene acceso operativo a ningún comercio aliado activo.");
        return scope;
    }

    // Valida que el comercio operativo del scope coincida con el solicitado
    public async Task<ComercioScope> RequireScopeForComercioAsync(long idUsuario, long idComercioExistente)
    {
        var scope = await RequireScopeAsync(idUsuario);
        if (scope.IdComercioExistente != idComercioExistente)
            throw new UnauthorizedAccessException(
                $"El usuario no tiene acceso al comercio {idComercioExistente}.");
        return scope;
    }

    // ── Usuarios operativos ───────────────────────────────────────────────────

    public async Task<List<ComercioUsuarioOperativoResponse>> ListarUsuariosOperativosAsync(long idComercioAliado)
    {
        var rows = await _db.ComercioUsuarios
            .Where(u => u.IdComercioAliado == idComercioAliado)
            .OrderBy(u => u.RolComercio)
            .ThenBy(u => u.IdUsuario)
            .ToListAsync();

        var userIds = rows.Select(r => r.IdUsuario).Distinct().ToList();
        var estIds  = rows.Where(r => r.IdEstablecimiento.HasValue)
                          .Select(r => r.IdEstablecimiento!.Value).Distinct().ToList();

        var usuarios = await _db.Usuarios
            .Where(u => userIds.Contains(u.IdUsuario))
            .Select(u => new { u.IdUsuario, u.NombreUsuario })
            .ToDictionaryAsync(u => u.IdUsuario, u => u.NombreUsuario);

        var establecimientos = await _db.ComercioEstablecimientos
            .Where(e => estIds.Contains(e.IdEstablecimiento))
            .Select(e => new { e.IdEstablecimiento, e.NombreEstablecimiento })
            .ToDictionaryAsync(e => e.IdEstablecimiento, e => e.NombreEstablecimiento);

        return rows.Select(r => new ComercioUsuarioOperativoResponse(
            r.IdComercioUsuario,
            r.IdComercioAliado,
            r.IdComercioExistente,
            r.IdEstablecimiento,
            r.IdEstablecimiento.HasValue ? establecimientos.GetValueOrDefault(r.IdEstablecimiento.Value) : null,
            r.IdUsuario,
            usuarios.GetValueOrDefault(r.IdUsuario, "—"),
            r.RolComercio,
            r.Estado,
            r.CreatedAt.ToString("o")
        )).ToList();
    }

    public async Task<ComercioUsuarioOperativoResponse> CrearUsuarioOperativoAsync(
        long idComercioAliado, CrearComercioUsuarioRequest req, long adminId)
    {
        var rol = (req.RolComercio ?? string.Empty).Trim().ToUpperInvariant();
        if (!new[] {"ADMIN_COMERCIO","ADMIN_SEDE_COMERCIO","CAJERO"}.Contains(rol))
            throw new InvalidOperationException($"rol_comercio inválido: {rol}.");

        if (rol is "ADMIN_SEDE_COMERCIO" or "CAJERO" && !req.IdEstablecimiento.HasValue)
            throw new InvalidOperationException($"id_establecimiento es obligatorio para rol {rol}.");

        if (req.IdEstablecimiento.HasValue)
        {
            var est = await _db.ComercioEstablecimientos
                .FirstOrDefaultAsync(e => e.IdEstablecimiento == req.IdEstablecimiento && e.IdComercioAliado == idComercioAliado)
                ?? throw new InvalidOperationException("La sede no pertenece a este comercio aliado.");
        }

        _ = await _db.Usuarios.FindAsync(req.IdUsuario)
            ?? throw new KeyNotFoundException($"Usuario {req.IdUsuario} no encontrado.");

        var aliado = await _db.ComerciosAliados.FindAsync(idComercioAliado)
            ?? throw new KeyNotFoundException($"Comercio aliado {idComercioAliado} no encontrado.");

        var exists = await _db.ComercioUsuarios
            .AnyAsync(u => u.IdUsuario == req.IdUsuario && u.IdComercioAliado == idComercioAliado && u.Estado == "ACTIVO");
        if (exists)
            throw new InvalidOperationException("El usuario ya tiene un rol activo en este comercio aliado.");

        var cu = new ComercioUsuario
        {
            IdComercioAliado    = idComercioAliado,
            IdComercioExistente = aliado.IdComercioExistente,
            IdEstablecimiento   = req.IdEstablecimiento,
            IdUsuario           = req.IdUsuario,
            RolComercio         = rol,
            Estado              = "ACTIVO",
            CreatedAt           = DateTime.UtcNow,
            CreatedByUsuario    = adminId,
        };
        _db.ComercioUsuarios.Add(cu);
        await _db.SaveChangesAsync();
        _logger.LogInformation("COMERCIO_USUARIO_OPERATIVO_CREAR id={Id} usuario={U} rol={R}", cu.IdComercioUsuario, req.IdUsuario, rol);

        return (await ListarUsuariosOperativosAsync(idComercioAliado))
            .First(r => r.IdComercioUsuario == cu.IdComercioUsuario);
    }

    public async Task<ComercioUsuarioOperativoResponse> ActualizarUsuarioOperativoAsync(
        long idComercioUsuario, ActualizarComercioUsuarioRequest req, long adminId)
    {
        var cu = await _db.ComercioUsuarios.FindAsync(idComercioUsuario)
            ?? throw new KeyNotFoundException($"Registro {idComercioUsuario} no encontrado.");

        if (!string.IsNullOrWhiteSpace(req.RolComercio))
        {
            var rol = req.RolComercio.Trim().ToUpperInvariant();
            if (!new[] {"ADMIN_COMERCIO","ADMIN_SEDE_COMERCIO","CAJERO"}.Contains(rol))
                throw new InvalidOperationException($"rol_comercio inválido: {rol}.");
            cu.RolComercio = rol;
        }
        if (req.IdEstablecimiento.HasValue) cu.IdEstablecimiento = req.IdEstablecimiento;
        if (!string.IsNullOrWhiteSpace(req.Estado))
        {
            var est = req.Estado.Trim().ToUpperInvariant();
            if (!new[] {"ACTIVO","INACTIVO"}.Contains(est))
                throw new InvalidOperationException($"Estado inválido: {est}.");
            cu.Estado = est;
        }
        cu.UpdatedAt          = DateTime.UtcNow;
        cu.UpdatedByUsuario   = adminId;
        await _db.SaveChangesAsync();
        return (await ListarUsuariosOperativosAsync(cu.IdComercioAliado))
            .First(r => r.IdComercioUsuario == idComercioUsuario);
    }

    // ── Contexto de ventas ────────────────────────────────────────────────────

    public async Task<List<VentaQrContextoResponse>> ListarVentasContextoAsync(long idComercioAliado)
    {
        var rows = await _db.ComercioVentasQrContexto
            .Where(c => c.IdComercioAliado == idComercioAliado)
            .OrderByDescending(c => c.IdVentaQr)
            .ToListAsync();

        var estIds   = rows.Where(r => r.IdEstablecimiento.HasValue).Select(r => r.IdEstablecimiento!.Value).Distinct().ToList();
        var cajIds   = rows.Where(r => r.IdCajeroUsuario.HasValue).Select(r => r.IdCajeroUsuario!.Value).Distinct().ToList();

        var establecimientos = await _db.ComercioEstablecimientos
            .Where(e => estIds.Contains(e.IdEstablecimiento))
            .Select(e => new { e.IdEstablecimiento, e.NombreEstablecimiento })
            .ToDictionaryAsync(e => e.IdEstablecimiento, e => e.NombreEstablecimiento);

        var cajeros = await _db.Usuarios
            .Where(u => cajIds.Contains(u.IdUsuario))
            .Select(u => new { u.IdUsuario, u.NombreUsuario })
            .ToDictionaryAsync(u => u.IdUsuario, u => u.NombreUsuario);

        return rows.Select(r => new VentaQrContextoResponse(
            r.IdContexto, r.IdVentaQr, r.IdComercioAliado, r.IdComercioExistente,
            r.IdEstablecimiento,
            r.IdEstablecimiento.HasValue ? establecimientos.GetValueOrDefault(r.IdEstablecimiento.Value) : null,
            r.IdCajeroUsuario,
            r.IdCajeroUsuario.HasValue ? cajeros.GetValueOrDefault(r.IdCajeroUsuario.Value) : null,
            r.CreatedAt.ToString("o")
        )).ToList();
    }

    public async Task<int> BackfillDemoContextoAsync(long idComercioAliado, long idComercioExistente, long idEstablecimiento)
    {
        var existing = await _db.ComercioVentasQrContexto
            .Where(c => c.IdComercioAliado == idComercioAliado)
            .Select(c => c.IdVentaQr)
            .ToListAsync();

        var ventas = await _db.VentasQr
            .Where(v => v.IdComercio == idComercioExistente && !existing.Contains(v.IdVentaQr))
            .ToListAsync();

        var nuevos = ventas.Select(v => new ComercioVentaQrContexto
        {
            IdVentaQr           = v.IdVentaQr,
            IdComercioAliado    = idComercioAliado,
            IdComercioExistente = idComercioExistente,
            IdEstablecimiento   = idEstablecimiento,
            IdCajeroUsuario     = null,
            CreatedAt           = DateTime.UtcNow,
        }).ToList();

        _db.ComercioVentasQrContexto.AddRange(nuevos);
        await _db.SaveChangesAsync();
        return nuevos.Count;
    }

    // ── Dashboard / totales / ventas ──────────────────────────────────────────

    public async Task<DashboardComercioResponse> GetDashboardAsync(ComercioScope scope)
    {
        decimal saldo = 0;
        if (scope.PuedeVerTodoComercio && scope.IdComercioExistente.HasValue)
        {
            var comercio = await _db.Comercios.FindAsync(scope.IdComercioExistente.Value);
            if (comercio?.IdWalletComercio != null)
            {
                var ws = await _db.WalletSaldos.FindAsync(comercio.IdWalletComercio.Value);
                saldo = ws?.SaldoDisponible ?? 0;
            }
        }

        var ventasQuery = BuildVentasQuery(scope);
        var ventas = await ventasQuery.ToListAsync();

        var totalVentas     = ventas.Count;
        var valorTotal      = ventas.Sum(v => v.ValorBruto);
        var contingencia    = ventas.Count(v => v.Estado == "CONTINGENCIA");
        var liquidadas      = ventas.Count(v => v.Estado == "LIQUIDADA");

        var dispQuery = _db.ComercioVentasQrDisponibilidad
            .Where(d => d.IdComercioAliado == scope.IdComercioAliado && d.Estado == "NO_DISPONIBLE");
        if (!scope.PuedeVerTodoComercio && scope.IdEstablecimiento.HasValue)
            dispQuery = _db.ComercioVentasQrDisponibilidad
                .Join(_db.ComercioVentasQrContexto.Where(c => c.IdEstablecimiento == scope.IdEstablecimiento),
                      d => d.IdVentaQr, c => c.IdVentaQr, (d, c) => d)
                .Where(d => d.IdComercioAliado == scope.IdComercioAliado && d.Estado == "NO_DISPONIBLE");

        var noDisp     = await dispQuery.CountAsync();
        var valorNoDisp = await dispQuery.SumAsync(d => (decimal?)d.ValorNetoProgramado) ?? 0;

        var proxima = await dispQuery
            .OrderBy(d => d.FechaDisponibleProgramada)
            .Select(d => (DateTime?)d.FechaDisponibleProgramada)
            .FirstOrDefaultAsync();

        return new DashboardComercioResponse(
            scope, saldo, totalVentas, valorTotal, contingencia, liquidadas,
            noDisp, valorNoDisp,
            proxima?.ToString("yyyy-MM-dd HH:mm")
        );
    }

    public async Task<TotalesComercioResponse> GetTotalesAsync(
        ComercioScope scope, string? fechaDesde, string? fechaHasta)
    {
        var desde = string.IsNullOrEmpty(fechaDesde)
            ? DateTime.UtcNow.AddMonths(-1)
            : DateTime.Parse(fechaDesde);
        var hasta = string.IsNullOrEmpty(fechaHasta)
            ? DateTime.UtcNow
            : DateTime.Parse(fechaHasta).AddDays(1);

        var q = BuildVentasQuery(scope)
            .Where(v => v.FechaVenta >= desde && v.FechaVenta < hasta);

        var ventasIds = await q.Select(v => v.IdVentaQr).ToListAsync();

        var contextos = await _db.ComercioVentasQrContexto
            .Where(c => ventasIds.Contains(c.IdVentaQr) && c.IdComercioAliado == scope.IdComercioAliado)
            .ToListAsync();

        var ventas = await q.ToListAsync();

        var totalBruto    = ventas.Sum(v => v.ValorBruto);
        var totalComision = ventas.Sum(v => v.ValorComision + v.ValorIvaComision);
        var totalNeto     = ventas.Sum(v => v.ValorNetoComercio);

        var estIds = contextos.Where(c => c.IdEstablecimiento.HasValue)
                              .Select(c => c.IdEstablecimiento!.Value).Distinct().ToList();
        var cajIds = contextos.Where(c => c.IdCajeroUsuario.HasValue)
                              .Select(c => c.IdCajeroUsuario!.Value).Distinct().ToList();

        var nombres = await _db.Usuarios.Where(u => cajIds.Contains(u.IdUsuario))
            .Select(u => new { u.IdUsuario, u.NombreUsuario })
            .ToDictionaryAsync(u => u.IdUsuario, u => u.NombreUsuario);

        var estNombres = await _db.ComercioEstablecimientos
            .Where(e => estIds.Contains(e.IdEstablecimiento))
            .Select(e => new { e.IdEstablecimiento, e.NombreEstablecimiento })
            .ToDictionaryAsync(e => e.IdEstablecimiento, e => e.NombreEstablecimiento);

        var porSede = contextos
            .Where(c => c.IdEstablecimiento.HasValue)
            .GroupBy(c => c.IdEstablecimiento!.Value)
            .Select(g => new TotalesPorSede(
                g.Key,
                estNombres.GetValueOrDefault(g.Key, "—"),
                g.Count(),
                ventas.Where(v => g.Select(c => c.IdVentaQr).Contains(v.IdVentaQr)).Sum(v => v.ValorBruto)
            )).ToList();

        var porCajero = contextos
            .Where(c => c.IdCajeroUsuario.HasValue)
            .GroupBy(c => c.IdCajeroUsuario!.Value)
            .Select(g => new TotalesPorCajero(
                g.Key,
                nombres.GetValueOrDefault(g.Key, "—"),
                g.Count(),
                ventas.Where(v => g.Select(c => c.IdVentaQr).Contains(v.IdVentaQr)).Sum(v => v.ValorBruto)
            )).ToList();

        return new TotalesComercioResponse(
            $"{desde:yyyy-MM-dd} / {hasta.AddDays(-1):yyyy-MM-dd}",
            ventas.Count, totalBruto, totalComision, totalNeto,
            porSede, porCajero
        );
    }

    public async Task<List<VentaConContextoResponse>> ListarVentasAsync(
        ComercioScope scope, long? filtroSede, long? filtroCajero, string? fechaDesde, string? fechaHasta)
    {
        var desde = string.IsNullOrEmpty(fechaDesde)
            ? (DateTime?)null : DateTime.Parse(fechaDesde);
        var hasta = string.IsNullOrEmpty(fechaHasta)
            ? (DateTime?)null : DateTime.Parse(fechaHasta).AddDays(1);

        var q = BuildVentasQuery(scope);
        if (desde.HasValue) q = q.Where(v => v.FechaVenta >= desde.Value);
        if (hasta.HasValue) q = q.Where(v => v.FechaVenta < hasta.Value);

        var ventasIds = await q.OrderByDescending(v => v.FechaVenta)
                               .Take(200)
                               .Select(v => v.IdVentaQr).ToListAsync();

        var ctxQ = _db.ComercioVentasQrContexto
            .Where(c => ventasIds.Contains(c.IdVentaQr) && c.IdComercioAliado == scope.IdComercioAliado);

        if (filtroSede.HasValue) ctxQ = ctxQ.Where(c => c.IdEstablecimiento == filtroSede);
        if (filtroCajero.HasValue) ctxQ = ctxQ.Where(c => c.IdCajeroUsuario == filtroCajero);

        var contextos = await ctxQ.ToDictionaryAsync(c => c.IdVentaQr);

        if (filtroSede.HasValue || filtroCajero.HasValue)
            ventasIds = contextos.Keys.ToList();

        var ventas = await _db.VentasQr
            .Where(v => ventasIds.Contains(v.IdVentaQr))
            .OrderByDescending(v => v.FechaVenta)
            .ToListAsync();

        var estIds = contextos.Values.Where(c => c.IdEstablecimiento.HasValue)
                              .Select(c => c.IdEstablecimiento!.Value).Distinct().ToList();
        var cajIds = contextos.Values.Where(c => c.IdCajeroUsuario.HasValue)
                              .Select(c => c.IdCajeroUsuario!.Value).Distinct().ToList();

        var estNombres = await _db.ComercioEstablecimientos
            .Where(e => estIds.Contains(e.IdEstablecimiento))
            .Select(e => new { e.IdEstablecimiento, e.NombreEstablecimiento })
            .ToDictionaryAsync(e => e.IdEstablecimiento, e => e.NombreEstablecimiento);

        var cajNombres = await _db.Usuarios
            .Where(u => cajIds.Contains(u.IdUsuario))
            .Select(u => new { u.IdUsuario, u.NombreUsuario })
            .ToDictionaryAsync(u => u.IdUsuario, u => u.NombreUsuario);

        return ventas.Select(v => {
            contextos.TryGetValue(v.IdVentaQr, out var ctx);
            return new VentaConContextoResponse(
                v.IdVentaQr, v.ValorBruto, v.Estado, v.FechaVenta.ToString("o"),
                ctx?.IdEstablecimiento,
                ctx?.IdEstablecimiento.HasValue == true ? estNombres.GetValueOrDefault(ctx.IdEstablecimiento!.Value) : null,
                ctx?.IdCajeroUsuario,
                ctx?.IdCajeroUsuario.HasValue == true ? cajNombres.GetValueOrDefault(ctx.IdCajeroUsuario!.Value) : null
            );
        }).ToList();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IQueryable<VentaQr> BuildVentasQuery(ComercioScope scope)
    {
        if (scope.PuedeVerTodoComercio && scope.IdComercioExistente.HasValue)
            return _db.VentasQr.Where(v => v.IdComercio == scope.IdComercioExistente.Value);

        if (scope.IdEstablecimiento.HasValue)
        {
            // Sede: ventas via contexto con id_establecimiento
            var ventasEnSede = _db.ComercioVentasQrContexto
                .Where(c => c.IdEstablecimiento == scope.IdEstablecimiento && c.IdComercioAliado == scope.IdComercioAliado)
                .Select(c => c.IdVentaQr);
            return _db.VentasQr.Where(v => ventasEnSede.Contains(v.IdVentaQr));
        }

        // CAJERO: solo sus propias ventas por id_cajero_usuario en contexto
        var ventasCajero = _db.ComercioVentasQrContexto
            .Where(c => c.IdCajeroUsuario == scope.IdUsuario && c.IdComercioAliado == scope.IdComercioAliado)
            .Select(c => c.IdVentaQr);
        return _db.VentasQr.Where(v => ventasCajero.Contains(v.IdVentaQr));
    }

    private static ComercioScope BuildScope(ComercioUsuario cu) => cu.RolComercio switch
    {
        "ADMIN_COMERCIO" => new ComercioScope(
            cu.IdUsuario, cu.RolComercio, cu.IdComercioAliado, cu.IdComercioExistente, null,
            PuedeVerTodoComercio: true, PuedeDisponerRecursos: true,
            PuedeLiquidarAnticipado: true, PuedeEnviarBreb: true,
            PuedeAnularVentasDiaActual: true, PuedeGenerarQr: false),

        "ADMIN_SEDE_COMERCIO" => new ComercioScope(
            cu.IdUsuario, cu.RolComercio, cu.IdComercioAliado, cu.IdComercioExistente, cu.IdEstablecimiento,
            PuedeVerTodoComercio: false, PuedeDisponerRecursos: false,
            PuedeLiquidarAnticipado: false, PuedeEnviarBreb: false,
            PuedeAnularVentasDiaActual: true, PuedeGenerarQr: false),

        "CAJERO" => new ComercioScope(
            cu.IdUsuario, cu.RolComercio, cu.IdComercioAliado, cu.IdComercioExistente, cu.IdEstablecimiento,
            PuedeVerTodoComercio: false, PuedeDisponerRecursos: false,
            PuedeLiquidarAnticipado: false, PuedeEnviarBreb: false,
            PuedeAnularVentasDiaActual: false, PuedeGenerarQr: true),

        _ => throw new InvalidOperationException($"Rol desconocido: {cu.RolComercio}"),
    };
}
