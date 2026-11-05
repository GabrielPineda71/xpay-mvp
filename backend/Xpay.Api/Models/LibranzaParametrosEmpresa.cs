namespace Xpay.Api.Models;

public class LibranzaParametrosEmpresa
{
    public long     IdParametro               { get; set; }
    public long     IdConvenio                { get; set; }
    public decimal  PorcentajeMaximoCupo      { get; set; }
    public decimal? SalarioMinimoEmpleado     { get; set; }
    public decimal? SalarioMaximoEmpleado     { get; set; }
    public bool     RequiereValidacionEmpresa { get; set; } = true;
    public bool     PermiteAnticipoMultiple   { get; set; } = false;
    public int      MaxAnticipacionesActivos  { get; set; } = 1;
    public decimal  IvaPorcentaje             { get; set; } = 19.00m;
    public string   MomentoCobroComision      { get; set; } = "VENCIDO";
    // P0 anticipo (047) - minimo comercial de utilizacion/desembolso.
    public decimal  MontoMinimoAnticipo       { get; set; } = 100000.00m;
    public string   Estado                    { get; set; } = "ACTIVO";
    public DateTime  CreatedAt                { get; set; }
    public DateTime? UpdatedAt                { get; set; }
    public long?    CreatedByUsuario          { get; set; }
    public long?    UpdatedByUsuario          { get; set; }
}
