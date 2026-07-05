namespace Xpay.Api.DTOs;

// ── Corte vigente ─────────────────────────────────────────────────────────────

public class CorteVigenteResponse
{
    public int      DiaPago             { get; set; }
    public int      PeriodoInicio       { get; set; }
    public int      PeriodoFin          { get; set; }
    public DateOnly FechaPago           { get; set; }
    public decimal  ValorPagoProgramado { get; set; }
    public decimal  PorcentajeCupo      { get; set; }
    public decimal  CupoBase            { get; set; }
    public decimal  CupoUsado           { get; set; }
    public decimal  CupoDisponible      { get; set; }
    public string   FechaSimulada       { get; set; } = string.Empty;
    public bool     EsDiaPago           { get; set; }
}

// ── Cupo del cliente ──────────────────────────────────────────────────────────

public class MiCupoResponse
{
    public long     IdConvenio          { get; set; }
    public string   NombreEmpresa       { get; set; } = string.Empty;
    public long     IdEmpleado          { get; set; }
    public string   NombresEmpleado     { get; set; } = string.Empty;
    public string   NumeroDocumento     { get; set; } = string.Empty;
    public string   PeriodicidadPago    { get; set; } = string.Empty;
    public CorteVigenteResponse? CorteVigente { get; set; }
    public List<AnticipoResponse> AnticiposActivos { get; set; } = [];
    public List<AnticipoResponse> HistorialAnticipos { get; set; } = [];
}

// ── Anticipo ──────────────────────────────────────────────────────────────────

public class AnticipoResponse
{
    public long     IdAnticipo              { get; set; }
    public long     IdConvenio              { get; set; }
    public long     IdEmpleado              { get; set; }
    public string?  FechaSimulada           { get; set; }
    public int      DiaPagoCorte            { get; set; }
    public string?  FechaPagoProgramada     { get; set; }
    public decimal  ValorPagoProgramado     { get; set; }
    public decimal  PorcentajeCupo          { get; set; }
    public decimal  ValorCupoBase           { get; set; }
    public decimal  ValorSolicitado         { get; set; }
    public decimal  ValorComision           { get; set; }
    public decimal  ValorIva                { get; set; }
    public decimal  ValorTotalACobrar       { get; set; }
    public decimal  ValorNetoDesembolsado   { get; set; }
    public string   MomentoCobroComision    { get; set; } = string.Empty;
    public string   Estado                  { get; set; } = string.Empty;
    public long?    IdTransaccionLedgerDesembolso { get; set; }
    public long?    IdTransaccionLedgerPago       { get; set; }
    public string?  ReferenciaPago          { get; set; }
    public DateTime FechaSolicitud          { get; set; }
    public DateTime? UpdatedAt             { get; set; }
}

public class SolicitarAnticipoRequest
{
    public decimal ValorSolicitado { get; set; }
    public string? FechaSimulada   { get; set; }
}

// ── Cobros empresa ────────────────────────────────────────────────────────────

public class CobroEmpresaItem
{
    public long     IdAnticipo          { get; set; }
    public long     IdEmpleado          { get; set; }
    public string   NombresEmpleado     { get; set; } = string.Empty;
    public string   NumeroDocumento     { get; set; } = string.Empty;
    public string   TipoDocumento       { get; set; } = string.Empty;
    public int      DiaPagoCorte        { get; set; }
    public string?  FechaPagoProgramada { get; set; }
    public decimal  ValorSolicitado     { get; set; }
    public decimal  ValorComision       { get; set; }
    public decimal  ValorIva            { get; set; }
    public decimal  ValorTotalACobrar   { get; set; }
    public string   MomentoCobroComision { get; set; } = string.Empty;
    public string   Estado              { get; set; } = string.Empty;
}

public class AplicarPagoRequest
{
    public string  FechaPago       { get; set; } = string.Empty;
    public string  ReferenciaPago  { get; set; } = string.Empty;
}

public class AplicarPagoResult
{
    public int     AnticiposAplicados   { get; set; }
    public decimal TotalCobrado         { get; set; }
    public string  ReferenciaPago       { get; set; } = string.Empty;
    public long    IdTransaccionLedger  { get; set; }
}

// ── Admin: ledger de anticipo ─────────────────────────────────────────────────

public class AnticipoLedgerResponse
{
    public AnticipoResponse Anticipo    { get; set; } = new();
    public List<LedgerMovimientoDto> Movimientos { get; set; } = [];
}

public class LedgerMovimientoDto
{
    public long     IdTransaccionLedger { get; set; }
    public string   TipoTransaccion     { get; set; } = string.Empty;
    public string   Naturaleza          { get; set; } = string.Empty;
    public string   CodigoCuenta        { get; set; } = string.Empty;
    public string   NombreCuenta        { get; set; } = string.Empty;
    public decimal  Valor               { get; set; }
    public string?  Descripcion         { get; set; }
    public DateTime FechaMovimiento     { get; set; }
}

// ── Admin: diagnóstico de corte por fecha ────────────────────────────────────

public class DiagnosticoCorteItem
{
    public long     IdEmpleado          { get; set; }
    public string   Nombres             { get; set; } = string.Empty;
    public string   NumeroDocumento     { get; set; } = string.Empty;
    public string   PeriodicidadPago    { get; set; } = string.Empty;
    public CorteVigenteResponse? CorteVigente { get; set; }
    public string?  Error               { get; set; }
}

// ── Admin: asociar empleado a usuario ────────────────────────────────────────

public class AsociarEmpleadoUsuarioRequest
{
    public long  IdUsuario { get; set; }
    public long? IdWallet  { get; set; }
}
