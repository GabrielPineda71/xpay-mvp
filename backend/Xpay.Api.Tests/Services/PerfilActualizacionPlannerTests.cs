using Xpay.Api.DTOs;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// XPAY-399 (Perfil Fase 2A) — PerfilActualizacionPlanner.Calcular es una
// función PURA: sin XpayDbContext, sin I/O, probada contra valores en
// memoria (mismo criterio que BrebPaymentReconciliationSelectorTests).
public class PerfilActualizacionPlannerTests
{
    private static PerfilActualizacionPlanner.CamposActuales Actuales(
        string celular = "3000000000",
        string? email = null,
        string? direccion = null,
        string? ciudad = null,
        string? departamento = null) => new(celular, email, direccion, ciudad, departamento);

    private static ActualizarMiPerfilRequest Request(
        string? celular = null, string? email = null, string? direccion = null,
        string? ciudad = null, string? departamento = null) => new(celular, email, direccion, ciudad, departamento);

    // ── Casos requeridos por XPAY-399 PASO 10 ───────────────────────────────

    [Fact]
    public void Calcular_ModificaDireccion_RegistraCambioYAplicaValor()
    {
        var plan = PerfilActualizacionPlanner.Calcular(Actuales(direccion: "Calle 1"), Request(direccion: "Calle 2 # 3-45"));

        Assert.Equal("Calle 2 # 3-45", plan.DireccionNuevo);
        Assert.True(plan.Cambios.ContainsKey("direccion"));
        Assert.Equal("Calle 1", plan.Cambios["direccion"].Antes);
        Assert.Equal("Calle 2 # 3-45", plan.Cambios["direccion"].Despues);
    }

    [Fact]
    public void Calcular_ModificaCiudadYDepartamento_RegistraAmbosCambios()
    {
        var plan = PerfilActualizacionPlanner.Calcular(
            Actuales(ciudad: "Bogota", departamento: "Cundinamarca"),
            Request(ciudad: "Medellin", departamento: "Antioquia"));

        Assert.Equal("Medellin", plan.CiudadNuevo);
        Assert.Equal("Antioquia", plan.DepartamentoNuevo);
        Assert.True(plan.Cambios.ContainsKey("ciudad"));
        Assert.True(plan.Cambios.ContainsKey("departamento"));
    }

    [Fact]
    public void Calcular_ModificaEmail_DejaEmailVerificadoParaResetear()
    {
        var plan = PerfilActualizacionPlanner.Calcular(Actuales(email: "viejo@correo.com"), Request(email: "nuevo@correo.com"));

        Assert.Equal("nuevo@correo.com", plan.EmailNuevo);
        Assert.True(plan.ResetearEmailVerificado);
        Assert.False(plan.ResetearCelularVerificado);
    }

    [Fact]
    public void Calcular_ModificaCelular_DejaCelularVerificadoParaResetear()
    {
        var plan = PerfilActualizacionPlanner.Calcular(Actuales(celular: "3000000000"), Request(celular: "3111111111"));

        Assert.Equal("3111111111", plan.CelularNuevo);
        Assert.True(plan.ResetearCelularVerificado);
        Assert.False(plan.ResetearEmailVerificado);
    }

    [Fact]
    public void Calcular_CampoAusente_NoSeModifica()
    {
        var actuales = Actuales(celular: "3000000000", email: "a@b.com", direccion: "Calle 1", ciudad: "Bogota", departamento: "Cundinamarca");
        // Request con todo null = "no enviado" en un PATCH real.
        var plan = PerfilActualizacionPlanner.Calcular(actuales, Request());

        Assert.Equal("3000000000", plan.CelularNuevo);
        Assert.Equal("a@b.com", plan.EmailNuevo);
        Assert.Equal("Calle 1", plan.DireccionNuevo);
        Assert.Equal("Bogota", plan.CiudadNuevo);
        Assert.Equal("Cundinamarca", plan.DepartamentoNuevo);
        Assert.Empty(plan.Cambios);
        Assert.False(plan.ResetearCelularVerificado);
        Assert.False(plan.ResetearEmailVerificado);
    }

    // ── Protecciones / validaciones ──────────────────────────────────────────

    [Fact]
    public void Calcular_CelularVacio_LanzaYNoLimpia()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            PerfilActualizacionPlanner.Calcular(Actuales(celular: "3000000000"), Request(celular: "   ")));
        Assert.Contains("celular", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Calcular_EmailVacio_LimpiaElCampo()
    {
        var plan = PerfilActualizacionPlanner.Calcular(Actuales(email: "a@b.com"), Request(email: ""));

        Assert.Null(plan.EmailNuevo);
        Assert.True(plan.ResetearEmailVerificado);
        Assert.Equal("a@b.com", plan.Cambios["email"].Antes);
        Assert.Null(plan.Cambios["email"].Despues);
    }

    [Fact]
    public void Calcular_DireccionVacia_LimpiaElCampo()
    {
        var plan = PerfilActualizacionPlanner.Calcular(Actuales(direccion: "Calle 1"), Request(direccion: ""));

        Assert.Null(plan.DireccionNuevo);
        Assert.True(plan.Cambios.ContainsKey("direccion"));
    }

    [Fact]
    public void Calcular_EmailYaVacio_EnviarVacioDeNuevo_NoGeneraCambio()
    {
        var plan = PerfilActualizacionPlanner.Calcular(Actuales(email: null), Request(email: ""));

        Assert.Null(plan.EmailNuevo);
        Assert.False(plan.Cambios.ContainsKey("email"));
        Assert.False(plan.ResetearEmailVerificado);
    }

    [Theory]
    [InlineData("no-es-un-email")]
    [InlineData("@sin-usuario.com")]
    [InlineData("sin-arroba.com")]
    public void Calcular_EmailFormatoInvalido_Lanza(string emailInvalido)
    {
        Assert.Throws<InvalidOperationException>(() =>
            PerfilActualizacionPlanner.Calcular(Actuales(), Request(email: emailInvalido)));
    }

    [Fact]
    public void Calcular_EmailFormatoValido_NoLanza()
    {
        var plan = PerfilActualizacionPlanner.Calcular(Actuales(), Request(email: "correo.valido@dominio.com"));
        Assert.Equal("correo.valido@dominio.com", plan.EmailNuevo);
    }

    [Fact]
    public void Calcular_CelularExcedeLongitud_Lanza()
    {
        var celularLargo = new string('9', 31); // columna VARCHAR(30)
        Assert.Throws<InvalidOperationException>(() =>
            PerfilActualizacionPlanner.Calcular(Actuales(), Request(celular: celularLargo)));
    }

    [Fact]
    public void Calcular_EmailExcedeLongitud_Lanza()
    {
        var emailLargo = new string('a', 195) + "@x.com"; // > 200 caracteres, columna VARCHAR(200)
        Assert.Throws<InvalidOperationException>(() =>
            PerfilActualizacionPlanner.Calcular(Actuales(), Request(email: emailLargo)));
    }

    [Fact]
    public void Calcular_MismoValorEnviado_NoRegistraCambioNiResetVerificacion()
    {
        var plan = PerfilActualizacionPlanner.Calcular(
            Actuales(celular: "3000000000", email: "a@b.com"),
            Request(celular: "3000000000", email: "a@b.com"));

        Assert.Empty(plan.Cambios);
        Assert.False(plan.ResetearCelularVerificado);
        Assert.False(plan.ResetearEmailVerificado);
    }

    [Fact]
    public void Calcular_ValorConEspaciosSeNormalizaAntesDeComparar()
    {
        var plan = PerfilActualizacionPlanner.Calcular(Actuales(ciudad: "Bogota"), Request(ciudad: "  Bogota  "));

        Assert.False(plan.Cambios.ContainsKey("ciudad")); // "  Bogota  ".Trim() == "Bogota" -> sin cambio real
    }

    [Fact]
    public void Calcular_NuncaModificaIdentidadLegal_PorqueNoEsParteDelRequestNiDelPlan()
    {
        // Prueba estructural: ActualizarMiPerfilRequest solo puede construirse
        // con estos 5 parámetros posicionales — no existe forma de que este
        // planner reciba (ni por tanto modifique) PrimerNombre/TipoDocumento/
        // FechaNacimiento/etc. Ver PerfilContractTests para la verificación
        // por reflexión de que el DTO no tiene más propiedades.
        var plan = PerfilActualizacionPlanner.Calcular(Actuales(), Request(direccion: "Calle 1"));
        Assert.DoesNotContain("primerNombre", plan.Cambios.Keys);
        Assert.DoesNotContain("tipoDocumento", plan.Cambios.Keys);
        Assert.DoesNotContain("fechaNacimiento", plan.Cambios.Keys);
        Assert.DoesNotContain("identidadVerificada", plan.Cambios.Keys);
    }
}
