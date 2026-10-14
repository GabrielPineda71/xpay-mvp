using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Xpay.Api.Integrations.MiDecisor;

// XPAY-717 — compuerta de MiDecisor para AMBIENTE PRODUCTIVO.
//
// Sólo se cablea cuando IAmbienteXpay NO permite mecanismos no productivos
// (ver CarteraRiesgoRuntimeWiring). Devuelve true EXCLUSIVAMENTE si se cumplen,
// en este orden:
//   1. MIDECISOR_PRODUCTION_ENABLED vale exactamente "true" (ordinal). Sin
//      ese valor no se accede a la base. La bandera sola NUNCA autoriza;
//   2. la autorización durable existente (solicitud propia, usuario ACTIVO,
//      persona ACTIVA, consentimiento V1) devuelve true;
//   3. el store devuelve la evidencia del titular documental (null =
//      identidad insuficiente → no autorizado);
//   4. ReglaReconsultaMiDecisor permite la consulta (ventana de 6 meses
//      calendario y ausencia de cupo ACTIVO/SUSPENDIDO).
// KYC: lo garantiza la policy "KycAprobado" (VERIFF en productivo) en el
// único endpoint que inicia la evaluación; no se duplica aquí.
// Las variables QA (MIDECISOR_QA_*, STOP_AFTER_CONSULTA) y SIMULACION_QA no se
// leen: no pueden autorizar ni alterar esta compuerta.
//
// Cualquier excepción distinta de cancelación → false (fail-closed). La
// cancelación se propaga. Logging saneado: sólo el motivo genérico.
public sealed class AutorizacionConsultaRiesgoProduccion(
    IConsultaRiesgoAutorizacion autorizacionDurable,
    IRiesgoProduccionStore store,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<AutorizacionConsultaRiesgoProduccion> logger)
    : IConsultaRiesgoAutorizacion
{
    // Expuesto sólo para verificar el wiring de DI en tests.
    internal IConsultaRiesgoAutorizacion AutorizacionDurable => autorizacionDurable;

    public async Task<bool> TieneAutorizacionVigenteAsync(
        long idUsuario, long idSolicitud, CancellationToken cancellationToken = default)
    {
        if (!MiDecisorProduccionConfig.Habilitado(configuration))
        {
            logger.LogInformation("midecisor.prodgate: MiDecisor productivo no habilitado; no autorizado.");
            return false;
        }

        try
        {
            if (!await autorizacionDurable
                    .TieneAutorizacionVigenteAsync(idUsuario, idSolicitud, cancellationToken)
                    .ConfigureAwait(false))
            {
                logger.LogInformation("midecisor.prodgate: autorización durable no vigente; no autorizado.");
                return false;
            }

            var snapshot = await store.CargarAsync(idSolicitud, cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                logger.LogWarning("midecisor.prodgate: identidad documental insuficiente; no autorizado.");
                return false;
            }

            if (!ReglaReconsultaMiDecisor.Permite(snapshot, timeProvider.GetUtcNow().UtcDateTime))
            {
                logger.LogInformation("midecisor.prodgate: ventana de reconsulta vigente o cupo existente; no autorizado.");
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("midecisor.prodgate: error evaluando la compuerta productiva; no autorizado.");
            return false;
        }
    }
}

// XPAY-717 — habilitación explícita de MiDecisor en ambiente productivo.
public static class MiDecisorProduccionConfig
{
    public const string EnvProductionEnabled = "MIDECISOR_PRODUCTION_ENABLED";

    // Único valor que habilita. Comparación ordinal exacta: "True", "TRUE",
    // " true", "1", "yes", vacío o ausente → no habilitado.
    public const string ValorHabilitado = "true";

    public static bool Habilitado(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return string.Equals(configuration[EnvProductionEnabled], ValorHabilitado, StringComparison.Ordinal);
    }
}
