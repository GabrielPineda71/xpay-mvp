using Microsoft.Extensions.Configuration;
using Xpay.Api.Common;
using Xpay.Api.Exceptions;
using Xpay.Api.Services;
using Xunit;
using M = Xpay.Api.Common.MotivoBloqueoFinanciero;
using C = Xpay.Api.Common.ControlOperacionesFinancieras;

namespace Xpay.Api.Tests.Common;

// Piloto PROD — corrección 3: interruptor financiero, tope de 1.000 UVB por
// operación y límite diario interno XPAY. Puro, sin BD.
public sealed class ControlOperacionesFinancierasTests
{
    // 2026-06-15 12:00 UTC = 07:00 en Colombia.
    private static readonly DateTime Ahora = new(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);

    private static M? Retiro(bool prod, string? interruptor = "true", string? uvb = "50000", string? anio = "2026",
        string? diario = "500000", decimal monto = 100_000m, decimal acumulado = 0m, DateTime? ahora = null) =>
        C.EvaluarRetiro(prod, interruptor, uvb, anio, diario, monto, acumulado, ahora ?? Ahora);

    // ── Interruptor ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("false")]
    [InlineData("True")]
    [InlineData("1")]
    [InlineData(" true")]
    public void Produccion_solo_true_exacto_habilita(string? valor) =>
        Assert.Equal(M.Pausa, C.EvaluarInterruptor(esProductivo: true, valor));

    [Fact]
    public void Produccion_true_habilita() => Assert.Null(C.EvaluarInterruptor(true, "true"));

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("true", null)]
    [InlineData("false", M.Pausa)]
    [InlineData("no", M.Pausa)]
    public void No_productivo_ausente_habilita_y_presente_se_respeta(string? valor, M? esperado) =>
        Assert.Equal(esperado, C.EvaluarInterruptor(esProductivo: false, valor));

    // ── Tope por operación: 1.000 UVB ──────────────────────────────────────

    [Fact]
    public void Monto_igual_a_1000_UVB_se_acepta_y_un_peso_mas_se_rechaza()
    {
        Assert.Null(Retiro(true, uvb: "400", monto: 400_000m));
        Assert.Equal(M.TopeOperacionUvb, Retiro(true, uvb: "400", monto: 400_001m));
        Assert.Equal(M.TopeOperacionUvb, Retiro(true, uvb: "400", monto: 400_000.01m));
    }

    [Theory]
    [InlineData(null,   "2026")]
    [InlineData("",     "2026")]
    [InlineData("0",    "2026")]
    [InlineData("-5",   "2026")]
    [InlineData("11.5", "2026")]
    [InlineData("abc",  "2026")]
    [InlineData("50000", null)]
    [InlineData("50000", "2025")]   // año caducado
    [InlineData("50000", "2027")]
    public void Produccion_UVB_ausente_invalida_o_de_otro_anio_bloquea(string? uvb, string? anio) =>
        Assert.Equal(M.ConfiguracionInvalida, Retiro(true, uvb: uvb, anio: anio));

    [Fact]
    public void Anio_UVB_se_compara_con_el_anio_de_Colombia_no_UTC()
    {
        // 2027-01-01 03:00 UTC = 2026-12-31 22:00 en Colombia: sigue valiendo 2026.
        var finDeAnio = new DateTime(2027, 1, 1, 3, 0, 0, DateTimeKind.Utc);
        Assert.Null(Retiro(true, anio: "2026", ahora: finDeAnio));
        Assert.Equal(M.ConfiguracionInvalida, Retiro(true, anio: "2027", ahora: finDeAnio));
    }

    [Fact]
    public void No_productivo_sin_UVB_no_aplica_tope_pero_UVB_parcial_bloquea()
    {
        Assert.Null(Retiro(false, uvb: null, anio: null, diario: null, monto: 999_999_999m));
        Assert.Equal(M.ConfiguracionInvalida, Retiro(false, uvb: "50000", anio: null, diario: null));
    }

    // ── Límite diario interno XPAY ─────────────────────────────────────────

    [Fact]
    public void Limite_diario_acumula_y_permite_llegar_exactamente_al_limite()
    {
        Assert.Null(Retiro(true, monto: 200_000m, acumulado: 300_000m));
        Assert.Equal(M.LimiteDiarioXpay, Retiro(true, monto: 200_001m, acumulado: 300_000m));
        Assert.Equal(M.LimiteDiarioXpay, Retiro(true, monto: 1m, acumulado: 500_000m));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("500.000")]
    [InlineData("x")]
    public void Produccion_limite_diario_ausente_o_invalido_bloquea(string? diario) =>
        Assert.Equal(M.ConfiguracionInvalida, Retiro(true, diario: diario));

    [Fact]
    public void Pausa_tiene_prioridad_sobre_los_limites() =>
        Assert.Equal(M.Pausa, Retiro(true, interruptor: null, uvb: null, diario: null));

    [Fact]
    public void Inicio_del_dia_Colombia_en_UTC()
    {
        // 2026-06-15 03:00 UTC = 2026-06-14 22:00 Colombia → el día empezó el 14 a las 05:00 UTC.
        Assert.Equal(new DateTime(2026, 6, 14, 5, 0, 0), C.InicioDiaColombiaUtc(new DateTime(2026, 6, 15, 3, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateTime(2026, 6, 15, 5, 0, 0), C.InicioDiaColombiaUtc(new DateTime(2026, 6, 15, 5, 0, 0, DateTimeKind.Utc)));
    }

    // ── Excepciones y mensajes ─────────────────────────────────────────────

    [Fact]
    public void Limite_diario_no_se_presenta_como_regla_de_BreB()
    {
        var control = new C(Config(("XPAY_OPERACIONES_FINANCIERAS_HABILITADAS", "true"), ("XPAY_UVB_VALOR_COP", "50000"),
            ("XPAY_UVB_ANIO", ColombiaTime.DesdeUtc(DateTime.UtcNow).Year.ToString()), ("XPAY_TOPE_RETIRO_DIARIO_COP", "500000")),
            AmbienteXpayTests.Ambiente("Production"));

        var ex = Assert.Throws<OperacionFinancieraBloqueadaException>(() => control.ExigirRetiro(100_000m, 450_000m, DateTime.UtcNow));
        Assert.Equal(M.LimiteDiarioXpay, ex.Motivo);
        Assert.True(ex.EsLimite);
        Assert.Contains("XPAY", ex.Message);
        Assert.DoesNotContain("Bre-B", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Produccion_sin_configuracion_bloquea_anticipo_y_retiro()
    {
        var control = new C(Config(), AmbienteXpayTests.Ambiente("Production"));
        Assert.Equal(M.Pausa, Assert.Throws<OperacionFinancieraBloqueadaException>(control.ExigirInterruptor).Motivo);
        Assert.False(Assert.Throws<OperacionFinancieraBloqueadaException>(() => control.ExigirRetiro(1m, 0m, DateTime.UtcNow)).EsLimite);
    }

    // ── EstaHabilitado: misma regla que el interruptor ────────────────────

    [Theory]
    [InlineData("Production", null, false)]
    [InlineData("Production", "false", false)]
    [InlineData("Production", "TRUE", false)]
    [InlineData("Production", "true", true)]
    [InlineData("Staging", null, false)]
    [InlineData("Development", null, true)]
    [InlineData("QA", "true", true)]
    [InlineData("QA", "false", false)]
    public void EstaHabilitado_sigue_la_regla_del_interruptor(string ambiente, string? valor, bool esperado)
    {
        var config = valor is null ? Config() : Config(("XPAY_OPERACIONES_FINANCIERAS_HABILITADAS", valor));
        Assert.Equal(esperado, new C(config, AmbienteXpayTests.Ambiente(ambiente)).EstaHabilitado());
    }

    // ── Fase visual: webhook de recaudo y conciliación también se pausan ──
    // (antes quedaban fuera del interruptor; con él cerrado no aplican nada).

    [Theory]
    [InlineData(typeof(LibranzaRecaudoAplicacionService))]
    [InlineData(typeof(BrebPaymentReconciliationService))]
    public void Recaudo_webhook_y_conciliacion_dependen_del_interruptor(Type servicio)
    {
        var dependencias = servicio.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType);
        Assert.Contains(typeof(C), dependencias);
    }

    private static IConfiguration Config(params (string Clave, string Valor)[] valores) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(valores.Select(v => new KeyValuePair<string, string?>(v.Clave, v.Valor)))
            .Build();
}
