namespace Xpay.Api.Models;

public class CarteraPoliticaCredito
{
    public long     IdPolitica              { get; set; }
    public int?     ScoreDatacreditoMinimo  { get; set; }
    public bool     RequiereVeriff          { get; set; }
    public decimal  CupoMinimo              { get; set; }
    public decimal  CupoMaximo              { get; set; }
    public int      EdadMinima              { get; set; }
    public int      EdadMaxima              { get; set; }
    public string   Estado                  { get; set; } = "ACTIVO";
    public DateTime VigenteDesde            { get; set; }
    public DateTime? VigenteHasta           { get; set; }
    public DateTime CreatedAt               { get; set; }
    public DateTime? UpdatedAt              { get; set; }
    public long?    CreatedByUsuario        { get; set; }
}
