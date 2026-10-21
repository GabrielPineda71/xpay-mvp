namespace Xpay.Api.Common;

// P0-B — propósitos admitidos por ck_ut_proposito (migración 048). Un token
// emitido para un propósito nunca es consumible con el otro.
public static class PropositoTokenUsuario
{
    public const string VerificarEmail = "VERIFICAR_EMAIL";
    public const string RecuperarClave = "RECUPERAR_CLAVE";

    public static bool EsValido(string? proposito) =>
        string.Equals(proposito, VerificarEmail, StringComparison.Ordinal)
        || string.Equals(proposito, RecuperarClave, StringComparison.Ordinal);
}
