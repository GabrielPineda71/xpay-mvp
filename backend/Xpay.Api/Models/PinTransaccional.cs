namespace Xpay.Api.Models;

// PIN transaccional — tabla pin_transaccional (database/001_security_identity.sql,
// ya presente en todos los ambientes; sin migración nueva). Una fila por
// usuario (índice único IX_pin_transaccional_usuario). Solo se persiste el
// hash BCrypt del PIN; el PIN en claro nunca se guarda ni se registra.
// Estado: ACTIVO (usable) o RESTABLECIDO (un administrador lo anuló y el
// usuario debe crear uno nuevo).
public class PinTransaccional
{
    public long IdPin { get; set; }
    public long IdUsuario { get; set; }
    public string PinHash { get; set; } = string.Empty;
    public int IntentosFallidos { get; set; }
    public bool Bloqueado { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaActualizacion { get; set; }
    public DateTime? FechaBloqueo { get; set; }
    public string Estado { get; set; } = EstadosPin.Activo;
}

public static class EstadosPin
{
    public const string Activo       = "ACTIVO";
    public const string Restablecido = "RESTABLECIDO";
}
