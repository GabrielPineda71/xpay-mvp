using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// QA-IDENTIDAD-EMAIL-3 — EmailSenderQaSmtp con transporte falso: nunca abre
// red. Allowlist antes del transporte, errores del proveedor saneados,
// cancelación propagada, tiempo agotado explícito y ningún token, cuerpo,
// credencial ni destinatario completo en logs o excepciones. R3: la allowlist
// es de direcciones exactas normalizadas (máximo 10 únicas).
public class EmailSenderQaSmtpTests
{
    private const string Token        = "TOKEN-SINTETICO-QA-0123456789abcdefghijklmnop";
    private const string Clave        = "CLAVE-SMTP-CENTINELA-NO-DEBE-APARECER";
    private const string Usuario      = "usuario-smtp-centinela@remitente.test";
    private const string Destinatario = "persona.secreta@permitido.test";
    private const string Asunto       = "Confirma tu correo en XPAY";
    private const string Cuerpo       = "Enlace: https://qa.xpay.test/verificar-email#token=" + Token;

    private static OpcionesEmailQaSmtp Opciones(TimeSpan? timeout = null) => new(
        "smtp.proveedor.test", 587, Usuario, Clave, "qa-no-reply@remitente.test",
        new[] { Destinatario }, timeout ?? TimeSpan.FromSeconds(30));

    private static MensajeEmail Mensaje(string destinatario = Destinatario) => new(destinatario, Asunto, Cuerpo);

    private static EmailSenderQaSmtp Sender(TransporteFalso transporte, LoggerCaptura<EmailSenderQaSmtp> logger, TimeSpan? timeout = null)
        => new(Opciones(timeout), transporte, logger);

    [Fact]
    public async Task DestinatarioPermitido_LlamaUnaVezAlTransporte()
    {
        var transporte = new TransporteFalso();
        var logger     = new LoggerCaptura<EmailSenderQaSmtp>();

        await Sender(transporte, logger).EnviarAsync(Mensaje(" Persona.Secreta@PERMITIDO.test "));

        Assert.Equal(1, transporte.Llamadas);
        var envio = Assert.Single(transporte.Envios);
        Assert.Equal(Destinatario, envio.Destinatario);
        Assert.Equal(EmailSenderQaSmtp.PrefijoAsunto + Asunto, envio.Asunto);
        Assert.Equal(Cuerpo, envio.CuerpoTexto);
        AssertSinDatosSensibles(logger.Textos);
        Assert.Contains(logger.Textos, t => t.Contains("***@permitido.test", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("otra.persona@permitido.test")]
    [InlineData("persona.secreta+alias@permitido.test")]
    [InlineData("persona.secret@permitido.test")]
    [InlineData("persona.secreta@sub.permitido.test")]
    [InlineData("persona.secreta@permitido.test.otro.test")]
    [InlineData("persona.secreta@otro.test")]
    [InlineData("persona@otro.test")]
    [InlineData("no-es-email")]
    [InlineData("")]
    [InlineData("Persona <persona.secreta@permitido.test>")]
    [InlineData("persona.secreta@permitido.test,otra@otro.test")]
    [InlineData("persona.secreta@permitido.test\r\nBcc: otra@otro.test")]
    public async Task DestinatarioNoPermitido_SeRechazaAntesDelTransporte(string destinatario)
    {
        var transporte = new TransporteFalso();
        var logger     = new LoggerCaptura<EmailSenderQaSmtp>();

        var ex = await Assert.ThrowsAsync<EmailEnvioQaException>(
            () => Sender(transporte, logger).EnviarAsync(Mensaje(destinatario)));

        Assert.Equal(EmailEnvioQaException.MensajeDestinoNoPermitido, ex.Message);
        Assert.Null(ex.InnerException);
        Assert.Equal(0, transporte.Llamadas);
        var textos = logger.Textos.Append(ex.ToString()).ToList();
        AssertSinDatosSensibles(textos);
        if (destinatario.Length > 0)
            Assert.All(textos, t => Assert.True(t.IndexOf(destinatario, StringComparison.Ordinal) < 0));
    }

    [Fact]
    public async Task FalloDelTransporte_SeSanea_SinMensajeNiCausaDelProveedor()
    {
        var transporte = new TransporteFalso
        {
            Falla = new SmtpException("5.7.1 rechazado " + Destinatario + " " + Token + " " + Clave),
        };
        var logger = new LoggerCaptura<EmailSenderQaSmtp>();

        var ex = await Assert.ThrowsAsync<EmailEnvioQaException>(
            () => Sender(transporte, logger).EnviarAsync(Mensaje()));

        Assert.Equal(EmailEnvioQaException.MensajeTransporte, ex.Message);
        Assert.Null(ex.InnerException);
        Assert.Equal(1, transporte.Llamadas);
        AssertSinDatosSensibles(logger.Textos.Append(ex.ToString()));
        Assert.Contains(logger.Textos, t => t.Contains(nameof(SmtpException), StringComparison.Ordinal));
    }

    [Fact]
    public async Task CancelacionPrevia_SePropaga_SinTransporte()
    {
        var transporte = new TransporteFalso();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Sender(transporte, new LoggerCaptura<EmailSenderQaSmtp>()).EnviarAsync(Mensaje(), cts.Token));
        Assert.Equal(0, transporte.Llamadas);
    }

    [Fact]
    public async Task CancelacionDuranteElEnvio_SePropagaComoCancelacion()
    {
        var transporte = new TransporteFalso { Esperar = true };
        using var cts = new CancellationTokenSource();

        var envio = Sender(transporte, new LoggerCaptura<EmailSenderQaSmtp>()).EnviarAsync(Mensaje(), cts.Token);
        await transporte.Iniciado.Task;
        cts.Cancel();

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => envio);
        Assert.Equal(cts.Token, ex.CancellationToken);
    }

    [Fact]
    public async Task TiempoAgotado_FallaConMensajeFijo_NoComoCancelacion()
    {
        var transporte = new TransporteFalso { Esperar = true };
        var logger     = new LoggerCaptura<EmailSenderQaSmtp>();

        var ex = await Assert.ThrowsAsync<EmailEnvioQaException>(
            () => Sender(transporte, logger, TimeSpan.FromMilliseconds(100)).EnviarAsync(Mensaje()));

        Assert.Equal(EmailEnvioQaException.MensajeTiempoAgotado, ex.Message);
        Assert.Null(ex.InnerException);
        AssertSinDatosSensibles(logger.Textos.Append(ex.ToString()));
    }

    [Fact]
    public async Task MensajeNulo_FallaSinTransporte()
    {
        var transporte = new TransporteFalso();
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => Sender(transporte, new LoggerCaptura<EmailSenderQaSmtp>()).EnviarAsync(null!));
        Assert.Equal(0, transporte.Llamadas);
    }

    [Fact]
    public void Opciones_ToString_NoRevelaUsuarioNiClave()
    {
        var texto = Opciones().ToString();
        Assert.True(texto.IndexOf(Clave, StringComparison.Ordinal) < 0);
        Assert.True(texto.IndexOf(Usuario, StringComparison.Ordinal) < 0);
    }

    [Fact]
    public void Opciones_ToString_NoRevelaDestinatarios()
    {
        var texto = OpcionesEmailQaSmtp.Desde(Configuracion("qa.uno@permitido.test;qa.dos@permitido.test")).ToString();
        Assert.True(texto.IndexOf("qa.uno", StringComparison.Ordinal) < 0);
        Assert.True(texto.IndexOf("qa.dos", StringComparison.Ordinal) < 0);
        Assert.True(texto.IndexOf("@permitido.test", StringComparison.Ordinal) < 0);
        Assert.Contains("DestinatariosPermitidos = 2", texto);
    }

    [Fact]
    public void Envio_ToString_NoRevelaCuerpoNiDestinatario()
    {
        var texto = new EnvioSmtpQa(Destinatario, Asunto, Cuerpo).ToString();
        AssertSinDatosSensibles(new[] { texto });
        Assert.Contains("CuerpoTexto = ***", texto);
    }

    [Fact]
    public void Desde_ConfiguracionValida_NormalizaDestinatariosYRemitente()
    {
        var opciones = OpcionesEmailQaSmtp.Desde(
            Configuracion(" QA.Uno@Permitido.TEST ; qa.dos@permitido.test,qa.uno@permitido.test"));

        Assert.Equal(new[] { "qa.uno@permitido.test", "qa.dos@permitido.test" }, opciones.DestinatariosPermitidos);
        Assert.True(opciones.PermiteDestinatario("qa.uno@permitido.test"));
        Assert.False(opciones.PermiteDestinatario("qa.tres@permitido.test"));
        Assert.Equal("qa-no-reply@remitente.test", opciones.Remitente);
        Assert.Equal(587, opciones.Puerto);
        Assert.Equal(TimeSpan.FromSeconds(30), opciones.Timeout);
    }

    [Fact]
    public void Desde_ExactamenteDiezDestinatariosUnicos_EsValido()
    {
        var opciones = OpcionesEmailQaSmtp.Desde(Configuracion(Lista(OpcionesEmailQaSmtp.MaximoDestinatarios)));
        Assert.Equal(OpcionesEmailQaSmtp.MaximoDestinatarios, opciones.DestinatariosPermitidos.Count);
    }

    [Fact]
    public void Desde_OnceDestinatariosUnicos_FallaAlArranque()
    {
        var ex = Assert.Throws<EmailConfiguracionException>(
            () => OpcionesEmailQaSmtp.Desde(Configuracion(Lista(OpcionesEmailQaSmtp.MaximoDestinatarios + 1))));
        Assert.Contains(OpcionesEmailQaSmtp.ClaveDestinatariosPermitidos, ex.Message);
        Assert.True(ex.Message.IndexOf("@permitido.test", StringComparison.Ordinal) < 0);
    }

    [Fact]
    public void Desde_MasDeDiezEntradasQueDeduplicanADiez_EsValido()
    {
        // 10 únicas + 3 variantes de mayúsculas/espacios de entradas ya presentes.
        var entradas = Lista(OpcionesEmailQaSmtp.MaximoDestinatarios)
            + "; QA.1@PERMITIDO.TEST ,qa.2@permitido.test; Qa.10@Permitido.Test";

        var opciones = OpcionesEmailQaSmtp.Desde(Configuracion(entradas));

        Assert.Equal(OpcionesEmailQaSmtp.MaximoDestinatarios, opciones.DestinatariosPermitidos.Count);
        Assert.Equal(opciones.DestinatariosPermitidos.Count, opciones.DestinatariosPermitidos.Distinct(StringComparer.Ordinal).Count());
    }

    private static string Lista(int cantidad) =>
        string.Join(",", Enumerable.Range(1, cantidad).Select(i => "qa." + i + "@permitido.test"));

    private static IConfiguration Configuracion(string destinatarios) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [OpcionesEmailQaSmtp.ClaveHost]                    = "smtp.proveedor.test",
            [OpcionesEmailQaSmtp.ClavePuerto]                  = "587",
            [OpcionesEmailQaSmtp.ClaveUsuario]                 = Usuario,
            [OpcionesEmailQaSmtp.ClaveClave]                   = Clave,
            [OpcionesEmailQaSmtp.ClaveRemitente]               = " QA-No-Reply@Remitente.test ",
            [OpcionesEmailQaSmtp.ClaveDestinatariosPermitidos] = destinatarios,
            [OpcionesEmailQaSmtp.ClaveTimeoutSegundos]         = "30",
        }).Build();

    private static void AssertSinDatosSensibles(IEnumerable<string> textos) =>
        Assert.All(textos, t =>
        {
            Assert.True(t.IndexOf(Token, StringComparison.Ordinal) < 0);
            Assert.True(t.IndexOf(Clave, StringComparison.Ordinal) < 0);
            Assert.True(t.IndexOf(Usuario, StringComparison.Ordinal) < 0);
            Assert.True(t.IndexOf("persona.secreta", StringComparison.Ordinal) < 0);
            Assert.True(t.IndexOf("https://", StringComparison.Ordinal) < 0);
        });

    private sealed class TransporteFalso : ITransporteSmtpQa
    {
        public List<EnvioSmtpQa> Envios { get; } = new();
        public int Llamadas { get; private set; }
        public Exception? Falla { get; init; }
        public bool Esperar { get; init; }
        public TaskCompletionSource Iniciado { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task EnviarAsync(EnvioSmtpQa envio, CancellationToken ct)
        {
            Llamadas++;
            Iniciado.TrySetResult();
            if (Falla is not null) throw Falla;
            if (Esperar) await Task.Delay(Timeout.Infinite, ct);
            Envios.Add(envio);
        }
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
            if (exception is not null) Textos.Add(exception.ToString());
            if (state is IEnumerable<KeyValuePair<string, object?>> valores)
                foreach (var v in valores)
                    Textos.Add(v.Value?.ToString() ?? string.Empty);
        }
    }
}
