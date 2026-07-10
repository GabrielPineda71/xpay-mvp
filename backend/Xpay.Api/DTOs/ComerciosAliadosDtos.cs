namespace Xpay.Api.DTOs;

// ── Comercio aliado ───────────────────────────────────────────────────────────

public class ComercioAliadoListItem
{
    public long     IdComercioAliado   { get; set; }
    public string   RazonSocial        { get; set; } = string.Empty;
    public string   NombreComercial    { get; set; } = string.Empty;
    public string   Nit                { get; set; } = string.Empty;
    public string?  Ciudad             { get; set; }
    public string   Estado             { get; set; } = string.Empty;
    public DateTime FechaSolicitud     { get; set; }
    public DateTime CreatedAt          { get; set; }
}

public class ComercioAliadoResponse
{
    public long     IdComercioAliado      { get; set; }
    public long?    IdComercioExistente   { get; set; }
    public string   RazonSocial           { get; set; } = string.Empty;
    public string   NombreComercial       { get; set; } = string.Empty;
    public string   Nit                   { get; set; } = string.Empty;
    public string   TipoPersona           { get; set; } = string.Empty;
    public string?  ActividadEconomica    { get; set; }
    public string?  CodigoCiiu            { get; set; }
    public string?  DireccionPrincipal    { get; set; }
    public string?  Ciudad                { get; set; }
    public string?  Departamento          { get; set; }
    public string?  Telefono              { get; set; }
    public string?  Correo                { get; set; }
    public string?  SitioWeb              { get; set; }
    public string   Estado                { get; set; } = string.Empty;
    public string?  CondicionesComerciales { get; set; }
    public DateTime FechaSolicitud        { get; set; }
    public DateTime? FechaAprobacion      { get; set; }
    public string?  FechaInicioConvenio   { get; set; }
    public string?  FechaFinConvenio      { get; set; }
    public string?  Observaciones         { get; set; }
    public DateTime  CreatedAt            { get; set; }
    public DateTime? UpdatedAt            { get; set; }
}

public class CrearComercioAliadoRequest
{
    public string   RazonSocial           { get; set; } = string.Empty;
    public string   NombreComercial       { get; set; } = string.Empty;
    public string   Nit                   { get; set; } = string.Empty;
    public string   TipoPersona           { get; set; } = string.Empty;
    public string?  ActividadEconomica    { get; set; }
    public string?  CodigoCiiu            { get; set; }
    public string?  DireccionPrincipal    { get; set; }
    public string?  Ciudad                { get; set; }
    public string?  Departamento          { get; set; }
    public string?  Telefono              { get; set; }
    public string?  Correo                { get; set; }
    public string?  SitioWeb              { get; set; }
    public string   Estado                { get; set; } = "BORRADOR";
    public string?  CondicionesComerciales { get; set; }
    public string?  FechaInicioConvenio   { get; set; }
    public string?  FechaFinConvenio      { get; set; }
    public string?  Observaciones         { get; set; }
    public long?    IdComercioExistente   { get; set; }
}

public class ActualizarComercioAliadoRequest
{
    public string?  RazonSocial           { get; set; }
    public string?  NombreComercial       { get; set; }
    public string?  TipoPersona           { get; set; }
    public string?  ActividadEconomica    { get; set; }
    public string?  CodigoCiiu            { get; set; }
    public string?  DireccionPrincipal    { get; set; }
    public string?  Ciudad                { get; set; }
    public string?  Departamento          { get; set; }
    public string?  Telefono              { get; set; }
    public string?  Correo                { get; set; }
    public string?  SitioWeb              { get; set; }
    public string?  Estado                { get; set; }
    public string?  CondicionesComerciales { get; set; }
    public string?  FechaInicioConvenio   { get; set; }
    public string?  FechaFinConvenio      { get; set; }
    public string?  Observaciones         { get; set; }
    public long?    IdComercioExistente   { get; set; }
}

// ── Representante legal ────────────────────────────────────────────────────────

public class RepresentanteResponse
{
    public long     IdRepresentante           { get; set; }
    public long     IdComercioAliado          { get; set; }
    public string   TipoDocumento             { get; set; } = string.Empty;
    public string   NumeroDocumento           { get; set; } = string.Empty;
    public string   Nombres                   { get; set; } = string.Empty;
    public string?  Apellidos                 { get; set; }
    public string?  Celular                   { get; set; }
    public string?  Correo                    { get; set; }
    public string?  Cargo                     { get; set; }
    public string?  FechaExpedicionDocumento  { get; set; }
    public string   Estado                    { get; set; } = string.Empty;
    public DateTime  CreatedAt                { get; set; }
    public DateTime? UpdatedAt                { get; set; }
}

public class CrearRepresentanteRequest
{
    public string   TipoDocumento             { get; set; } = string.Empty;
    public string   NumeroDocumento           { get; set; } = string.Empty;
    public string   Nombres                   { get; set; } = string.Empty;
    public string?  Apellidos                 { get; set; }
    public string?  Celular                   { get; set; }
    public string?  Correo                    { get; set; }
    public string?  Cargo                     { get; set; }
    public string?  FechaExpedicionDocumento  { get; set; }
}

public class ActualizarRepresentanteRequest
{
    public string?  TipoDocumento             { get; set; }
    public string?  NumeroDocumento           { get; set; }
    public string?  Nombres                   { get; set; }
    public string?  Apellidos                 { get; set; }
    public string?  Celular                   { get; set; }
    public string?  Correo                    { get; set; }
    public string?  Cargo                     { get; set; }
    public string?  FechaExpedicionDocumento  { get; set; }
    public string?  Estado                    { get; set; }
}

// ── Establecimiento ───────────────────────────────────────────────────────────

public class EstablecimientoResponse
{
    public long     IdEstablecimiento      { get; set; }
    public long     IdComercioAliado       { get; set; }
    public string   NombreEstablecimiento  { get; set; } = string.Empty;
    public string?  Direccion              { get; set; }
    public string?  Ciudad                 { get; set; }
    public string?  Telefono               { get; set; }
    public string?  Responsable            { get; set; }
    public string   Estado                 { get; set; } = string.Empty;
    public DateTime  CreatedAt             { get; set; }
    public DateTime? UpdatedAt             { get; set; }
}

public class CrearEstablecimientoRequest
{
    public string   NombreEstablecimiento  { get; set; } = string.Empty;
    public string?  Direccion              { get; set; }
    public string?  Ciudad                 { get; set; }
    public string?  Telefono               { get; set; }
    public string?  Responsable            { get; set; }
}

public class ActualizarEstablecimientoRequest
{
    public string?  NombreEstablecimiento  { get; set; }
    public string?  Direccion              { get; set; }
    public string?  Ciudad                 { get; set; }
    public string?  Telefono               { get; set; }
    public string?  Responsable            { get; set; }
    public string?  Estado                 { get; set; }
}

// ── Usuario solicitado ────────────────────────────────────────────────────────

public class UsuarioSolicitadoResponse
{
    public long     IdUsuarioSolicitado { get; set; }
    public long     IdComercioAliado    { get; set; }
    public long?    IdEstablecimiento   { get; set; }
    public long?    IdUsuario           { get; set; }
    public string   Nombres             { get; set; } = string.Empty;
    public string?  Correo              { get; set; }
    public string?  Celular             { get; set; }
    public string   RolSolicitado       { get; set; } = string.Empty;
    public string   Estado              { get; set; } = string.Empty;
    public DateTime  CreatedAt          { get; set; }
    public DateTime? UpdatedAt          { get; set; }
}

public class CrearUsuarioSolicitadoRequest
{
    public string   Nombres           { get; set; } = string.Empty;
    public string?  Correo            { get; set; }
    public string?  Celular           { get; set; }
    public string   RolSolicitado     { get; set; } = string.Empty;
    public long?    IdEstablecimiento { get; set; }
    public long?    IdUsuario         { get; set; }
}

public class ActualizarUsuarioSolicitadoRequest
{
    public string?  Nombres           { get; set; }
    public string?  Correo            { get; set; }
    public string?  Celular           { get; set; }
    public string?  RolSolicitado     { get; set; }
    public string?  Estado            { get; set; }
    public long?    IdEstablecimiento { get; set; }
    public long?    IdUsuario         { get; set; }
}

// ── Documentos ────────────────────────────────────────────────────────────────

public class DocumentoResponse
{
    public long     IdDocumento            { get; set; }
    public long     IdComercioAliado       { get; set; }
    public string   TipoDocumento          { get; set; } = string.Empty;
    public string   NombreArchivoOriginal  { get; set; } = string.Empty;
    public string?  ContentType            { get; set; }
    public long?    SizeBytes              { get; set; }
    public string   Estado                 { get; set; } = string.Empty;
    public string?  Observaciones          { get; set; }
    public DateTime  UploadedAt            { get; set; }
    public long?    UploadedByUsuario      { get; set; }
}

// ── Completitud documental ────────────────────────────────────────────────────

public class CompleitudDocumentalResponse
{
    public bool Contrato              { get; set; }
    public bool CamaraComercio        { get; set; }
    public bool Rut                   { get; set; }
    public bool DocumentoRepresentante { get; set; }
    public bool FormularioSolicitud   { get; set; }
    public int  TotalCargados         { get; set; }
    public int  TotalRequeridos       { get; set; } = 5;
}
