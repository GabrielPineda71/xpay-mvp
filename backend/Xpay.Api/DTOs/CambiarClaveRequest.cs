namespace Xpay.Api.DTOs;

// XPAY-400 — POST /api/auth/cambiar-clave (voluntario, usuario ya
// autenticado con clave vigente). Deliberadamente SOLO estos dos campos —
// NUNCA idUsuario/idPersona/username/email/roles: la identidad objetivo
// sale exclusivamente del JWT (ver AuthController.CambiarClave). Sin
// ConfirmacionClaveNueva a propósito (a diferencia de
// CambiarClaveObligatoriaRequest) — contrato mínimo explícito del ticket.
public class CambiarClaveRequest
{
    public string ClaveActual { get; set; } = string.Empty;
    public string ClaveNueva { get; set; } = string.Empty;
}
