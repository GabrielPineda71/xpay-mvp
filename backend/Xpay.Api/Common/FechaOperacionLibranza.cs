namespace Xpay.Api.Common;

// XPAY-717 — fecha efectiva del flujo de Libranza del cliente (mi-cupo y
// anticipos). Una fecha suministrada por el cliente ("fecha simulada") sólo
// existe en ambiente no productivo: IAmbienteXpay.PermiteMecanismosNoProductivos
// es la ÚNICA autoridad. En productivo cualquier valor presente (válido, vacío,
// espacios o basura) se rechaza SIN parsearlo; ninguna configuración lo
// reactiva. Helper puro: no lee IConfiguration, IHostEnvironment ni el reloj
// (recibe hoyUtc), y no añade reglas de rango: en no productivo conserva el
// parseo existente (DateOnly.TryParse).
public enum ResultadoFechaLibranza
{
    // El cliente no envió fecha (null): fecha efectiva = hoyUtc.
    NoSuministrada,
    // Ambiente no productivo y fecha válida: fecha efectiva = la enviada.
    Simulada,
    // Ambiente productivo y fecha presente: la solicitud debe rechazarse.
    RechazadaAmbienteProductivo,
    // Ambiente no productivo y fecha no interpretable.
    FormatoInvalido,
}

public static class FechaOperacionLibranza
{
    public static ResultadoFechaLibranza Resolver(
        IAmbienteXpay ambiente, string? fechaSolicitada, DateOnly hoyUtc, out DateOnly fecha)
    {
        ArgumentNullException.ThrowIfNull(ambiente);
        fecha = hoyUtc;

        if (fechaSolicitada is null)
            return ResultadoFechaLibranza.NoSuministrada;

        if (!ambiente.PermiteMecanismosNoProductivos)
            return ResultadoFechaLibranza.RechazadaAmbienteProductivo;

        if (!DateOnly.TryParse(fechaSolicitada, out var simulada))
            return ResultadoFechaLibranza.FormatoInvalido;

        fecha = simulada;
        return ResultadoFechaLibranza.Simulada;
    }
}
