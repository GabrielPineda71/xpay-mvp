using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.DTOs;

namespace Xpay.Api.Services;

public class ComercioLiquidacionAutomaticaService
{
    private readonly XpayDbContext                  _db;
    private readonly ComercioDisponibilidadService  _disp;
    private readonly ILogger<ComercioLiquidacionAutomaticaService> _logger;

    public ComercioLiquidacionAutomaticaService(
        XpayDbContext db,
        ComercioDisponibilidadService disp,
        ILogger<ComercioLiquidacionAutomaticaService> logger)
    {
        _db     = db;
        _disp   = disp;
        _logger = logger;
    }

    public async Task<LiquidacionAutomaticaResult> LiquidarVentasVencidasAsync(
        DateTime    fechaCorte,
        long?       soloComercioAliadoId,
        long?       soloIdDisponibilidad = null,
        long?       actorId = null)
    {
        var query = _db.ComercioVentasQrDisponibilidad
            .Where(d => d.Estado == "NO_DISPONIBLE" && d.FechaDisponibleProgramada <= fechaCorte);

        if (soloComercioAliadoId.HasValue)
            query = query.Where(d => d.IdComercioAliado == soloComercioAliadoId.Value);

        if (soloIdDisponibilidad.HasValue)
            query = query.Where(d => d.IdDisponibilidad == soloIdDisponibilidad.Value);

        var pendientes = await query
            .Select(d => new { d.IdDisponibilidad, d.IdVentaQr })
            .ToListAsync();

        _logger.LogInformation(
            "Liquidación automática iniciada: {Count} ventas vencidas al corte {Corte}, actor={Actor}",
            pendientes.Count, fechaCorte, actorId?.ToString() ?? "sistema");

        var resultados = new List<ResultadoLiquidacionIndividual>();
        var errores    = new List<ErrorLiquidacionIndividual>();

        foreach (var item in pendientes)
        {
            try
            {
                var r = await _disp.LiquidarAutomaticaAsync(item.IdDisponibilidad, actorId);
                resultados.Add(new ResultadoLiquidacionIndividual(
                    item.IdDisponibilidad,
                    r.IdVentaQr,
                    r.ValorBruto,
                    r.ValorDescuentoConvenio,
                    r.ValorNetoLiberado,
                    r.IdTransaccionLedger));
            }
            catch (Exception ex)
            {
                var inner   = ex.InnerException?.Message ?? string.Empty;
                var mensaje = string.IsNullOrEmpty(inner) ? ex.Message : $"{ex.Message} | Inner: {inner}";

                _logger.LogError(ex,
                    "Error liquidando automáticamente disp={IdDisp} venta={IdVenta}: {Msg}",
                    item.IdDisponibilidad, item.IdVentaQr, mensaje);

                errores.Add(new ErrorLiquidacionIndividual(
                    item.IdDisponibilidad, item.IdVentaQr, mensaje));
            }
        }

        _logger.LogInformation(
            "Liquidación automática terminada: {Ok} OK / {Err} errores",
            resultados.Count, errores.Count);

        return new LiquidacionAutomaticaResult(
            resultados.Count,
            resultados.Sum(r => r.ValorBruto),
            resultados.Sum(r => r.ValorNeto),
            resultados.Sum(r => r.ValorDescuento),
            resultados.Select(r => r.IdVentaQr).ToList(),
            resultados,
            errores,
            fechaCorte.ToString("yyyy-MM-dd HH:mm:ss"));
    }
}
