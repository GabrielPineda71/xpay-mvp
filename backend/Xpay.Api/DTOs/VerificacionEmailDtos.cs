namespace Xpay.Api.DTOs;

// P0-B1 — el token de verificación viaja en el BODY, nunca en la query
// string (no queda en logs de acceso). ToString() no lo revela.
public sealed record VerificarEmailRequest(string? Token)
{
    public override string ToString() => "VerificarEmailRequest { Token = *** }";
}

// P0-B3 — reenvío de verificación: el identificador público es el nombre de
// usuario; nunca email ni ID.
public sealed record ReenviarVerificacionRequest(string? Usuario);
