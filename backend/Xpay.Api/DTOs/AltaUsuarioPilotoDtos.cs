namespace Xpay.Api.DTOs;

// Piloto PROD — corrección 2b: alta controlada del usuario final del piloto
// por un SUPERUSUARIO. Sin contraseña (se emite después con
// restablecer-clave) y sin documento (lo aporta Veriff con la opción A).
// Los campos de consentimiento son obligatorios: sin evidencia de aceptación
// de los documentos legales vigentes no hay alta.
public sealed class AltaUsuarioPilotoRequest
{
    public string?   NombreUsuario          { get; set; }
    public string?   Email                  { get; set; }
    public string?   Celular                { get; set; }
    public string?   VersionTerminos        { get; set; }
    public string?   VersionPoliticaDatos   { get; set; }
    public string?   VersionAutorizacionKyc { get; set; }
    public DateTime? FechaAceptacionUtc     { get; set; }
    // Referencia a la evidencia de aceptación (p. ej. identificador del
    // documento firmado). Solo [A-Za-z0-9._:/-]: nunca datos personales.
    public string?   ReferenciaEvidencia    { get; set; }
}

public sealed record AltaUsuarioPilotoResponse(long IdUsuario, long IdWallet, string Siguiente);
