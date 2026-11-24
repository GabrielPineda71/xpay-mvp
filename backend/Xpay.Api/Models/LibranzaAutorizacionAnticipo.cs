namespace Xpay.Api.Models;

// Permiso expreso de producto. No representa un cupo ni una decisión de desembolso.
public sealed class LibranzaAutorizacionAnticipo
{
    public long IdEmpleadoUsuario { get; set; }
    public long IdUsuario { get; set; }
    public long IdEmpleado { get; set; }
    public long IdConvenio { get; set; }
    public long? IdWallet { get; set; }
    public string Estado { get; set; } = "REVOCADO";
    public long Revision { get; set; }
    public string HuellaContexto { get; set; } = "";
    public long CreadoPorUsuario { get; set; }
    public DateTime FechaCreacion { get; set; }
    public long ModificadoPorUsuario { get; set; }
    public DateTime FechaCambio { get; set; }
    public DateTime? FechaAutorizacion { get; set; }
    public DateTime? FechaRevocacion { get; set; }
    public string ReferenciaRevision { get; set; } = "";
}
