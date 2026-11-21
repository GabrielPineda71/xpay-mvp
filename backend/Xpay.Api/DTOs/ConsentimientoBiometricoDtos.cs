namespace Xpay.Api.DTOs;

public sealed record ConsentimientoBiometricoResponse(
    RegistroPublicoDocumentoResponse? Documento,
    string TextoCasilla,
    bool Aceptado,
    bool Habilitado);

// No incluye idUsuario: la autorización solo puede otorgarla el usuario de la
// sesión. Un campo ausente o false nunca constituye una aceptación.
public sealed record AutorizarConsentimientoBiometricoRequest(
    bool AutorizaBiometria,
    string? VersionDocumento);
