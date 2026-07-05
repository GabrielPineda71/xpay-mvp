namespace Xpay.Api.DTOs;

// ── Convenio ──────────────────────────────────────────────────────────────

public class ConvenioResponse
{
    public long     IdConvenio           { get; set; }
    public string   NombreEmpresa        { get; set; } = string.Empty;
    public string   Nit                  { get; set; } = string.Empty;
    public string?  RepresentanteLegal   { get; set; }
    public string?  EmailContacto        { get; set; }
    public string?  TelefonoContacto     { get; set; }
    public string?  Direccion            { get; set; }
    public string   Estado               { get; set; } = string.Empty;
    public int?     DiaPago1             { get; set; }
    public int?     DiaPago2             { get; set; }
    public int?     DiaPago3             { get; set; }
    public bool     PermiteAnticipodiaPago { get; set; }
    public string   PeriodicidadPago     { get; set; } = string.Empty;
    public decimal  PorcentajeMaximoCupo { get; set; }
    public string?  Observaciones        { get; set; }
    public DateTime FechaInicio          { get; set; }
    public DateTime? FechaFin            { get; set; }
    public DateTime  CreatedAt           { get; set; }
    public DateTime? UpdatedAt           { get; set; }
}

public class CrearConvenioRequest
{
    public string   NombreEmpresa        { get; set; } = string.Empty;
    public string   Nit                  { get; set; } = string.Empty;
    public string?  RepresentanteLegal   { get; set; }
    public string?  EmailContacto        { get; set; }
    public string?  TelefonoContacto     { get; set; }
    public string?  Direccion            { get; set; }
    public string   Estado               { get; set; } = "ACTIVO";
    public int?     DiaPago1             { get; set; }
    public int?     DiaPago2             { get; set; }
    public int?     DiaPago3             { get; set; }
    public bool     PermiteAnticipodiaPago { get; set; }
    public string   PeriodicidadPago     { get; set; } = string.Empty;
    public decimal  PorcentajeMaximoCupo { get; set; }
    public string?  Observaciones        { get; set; }
    public DateTime FechaInicio          { get; set; }
    public DateTime? FechaFin            { get; set; }
}

public class ActualizarConvenioRequest
{
    public string?  NombreEmpresa        { get; set; }
    public string?  RepresentanteLegal   { get; set; }
    public string?  EmailContacto        { get; set; }
    public string?  TelefonoContacto     { get; set; }
    public string?  Direccion            { get; set; }
    public string?  Estado               { get; set; }
    public int?     DiaPago1             { get; set; }
    public int?     DiaPago2             { get; set; }
    public int?     DiaPago3             { get; set; }
    public bool?    PermiteAnticipodiaPago { get; set; }
    public string?  PeriodicidadPago     { get; set; }
    public decimal? PorcentajeMaximoCupo { get; set; }
    public string?  Observaciones        { get; set; }
    public DateTime? FechaFin            { get; set; }
}

// ── Parámetros ────────────────────────────────────────────────────────────

public class ParametrosResponse
{
    public long     IdParametro               { get; set; }
    public long     IdConvenio                { get; set; }
    public decimal  PorcentajeMaximoCupo      { get; set; }
    public decimal? SalarioMinimoEmpleado     { get; set; }
    public decimal? SalarioMaximoEmpleado     { get; set; }
    public bool     RequiereValidacionEmpresa { get; set; }
    public bool     PermiteAnticipoMultiple   { get; set; }
    public int      MaxAnticipacionesActivos  { get; set; }
    public decimal  IvaPorcentaje             { get; set; }
    public string   MomentoCobroComision      { get; set; } = string.Empty;
    public string   Estado                    { get; set; } = string.Empty;
    public DateTime  CreatedAt                { get; set; }
    public DateTime? UpdatedAt                { get; set; }
}

public class CrearParametrosRequest
{
    public decimal  PorcentajeMaximoCupo      { get; set; }
    public decimal? SalarioMinimoEmpleado     { get; set; }
    public decimal? SalarioMaximoEmpleado     { get; set; }
    public bool     RequiereValidacionEmpresa { get; set; } = true;
    public bool     PermiteAnticipoMultiple   { get; set; } = false;
    public int      MaxAnticipacionesActivos  { get; set; } = 1;
    public decimal  IvaPorcentaje             { get; set; } = 19.00m;
    public string   MomentoCobroComision      { get; set; } = "VENCIDO";
    public string   Estado                    { get; set; } = "ACTIVO";
}

public class ActualizarParametrosRequest
{
    public decimal?  PorcentajeMaximoCupo      { get; set; }
    public decimal?  SalarioMinimoEmpleado     { get; set; }
    public decimal?  SalarioMaximoEmpleado     { get; set; }
    public bool?     RequiereValidacionEmpresa { get; set; }
    public bool?     PermiteAnticipoMultiple   { get; set; }
    public int?      MaxAnticipacionesActivos  { get; set; }
    public decimal?  IvaPorcentaje             { get; set; }
    public string?   MomentoCobroComision      { get; set; }
    public string?   Estado                    { get; set; }
}

// ── Rangos de cobro ───────────────────────────────────────────────────────

public class RangoCobroResponse
{
    public long     IdRango          { get; set; }
    public long     IdConvenio       { get; set; }
    public decimal  ValorDesde       { get; set; }
    public decimal  ValorHasta       { get; set; }
    public string   TipoCobro        { get; set; } = string.Empty;
    public decimal  ValorCobro       { get; set; }
    public bool     AplicaIva        { get; set; }
    public string   Estado           { get; set; } = string.Empty;
    public DateTime  CreatedAt       { get; set; }
    public DateTime? UpdatedAt       { get; set; }
}

public class CrearRangoRequest
{
    public decimal ValorDesde  { get; set; }
    public decimal ValorHasta  { get; set; }
    public string  TipoCobro   { get; set; } = string.Empty;
    public decimal ValorCobro  { get; set; }
    public bool    AplicaIva   { get; set; } = true;
    public string  Estado      { get; set; } = "ACTIVO";
}

public class ActualizarRangoRequest
{
    public decimal? ValorDesde { get; set; }
    public decimal? ValorHasta { get; set; }
    public string?  TipoCobro  { get; set; }
    public decimal? ValorCobro { get; set; }
    public bool?    AplicaIva  { get; set; }
    public string?  Estado     { get; set; }
}
