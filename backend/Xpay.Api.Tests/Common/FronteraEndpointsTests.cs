using System.Reflection;
using Xpay.Api.Common;
using Xpay.Api.Controllers;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Common;

// XPAY-717 — matriz de frontera de endpoints QA/PROD. Pura reflexión: sin DB,
// red ni HTTP. El comportamiento del filtro (404 / no interviene) ya está
// cubierto en AmbienteXpayTests; aquí se fija QUÉ acciones lo llevan y cuáles
// NO deben llevarlo nunca (flujo real). Bre-B/Passport (B2) en sección propia.
public sealed class FronteraEndpointsTests
{
    // ── Positivos B1: mecanismos QA/demo/diagnóstico ───────────────────────
    public static TheoryData<Type, string> BloqueadasB1 => new()
    {
        { typeof(KycController),              nameof(KycController.SimularEstadoQa) },
        { typeof(DiagnosticsController),      nameof(DiagnosticsController.ErrorTest) },
        { typeof(WalletsController),          nameof(WalletsController.RecargarManual) },
        { typeof(CarteraOrdinariaController), nameof(CarteraOrdinariaController.AsignarCupo) },
        { typeof(LibranzaController),         nameof(LibranzaController.GetCortesDemoAsync) },
        { typeof(ComerciosAliadosController), nameof(ComerciosAliadosController.BackfillDemoContexto) },
    };

    // ── Controles negativos: flujo real / health ───────────────────────────
    public static TheoryData<Type, string> ControlesNegativos => new()
    {
        { typeof(DiagnosticsController),           nameof(DiagnosticsController.Ping) },
        { typeof(DiagnosticsController),           nameof(DiagnosticsController.Ready) },
        { typeof(KycController),                   nameof(KycController.VeriffSession) },
        { typeof(KycController),                   nameof(KycController.VeriffWebhook) },
        { typeof(WalletsController),               nameof(WalletsController.Transferir) },
        { typeof(CarteraOrdinariaController),      nameof(CarteraOrdinariaController.GetCupos) },
        { typeof(CarteraOrdinariaController),      nameof(CarteraOrdinariaController.IniciarEvaluacion) },
        { typeof(CarteraRevisionManualController), nameof(CarteraRevisionManualController.Resolver) },
        { typeof(CarteraRevisionManualController), nameof(CarteraRevisionManualController.Listar) },
        { typeof(CarteraRevisionManualController), nameof(CarteraRevisionManualController.Detalle) },
    };

    // El atributo va SIEMPRE por acción: ningún controller lo lleva a nivel de clase.
    public static TheoryData<Type> ControllersSinAtributoDeClase => new()
    {
        typeof(KycController),
        typeof(DiagnosticsController),
        typeof(WalletsController),
        typeof(CarteraOrdinariaController),
        typeof(LibranzaController),
        typeof(ComerciosAliadosController),
        typeof(CarteraRevisionManualController),
    };

    [Theory]
    [MemberData(nameof(BloqueadasB1))]
    public void Accion_QA_tiene_frontera(Type controller, string metodo)
        => Assert.True(TieneFrontera(Accion(controller, metodo)));

    [Theory]
    [MemberData(nameof(ControlesNegativos))]
    public void Accion_real_no_tiene_frontera(Type controller, string metodo)
        => Assert.False(TieneFrontera(Accion(controller, metodo)));

    [Theory]
    [MemberData(nameof(ControllersSinAtributoDeClase))]
    public void Controller_no_tiene_frontera_a_nivel_de_clase(Type controller)
        => Assert.False(TieneFrontera(controller));

    // ── Evidencia estructural: revisión manual legítima (XPAY-702) ──────────
    [Fact]
    public void RevisionManual_ninguna_accion_tiene_frontera()
    {
        var acciones = typeof(CarteraRevisionManualController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.NotEmpty(acciones);
        Assert.All(acciones, a => Assert.False(TieneFrontera(a)));
    }

    [Theory]
    [InlineData(typeof(CarteraRevisionManualStore))]
    [InlineData(typeof(CarteraMaterializacionCupoStore))]
    public void Materializacion_y_RevisionManual_no_dependen_de_CarteraOrdinariaService(Type tipo)
    {
        var parametros = tipo.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType);

        Assert.DoesNotContain(parametros, t => typeof(CarteraOrdinariaService).IsAssignableFrom(t));
    }

    [Fact]
    public void AsignarCupo_tiene_frontera_pero_GetCupos_no()
    {
        var c = typeof(CarteraOrdinariaController);

        Assert.True(TieneFrontera(Accion(c, nameof(CarteraOrdinariaController.AsignarCupo))));
        Assert.False(TieneFrontera(Accion(c, nameof(CarteraOrdinariaController.GetCupos))));
    }

    // ── B2: Bre-B simulado vs. flujo real Passport ─────────────────────────
    // Las 6 operaciones simuladas sólo operan sobre retiros CREADO/CONFIRMADO o
    // marcan llaves sin Passport; el flujo real nunca las necesita.
    public static TheoryData<Type, string> BloqueadasB2 => new()
    {
        { typeof(BrebController), nameof(BrebController.SimularValidacionLlave) },
        { typeof(BrebController), nameof(BrebController.SimularRetiro) },
        { typeof(BrebController), nameof(BrebController.SimularRetiroComercio) },
        { typeof(BrebController), nameof(BrebController.ConfirmarRetiro) },
        { typeof(BrebController), nameof(BrebController.LiquidarRetiro) },
        { typeof(BrebController), nameof(BrebController.RechazarRetiro) },
    };

    public static TheoryData<Type, string> ControlesNegativosBreb => new()
    {
        { typeof(BrebController),            nameof(BrebController.SolicitarRetiroReal) },
        { typeof(BrebController),            nameof(BrebController.ResolverMiLlave) },
        { typeof(BrebController),            nameof(BrebController.RegistrarMiLlave) },
        { typeof(BrebController),            nameof(BrebController.GetMiLlave) },
        { typeof(BrebController),            nameof(BrebController.RegistrarLlaveComercio) },
        { typeof(BrebController),            nameof(BrebController.ActualizarEstadoRetiroReal) },
        { typeof(BrebController),            nameof(BrebController.ActualizarEstadoMiRetiro) },
        { typeof(BrebController),            nameof(BrebController.GetAdminRetiros) },
        { typeof(BrebController),            nameof(BrebController.GetAdminLlaves) },
        { typeof(BrebController),            nameof(BrebController.GetCuentaOperativa) },
        { typeof(BrebController),            nameof(BrebController.PassportHealthConfig) },
        { typeof(PassportWebhookController), nameof(PassportWebhookController.Webhook) },
    };

    public static TheoryData<Type> ControllersSinAtributoDeClaseB2 => new()
    {
        typeof(BrebController),
        typeof(PassportWebhookController),
    };

    [Theory]
    [MemberData(nameof(BloqueadasB2))]
    public void Accion_BreB_simulada_tiene_frontera(Type controller, string metodo)
        => Assert.True(TieneFrontera(Accion(controller, metodo)));

    [Theory]
    [MemberData(nameof(ControlesNegativosBreb))]
    public void Accion_real_Passport_BreB_no_tiene_frontera(Type controller, string metodo)
        => Assert.False(TieneFrontera(Accion(controller, metodo)));

    [Theory]
    [MemberData(nameof(ControllersSinAtributoDeClaseB2))]
    public void Controller_BreB_Passport_no_tiene_frontera_a_nivel_de_clase(Type controller)
        => Assert.False(TieneFrontera(controller));

    [Fact]
    public void PassportWebhook_ninguna_accion_tiene_frontera()
    {
        var acciones = typeof(PassportWebhookController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.NotEmpty(acciones);
        Assert.All(acciones, a => Assert.False(TieneFrontera(a)));
    }

    // Exactamente UN método público de instancia con ese nombre (sin overloads).
    private static MethodInfo Accion(Type controller, string metodo) =>
        Assert.Single(controller.GetMethods(BindingFlags.Public | BindingFlags.Instance),
                      m => m.Name == metodo);

    private static bool TieneFrontera(MemberInfo miembro) =>
        miembro.GetCustomAttributes(typeof(SoloAmbienteNoProductivoAttribute), inherit: true).Length > 0;
}
