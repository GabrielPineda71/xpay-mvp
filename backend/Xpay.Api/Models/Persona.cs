namespace Xpay.Api.Models;
public class Persona
{
    public long IdPersona { get; set; }
    public long IdUnidadNegocio { get; set; }
    public string? TipoDocumento { get; set; }
    public string? NumeroDocumento { get; set; }
    public string? PrimerNombre { get; set; }
    public string? SegundoNombre { get; set; }
    public string? PrimerApellido { get; set; }
    public string? SegundoApellido { get; set; }
    public DateTime? FechaNacimiento { get; set; }
    public string Celular { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Direccion { get; set; }
    public string? Ciudad { get; set; }
    public string? Departamento { get; set; }
    public string Pais { get; set; } = "Colombia";
    public string Estado { get; set; } = "ACTIVA";
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaActualizacion { get; set; }

    // Commit 2 — identidad verificada por Veriff (dominio identidad, no crediticio).
    // Persisten únicamente los atributos crudos que Veriff entrega, sin dividir
    // apellidos ni interpretar reglas de Datacrédito. Ver database/032_persona_identidad_verificada.sql.
    public bool IdentidadVerificada { get; set; }
    public string? IdentidadVerificadaProveedor { get; set; }
    public DateTime? IdentidadVerificadaFecha { get; set; }
    public string? NombreVerificadoCompleto { get; set; }
    public string? ApellidoVerificadoCompleto { get; set; }
    public string? TipoDocumentoVeriffRaw { get; set; }
    public string? NumeroDocumentoVerificado { get; set; }
}
