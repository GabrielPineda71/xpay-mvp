namespace Xpay.Api.Models;
public class Persona
{
    public long IdPersona { get; set; }
    public long IdUnidadNegocio { get; set; }
    public string TipoDocumento { get; set; } = string.Empty;
    public string NumeroDocumento { get; set; } = string.Empty;
    public string PrimerNombre { get; set; } = string.Empty;
    public string? SegundoNombre { get; set; }
    public string PrimerApellido { get; set; } = string.Empty;
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
}
