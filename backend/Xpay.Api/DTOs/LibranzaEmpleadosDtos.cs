namespace Xpay.Api.DTOs;

// ── Empleados ──────────────────────────────────────────────────────────────

public class EmpleadoResponse
{
    public long     IdEmpleado           { get; set; }
    public long     IdConvenio           { get; set; }
    public string   TipoDocumento        { get; set; } = string.Empty;
    public string   NumeroDocumento      { get; set; } = string.Empty;
    public string   Nombres              { get; set; } = string.Empty;
    public string?  Apellidos            { get; set; }
    public string?  Celular              { get; set; }
    public string?  Correo               { get; set; }
    public string?  Cargo                { get; set; }
    public decimal  SalarioMensual       { get; set; }
    public string   PeriodicidadPago     { get; set; } = string.Empty;
    public int?     DiaPago1             { get; set; }
    public int?     DiaPago2             { get; set; }
    public int?     DiaPago3             { get; set; }
    public string?  FechaIngreso         { get; set; }
    public string   Estado               { get; set; } = string.Empty;
    public decimal  CupoPreliminar       { get; set; }
    public string   OrigenCarga          { get; set; } = string.Empty;
    public List<CortesPagoResponse> Cortes { get; set; } = [];
    public string?  LoteImportacion      { get; set; }
    public string?  Observaciones        { get; set; }
    public DateTime  CreatedAt           { get; set; }
    public DateTime? UpdatedAt           { get; set; }
}

public class CrearEmpleadoRequest
{
    public string   TipoDocumento        { get; set; } = string.Empty;
    public string   NumeroDocumento      { get; set; } = string.Empty;
    public string   Nombres              { get; set; } = string.Empty;
    public string?  Apellidos            { get; set; }
    public string?  Celular              { get; set; }
    public string?  Correo               { get; set; }
    public string?  Cargo                { get; set; }
    public decimal  SalarioMensual       { get; set; }
    public string   PeriodicidadPago     { get; set; } = string.Empty;
    public int?     DiaPago1             { get; set; }
    public int?     DiaPago2             { get; set; }
    public int?     DiaPago3             { get; set; }
    public string?  FechaIngreso         { get; set; }
    public string?  Observaciones        { get; set; }
}

// ── Importación ────────────────────────────────────────────────────────────

public class ImportacionResponse
{
    public long     IdImportacion        { get; set; }
    public long     IdConvenio           { get; set; }
    public string?  NombreArchivo        { get; set; }
    public string   LoteImportacion      { get; set; } = string.Empty;
    public int      TotalFilas           { get; set; }
    public int      FilasValidas         { get; set; }
    public int      FilasError           { get; set; }
    public int      EmpleadosCreados     { get; set; }
    public int      EmpleadosActualizados { get; set; }
    public string   Estado               { get; set; } = string.Empty;
    public List<ErrorFilaImportacion> Errores { get; set; } = [];
    public DateTime  CreatedAt           { get; set; }
}

public class ErrorFilaImportacion
{
    public int     Fila     { get; set; }
    public string  Campo    { get; set; } = string.Empty;
    public string  Mensaje  { get; set; } = string.Empty;
}

public class ImportarEmpleadosResult
{
    public int      TotalFilas           { get; set; }
    public int      FilasValidas         { get; set; }
    public int      FilasError           { get; set; }
    public int      EmpleadosCreados     { get; set; }
    public int      EmpleadosActualizados { get; set; }
    public string   LoteImportacion      { get; set; } = string.Empty;
    public List<ErrorFilaImportacion> Errores { get; set; } = [];
}

// ── Usuarios empresa ───────────────────────────────────────────────────────

public class UsuarioEmpresaResponse
{
    public long    IdUsuarioEmpresa  { get; set; }
    public long    IdUsuario         { get; set; }
    public long    IdConvenio        { get; set; }
    public string  RolEmpresa        { get; set; } = string.Empty;
    public string  Estado            { get; set; } = string.Empty;
    public DateTime CreatedAt        { get; set; }
}

public class AsociarUsuarioEmpresaRequest
{
    public long    IdUsuario    { get; set; }
    public string  RolEmpresa   { get; set; } = "ADMIN_EMPRESA";
}

// ── Cortes de pago ────────────────────────────────────────────────────────────

public class CortesPagoResponse
{
    public long    IdCortePago         { get; set; }
    public int     NumeroCorte         { get; set; }
    public int     DiaPago             { get; set; }
    public decimal ValorPagoProgramado { get; set; }
    public string  Estado              { get; set; } = string.Empty;
}

// ── Vista empresa (mi-convenio) ────────────────────────────────────────────

public class MiConvenioResponse
{
    public long     IdConvenio           { get; set; }
    public string   NombreEmpresa        { get; set; } = string.Empty;
    public string   Nit                  { get; set; } = string.Empty;
    public string?  RepresentanteLegal   { get; set; }
    public string?  EmailContacto        { get; set; }
    public string?  TelefonoContacto     { get; set; }
    public string   Estado               { get; set; } = string.Empty;
    public string   PeriodicidadPago     { get; set; } = string.Empty;
    public int?     DiaPago1             { get; set; }
    public int?     DiaPago2             { get; set; }
    public int?     DiaPago3             { get; set; }
    public decimal  PorcentajeMaximoCupo { get; set; }
    public decimal  IvaPorcentaje        { get; set; }
    public string   MomentoCobroComision { get; set; } = string.Empty;
    public bool     PermiteAnticipodiaPago { get; set; }
    public int      TotalEmpleados       { get; set; }
    public int      EmpleadosActivos     { get; set; }
    public string   RolEmpresa           { get; set; } = string.Empty;
}

// ── Vista empresa: lista de convenios ─────────────────────────────────────────

public class MisConveniosItem
{
    public long   IdConvenio      { get; set; }
    public string NombreEmpresa   { get; set; } = string.Empty;
    public string PeriodicidadPago { get; set; } = string.Empty;
    public string Estado          { get; set; } = string.Empty;
    public string RolEmpresa      { get; set; } = string.Empty;
}
