namespace Xpay.Api.DTOs;

public class EjecutarLiquidacionAutomaticaRequest
{
    public DateTime? FechaCorte           { get; set; }
    public long?     SoloComercioAliadoId { get; set; }
}

public record ResultadoLiquidacionIndividual(
    long    IdDisponibilidad,
    long    IdVentaQr,
    decimal ValorBruto,
    decimal ValorDescuento,
    decimal ValorNeto,
    long    IdTransaccionLedger
);

public record ErrorLiquidacionIndividual(
    long   IdDisponibilidad,
    long   IdVentaQr,
    string Mensaje
);

public record LiquidacionAutomaticaResult(
    int                                 CantidadProcesadas,
    decimal                             TotalBruto,
    decimal                             TotalNetoLiberado,
    decimal                             TotalDescuento,
    List<long>                          IdsVentasLiquidadas,
    List<ErrorLiquidacionIndividual>    Errores,
    string                              FechaCorteUsada
);
