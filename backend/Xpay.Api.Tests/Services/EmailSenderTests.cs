using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xpay.Api.Common;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// P0-B — IEmailSender: el sender local no envía ni registra el token; los
// ambientes productivos reciben un sender fail-closed.
// QA-IDENTIDAD-EMAIL-3 — Email:Modo=QaSmtp solo en QA exacto y con
// configuración válida; en cualquier otro caso, sender local o fallo al arranque.
// R3 — la allowlist es Email:QaSmtp:DestinatariosPermitidos (direcciones
// exactas); la clave obsoleta DominiosPermitidos falla al arranque.
public class EmailSenderTests
{
    private const string Token        = "TOKEN-SINTETICO-0123456789abcdefghijklmnopqrstu";
    private const string Destinatario = "persona.secreta@example.test";

    private static MensajeEmail Mensaje() =>
        new(Destinatario, "Verifica tu email", $"Enlace: https://local.test/verificar#{Token}");

    [Fact]
    public async Task Local_NoRegistraTokenNiCuerpoNiParteLocal()
    {
        var logger = new LoggerCaptura<EmailSenderLocalSinEnvio>();
        await new EmailSenderLocalSinEnvio(logger).EnviarAsync(Mensaje());

        Assert.NotEmpty(logger.Textos);
        Assert.All(logger.Textos, t =>
        {
            Assert.True(t.IndexOf(Token, StringComparison.Ordinal) < 0);
            Assert.True(t.IndexOf("https://", StringComparison.Ordinal) < 0);
            Assert.True(t.IndexOf("persona.secreta", StringComparison.Ordinal) < 0);
        });
        Assert.Contains(logger.Textos, t => t.Contains("***@example.test", StringComparison.Ordinal));
    }

    [Fact]
    public void MensajeEmail_ToString_NoRevelaCuerpoNiDestinatario()
    {
        var texto = Mensaje().ToString();
        Assert.True(texto.IndexOf(Token, StringComparison.Ordinal) < 0);
        Assert.True(texto.IndexOf("persona.secreta", StringComparison.Ordinal) < 0);
        Assert.Contains("CuerpoTexto = ***", texto);
    }

    [Fact]
    public async Task NoConfigurado_FallaCerrado()
        => await Assert.ThrowsAsync<InvalidOperationException>(
            () => new EmailSenderNoConfigurado().EnviarAsync(Mensaje()));

    [Theory]
    [InlineData("Development")]
    [InlineData("QA")]
    [InlineData("Test")]
    public void Registro_AmbienteNoProductivo_UsaSenderLocal(string ambiente)
        => Assert.IsType<EmailSenderLocalSinEnvio>(Resolver(ambiente));

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("qa")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Registro_AmbienteProductivo_FallaCerrado(string? ambiente)
    {
        var sender = Resolver(ambiente);
        Assert.IsType<EmailSenderNoConfigurado>(sender);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.EnviarAsync(Mensaje()));
    }

    // ── QA-IDENTIDAD-EMAIL-3: matriz de registro por ambiente y Email:Modo ──

    private const string ClaveSmtpCentinela = "CLAVE-SMTP-CENTINELA-NO-DEBE-APARECER";

    private static Dictionary<string, string?> ConfigQaSmtpCompleta() => new()
    {
        [OpcionesEmailQaSmtp.ClaveModo]                    = OpcionesEmailQaSmtp.ModoQaSmtp,
        [OpcionesEmailQaSmtp.ClaveHost]                    = "smtp.proveedor.test",
        [OpcionesEmailQaSmtp.ClavePuerto]                  = "587",
        [OpcionesEmailQaSmtp.ClaveUsuario]                 = "usuario-qa@remitente.test",
        [OpcionesEmailQaSmtp.ClaveClave]                   = ClaveSmtpCentinela,
        [OpcionesEmailQaSmtp.ClaveRemitente]               = "qa-no-reply@remitente.test",
        [OpcionesEmailQaSmtp.ClaveDestinatariosPermitidos] = "qa.uno@permitido.test; qa.dos@permitido.test",
        [OpcionesEmailQaSmtp.ClaveTimeoutSegundos]         = "30",
        [PlantillasEmailIdentidad.ClaveUrlFrontend]        = "https://qa.xpay.test",
        [PlantillasEmailIdentidad.ClaveRutaVerificacion]   = "/verificar-email",
        [PlantillasEmailIdentidad.ClaveRutaRecuperacion]   = "/restablecer-clave",
    };

    [Fact]
    public void Registro_Qa_QaSmtpCompleto_UsaSenderQaSmtp()
        => Assert.IsType<EmailSenderQaSmtp>(Resolver("QA", ConfigQaSmtpCompleta()));

    [Theory]
    [InlineData("Development")]
    [InlineData("QA")]
    [InlineData("Test")]
    public void Registro_NoProductivo_ClavesQaSmtpSinModo_UsaSenderLocal(string ambiente)
    {
        var valores = ConfigQaSmtpCompleta();
        valores.Remove(OpcionesEmailQaSmtp.ClaveModo);
        Assert.IsType<EmailSenderLocalSinEnvio>(Resolver(ambiente, valores));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void Registro_QaSmtpFueraDeQa_FallaAlArranque(string ambiente)
        => AssertFallaArranque(() => Resolver(ambiente, ConfigQaSmtpCompleta()));

    [Theory]
    [InlineData("QA", "qasmtp")]
    [InlineData("QA", "QaSmtp ")]
    [InlineData("QA", " ")]
    [InlineData("QA", "Smtp")]
    [InlineData("Development", "Smtp")]
    [InlineData("Test", "QASMTP")]
    public void Registro_ModoDesconocido_FallaAlArranque(string ambiente, string modo)
    {
        var valores = ConfigQaSmtpCompleta();
        valores[OpcionesEmailQaSmtp.ClaveModo] = modo;
        AssertFallaArranque(() => Resolver(ambiente, valores));
    }

    [Theory]
    [InlineData(OpcionesEmailQaSmtp.ClaveHost, null)]
    [InlineData(OpcionesEmailQaSmtp.ClaveHost, "")]
    [InlineData(OpcionesEmailQaSmtp.ClaveHost, "smtp proveedor.test")]
    [InlineData(OpcionesEmailQaSmtp.ClaveHost, "smtp://proveedor.test")]
    [InlineData(OpcionesEmailQaSmtp.ClavePuerto, null)]
    [InlineData(OpcionesEmailQaSmtp.ClavePuerto, "0")]
    [InlineData(OpcionesEmailQaSmtp.ClavePuerto, "65536")]
    [InlineData(OpcionesEmailQaSmtp.ClavePuerto, "465")]
    [InlineData(OpcionesEmailQaSmtp.ClavePuerto, "587x")]
    [InlineData(OpcionesEmailQaSmtp.ClavePuerto, "-587")]
    [InlineData(OpcionesEmailQaSmtp.ClaveUsuario, null)]
    [InlineData(OpcionesEmailQaSmtp.ClaveUsuario, " ")]
    [InlineData(OpcionesEmailQaSmtp.ClaveClave, null)]
    [InlineData(OpcionesEmailQaSmtp.ClaveClave, "")]
    [InlineData(OpcionesEmailQaSmtp.ClaveRemitente, null)]
    [InlineData(OpcionesEmailQaSmtp.ClaveRemitente, "no-es-email")]
    [InlineData(OpcionesEmailQaSmtp.ClaveRemitente, "QA <qa-no-reply@remitente.test>")]
    [InlineData(OpcionesEmailQaSmtp.ClaveRemitente, EmailRemitentes.NoReply)]
    [InlineData(OpcionesEmailQaSmtp.ClaveRemitente, EmailRemitentes.Soporte)]
    [InlineData(OpcionesEmailQaSmtp.ClaveDestinatariosPermitidos, null)]
    [InlineData(OpcionesEmailQaSmtp.ClaveDestinatariosPermitidos, "")]
    [InlineData(OpcionesEmailQaSmtp.ClaveDestinatariosPermitidos, " ")]
    [InlineData(OpcionesEmailQaSmtp.ClaveDestinatariosPermitidos, "qa.uno@permitido.test,")]
    [InlineData(OpcionesEmailQaSmtp.ClaveDestinatariosPermitidos, "qa.uno@permitido.test,,qa.dos@permitido.test")]
    [InlineData(OpcionesEmailQaSmtp.ClaveDestinatariosPermitidos, "permitido.test")]
    [InlineData(OpcionesEmailQaSmtp.ClaveDestinatariosPermitidos, "*@permitido.test")]
    [InlineData(OpcionesEmailQaSmtp.ClaveDestinatariosPermitidos, "qa.*@permitido.test")]
    [InlineData(OpcionesEmailQaSmtp.ClaveDestinatariosPermitidos, "QA <qa.uno@permitido.test>")]
    [InlineData(OpcionesEmailQaSmtp.ClaveDestinatariosPermitidos, "qa.uno@permitido.test\r\nqa.dos@permitido.test")]
    [InlineData(OpcionesEmailQaSmtp.ClaveDestinatariosPermitidos, "qa.uno@permitido.test, no-es-email")]
    [InlineData(OpcionesEmailQaSmtp.ClaveDestinatariosPermitidos, "a@@permitido.test")]
    [InlineData(OpcionesEmailQaSmtp.ClaveTimeoutSegundos, null)]
    [InlineData(OpcionesEmailQaSmtp.ClaveTimeoutSegundos, "0")]
    [InlineData(OpcionesEmailQaSmtp.ClaveTimeoutSegundos, "121")]
    [InlineData(OpcionesEmailQaSmtp.ClaveTimeoutSegundos, "30s")]
    public void Registro_Qa_QaSmtpConfiguracionAusenteOInvalida_FallaAlArranque(string clave, string? valor)
    {
        var valores = ConfigQaSmtpCompleta();
        if (valor is null) valores.Remove(clave); else valores[clave] = valor;
        var ex = AssertFallaArranque(() => Resolver("QA", valores));
        Assert.Contains(clave, ex.Message);
    }

    [Theory]
    [InlineData(null, "permitido.test")]
    [InlineData("qa.uno@permitido.test", "permitido.test")]
    [InlineData("qa.uno@permitido.test", "")]
    public void Registro_Qa_DominiosPermitidosObsoleta_FallaAlArranque(string? destinatarios, string dominios)
    {
        var valores = ConfigQaSmtpCompleta();
        if (destinatarios is null) valores.Remove(OpcionesEmailQaSmtp.ClaveDestinatariosPermitidos);
        else valores[OpcionesEmailQaSmtp.ClaveDestinatariosPermitidos] = destinatarios;
        valores[OpcionesEmailQaSmtp.ClaveDominiosPermitidos] = dominios;
        var ex = AssertFallaArranque(() => Resolver("QA", valores));
        Assert.Contains(OpcionesEmailQaSmtp.ClaveDominiosPermitidos, ex.Message);
    }

    [Fact]
    public void Registro_Qa_DestinatariosEnFormaDeArray_FallaAlArranque()
    {
        var valores = ConfigQaSmtpCompleta();
        valores.Remove(OpcionesEmailQaSmtp.ClaveDestinatariosPermitidos);
        valores[OpcionesEmailQaSmtp.ClaveDestinatariosPermitidos + ":0"] = "qa.uno@permitido.test";
        var ex = AssertFallaArranque(() => Resolver("QA", valores));
        Assert.Contains(OpcionesEmailQaSmtp.ClaveDestinatariosPermitidos, ex.Message);
    }

    [Theory]
    [InlineData(PlantillasEmailIdentidad.ClaveUrlFrontend, null)]
    [InlineData(PlantillasEmailIdentidad.ClaveUrlFrontend, "http://qa.xpay.test")]
    [InlineData(PlantillasEmailIdentidad.ClaveUrlFrontend, "https://qa.xpay.test?x=1")]
    [InlineData(PlantillasEmailIdentidad.ClaveRutaVerificacion, null)]
    [InlineData(PlantillasEmailIdentidad.ClaveRutaVerificacion, "verificar-email")]
    [InlineData(PlantillasEmailIdentidad.ClaveRutaRecuperacion, null)]
    [InlineData(PlantillasEmailIdentidad.ClaveRutaRecuperacion, "/restablecer-clave?x=1")]
    public void Registro_Qa_QaSmtpSinIdentidadValida_FallaAlArranque(string clave, string? valor)
    {
        var valores = ConfigQaSmtpCompleta();
        if (valor is null) valores.Remove(clave); else valores[clave] = valor;
        var ex = Assert.Throws<IdentidadConfiguracionException>(() => Resolver("QA", valores));
        Assert.True(ex.Message.IndexOf(ClaveSmtpCentinela, StringComparison.Ordinal) < 0);
    }

    [Theory]
    [InlineData("Production", true, true)]
    [InlineData("Production", true, false)]
    [InlineData("Production", false, true)]
    [InlineData("Staging", true, true)]
    [InlineData("Staging", false, true)]
    [InlineData("qa", true, true)]
    [InlineData("", true, false)]
    [InlineData(null, false, true)]
    public void Registro_Productivo_ConConfiguracionEmail_FallaAlArranque(string? ambiente, bool conModo, bool conSeccion)
    {
        var valores = conSeccion ? ConfigQaSmtpCompleta() : new Dictionary<string, string?>();
        if (conModo) valores[OpcionesEmailQaSmtp.ClaveModo] = OpcionesEmailQaSmtp.ModoQaSmtp;
        else valores.Remove(OpcionesEmailQaSmtp.ClaveModo);
        AssertFallaArranque(() => Resolver(ambiente, valores));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("qa")]
    public void Registro_Productivo_SoloDestinatariosPermitidos_FallaAlArranque(string ambiente)
    {
        var valores = new Dictionary<string, string?> { [OpcionesEmailQaSmtp.ClaveDestinatariosPermitidos] = "qa.uno@permitido.test" };
        AssertFallaArranque(() => Resolver(ambiente, valores));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Registro_Productivo_SoloIdentidadSinEmail_UsaNoConfigurado(string ambiente)
    {
        var valores = new Dictionary<string, string?> { [PlantillasEmailIdentidad.ClaveUrlFrontend] = "https://app.xpay.test" };
        Assert.IsType<EmailSenderNoConfigurado>(Resolver(ambiente, valores));
    }

    private static EmailConfiguracionException AssertFallaArranque(Func<IEmailSender> resolver)
    {
        var ex = Assert.Throws<EmailConfiguracionException>(() => resolver());
        Assert.True(ex.Message.IndexOf(ClaveSmtpCentinela, StringComparison.Ordinal) < 0);
        return ex;
    }

    private static IEmailSender Resolver(string? ambiente, IDictionary<string, string?>? valores = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEmailSenderXpay(ambiente, new ConfigurationBuilder()
            .AddInMemoryCollection(valores ?? new Dictionary<string, string?>()).Build());
        return services.BuildServiceProvider().GetRequiredService<IEmailSender>();
    }

    private sealed class LoggerCaptura<T> : ILogger<T>
    {
        public List<string> Textos { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Textos.Add(formatter(state, exception));
            if (state is IEnumerable<KeyValuePair<string, object?>> valores)
                foreach (var v in valores)
                    Textos.Add(v.Value?.ToString() ?? string.Empty);
        }
    }
}
