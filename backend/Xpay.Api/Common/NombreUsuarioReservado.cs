namespace Xpay.Api.Common;

// XPAY-717 — nombres de usuario reservados para cuentas QA ("qa.*").
//
// En ambiente productivo (IAmbienteXpay.PermiteMecanismosNoProductivos ==
// false, ÚNICA autoridad) no puede crearse ningún usuario cuyo nombre,
// normalizado como se persiste (Trim + minúsculas), empiece exactamente por
// "qa." (ordinal). En Development/QA/Test se permite. No es una política
// general de formato: "qa", "qa_x", "qa-x", "usuario.qa" u homóglifos son
// cadenas distintas que el sistema tampoco interpreta como "qa.*". Helper
// puro: no lee IConfiguration ni IHostEnvironment. No afecta login ni
// cuentas existentes: sólo se evalúa al crear.
public static class NombreUsuarioReservado
{
    public const string PrefijoQa = "qa.";

    // Misma forma con la que se persiste el nombre, sin depender de la cultura.
    public static string Normalizar(string? usuario) =>
        (usuario ?? string.Empty).Trim().ToLowerInvariant();

    public static bool EsReservadoQa(string? usuario) =>
        Normalizar(usuario).StartsWith(PrefijoQa, StringComparison.Ordinal);

    public static bool PermiteCrear(string? usuario, IAmbienteXpay ambiente)
    {
        ArgumentNullException.ThrowIfNull(ambiente);
        return ambiente.PermiteMecanismosNoProductivos || !EsReservadoQa(usuario);
    }
}
