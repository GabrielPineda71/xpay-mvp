namespace Xpay.Api.Common;

// XPAY-717 — exposición de Swagger/API Docs subordinada a la frontera central.
//
// IAmbienteXpay.PermiteMecanismosNoProductivos es la ÚNICA autoridad: si es
// false, los docs no existen, sea cual sea ApiDocs:EnableSwagger o el nombre
// del ambiente. La configuración sólo decide (o apaga) dentro de un ambiente
// no productivo. Sin configuración se conserva el fallback histórico: sólo
// Development. Esta clase no lee IConfiguration ni IHostEnvironment: recibe
// ambos datos ya resueltos.
public static class ApiDocsPolicy
{
    public const string ClaveEnableSwagger = "ApiDocs:EnableSwagger";

    public static bool SwaggerHabilitado(IAmbienteXpay ambiente, bool? enableSwaggerConfig)
    {
        ArgumentNullException.ThrowIfNull(ambiente);

        if (!ambiente.PermiteMecanismosNoProductivos)
            return false;

        return enableSwaggerConfig
            ?? string.Equals(ambiente.Nombre, AmbienteXpay.Development, StringComparison.Ordinal);
    }
}
