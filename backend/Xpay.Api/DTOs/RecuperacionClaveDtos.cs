namespace Xpay.Api.DTOs;

// P0-B2 — recuperación de contraseña. El identificador público es el nombre
// de usuario: nunca se acepta email ni ID. Token y claves viajan en el BODY,
// nunca en la query string, y ToString() no los revela.
public sealed record SolicitarRecuperacionClaveRequest(string? Usuario);

public sealed record RestablecerClaveRecuperacionRequest(string? Token, string? ClaveNueva, string? ConfirmacionClaveNueva)
{
    public override string ToString() =>
        "RestablecerClaveRecuperacionRequest { Token = ***, ClaveNueva = ***, ConfirmacionClaveNueva = *** }";
}
