using Xpay.Api.Integrations.MiDecisor;
using Xpay.Api.Tests.Services;
using Xunit;

namespace Xpay.Api.Tests.Integrations.MiDecisor;

// XPAY-656 — tests offline del gate QA de la primera consulta real. SIN red,
// SIN base de datos, SIN credenciales, SIN cédulas: la autorización durable se
// sustituye por FakeConsultaRiesgoAutorizacion (cuenta llamadas) y la
// configuración por FakeConfiguration. Los ids son sintéticos.
public sealed class AutorizacionConsultaRiesgoQaGateTests
{
    private const long IdUsuario   = 7;
    private const long IdSolicitud = 10;

    private static Dictionary<string, string?> ConfigValida() => new()
    {
        [MiDecisorQaGateConfig.EnvAllowedSolicitudId] = "10",
        [MiDecisorQaGateConfig.EnvStopAfterConsulta]  = "true",
    };

    private static (AutorizacionConsultaRiesgoQaGate Gate, FakeConsultaRiesgoAutorizacion Durable,
        CapturingLogger<AutorizacionConsultaRiesgoQaGate> Logger) Crear(
            Dictionary<string, string?> config, bool durableAutoriza = true)
    {
        var durable = new FakeConsultaRiesgoAutorizacion(durableAutoriza);
        var logger  = new CapturingLogger<AutorizacionConsultaRiesgoQaGate>();
        var gate    = new AutorizacionConsultaRiesgoQaGate(durable, new FakeConfiguration(config), logger);
        return (gate, durable, logger);
    }

    // ── Caso positivo único ────────────────────────────────────────────────
    [Fact]
    public async Task IdExacto_StopActivo_DurableTrue_Autoriza()
    {
        var (gate, durable, _) = Crear(ConfigValida(), durableAutoriza: true);

        Assert.True(await gate.TieneAutorizacionVigenteAsync(IdUsuario, IdSolicitud));
        Assert.Equal(1, durable.CallCount);
    }

    // ── Consentimiento durable obligatorio ─────────────────────────────────
    [Fact]
    public async Task IdExacto_StopActivo_DurableFalse_NoAutoriza()
    {
        var (gate, durable, _) = Crear(ConfigValida(), durableAutoriza: false);

        Assert.False(await gate.TieneAutorizacionVigenteAsync(IdUsuario, IdSolicitud));
        Assert.Equal(1, durable.CallCount);
    }

    // ── MIDECISOR_QA_ALLOWED_SOLICITUD_ID estricto ─────────────────────────
    // Configuración inválida → false y la durable NUNCA se consulta (sin DB).
    [Fact]
    public async Task AllowedId_Ausente_NoAutoriza_SinConsultarDurable()
    {
        var config = ConfigValida();
        config.Remove(MiDecisorQaGateConfig.EnvAllowedSolicitudId);
        var (gate, durable, _) = Crear(config);

        Assert.False(await gate.TieneAutorizacionVigenteAsync(IdUsuario, IdSolicitud));
        Assert.Equal(0, durable.CallCount);
    }

    [Theory]
    [InlineData("")]           // vacío
    [InlineData("abc")]        // no numérico
    [InlineData("0")]          // cero
    [InlineData("-10")]        // negativo
    [InlineData("10,11")]      // lista con coma
    [InlineData("10;11")]      // lista con punto y coma
    [InlineData("10 11")]      // lista con espacio
    [InlineData(" 10")]        // espacio inicial
    [InlineData("10 ")]        // espacio final
    [InlineData("+10")]        // signo
    [InlineData("1.0")]        // decimal
    [InlineData("99999999999999999999")] // desborda long
    public async Task AllowedId_Invalido_NoAutoriza_SinConsultarDurable(string valor)
    {
        var config = ConfigValida();
        config[MiDecisorQaGateConfig.EnvAllowedSolicitudId] = valor;
        var (gate, durable, _) = Crear(config);

        Assert.False(await gate.TieneAutorizacionVigenteAsync(IdUsuario, IdSolicitud));
        Assert.Equal(0, durable.CallCount);
    }

    [Fact]
    public async Task AllowedId_ValidoPeroDistinto_NoAutoriza_SinConsultarDurable()
    {
        var config = ConfigValida();
        config[MiDecisorQaGateConfig.EnvAllowedSolicitudId] = "11";
        var (gate, durable, _) = Crear(config);

        Assert.False(await gate.TieneAutorizacionVigenteAsync(IdUsuario, IdSolicitud));
        Assert.Equal(0, durable.CallCount);
    }

    // ── MIDECISOR_STOP_AFTER_CONSULTA estricto (sólo "true" exacto, ordinal) ─
    [Fact]
    public async Task Stop_Ausente_NoAutoriza_SinConsultarDurable()
    {
        var config = ConfigValida();
        config.Remove(MiDecisorQaGateConfig.EnvStopAfterConsulta);
        var (gate, durable, _) = Crear(config);

        Assert.False(await gate.TieneAutorizacionVigenteAsync(IdUsuario, IdSolicitud));
        Assert.Equal(0, durable.CallCount);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("")]
    [InlineData("True")]
    [InlineData("TRUE")]
    [InlineData(" true")]
    [InlineData("true ")]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("on")]
    public async Task Stop_ValorNoExacto_NoAutoriza_SinConsultarDurable(string valor)
    {
        var config = ConfigValida();
        config[MiDecisorQaGateConfig.EnvStopAfterConsulta] = valor;
        var (gate, durable, _) = Crear(config);

        Assert.False(await gate.TieneAutorizacionVigenteAsync(IdUsuario, IdSolicitud));
        Assert.Equal(0, durable.CallCount);
    }

    // ── Helper de configuración: semántica estricta compartida ─────────────
    [Theory]
    [InlineData("true", true)]
    [InlineData("True", false)]
    [InlineData("false", false)]
    [InlineData(null, false)]
    public void StopAfterConsultaActivo_SoloTrueExacto(string? valor, bool esperado)
    {
        var config = new FakeConfiguration(new Dictionary<string, string?>
        {
            [MiDecisorQaGateConfig.EnvStopAfterConsulta] = valor,
        });

        Assert.Equal(esperado, MiDecisorQaGateConfig.StopAfterConsultaActivo(config));
    }

    // ── Logging saneado ────────────────────────────────────────────────────
    [Fact]
    public async Task Rechazo_NoLogueaIdsNiValoresDeConfiguracion()
    {
        var config = ConfigValida();
        config[MiDecisorQaGateConfig.EnvAllowedSolicitudId] = "424242";
        var (gate, _, logger) = Crear(config);

        Assert.False(await gate.TieneAutorizacionVigenteAsync(IdUsuario, 535353));

        var todos = string.Join("\n", logger.Mensajes);
        Assert.NotEmpty(logger.Mensajes);
        Assert.DoesNotContain("424242", todos);
        Assert.DoesNotContain("535353", todos);
    }
}
