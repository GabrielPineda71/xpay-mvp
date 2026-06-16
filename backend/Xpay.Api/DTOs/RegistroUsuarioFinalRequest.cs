namespace Xpay.Api.DTOs;

public class RegistroUsuarioFinalRequest
{
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
    public string Usuario { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
