namespace Xpay.Api.Common;

// P0-B3 — fallo de autenticación con respuesta pública única. Cubre usuario
// inexistente, contraseña incorrecta y cualquier estado no ACTIVO
// (PENDIENTE_VERIFICACION, BLOQUEADO, INACTIVO u otro): el cliente nunca
// puede distinguirlos. NO deriva de InvalidOperationException a propósito: el
// login solo devuelve este mensaje fijo y cualquier otra excepción (incluidas
// las InvalidOperationException internas de EF) termina en el 500 genérico.
public sealed class CredencialesInvalidasException : Exception
{
    public const string MensajePublico = "Usuario o contraseña inválidos.";

    public CredencialesInvalidasException() : base(MensajePublico) { }
}
