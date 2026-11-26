namespace Xpay.Api.DTOs;

// P0-B — registro público de usuario final. Deliberadamente SIN
// IdUnidadNegocio, rol, empresa, comercio ni ningún identificador interno de
// autorización: el backend los decide. El primer apellido se declara
// explícitamente al registrar; el documento y los nombres completos verificados
// los aporta el KYC. Nunca se divide lastName. ToString() oculta datos personales.
public sealed record RegistroPublicoRequest(
    string Email,
    string NombreUsuario,
    string Password,
    string Celular,
    RegistroPublicoConsentimiento? Consentimiento = null,
    string? PrimerApellido = null)
{
    public override string ToString() =>
        $"RegistroPublicoRequest {{ NombreUsuario = {NombreUsuario}, Email = ***, Password = ***, Celular = ***, PrimerApellido = *** }}";
}

public sealed record RegistroPublicoConsentimiento(
    bool AceptaTerminos,
    string? VersionTerminos,
    bool AutorizaTratamientoDatos,
    string? VersionPoliticaDatos);

public sealed record RegistroPublicoDocumentoResponse(string Version, DateOnly Fecha, string Url);

public sealed record RegistroPublicoConfiguracionResponse(
    RegistroPublicoDocumentoResponse Terminos,
    RegistroPublicoDocumentoResponse PoliticaDatos);

// Resultado validado: PrimerApellido ya es obligatorio, FormC + Trim, sin controles.
// No reutilizarlo como evidencia de identidad verificada por KYC.
public sealed record RegistroPublicoNormalizado(
    string Email, string NombreUsuario, string Celular, string PrimerApellido)
{
    public override string ToString() =>
        "RegistroPublicoNormalizado { Email = ***, NombreUsuario = ***, Celular = ***, PrimerApellido = *** }";
}
