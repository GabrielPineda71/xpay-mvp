using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Xpay.Api.Controllers;
using Xpay.Api.Services;
using Xpay.Api.Tests.Common;
using Xpay.Api.Tests.Integrations.MiDecisor;
using Xunit;

namespace Xpay.Api.Tests.Services;

// XPAY-669 — tests offline de la preparación QA de identidad de prueba.
// SIN red, SIN base de datos, SIN Veriff, SIN MiDecisor, SIN PII real: el
// store se sustituye por un fake que cuenta llamadas, y los datos declarados
// son sintéticos. Las escrituras reales se cubren en QaIdentidadPruebaStoreTests
// (SQL, gated por ConnectionStrings__XpayConnection).
public sealed class QaIdentidadPruebaServiceTests
{
    private const long IdDestino = 501;
    private const long IdAdmin   = 9;
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class FakeStore : IQaIdentidadPruebaStore
    {
        public QaIdentidadPruebaSnapshot? Snapshot { get; set; }
        public bool AplicarResultado { get; set; } = true;
        public int CargarCalls { get; private set; }
        public int AplicarCalls { get; private set; }
        public AplicacionPreparacionIdentidadQa? UltimaAplicacion { get; private set; }

        public Task<QaIdentidadPruebaSnapshot?> CargarAsync(long idUsuario, CancellationToken cancellationToken)
        {
            CargarCalls++;
            return Task.FromResult(Snapshot);
        }

        public Task<bool> AplicarPreparacionAsync(AplicacionPreparacionIdentidadQa aplicacion, CancellationToken cancellationToken)
        {
            AplicarCalls++;
            UltimaAplicacion = aplicacion;
            return Task.FromResult(AplicarResultado);
        }
    }

    private static Dictionary<string, string?> ConfigValida() => new()
    {
        [QaIdentidadPruebaConfig.EnvEnabled]          = "true",
        [QaIdentidadPruebaConfig.EnvAllowedUsuarioId] = "501",
    };

    // Datos declarados SINTÉTICOS (no derivan de ninguna identificación real).
    private static QaIdentidadPruebaSnapshot SnapElegible() => new()
    {
        IdUsuario = IdDestino, IdPersona = 77, UsuarioEstado = "ACTIVO", PersonaEstado = "ACTIVA",
        EstadoKycActual = "NO_INICIADO", TipoDocumento = "CC", NumeroDocumento = "1234567",
        PrimerApellido = "Sintetico", IdentidadVerificada = false, IdentidadVerificadaProveedor = null,
        TieneDatosVeriff = false, TieneVerificacionVeriff = false, KycActuales = 0, KycActualEsPreparacionQa = false,
    };

    private static QaIdentidadPruebaSnapshot SnapPreparada() => new()
    {
        IdUsuario = IdDestino, IdPersona = 77, UsuarioEstado = "ACTIVO", PersonaEstado = "ACTIVA",
        EstadoKycActual = "APROBADO", TipoDocumento = "CC", NumeroDocumento = "1234567",
        PrimerApellido = "Sintetico", IdentidadVerificada = true, IdentidadVerificadaProveedor = "SIMULACION_QA",
        TieneDatosVeriff = false, TieneVerificacionVeriff = false, KycActuales = 1, KycActualEsPreparacionQa = true,
    };

    private static (QaIdentidadPruebaService Svc, FakeStore Store) Crear(
        Dictionary<string, string?> config, QaIdentidadPruebaSnapshot? snap = null, string ambiente = "QA")
    {
        var store = new FakeStore { Snapshot = snap };
        var svc = new QaIdentidadPruebaService(store, new FakeConfiguration(config), new FijoTimeProvider(T0),
            AmbienteXpayTests.Ambiente(ambiente));
        return (svc, store);
    }

    private static async Task AssertSinLecturaNiEscritura(
        Dictionary<string, string?> config, ResultadoPreparacionIdentidadQa esperado, long idAdmin = IdAdmin)
    {
        var (svc, store) = Crear(config, SnapElegible());
        Assert.Equal(esperado, await svc.PrepararAsync(IdDestino, idAdmin));
        Assert.Equal(0, store.CargarCalls);
        Assert.Equal(0, store.AplicarCalls);
    }

    // ── 1–4. ENABLED estricto ────────────────────────────────────────────
    [Fact]
    public Task Enabled_ausente_cerrado()
    {
        var c = ConfigValida(); c.Remove(QaIdentidadPruebaConfig.EnvEnabled);
        return AssertSinLecturaNiEscritura(c, ResultadoPreparacionIdentidadQa.NoDisponible);
    }

    [Theory]
    [InlineData("")]
    [InlineData("false")]
    [InlineData("True")]
    [InlineData("TRUE")]
    [InlineData(" true")]
    [InlineData("true ")]
    [InlineData("1")]
    [InlineData("yes")]
    public Task Enabled_no_exacto_cerrado(string valor)
    {
        var c = ConfigValida(); c[QaIdentidadPruebaConfig.EnvEnabled] = valor;
        return AssertSinLecturaNiEscritura(c, ResultadoPreparacionIdentidadQa.NoDisponible);
    }

    // ── 5–7. Usuario permitido ───────────────────────────────────────────
    [Fact]
    public Task AllowedId_ausente_cerrado()
    {
        var c = ConfigValida(); c.Remove(QaIdentidadPruebaConfig.EnvAllowedUsuarioId);
        return AssertSinLecturaNiEscritura(c, ResultadoPreparacionIdentidadQa.NoAutorizado);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-501")]
    [InlineData("+501")]
    [InlineData(" 501")]
    [InlineData("501 ")]
    [InlineData("501,502")]
    [InlineData("501;502")]
    [InlineData("99999999999999999999")]
    public Task AllowedId_invalido_cerrado(string valor)
    {
        var c = ConfigValida(); c[QaIdentidadPruebaConfig.EnvAllowedUsuarioId] = valor;
        return AssertSinLecturaNiEscritura(c, ResultadoPreparacionIdentidadQa.NoAutorizado);
    }

    [Fact]
    public Task AllowedId_distinto_cerrado()
    {
        var c = ConfigValida(); c[QaIdentidadPruebaConfig.EnvAllowedUsuarioId] = "502";
        return AssertSinLecturaNiEscritura(c, ResultadoPreparacionIdentidadQa.NoAutorizado);
    }

    [Fact]
    public Task Admin_no_resoluble_cerrado()
        => AssertSinLecturaNiEscritura(ConfigValida(), ResultadoPreparacionIdentidadQa.NoAutorizado, idAdmin: 0);

    // ── 8. Rol: atributo del controlador ─────────────────────────────────
    [Fact]
    public void Controlador_exige_rol_admin_y_ruta_esperada()
    {
        var t = typeof(QaIdentidadPruebaController);
        var autz = Assert.Single(t.GetCustomAttributes<AuthorizeAttribute>(inherit: true));
        Assert.Equal("ADMIN_XPAY,SUPERUSUARIO", autz.Roles);
        Assert.Equal("api/qa/identidad-prueba", Assert.Single(t.GetCustomAttributes<RouteAttribute>()).Template);

        var accion = t.GetMethod(nameof(QaIdentidadPruebaController.Preparar))!;
        Assert.Equal("{idUsuario:long}/preparar", Assert.Single(accion.GetCustomAttributes<HttpPostAttribute>()).Template);
        Assert.Empty(accion.GetCustomAttributes<AllowAnonymousAttribute>());
        Assert.Empty(t.GetCustomAttributes<AllowAnonymousAttribute>());
    }

    // ── 9. Auto-preparación ──────────────────────────────────────────────
    [Fact]
    public async Task Admin_no_puede_prepararse_a_si_mismo()
    {
        var (svc, store) = Crear(ConfigValida(), SnapElegible());
        Assert.Equal(ResultadoPreparacionIdentidadQa.AutoPreparacionProhibida, await svc.PrepararAsync(IdDestino, IdDestino));
        Assert.Equal(0, store.CargarCalls);
        Assert.Equal(0, store.AplicarCalls);
    }

    // ── 10–15. Destino y precondiciones ──────────────────────────────────
    [Fact]
    public async Task Usuario_inexistente_sin_escritura()
    {
        var (svc, store) = Crear(ConfigValida(), snap: null);
        Assert.Equal(ResultadoPreparacionIdentidadQa.NoEncontrado, await svc.PrepararAsync(IdDestino, IdAdmin));
        Assert.Equal(1, store.CargarCalls);
        Assert.Equal(0, store.AplicarCalls);
    }

    public static IEnumerable<object[]> SnapsSinPrecondiciones()
    {
        QaIdentidadPruebaSnapshot Con(Func<QaIdentidadPruebaSnapshot, QaIdentidadPruebaSnapshot> f) => f(SnapElegible());
        static QaIdentidadPruebaSnapshot Copia(QaIdentidadPruebaSnapshot s, string? usuarioEstado = "ACTIVO",
            string? personaEstado = "ACTIVA", string? tipo = "CC", string? numero = "1234567", string? apellido = "Sintetico") => new()
        {
            IdUsuario = s.IdUsuario, IdPersona = s.IdPersona, UsuarioEstado = usuarioEstado, PersonaEstado = personaEstado,
            EstadoKycActual = s.EstadoKycActual, TipoDocumento = tipo, NumeroDocumento = numero, PrimerApellido = apellido,
        };

        yield return new object[] { "usuario INACTIVO", Con(s => Copia(s, usuarioEstado: "INACTIVO")) };
        yield return new object[] { "usuario BLOQUEADO", Con(s => Copia(s, usuarioEstado: "BLOQUEADO")) };
        yield return new object[] { "persona INACTIVA", Con(s => Copia(s, personaEstado: "INACTIVA")) };
        yield return new object[] { "tipo CE", Con(s => Copia(s, tipo: "CE")) };
        yield return new object[] { "tipo vacío", Con(s => Copia(s, tipo: "")) };
        yield return new object[] { "tipo null", Con(s => Copia(s, tipo: null)) };
        yield return new object[] { "tipo minúsculas", Con(s => Copia(s, tipo: "cc")) };
        yield return new object[] { "número corto", Con(s => Copia(s, numero: "12")) };
        yield return new object[] { "número largo", Con(s => Copia(s, numero: "12345678901234")) };
        yield return new object[] { "número con letra", Con(s => Copia(s, numero: "12345a7")) };
        yield return new object[] { "número con espacio interno", Con(s => Copia(s, numero: "123 456")) };
        yield return new object[] { "número null", Con(s => Copia(s, numero: null)) };
        yield return new object[] { "apellido vacío", Con(s => Copia(s, apellido: "")) };
        yield return new object[] { "apellido espacios", Con(s => Copia(s, apellido: "   ")) };
        yield return new object[] { "apellido null", Con(s => Copia(s, apellido: null)) };
    }

    [Theory]
    [MemberData(nameof(SnapsSinPrecondiciones))]
    public async Task Precondiciones_no_cumplidas_sin_escritura(string caso, QaIdentidadPruebaSnapshot snap)
    {
        Assert.False(string.IsNullOrEmpty(caso));
        var (svc, store) = Crear(ConfigValida(), snap);
        Assert.Equal(ResultadoPreparacionIdentidadQa.PrecondicionesNoCumplidas, await svc.PrepararAsync(IdDestino, IdAdmin));
        Assert.Equal(0, store.AplicarCalls);
    }

    // ── 16. Identidad real protegida ─────────────────────────────────────
    public static IEnumerable<object[]> SnapsIdentidadProtegida()
    {
        static QaIdentidadPruebaSnapshot S(bool verificada, string? proveedor, bool datosVeriff, bool kycVeriff,
            string kyc = "NO_INICIADO", int actuales = 0, bool actualQa = false) => new()
        {
            IdUsuario = IdDestino, IdPersona = 77, UsuarioEstado = "ACTIVO", PersonaEstado = "ACTIVA",
            EstadoKycActual = kyc, TipoDocumento = "CC", NumeroDocumento = "1234567", PrimerApellido = "Sintetico",
            IdentidadVerificada = verificada, IdentidadVerificadaProveedor = proveedor,
            TieneDatosVeriff = datosVeriff, TieneVerificacionVeriff = kycVeriff, KycActuales = actuales, KycActualEsPreparacionQa = actualQa,
        };

        yield return new object[] { "verificada por VERIFF", S(true, "VERIFF", true, true, "APROBADO", 1) };
        yield return new object[] { "proveedor VERIFF sin flag", S(false, "VERIFF", false, false) };
        yield return new object[] { "datos crudos Veriff sin flag", S(false, null, true, false) };
        yield return new object[] { "sesión Veriff existente", S(false, null, false, true, "PENDIENTE", 1) };
        yield return new object[] { "verificada sin proveedor", S(true, null, false, false, "APROBADO", 1, true) };
        yield return new object[] { "SIMULACION_QA pero KYC no aprobado", S(true, "SIMULACION_QA", false, false, "PENDIENTE", 1, true) };
        yield return new object[] { "SIMULACION_QA sin fila KYC QA actual", S(true, "SIMULACION_QA", false, false, "APROBADO", 1, false) };
        yield return new object[] { "SIMULACION_QA con dos filas actuales", S(true, "SIMULACION_QA", false, false, "APROBADO", 2, false) };
    }

    [Theory]
    [MemberData(nameof(SnapsIdentidadProtegida))]
    public async Task Identidad_protegida_rechazo_sin_cambios(string caso, QaIdentidadPruebaSnapshot snap)
    {
        Assert.False(string.IsNullOrEmpty(caso));
        var (svc, store) = Crear(ConfigValida(), snap);
        Assert.Equal(ResultadoPreparacionIdentidadQa.IdentidadProtegida, await svc.PrepararAsync(IdDestino, IdAdmin));
        Assert.Equal(0, store.AplicarCalls);
    }

    // ── 17. Caso feliz (servicio) ────────────────────────────────────────
    [Fact]
    public async Task Caso_feliz_aplica_una_vez_con_ids_y_fecha()
    {
        var (svc, store) = Crear(ConfigValida(), SnapElegible());
        Assert.Equal(ResultadoPreparacionIdentidadQa.Preparada, await svc.PrepararAsync(IdDestino, IdAdmin));
        Assert.Equal(1, store.AplicarCalls);
        Assert.Equal(new AplicacionPreparacionIdentidadQa(IdDestino, 77, IdAdmin, T0.UtcDateTime), store.UltimaAplicacion);
    }

    [Fact]
    public async Task Estado_cambiado_en_transaccion_no_reporta_exito()
    {
        var (svc, store) = Crear(ConfigValida(), SnapElegible());
        store.AplicarResultado = false;
        Assert.Equal(ResultadoPreparacionIdentidadQa.EstadoCambiado, await svc.PrepararAsync(IdDestino, IdAdmin));
        Assert.Equal(1, store.AplicarCalls);
    }

    // ── 18. Idempotencia ─────────────────────────────────────────────────
    [Fact]
    public async Task Repeticion_exacta_es_idempotente_sin_escritura()
    {
        var (svc, store) = Crear(ConfigValida(), SnapPreparada());
        Assert.Equal(ResultadoPreparacionIdentidadQa.YaPreparada, await svc.PrepararAsync(IdDestino, IdAdmin));
        Assert.Equal(ResultadoPreparacionIdentidadQa.YaPreparada, await svc.PrepararAsync(IdDestino, IdAdmin));
        Assert.Equal(0, store.AplicarCalls);
    }

    // ── XPAY-717 — frontera QA/PROD: el servicio consume IAmbienteXpay ──
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("qa")]
    public async Task Ambiente_productivo_con_flags_qa_no_disponible_sin_lectura_ni_escritura(string ambiente)
    {
        var (svc, store) = Crear(ConfigValida(), SnapElegible(), ambiente);
        Assert.Equal(ResultadoPreparacionIdentidadQa.NoDisponible, await svc.PrepararAsync(IdDestino, IdAdmin));
        Assert.Equal(0, store.CargarCalls);
        Assert.Equal(0, store.AplicarCalls);
    }

    [Fact]
    public async Task Ambiente_productivo_sin_flags_no_disponible_sin_lectura_ni_escritura()
    {
        var (svc, store) = Crear(new Dictionary<string, string?>(), SnapElegible(), "Production");
        Assert.Equal(ResultadoPreparacionIdentidadQa.NoDisponible, await svc.PrepararAsync(IdDestino, IdAdmin));
        Assert.Equal(0, store.CargarCalls);
        Assert.Equal(0, store.AplicarCalls);
    }

    [Fact]
    public async Task Ambiente_QA_con_flags_mecanismo_disponible()
    {
        var (svc, store) = Crear(ConfigValida(), SnapElegible(), "QA");
        Assert.Equal(ResultadoPreparacionIdentidadQa.Preparada, await svc.PrepararAsync(IdDestino, IdAdmin));
        Assert.Equal(1, store.AplicarCalls);
    }

    [Fact]
    public async Task Ambiente_QA_sin_flags_sigue_no_disponible()
    {
        var (svc, store) = Crear(new Dictionary<string, string?>(), SnapElegible(), "QA");
        Assert.Equal(ResultadoPreparacionIdentidadQa.NoDisponible, await svc.PrepararAsync(IdDestino, IdAdmin));
        Assert.Equal(0, store.CargarCalls);
        Assert.Equal(0, store.AplicarCalls);
    }

    // ── Respuestas HTTP sin PII ──────────────────────────────────────────
    [Theory]
    [InlineData(ResultadoPreparacionIdentidadQa.NoDisponible, 404)]
    [InlineData(ResultadoPreparacionIdentidadQa.NoAutorizado, 403)]
    [InlineData(ResultadoPreparacionIdentidadQa.AutoPreparacionProhibida, 403)]
    [InlineData(ResultadoPreparacionIdentidadQa.NoEncontrado, 404)]
    [InlineData(ResultadoPreparacionIdentidadQa.IdentidadProtegida, 409)]
    [InlineData(ResultadoPreparacionIdentidadQa.PrecondicionesNoCumplidas, 409)]
    [InlineData(ResultadoPreparacionIdentidadQa.EstadoCambiado, 409)]
    [InlineData(ResultadoPreparacionIdentidadQa.YaPreparada, 200)]
    [InlineData(ResultadoPreparacionIdentidadQa.Preparada, 200)]
    public void Mapeo_http(ResultadoPreparacionIdentidadQa resultado, int status)
    {
        var r = QaIdentidadPruebaController.MapearRespuesta(IdDestino, resultado);
        var code = r switch
        {
            IStatusCodeActionResult s => s.StatusCode,
            _ => null,
        };
        Assert.Equal(status, code);
    }

    [Fact]
    public void Snapshot_ToString_no_expone_datos_declarados()
    {
        var texto = SnapElegible().ToString() ?? string.Empty;
        Assert.DoesNotContain("1234567", texto);
        Assert.DoesNotContain("Sintetico", texto);
    }

    // ── 20–21. Sin dependencias Veriff / MiDecisor / KYC productivo ─────
    [Theory]
    [InlineData(typeof(QaIdentidadPruebaService))]
    [InlineData(typeof(QaIdentidadPruebaStore))]
    [InlineData(typeof(QaIdentidadPruebaController))]
    public void Sin_dependencias_de_Veriff_MiDecisor_ni_KycService(Type tipo)
    {
        foreach (var ctor in tipo.GetConstructors())
            foreach (var p in ctor.GetParameters())
            {
                var n = p.ParameterType.FullName ?? p.ParameterType.Name;
                Assert.DoesNotContain("MiDecisor", n, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Veriff", n, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("KycService", n, StringComparison.Ordinal);
                Assert.DoesNotContain("HttpClient", n, StringComparison.Ordinal);
                Assert.DoesNotContain("Passport", n, StringComparison.OrdinalIgnoreCase);
            }
    }

    [Fact]
    public void Config_estricta_compartida()
    {
        Assert.True(QaIdentidadPruebaConfig.EstaHabilitado(new FakeConfiguration(ConfigValida())));
        Assert.True(QaIdentidadPruebaConfig.TryLeerUsuarioPermitido(new FakeConfiguration(ConfigValida()), out var id));
        Assert.Equal(IdDestino, id);
    }

    // ── HIGH-1 / LOW-2 — applock compartido con KycService (offline) ──

    [Theory]
    [InlineData(501L, "XPAY:KYC_USUARIO:501")]
    [InlineData(1L, "XPAY:KYC_USUARIO:1")]
    [InlineData(9223372036854775807L, "XPAY:KYC_USUARIO:9223372036854775807")]
    public void ClaveLock_usa_recurso_exacto_de_KycService(long idUsuario, string esperado)
    {
        var clave = QaIdentidadPruebaStore.ClaveLock(idUsuario);
        Assert.Equal(esperado, clave);
        Assert.DoesNotContain("QA_IDENTIDAD_PRUEBA", clave, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ValidarResultadoLock_exito_no_lanza(int resultado)
    {
        var ex = Record.Exception(() => QaIdentidadPruebaStore.ValidarResultadoLock(resultado));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-2)]
    [InlineData(-3)]
    public void ValidarResultadoLock_contencion_lanza_KycUsuarioConcurrenteException(int resultado)
    {
        Assert.Throws<Xpay.Api.Exceptions.KycUsuarioConcurrenteException>(
            () => QaIdentidadPruebaStore.ValidarResultadoLock(resultado));
    }

    [Theory]
    [InlineData(-999)]
    [InlineData(-4)]
    [InlineData(2)]
    public void ValidarResultadoLock_codigo_inesperado_lanza_Exception_sin_tipar(int resultado)
    {
        // Assert.Throws exige el tipo EXACTO: ni InvalidOperationException ni
        // KycUsuarioConcurrenteException.
        Assert.Throws<Exception>(() => QaIdentidadPruebaStore.ValidarResultadoLock(resultado));
    }
}
