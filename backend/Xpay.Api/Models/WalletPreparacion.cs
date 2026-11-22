namespace Xpay.Api.Models;

// Prueba durable de activación. Las llaves se registran después en Perfil o al
// retirar; no forman parte del registro ni se guardan en esta tabla.
public sealed class WalletPreparacion
{
    public long IdUsuario { get; set; }
    public long IdPersona { get; set; }
    public long IdWallet { get; set; }
    public DateTime FechaActivacion { get; set; }
    public string CriteriosVersion { get; set; } = string.Empty;
    public string TerminosVersion { get; set; } = string.Empty;
    public string TerminosSha256 { get; set; } = string.Empty;
    public string PoliticaDatosVersion { get; set; } = string.Empty;
    public string PoliticaDatosSha256 { get; set; } = string.Empty;
    public DateTime FechaCreacion { get; set; }
    public DateTime FechaActualizacion { get; set; }
}
