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
    // Fase USUARIOS-ADMIN-4: IdRol necesario para revocar
    // (POST .../roles/{idRol}/revocar usa el id numérico, no el código).
    public long IdRol { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public DateTime FechaAsignacion { get; set; }
}

// Fase USUARIOS-ADMIN-4: rol ofrecido por GET /api/admin/roles/asignables —
// ya filtrado por el backend según el privilegio del actor (nunca incluye
// SUPERUSUARIO si el actor no lo tiene, ni roles técnicos heredados).
public class RolAsignableDto
{
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
}

public class AsignarRolRequest
{
    public string RolCodigo { get; set; } = string.Empty;
    public string? Observacion { get; set; }
}

public class RevocarRolRequest
{
    public string? Observacion { get; set; }
}

// Fase USUARIOS-ADMIN-5: el admin nunca elige la clave — el backend genera
// una temporal aleatoria. Observacion sigue el mismo criterio de todo el
// módulo (opcional, normalizada con NormalizarObservacion).
public class RestablecerClaveRequest
{
    public string? Observacion { get; set; }
}

// La clave temporal viaja en esta respuesta una única vez — el backend nunca
// vuelve a poder devolverla (solo conserva su hash).
public class RestablecerClaveResponse
{
    public UsuarioAdminDetalleDto Usuario { get; set; } = null!;
    public string ClaveTemporal { get; set; } = string.Empty;
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
