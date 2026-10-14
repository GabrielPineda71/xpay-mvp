using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Xpay.Api.Controllers;
using Xpay.Api.Integrations.MiDecisor;
using Xpay.Api.Services;
using Xpay.Api.Tests.Services;
using Xunit;

namespace Xpay.Api.Tests.Integrations.MiDecisor;

// XPAY-717 — compuerta productiva de MiDecisor. Tests offline: SIN red, SIN
// base de datos, SIN credenciales, SIN cédulas. La durable es
// FakeConsultaRiesgoAutorizacion (cuenta llamadas), el store un fake que
// cuenta llamadas y el reloj FijoTimeProvider. "0 llamadas a la durable y al
// store" ⇒ ninguna lectura de BD y ninguna consulta al proveedor.
public sealed class AutorizacionConsultaRiesgoProduccionTests
{
    private const long IdUsuario   = 7;
    private const long IdSolicitud = 10;

    private static readonly DateTime Ahora = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FakeStore(RiesgoProduccionSnapshot? snapshot, Exception? falla = null) : IRiesgoProduccionStore
    {
        public int CallCount { get; private set; }

        public Task<RiesgoProduccionSnapshot?> CargarAsync(long idSolicitud, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (falla is not null) throw falla;
            return Task.FromResult(snapshot);
        }
    }

    // Registra qué claves se leen: prueba que la rama productiva no consulta variables QA.
    private sealed class ConfiguracionQueRegistra(Dictionary<string, string?> valores) : IConfiguration
    {
        public List<string> ClavesLeidas { get; } = new();

        public string? this[string key]
        {
            get { ClavesLeidas.Add(key); return valores.TryGetValue(key, out var v) ? v : null; }
            set => valores[key] = value;
        }

        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
        public IEnumerable<IConfigurationSection> GetChildren() => throw new NotSupportedException();
        public IChangeToken GetReloadToken() => throw new NotSupportedException();
    }

    private static Dictionary<string, string?> ConFlag(string? valor = "true")
    {
        var config = new Dictionary<string, string?>();
        if (valor is not null)
            config[MiDecisorProduccionConfig.EnvProductionEnabled] = valor;
        return config;
    }

    private static Dictionary<string, string?> ConVariablesQa(Dictionary<string, string?> config)
    {
        config[MiDecisorQaGateConfig.EnvAllowedSolicitudId] = IdSolicitud.ToString();
        config[MiDecisorQaGateConfig.EnvStopAfterConsulta]  = "false";
        config[MiDecisorQaGateConfig.EnvGateMode]           = MiDecisorQaGateConfig.ModoClienteQaFullFlow;
        return config;
    }

    private static RiesgoProduccionSnapshot Libre() => new(null, TieneCupoQueBloquea: false);

    private static (AutorizacionConsultaRiesgoProduccion Gate, FakeConsultaRiesgoAutorizacion Durable, FakeStore Store) Crear(
        IConfiguration config, bool durableAutoriza = true, RiesgoProduccionSnapshot? snapshot = null,
        Exception? falla = null, DateTime? ahora = null, bool sinSnapshot = false)
    {
        var durable = new FakeConsultaRiesgoAutorizacion(durableAutoriza);
        var store   = new FakeStore(sinSnapshot ? null : snapshot ?? Libre(), falla);
        var gate    = new AutorizacionConsultaRiesgoProduccion(
            durable, store, config, new FijoTimeProvider(new DateTimeOffset(ahora ?? Ahora)),
            NullLogger<AutorizacionConsultaRiesgoProduccion>.Instance);
        return (gate, durable, store);
    }

    // ── Flag MIDECISOR_PRODUCTION_ENABLED: sólo "true" exacto ─────────────
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("True")]
    [InlineData("TRUE")]
    [InlineData(" true")]
    [InlineData("true ")]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("on")]
    [InlineData("false")]
    public async Task Flag_ausente_o_malformado_no_autoriza_ni_lee_BD(string? valor)
    {
        var (gate, durable, store) = Crear(new FakeConfiguration(ConFlag(valor)));

        Assert.False(await gate.TieneAutorizacionVigenteAsync(IdUsuario, IdSolicitud));
        Assert.Equal(0, durable.CallCount);
        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task Flag_true_exacto_continua_a_las_siguientes_barreras()
    {
        var (gate, durable, store) = Crear(new FakeConfiguration(ConFlag()));

        Assert.True(await gate.TieneAutorizacionVigenteAsync(IdUsuario, IdSolicitud));
        Assert.Equal(1, durable.CallCount);
        Assert.Equal(1, store.CallCount);
    }

    // ── Variables QA: ni habilitan ni alteran PROD ─────────────────────────
    [Fact]
    public async Task Variables_QA_sin_flag_no_autorizan_ni_leen_BD()
    {
        var (gate, durable, store) = Crear(new FakeConfiguration(ConVariablesQa(ConFlag(null))));

        Assert.False(await gate.TieneAutorizacionVigenteAsync(IdUsuario, IdSolicitud));
        Assert.Equal(0, durable.CallCount);
        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task Variables_QA_con_flag_no_saltan_la_ventana()
    {
        var (gate, _, _) = Crear(new FakeConfiguration(ConVariablesQa(ConFlag())),
            snapshot: new(Ahora.AddDays(-1), TieneCupoQueBloquea: false));

        Assert.False(await gate.TieneAutorizacionVigenteAsync(IdUsuario, IdSolicitud));
    }

    [Fact]
    public async Task Rama_productiva_solo_lee_MIDECISOR_PRODUCTION_ENABLED()
    {
        var config = new ConfiguracionQueRegistra(ConVariablesQa(ConFlag()));
        var (gate, _, _) = Crear(config);

        Assert.True(await gate.TieneAutorizacionVigenteAsync(IdUsuario, IdSolicitud));
        Assert.Equal(new[] { MiDecisorProduccionConfig.EnvProductionEnabled }, config.ClavesLeidas.Distinct());
    }

    // ── Barreras posteriores al flag ───────────────────────────────────────
    [Fact]
    public async Task Durable_false_no_autoriza_ni_lee_store()
    {
        var (gate, durable, store) = Crear(new FakeConfiguration(ConFlag()), durableAutoriza: false);

        Assert.False(await gate.TieneAutorizacionVigenteAsync(IdUsuario, IdSolicitud));
        Assert.Equal(1, durable.CallCount);
        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task Identidad_documental_insuficiente_no_autoriza()
    {
        var (gate, _, _) = Crear(new FakeConfiguration(ConFlag()), sinSnapshot: true);

        Assert.False(await gate.TieneAutorizacionVigenteAsync(IdUsuario, IdSolicitud));
    }

    [Fact]
    public async Task Error_del_store_no_autoriza()
    {
        var (gate, _, _) = Crear(new FakeConfiguration(ConFlag()), falla: new InvalidOperationException("falla simulada"));

        Assert.False(await gate.TieneAutorizacionVigenteAsync(IdUsuario, IdSolicitud));
    }

    [Fact]
    public async Task Cancelacion_se_propaga()
    {
        var (gate, _, _) = Crear(new FakeConfiguration(ConFlag()), falla: new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => gate.TieneAutorizacionVigenteAsync(IdUsuario, IdSolicitud));
    }

    // ── Ventana de 6 meses calendario (reloj fijo) ─────────────────────────
    // fecha_fin 2026-08-31 10:00 → límite 2027-02-28 10:00.
    [Theory]
    [InlineData(2026, 9, 1, 10, 0, false)]    // dentro de la ventana
    [InlineData(2027, 2, 28, 9, 59, false)]   // un minuto antes del límite
    [InlineData(2027, 2, 28, 10, 0, true)]    // límite exacto
    [InlineData(2027, 3, 1, 0, 0, true)]      // después del límite
    public async Task Ventana_seis_meses_desde_la_ultima_respuesta(int y, int m, int d, int h, int min, bool autoriza)
    {
        var fechaFin = new DateTime(2026, 8, 31, 10, 0, 0, DateTimeKind.Utc);
        var ahora    = new DateTime(y, m, d, h, min, 0, DateTimeKind.Utc);
        var (gate, _, _) = Crear(new FakeConfiguration(ConFlag()), snapshot: new(fechaFin, false), ahora: ahora);

        Assert.Equal(autoriza, await gate.TieneAutorizacionVigenteAsync(IdUsuario, IdSolicitud));
    }

    [Fact]
    public async Task Limite_menos_un_tick_bloquea()
    {
        var fechaFin = new DateTime(2027, 8, 31, 10, 0, 0, DateTimeKind.Utc); // límite 2028-02-29 (bisiesto)
        var limite   = ReglaReconsultaMiDecisor.Limite(fechaFin);
        var (gate, _, _) = Crear(new FakeConfiguration(ConFlag()), snapshot: new(fechaFin, false), ahora: limite.AddTicks(-1));

        Assert.False(await gate.TieneAutorizacionVigenteAsync(IdUsuario, IdSolicitud));
    }

    [Fact]
    public async Task Cupo_que_bloquea_no_autoriza()
    {
        var (gate, _, _) = Crear(new FakeConfiguration(ConFlag()), snapshot: new(null, TieneCupoQueBloquea: true));

        Assert.False(await gate.TieneAutorizacionVigenteAsync(IdUsuario, IdSolicitud));
    }

    // ── Estructura ─────────────────────────────────────────────────────────
    [Fact]
    public void Constructor_no_recibe_dependencias_QA_ni_otra_autoridad_de_ambiente()
    {
        var parametros = Assert.Single(typeof(AutorizacionConsultaRiesgoProduccion).GetConstructors())
            .GetParameters().Select(p => p.ParameterType).ToList();

        Assert.Equal(new[]
        {
            typeof(IConsultaRiesgoAutorizacion), typeof(IRiesgoProduccionStore), typeof(IConfiguration),
            typeof(TimeProvider), typeof(ILogger<AutorizacionConsultaRiesgoProduccion>),
        }, parametros);
    }

    // KYC (B5): el único endpoint que inicia la evaluación conserva KycAprobado.
    [Fact]
    public void Endpoint_iniciar_evaluacion_conserva_KycAprobado()
    {
        var accion = Assert.Single(typeof(CarteraOrdinariaController).GetMethods(BindingFlags.Public | BindingFlags.Instance),
            m => m.Name == nameof(CarteraOrdinariaController.IniciarEvaluacion));

        Assert.Contains(accion.GetCustomAttributes<AuthorizeAttribute>(), a => a.Policy == "KycAprobado");
    }

    [Fact]
    public void Orquestador_solo_se_inyecta_en_CarteraOrdinariaController()
    {
        var controllers = typeof(CarteraOrdinariaController).Assembly.GetTypes()
            .Where(t => typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .Where(t => t.GetConstructors().Any(c => c.GetParameters()
                .Any(p => p.ParameterType == typeof(ICarteraDecisionCrediticiaOrchestrator))))
            .ToList();

        Assert.Equal(new[] { typeof(CarteraOrdinariaController) }, controllers);
    }
}
