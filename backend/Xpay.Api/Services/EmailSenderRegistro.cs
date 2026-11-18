using Xpay.Api.Common;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Xpay.Api.Services;

// Selección explícita por ambiente. Production admite solo ACS y nunca SMTP
// de QA. Sin modo configurado no envía ni cae a otro proveedor.
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
            if (configuration.GetSection(OpcionesEmailQaSmtp.Seccion).Exists())
                throw new EmailConfiguracionException(
                    "Configuración de email QA presente en un ambiente productivo; arranque abortado.");

            if (string.Equals(modo, EmailSenderAcs.Modo, StringComparison.Ordinal)
                && string.Equals(environmentName, "Production", StringComparison.Ordinal))
            {
                var opcionesAcs = OpcionesEmailAcs.Desde(configuration)
                    ?? throw new EmailConfiguracionException("Configuración ACS de identidad ausente o inválida.");
                new PlantillasEmailIdentidad(configuration, new AmbienteValidacionEstricta()).ValidarConfiguracion();
                services.AddHttpClient(EmailSenderAcs.Cliente, c => c.Timeout = TimeSpan.FromSeconds(15));
                services.AddHttpClient(ConfiguracionCanalesContacto.ClienteIdentidad, c => c.Timeout = TimeSpan.FromSeconds(10));
                services.TryAddSingleton(TimeProvider.System);
                services.TryAddSingleton<ProveedorTokenIdentidadAdministrada>();
                services.AddSingleton<IEmailSender>(sp => new EmailSenderAcs(opcionesAcs,
                    sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<ProveedorTokenIdentidadAdministrada>()));
                return services;
            }

            if (!string.IsNullOrEmpty(modo))
                throw new EmailConfiguracionException("Modo de email no admitido en este ambiente productivo.");
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
