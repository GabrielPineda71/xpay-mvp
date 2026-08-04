namespace Xpay.Api.DTOs;

public class UsuarioAdminListItemDto
{
    public long IdUsuario { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string NombreCompleto { get; set; } = string.Empty;
    public string TipoDocumento { get; set; } = string.Empty;
    public string NumeroDocumento { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Celular { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public int IntentosFallidos { get; set; }
    public DateTime? FechaBloqueo { get; set; }
    public string? MotivoBloqueo { get; set; }
    public DateTime? UltimoIngreso { get; set; }
    public bool RequiereCambioClave { get; set; }
    public List<string> Roles { get; set; } = new();
    public DateTime FechaCreacion { get; set; }
}

public class UsuarioAdminRolDto
{
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public DateTime FechaAsignacion { get; set; }
}

public class UsuarioAdminDetalleDto
{
    public long IdUsuario { get; set; }
    public long IdPersona { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string NombreCompleto { get; set; } = string.Empty;
    public string TipoDocumento { get; set; } = string.Empty;
    public string NumeroDocumento { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Celular { get; set; } = string.Empty;
    public string? Direccion { get; set; }
    public string? Ciudad { get; set; }
    public string? Departamento { get; set; }
    public string Pais { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public bool EmailVerificado { get; set; }
    public bool CelularVerificado { get; set; }
    public int IntentosFallidos { get; set; }
    public DateTime? FechaBloqueo { get; set; }
    public string? MotivoBloqueo { get; set; }
    public DateTime? UltimoIngreso { get; set; }
    public bool RequiereCambioClave { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaActualizacion { get; set; }
    public List<UsuarioAdminRolDto> Roles { get; set; } = new();
}
