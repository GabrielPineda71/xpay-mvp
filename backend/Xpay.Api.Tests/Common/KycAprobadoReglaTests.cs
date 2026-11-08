using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xpay.Api.Authorization;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xunit;

namespace Xpay.Api.Tests.Common;

// XPAY-717 — policy "KycAprobado" subordinada a la frontera central
// IAmbienteXpay. Tests puros: sin DB ni red. Los ambientes se construyen con el
// AmbienteXpay real (AmbienteXpayTests.Ambiente). El handler se prueba con un
// cargador de evidencia falso (constructor internal) para demostrar que
// realmente usa la regla y qué evidencia pide según el ambiente.
public sealed class KycAprobadoReglaTests
{
    private const string Veriff       = "VERIFF";
    private const string SimulacionQa = "SIMULACION_QA";
    private const string Aprobado     = "APROBADO";

    private static EvidenciaKyc Evidencia(
        string? estadoKyc = Aprobado, bool verificada = true, string? proveedorIdentidad = Veriff,
        int filas = 1, string? proveedorFila = Veriff, string? estadoFila = Aprobado) => new()
    {
        EstadoKycActual     = estadoKyc,
        IdentidadVerificada = verificada,
        ProveedorIdentidad  = proveedorIdentidad,
        FilasKycActuales    = filas,
        ProveedorKycActual  = proveedorFila,
        EstadoKycFilaActual = estadoFila,
    };

    private static EvidenciaKyc VeriffCompleta()       => Evidencia();
    private static EvidenciaKyc SimulacionQaCompleta() => Evidencia(proveedorIdentidad: SimulacionQa, proveedorFila: SimulacionQa);

    // ── Regla: productivo / fail-closed ────────────────────────────────────
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("qa")]
    [InlineData("development")]
    [InlineData("Desconocido")]
    public void Productivo_VERIFF_completa_autoriza(string nombre)
        => Assert.True(KycAprobadoRegla.Cumple(VeriffCompleta(), AmbienteXpayTests.Ambiente(nombre)));

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("qa")]
    [InlineData("development")]
    [InlineData("Desconocido")]
    public void Productivo_SIMULACION_QA_rechaza(string nombre)
        => Assert.False(KycAprobadoRegla.Cumple(SimulacionQaCompleta(), AmbienteXpayTests.Ambiente(nombre)));

    // Persona VERIFF pero sesión vigente de otra procedencia (p.ej. simulación).
    [Theory]
    [InlineData(SimulacionQa)]
    [InlineData(null)]
    [InlineData("OTRO")]
    public void Productivo_persona_VERIFF_con_fila_actual_no_VERIFF_rechaza(string? proveedorFila)
        => Assert.False(KycAprobadoRegla.Cumple(
            Evidencia(proveedorFila: proveedorFila), AmbienteXpayTests.Ambiente("Production")));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("veriff")]
    [InlineData("OTRO")]
    [InlineData(SimulacionQa)]
    public void Productivo_proveedor_de_identidad_no_VERIFF_rechaza(string? proveedorIdentidad)
        => Assert.False(KycAprobadoRegla.Cumple(
            Evidencia(proveedorIdentidad: proveedorIdentidad), AmbienteXpayTests.Ambiente("Production")));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public void Productivo_filas_actuales_distintas_de_una_rechaza(int filas)
        => Assert.False(KycAprobadoRegla.Cumple(
            Evidencia(filas: filas), AmbienteXpayTests.Ambiente("Production")));

    [Theory]
    [InlineData("PENDIENTE")]
    [InlineData("RECHAZADO")]
    [InlineData("EXPIRADO")]
    [InlineData(null)]
    [InlineData("aprobado")]
    public void Productivo_fila_VERIFF_no_aprobada_rechaza(string? estadoFila)
        => Assert.False(KycAprobadoRegla.Cumple(
            Evidencia(estadoFila: estadoFila), AmbienteXpayTests.Ambiente("Production")));

    // ── Regla: no productivo (semántica histórica intacta) ─────────────────
    [Theory]
    [InlineData("Development")]
    [InlineData("QA")]
    [InlineData("Test")]
    public void No_productivo_SIMULACION_QA_autoriza(string nombre)
        => Assert.True(KycAprobadoRegla.Cumple(SimulacionQaCompleta(), AmbienteXpayTests.Ambiente(nombre)));

    [Theory]
    [InlineData("Development")]
    [InlineData("QA")]
    [InlineData("Test")]
    public void No_productivo_VERIFF_autoriza(string nombre)
        => Assert.True(KycAprobadoRegla.Cumple(VeriffCompleta(), AmbienteXpayTests.Ambiente(nombre)));

    // Sin whitelist de proveedores ni dependencia de kyc_verificaciones en no productivo.
    [Theory]
    [InlineData("Development")]
    [InlineData("QA")]
    [InlineData("Test")]
    public void No_productivo_solo_exige_condicion_canonica(string nombre)
        => Assert.True(KycAprobadoRegla.Cumple(
            Evidencia(proveedorIdentidad: null, filas: 0, proveedorFila: null, estadoFila: null),
            AmbienteXpayTests.Ambiente(nombre)));

    // ── Regla: condición canónica falsa en cualquier ambiente ──────────────
    public static TheoryData<string, string?, bool> CanonicaFalsa()
    {
        var data = new TheoryData<string, string?, bool>();
        foreach (var nombre in new[] { "Production", "Development", "QA", "Test", "Desconocido" })
        {
            data.Add(nombre, "PENDIENTE", true);  // estado no aprobado
            data.Add(nombre, Aprobado,    false); // aprobado sin identidad (Casos 3/6)
            data.Add(nombre, "aprobado",  true);  // comparación ordinal
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(CanonicaFalsa))]
    public void Condicion_canonica_falsa_rechaza_en_cualquier_ambiente(string nombre, string? estadoKyc, bool verificada)
        => Assert.False(KycAprobadoRegla.Cumple(
            Evidencia(estadoKyc: estadoKyc, verificada: verificada), AmbienteXpayTests.Ambiente(nombre)));

    // ── Regla: manda la frontera, no el nombre ─────────────────────────────
    [Fact]
    public void Nombre_Development_con_frontera_productiva_rechaza_SIMULACION_QA()
        => Assert.False(KycAprobadoRegla.Cumple(
            SimulacionQaCompleta(), new AmbienteFalso(AmbienteXpay.Development, permiteNoProductivos: false)));

    [Fact]
    public void Nombre_Production_con_frontera_no_productiva_autoriza_SIMULACION_QA()
        => Assert.True(KycAprobadoRegla.Cumple(
            SimulacionQaCompleta(), new AmbienteFalso("Production", permiteNoProductivos: true)));

    // ── Regla: contrato y estructura ───────────────────────────────────────
    [Fact]
    public void Evidencia_nula_lanza_ArgumentNullException()
        => Assert.Throws<ArgumentNullException>(() => KycAprobadoRegla.Cumple(null!, AmbienteXpayTests.Ambiente("QA")));

    [Fact]
    public void Ambiente_nulo_lanza_ArgumentNullException()
        => Assert.Throws<ArgumentNullException>(() => KycAprobadoRegla.Cumple(VeriffCompleta(), null!));

    [Fact]
    public void Regla_no_depende_de_configuracion_ni_host()
    {
        var tipo = typeof(KycAprobadoRegla);
        Assert.True(tipo.IsAbstract && tipo.IsSealed); // clase estática

        var tipos = tipo.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetParameters())
            .Select(p => p.ParameterType)
            .Concat(tipo.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Select(f => f.FieldType))
            .ToList();

        Assert.DoesNotContain(tipos, t => typeof(IConfiguration).IsAssignableFrom(t));
        Assert.DoesNotContain(tipos, t => typeof(IHostEnvironment).IsAssignableFrom(t));
    }

    // ── Handler con cargador falso ─────────────────────────────────────────
    private sealed class CargadorFalso(EvidenciaKyc? evidencia, Exception? falla = null)
    {
        public int Llamadas { get; private set; }
        public bool? IncluyoSesionVigente { get; private set; }

        public Task<EvidenciaKyc?> Cargar(long idUsuario, bool incluirSesionVigente)
        {
            Llamadas++;
            IncluyoSesionVigente = incluirSesionVigente;
            if (falla is not null) throw falla;
            return Task.FromResult(evidencia);
        }
    }

    private static async Task<bool> Autoriza(CargadorFalso cargador, IAmbienteXpay ambiente, string? idUsuarioClaim = "10")
    {
        var claims = idUsuarioClaim is null ? Array.Empty<Claim>() : new[] { new Claim("idUsuario", idUsuarioClaim) };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var req = new KycAprobadoRequirement();
        var ctx = new AuthorizationHandlerContext(new[] { req }, user, resource: null);

        await new KycAprobadoAuthorizationHandler(cargador.Cargar, ambiente).HandleAsync(ctx);
        return ctx.HasSucceeded;
    }

    [Fact]
    public async Task Handler_Production_SIMULACION_QA_no_autoriza()
    {
        var cargador = new CargadorFalso(SimulacionQaCompleta());

        Assert.False(await Autoriza(cargador, AmbienteXpayTests.Ambiente("Production")));
        Assert.True(cargador.IncluyoSesionVigente);
    }

    [Fact]
    public async Task Handler_Production_VERIFF_autoriza()
    {
        var cargador = new CargadorFalso(VeriffCompleta());

        Assert.True(await Autoriza(cargador, AmbienteXpayTests.Ambiente("Production")));
        Assert.True(cargador.IncluyoSesionVigente);
    }

    // En no productivo no se pide la sesión vigente (kyc_verificaciones no participa).
    [Fact]
    public async Task Handler_QA_SIMULACION_QA_autoriza_sin_pedir_sesion_vigente()
    {
        var cargador = new CargadorFalso(SimulacionQaCompleta());

        Assert.True(await Autoriza(cargador, AmbienteXpayTests.Ambiente("QA")));
        Assert.False(cargador.IncluyoSesionVigente);
    }

    [Fact]
    public async Task Handler_ambiente_desconocido_SIMULACION_QA_no_autoriza()
        => Assert.False(await Autoriza(new CargadorFalso(SimulacionQaCompleta()), AmbienteXpayTests.Ambiente("Desconocido")));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("12x")]
    public async Task Handler_claim_ausente_o_invalido_no_carga_ni_autoriza(string? claim)
    {
        var cargador = new CargadorFalso(VeriffCompleta());

        Assert.False(await Autoriza(cargador, AmbienteXpayTests.Ambiente("Production"), claim));
        Assert.Equal(0, cargador.Llamadas);
    }

    [Fact]
    public async Task Handler_usuario_inexistente_no_autoriza()
    {
        var cargador = new CargadorFalso(evidencia: null);

        Assert.False(await Autoriza(cargador, AmbienteXpayTests.Ambiente("QA")));
        Assert.Equal(1, cargador.Llamadas);
    }

    // Un fallo del cargador se propaga y nunca deja el requisito satisfecho.
    [Fact]
    public async Task Handler_fallo_del_cargador_no_autoriza()
    {
        var cargador = new CargadorFalso(VeriffCompleta(), new InvalidOperationException("falla simulada"));
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("idUsuario", "10") }, "test"));
        var ctx = new AuthorizationHandlerContext(new[] { new KycAprobadoRequirement() }, user, resource: null);
        var handler = new KycAprobadoAuthorizationHandler(cargador.Cargar, AmbienteXpayTests.Ambiente("Production"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(ctx));
        Assert.False(ctx.HasSucceeded);
    }

    [Fact]
    public void Constructor_publico_conserva_XpayDbContext_e_IAmbienteXpay()
    {
        var publico = Assert.Single(typeof(KycAprobadoAuthorizationHandler).GetConstructors());

        Assert.Equal(new[] { typeof(XpayDbContext), typeof(IAmbienteXpay) },
            publico.GetParameters().Select(p => p.ParameterType));
    }

    private sealed class AmbienteFalso(string nombre, bool permiteNoProductivos) : IAmbienteXpay
    {
        public string Nombre => nombre;
        public bool PermiteMecanismosNoProductivos => permiteNoProductivos;
        public bool EsProductivo => !permiteNoProductivos;
    }
}
