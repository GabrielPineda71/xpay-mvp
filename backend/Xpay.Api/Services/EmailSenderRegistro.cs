using Xpay.Api.Common;

namespace Xpay.Api.Services;

// P0-B — selección del sender por ambiente con la frontera central
// AmbienteXpay (lista cerrada Development/QA/Test). Ninguna configuración
// puede activar un sender que envíe en PROD.
//
// QA-IDENTIDAD-EMAIL-3 — matriz cerrada, evaluada al ARRANQUE:
// - Ambiente productivo (cualquiera fuera de la lista): EmailSenderNoConfigurado.
//   Si aparece Email:Modo o la sección Email:QaSmtp, el arranque falla.
// - Development/QA/Test sin Email:Modo: EmailSenderLocalSinEnvio (no abre red).
// - Email:Modo distinto de "QaSmtp" (comparación ordinal): el arranque falla.
// - Email:Modo = "QaSmtp" fuera de QA exacto (Development/Test): el arranque falla.
// - QA + "QaSmtp": EmailSenderQaSmtp solo si Email:QaSmtp:* e Identidad:* son
//   válidas; si no, el arranque falla. Nunca hay caída silenciosa a otro sender.
public static class EmailSenderRegistro
{
    public static IServiceCollection AddEmailSenderXpay(
        this IServiceCollection services, string? environmentName, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var modo = configuration[OpcionesEmailQaSmtp.ClaveModo];

        if (!AmbienteXpay.EsNoProductivo(environmentName))
        {
            if (!string.IsNullOrEmpty(modo) || configuration.GetSection(OpcionesEmailQaSmtp.Seccion).Exists())
                throw new EmailConfiguracionException(
                    "Configuración de email QA presente en un ambiente productivo; arranque abortado.");
            services.AddSingleton<IEmailSender, EmailSenderNoConfigurado>();
            return services;
        }

        if (string.IsNullOrEmpty(modo))
        {
            services.AddSingleton<IEmailSender, EmailSenderLocalSinEnvio>();
            return services;
        }

        if (!string.Equals(modo, OpcionesEmailQaSmtp.ModoQaSmtp, StringComparison.Ordinal))
            throw new EmailConfiguracionException("Email:Modo no reconocido; arranque abortado.");

        if (!string.Equals(environmentName, AmbienteXpay.Qa, StringComparison.Ordinal))
            throw new EmailConfiguracionException(
                "Email:Modo=QaSmtp solo se admite en el ambiente QA; arranque abortado.");

        var opciones = OpcionesEmailQaSmtp.Desde(configuration);

        // Enlaces de identidad obligatorios y con https: se validan con la
        // regla productiva de PlantillasEmailIdentidad (sin destino .invalid).
        var plantillasEstrictas = new PlantillasEmailIdentidad(configuration, new AmbienteValidacionEstricta());
        plantillasEstrictas.ValidarConfiguracion();
        plantillasEstrictas.ValidarConfiguracionRecuperacion();

        services.AddSingleton<IEmailSender>(sp => new EmailSenderQaSmtp(
            opciones, new TransporteSmtpQa(opciones), sp.GetRequiredService<ILogger<EmailSenderQaSmtp>>()));
        return services;
    }

    // Solo para validar al arranque: aplica la regla productiva (https y
    // configuración presente) sin cambiar el ambiente real de la aplicación.
    private sealed class AmbienteValidacionEstricta : IAmbienteXpay
    {
        public string Nombre => "validacion-email-qa";
        public bool PermiteMecanismosNoProductivos => false;
        public bool EsProductivo => true;
    }
}
