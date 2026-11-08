using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xpay.Api.Common;
using Xpay.Api.Integrations.MiDecisor;
using Xunit;

namespace Xpay.Api.Tests.Integrations.MiDecisor;

// XPAY-717 — regla pura de reconsulta MiDecisor en productivo: 6 meses
// calendario (AddMonths) desde fecha_fin de la respuesta, frontera exacta,
// conjunto R de resultados que prueban respuesta y cupos que bloquean.
// Sin DB, red, configuración ni reloj.
public sealed class ReglaReconsultaMiDecisorTests
{
    private static DateTime Utc(int y, int m, int d, int h = 10, int min = 0) => new(y, m, d, h, min, 0, DateTimeKind.Utc);

    private static RiesgoProduccionSnapshot Respuesta(DateTime fechaFin) => new(fechaFin, TieneCupoQueBloquea: false);

    // ── Ventana y frontera ─────────────────────────────────────────────────
    [Fact]
    public void Sin_respuesta_previa_ni_cupo_permite()
        => Assert.True(ReglaReconsultaMiDecisor.Permite(new(null, false), Utc(2026, 10, 12)));

    public static TheoryData<int, int, int> Fechas() => new()
    {
        { 2026, 1, 15 },
        { 2026, 8, 31 },
        { 2027, 8, 31 },
    };

    [Theory]
    [MemberData(nameof(Fechas))]
    public void Limite_menos_un_tick_bloquea(int y, int m, int d)
    {
        var fechaFin = Utc(y, m, d);
        var limite = ReglaReconsultaMiDecisor.Limite(fechaFin);

        Assert.False(ReglaReconsultaMiDecisor.Permite(Respuesta(fechaFin), limite.AddTicks(-1)));
    }

    [Theory]
    [MemberData(nameof(Fechas))]
    public void Limite_exacto_permite(int y, int m, int d)
    {
        var fechaFin = Utc(y, m, d);

        Assert.True(ReglaReconsultaMiDecisor.Permite(Respuesta(fechaFin), ReglaReconsultaMiDecisor.Limite(fechaFin)));
    }

    [Fact]
    public void Despues_del_limite_permite()
    {
        var fechaFin = Utc(2026, 1, 15);

        Assert.True(ReglaReconsultaMiDecisor.Permite(Respuesta(fechaFin), Utc(2026, 7, 16)));
    }

    [Theory]
    [InlineData(0)]   // misma fecha/hora de la respuesta
    [InlineData(1)]   // al día siguiente
    [InlineData(150)] // ~5 meses después
    public void Dentro_de_la_ventana_bloquea(int dias)
    {
        var fechaFin = Utc(2026, 1, 15);

        Assert.False(ReglaReconsultaMiDecisor.Permite(Respuesta(fechaFin), fechaFin.AddDays(dias)));
    }

    // ── AddMonths: finales de mes y bisiesto (conserva la hora) ────────────
    [Theory]
    [InlineData(2026, 1, 15, 2026, 7, 15)]
    [InlineData(2026, 3, 31, 2026, 9, 30)]
    [InlineData(2026, 8, 30, 2027, 2, 28)]
    [InlineData(2026, 8, 31, 2027, 2, 28)]
    [InlineData(2027, 8, 31, 2028, 2, 29)] // año bisiesto
    [InlineData(2025, 12, 31, 2026, 6, 30)]
    public void Limite_es_AddMonths_6_calendario(int y, int m, int d, int ly, int lm, int ld)
    {
        var fechaFin = new DateTime(y, m, d, 13, 45, 30, DateTimeKind.Utc);

        Assert.Equal(new DateTime(ly, lm, ld, 13, 45, 30, DateTimeKind.Utc), ReglaReconsultaMiDecisor.Limite(fechaFin));
    }

    // La forma inversa ahora.AddMonths(-6) NO es equivalente en fin de mes.
    [Fact]
    public void Frontera_hacia_adelante_difiere_de_la_forma_inversa_en_fin_de_mes()
    {
        var fechaFin = Utc(2026, 8, 31);
        var ahora    = Utc(2027, 2, 28);

        Assert.True(ReglaReconsultaMiDecisor.Permite(Respuesta(fechaFin), ahora));
        Assert.True(fechaFin > ahora.AddMonths(-6)); // la forma inversa habría bloqueado
    }

    // No son 180 días: a los 180 días todavía no se cumplen 6 meses calendario.
    [Fact]
    public void No_son_180_dias()
    {
        var fechaFin = Utc(2026, 1, 1);

        Assert.False(ReglaReconsultaMiDecisor.Permite(Respuesta(fechaFin), fechaFin.AddDays(180)));
        Assert.True(ReglaReconsultaMiDecisor.Permite(Respuesta(fechaFin), Utc(2026, 7, 1)));
    }

    // ── Cupo existente ─────────────────────────────────────────────────────
    [Fact]
    public void Cupo_que_bloquea_sin_respuesta_previa_bloquea()
        => Assert.False(ReglaReconsultaMiDecisor.Permite(new(null, TieneCupoQueBloquea: true), Utc(2026, 10, 12)));

    [Fact]
    public void Cupo_que_bloquea_con_ventana_vencida_bloquea()
        => Assert.False(ReglaReconsultaMiDecisor.Permite(new(Utc(2020, 1, 1), TieneCupoQueBloquea: true), Utc(2026, 10, 12)));

    [Theory]
    [InlineData("ACTIVO", true)]
    [InlineData("SUSPENDIDO", true)]
    [InlineData("CANCELADO", false)]
    public void Estados_de_cupo_que_bloquean(string estado, bool bloquea)
        => Assert.Equal(bloquea, ReglaReconsultaMiDecisor.EstadosCupoQueBloquean.Contains(estado));

    // ── Conjunto R: resultados que prueban respuesta del proveedor ─────────
    [Theory]
    [InlineData(CarteraConsultaRiesgoResultados.Aceptada, true)]
    [InlineData(CarteraConsultaRiesgoResultados.SinInformacion, true)]
    [InlineData(CarteraConsultaRiesgoResultados.RechazadaProveedor, true)]
    [InlineData(CarteraConsultaRiesgoResultados.ErrorProtocolo, false)]
    [InlineData(CarteraConsultaRiesgoResultados.ResultadoIncierto, false)]
    [InlineData(CarteraConsultaRiesgoResultados.ErrorAutenticacion, false)]
    [InlineData(CarteraConsultaRiesgoResultados.ErrorConfiguracion, false)]
    [InlineData(CarteraConsultaRiesgoResultados.ErrorValidacionLocal, false)]
    public void Resultados_que_prueban_respuesta(string resultado, bool cuenta)
        => Assert.Equal(cuenta, ReglaReconsultaMiDecisor.ResultadosConRespuesta.Contains(resultado));

    [Fact]
    public void Conjunto_R_tiene_exactamente_tres_resultados()
        => Assert.Equal(3, ReglaReconsultaMiDecisor.ResultadosConRespuesta.Count);

    // ── Contrato y estructura ──────────────────────────────────────────────
    [Fact]
    public void Ventana_es_de_seis_meses()
        => Assert.Equal(6, ReglaReconsultaMiDecisor.MesesVentana);

    [Fact]
    public void Snapshot_nulo_lanza_ArgumentNullException()
        => Assert.Throws<ArgumentNullException>(() => ReglaReconsultaMiDecisor.Permite(null!, Utc(2026, 10, 12)));

    [Fact]
    public void Regla_no_depende_de_configuracion_ni_host()
    {
        var tipo = typeof(ReglaReconsultaMiDecisor);
        Assert.True(tipo.IsAbstract && tipo.IsSealed); // clase estática

        var tipos = tipo.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetParameters())
            .Select(p => p.ParameterType)
            .Concat(tipo.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Select(f => f.FieldType))
            .ToList();

        Assert.DoesNotContain(tipos, t => typeof(IConfiguration).IsAssignableFrom(t));
        Assert.DoesNotContain(tipos, t => typeof(IHostEnvironment).IsAssignableFrom(t));
        Assert.DoesNotContain(tipos, t => typeof(TimeProvider).IsAssignableFrom(t));
    }
}
