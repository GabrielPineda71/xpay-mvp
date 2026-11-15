namespace Xpay.Api.Models;

// Ver database/050_verificacion_contactos.sql. Código de un solo uso para
// verificar el correo (EMAIL) o el celular (WHATSAPP). Solo se persiste
// SHA-256(sal || código); el código en claro nunca se guarda ni se registra.
public class ContactoCodigoVerificacion
{
    public long IdCodigo { get; set; }
    public long IdUsuario { get; set; }
    public string Canal { get; set; } = string.Empty;
    public string Destino { get; set; } = string.Empty;
    public byte[] CodigoSal { get; set; } = Array.Empty<byte>();
    public byte[] CodigoHash { get; set; } = Array.Empty<byte>();
    public int IntentosFallidos { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime FechaExpiracion { get; set; }
    public DateTime? FechaUso { get; set; }
    public DateTime? FechaInvalidacion { get; set; }
    public string? MotivoInvalidacion { get; set; }
    public string? IpSolicitud { get; set; }
}

// Historial de autorizaciones (solo inserción): el estado vigente es la última
// fila por (usuario, canal, finalidad). Independiente de la verificación.
public class UsuarioConsentimientoComunicacion
{
    public long IdConsentimiento { get; set; }
    public long IdUsuario { get; set; }
    public string Canal { get; set; } = string.Empty;
    public string Finalidad { get; set; } = string.Empty;
    public bool Otorgado { get; set; }
    public string Origen { get; set; } = string.Empty;
    public DateTime FechaRegistro { get; set; }
    public string? IpSolicitud { get; set; }
}

public static class CanalContacto
{
    public const string Email    = "EMAIL";
    public const string WhatsApp = "WHATSAPP";

    public static bool EsValido(string? canal) => canal is Email or WhatsApp;
}

public static class FinalidadComunicacion
{
    public const string Servicio   = "SERVICIO";
    public const string Publicidad = "PUBLICIDAD";

    public static bool EsValida(string? finalidad) => finalidad is Servicio or Publicidad;
}
