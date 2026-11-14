namespace Xpay.Api.DTOs;

public sealed class SolicitarCodigoContactoRequest
{
    // Solo para el celular: autorización para recibir mensajes de SERVICIO
    // por WhatsApp (códigos y avisos de la cuenta). No incluye publicidad.
    public bool AceptaServicioWhatsApp { get; set; }
}

public sealed class ConfirmarCodigoContactoRequest
{
    public string? Codigo { get; set; }
}

// null = no cambiar.
public sealed class PreferenciasComunicacionRequest
{
    public bool? ServicioWhatsApp { get; set; }
    public bool? PublicidadEmail { get; set; }
    public bool? PublicidadWhatsApp { get; set; }
}

public sealed class EstadoContactosDto
{
    public bool EmailVerificado { get; set; }
    public bool CelularVerificado { get; set; }
    public bool CanalEmailDisponible { get; set; }
    public bool CanalWhatsAppDisponible { get; set; }
    // true solo cuando el requisito está activo Y ambos canales funcionan.
    public bool RequeridoParaOperar { get; set; }
    public bool ServicioWhatsApp { get; set; }
    public bool PublicidadEmail { get; set; }
    public bool PublicidadWhatsApp { get; set; }
}
