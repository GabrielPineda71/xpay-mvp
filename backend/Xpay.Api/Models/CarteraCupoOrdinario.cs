namespace Xpay.Api.Models;

public class CarteraCupoOrdinario
{
    public long     IdCupo              { get; set; }
    public long     IdUsuario           { get; set; }
    public long     IdWallet            { get; set; }
    public decimal  CupoAprobado        { get; set; }
    public decimal  CupoUsado           { get; set; }
    public string   Estado              { get; set; } = "ACTIVO"; // ACTIVO | SUSPENDIDO | CANCELADO
    public DateTime FechaAprobacion     { get; set; }
    public DateTime? FechaVencimiento   { get; set; }
    public long?    AprobadoPorUsuario  { get; set; }
    public string?  Observaciones       { get; set; }
    public DateTime CreatedAt           { get; set; }
    public DateTime? UpdatedAt          { get; set; }
}
