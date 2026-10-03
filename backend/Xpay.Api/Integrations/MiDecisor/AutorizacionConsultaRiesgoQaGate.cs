using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Xpay.Api.Integrations.MiDecisor;

// XPAY-656 — compuerta de QA para la PRIMERA consulta real de MiDecisor.
//
// Decora la autorización durable (consentimiento V1,
// AutorizacionConsultaRiesgoDurable) y la restringe a UNA sola solicitud
// configurada explícitamente. Devuelve true EXCLUSIVAMENTE si se cumplen
// TODAS estas condiciones:
//   1. MIDECISOR_QA_ALLOWED_SOLICITUD_ID representa exactamente UN long > 0
//      (sólo dígitos ASCII: sin signo, espacios, separadores ni listas) y es
//      igual a idSolicitud;
//   2. MIDECISOR_STOP_AFTER_CONSULTA vale exactamente "true" (comparación
//      ordinal). Sin el stop activo la consulta no se habilita, para que la
//      ejecución nunca avance a consumo, decisión ni cupo;
//   3. la autorización durable devuelve true. El consentimiento sigue siendo
//      obligatorio; esta clase NUNCA lo sustituye.
// Cualquier otra combinación devuelve false (fail-closed). Sin configuración
// nadie queda autorizado: registrar esta clase en DI tiene el mismo efecto que
// el stub AutorizacionConsultaRiesgoNoDisponible.
//
// La configuración se evalúa ANTES de consultar la autorización durable; sin
// configuración QA no se accede a la base de datos.
//
// En DI la dependencia interna es SIEMPRE AutorizacionConsultaRiesgoDurable
// (ver CarteraRiesgoRuntimeWiring). Se recibe como IConsultaRiesgoAutorizacion
// únicamente para poder probar el gate sin SQL.
//
// Logging saneado: nunca idSolicitud, idUsuario, valores de configuración,
// documento, apellido ni secretos; sólo el motivo genérico del rechazo.
public sealed class AutorizacionConsultaRiesgoQaGate(
    IConsultaRiesgoAutorizacion autorizacionDurable,
    IConfiguration configuration,
    ILogger<AutorizacionConsultaRiesgoQaGate> logger)
    : IConsultaRiesgoAutorizacion
{
    // Expuesto sólo para verificar el wiring de DI en tests.
    internal IConsultaRiesgoAutorizacion AutorizacionDurable => autorizacionDurable;

    public async Task<bool> TieneAutorizacionVigenteAsync(
        long idUsuario, long idSolicitud, CancellationToken cancellationToken = default)
    {
        if (!MiDecisorQaGateConfig.TryLeerSolicitudPermitida(configuration, out var permitida))
        {
            logger.LogInformation("midecisor.qagate: no hay solicitud QA permitida configurada; no autorizado.");
            return false;
        }

        if (idSolicitud != permitida)
        {
            logger.LogInformation("midecisor.qagate: la solicitud no es la permitida en QA; no autorizado.");
            return false;
        }

        if (!MiDecisorQaGateConfig.StopAfterConsultaActivo(configuration))
        {
            logger.LogWarning("midecisor.qagate: STOP_AFTER_CONSULTA no está activo; no autorizado.");
            return false;
        }

        // La cancelación se propaga. La autorización durable ya es fail-closed
        // ante errores operacionales.
        return await autorizacionDurable
            .TieneAutorizacionVigenteAsync(idUsuario, idSolicitud, cancellationToken)
            .ConfigureAwait(false);
    }
}

// XPAY-656 — claves e interpretación ESTRICTA de la configuración QA para la
// primera consulta. La comparten el gate y el orquestador (stop).
public static class MiDecisorQaGateConfig
{
    public const string EnvAllowedSolicitudId = "MIDECISOR_QA_ALLOWED_SOLICITUD_ID";
    public const string EnvStopAfterConsulta  = "MIDECISOR_STOP_AFTER_CONSULTA";

    // Único valor que activa el stop. Comparación ordinal exacta.
    public const string ValorStopActivo = "true";

    // Exactamente UN id: sólo dígitos ASCII (NumberStyles.None: sin signo,
    // espacios, separadores de miles ni listas) y mayor que cero.
    // Ausente, vacío o inválido → false.
    public static bool TryLeerSolicitudPermitida(IConfiguration configuration, out long idSolicitud)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        idSolicitud = 0;

        var raw = configuration[EnvAllowedSolicitudId];
        if (string.IsNullOrEmpty(raw))
            return false;

        if (!long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var valor) || valor <= 0)
            return false;

        idSolicitud = valor;
        return true;
    }

    // Activo SÓLO si el valor es exactamente "true" (ordinal). "True", "TRUE",
    // " true", "1", "yes", "on", "false", vacío o ausente → inactivo.
    public static bool StopAfterConsultaActivo(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return string.Equals(configuration[EnvStopAfterConsulta], ValorStopActivo, StringComparison.Ordinal);
    }
}
