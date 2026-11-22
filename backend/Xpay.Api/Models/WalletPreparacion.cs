namespace Xpay.Api.Models;

// Preparación del propio titular. No contiene llaves en claro, credenciales ni
// biometría; la Wallet solo se crea en el último paso confirmado por el usuario.
public sealed class WalletPreparacion
{
    public long IdUsuario { get; set; }
    public long IdPersona { get; set; }
    public string OpcionLlave { get; set; } = "PENDIENTE";
    public string? LlaveTipo { get; set; }
    public string? LlaveHash { get; set; }
    public string? LlaveMasked { get; set; }
    public bool PropiedadConfirmada { get; set; }
    public long? IdWallet { get; set; }
    public DateTime? FechaActivacion { get; set; }
    public string? CriteriosVersion { get; set; }
    public string? CondicionesVersion { get; set; }
    public string? CondicionesSha256 { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime FechaActualizacion { get; set; }
}
