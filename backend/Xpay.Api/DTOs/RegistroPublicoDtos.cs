namespace Xpay.Api.DTOs;

// P0-B — registro público de usuario final. Deliberadamente SIN
// IdUnidadNegocio, rol, empresa, comercio ni ningún identificador interno de
// autorización: el backend los decide. Documento y nombres los aporta después
// el KYC, igual que registro-inicial (migraciones 032/033). ToString() no
// revela contraseña, email ni celular.
public sealed record RegistroPublicoRequest(
    string Email,
    string NombreUsuario,
    string Password,
    string Celular)
{
    public override string ToString() =>
        $"RegistroPublicoRequest {{ NombreUsuario = {NombreUsuario}, Email = ***, Password = ***, Celular = *** }}";
}

// Resultado de RegistroPublicoValidacion. No incluye la contraseña.
public sealed record RegistroPublicoNormalizado(string Email, string NombreUsuario, string Celular);
