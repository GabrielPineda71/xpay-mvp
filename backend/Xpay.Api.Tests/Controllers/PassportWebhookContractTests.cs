using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.Controllers;
using Xpay.Api.Integrations.Passport;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Controllers;

// XPAY-594 — M5-T6/T7. Pruebas de CONTRATO y de AISLAMIENTO FINANCIERO del
// webhook entrante de Passport.
//
// Este proyecto no tiene infraestructura de pruebas con base de datos en
// memoria (criterio ya documentado en CambiarClaveContractTests.cs y
// MigrationContentTests.cs), así que las garantías estructurales se verifican
// por reflexión y por inspección del código fuente real que se compila — no de
// una copia en memoria. Es deliberado: un test que afirme "no toca Wallet"
// debe fallar si alguien introduce esa llamada, y esto lo consigue.
public class PassportWebhookContractTests
{
    private static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string LeerFuente(string rutaRelativa)
    {
        var path = Path.Combine(RepoRoot(), rutaRelativa);
        Assert.True(File.Exists(path), $"No se encontró el archivo esperado: {path}");
        return File.ReadAllText(path);
    }

    private static string FuenteServicio()
        => LeerFuente(Path.Combine("backend", "Xpay.Api", "Services", "PassportWebhookService.cs"));

    private static string FuenteController()
        => LeerFuente(Path.Combine("backend", "Xpay.Api", "Controllers", "PassportWebhookController.cs"));

    // --- Contrato del endpoint ---------------------------------------------

    [Fact]
    public void Endpoint_EsPostEnLaRutaAcordada()
    {
        var ruta = typeof(PassportWebhookController).GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(ruta);
        Assert.Equal("api/passport", ruta!.Template);

        var metodo = typeof(PassportWebhookController).GetMethod(nameof(PassportWebhookController.Webhook));
        Assert.NotNull(metodo);

        var post = metodo!.GetCustomAttribute<HttpPostAttribute>();
        Assert.NotNull(post);
        Assert.Equal("webhook", post!.Template);

        // Ruta efectiva: POST /api/passport/webhook
        Assert.NotNull(metodo.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(metodo.GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public void Controller_NoDependeDeKyc_NiDeClientesHttpDePassport()
    {
        var tipos = typeof(PassportWebhookController)
            .GetConstructors().Single()
            .GetParameters().Select(p => p.ParameterType).ToArray();

        Assert.Contains(typeof(IPassportWebhookSignatureValidator), tipos);
        Assert.Contains(typeof(PassportWebhookService), tipos);

        // No se acopla KYC con Passport, ni se inyecta ningún cliente capaz de
        // llamar a la API de Passport.
        Assert.DoesNotContain(typeof(KycService), tipos);
        Assert.DoesNotContain(typeof(IPassportHttpClient), tipos);
        Assert.DoesNotContain(typeof(IPassportPaymentClient), tipos);
        Assert.DoesNotContain(typeof(BrebPaymentService), tipos);
    }

    // --- Aislamiento financiero --------------------------------------------

    [Fact]
    public void Servicio_NoRecibeNingunaDependenciaFinanciera()
    {
        var tipos = typeof(PassportWebhookService)
            .GetConstructors().Single()
            .GetParameters().Select(p => p.ParameterType).ToArray();

        Assert.DoesNotContain(typeof(BrebPaymentService), tipos);
        Assert.DoesNotContain(typeof(IPassportPaymentClient), tipos);
        Assert.DoesNotContain(typeof(IPassportHttpClient), tipos);
        Assert.DoesNotContain(typeof(IPassportTokenProvider), tipos);
    }

    [Theory]
    [InlineData("ApplyPassportPaymentStatusAsync")]
    [InlineData("BrebPaymentStateMachine")]
    [InlineData("BrebPaymentService")]
    [InlineData("WalletMovimientos")]
    [InlineData("LedgerMovimientos")]
    [InlineData("PassportBrebRetiros")]
    [InlineData("IPassportHttpClient")]
    [InlineData("IPassportPaymentClient")]
    public void ElCodigoDeT6T7_NoMencionaNingunaRutaFinancieraNiLlamadaAPassport(string prohibido)
    {
        // El único uso admisible es nombrarlo en un comentario que explique su
        // exclusión, así que se revisa el código con los comentarios quitados.
        foreach (var fuente in new[] { FuenteServicio(), FuenteController() })
        {
            var sinComentarios = QuitarComentarios(fuente);
            Assert.DoesNotContain(prohibido, sinComentarios);
        }
    }

    [Fact]
    public void ElServicio_SoloEscribeLaTablaDeEventosDeWebhook()
    {
        var sinComentarios = QuitarComentarios(FuenteServicio());

        Assert.Contains("PassportWebhookEvents", sinComentarios);

        // Ninguna otra colección del DbContext se toca.
        foreach (var prohibida in new[]
                 {
                     "WalletCuentas", "WalletMovimientos", "LedgerAsientos", "LedgerMovimientos",
                     "PassportBrebRetiros", "PassportBrebLlaves", "CarteraUtilizaciones",
                 })
            Assert.DoesNotContain("_db." + prohibida, sinComentarios);
    }

    [Fact]
    public void ElServicio_NoUsaLaMaquinaDeEstadosDelProducto()
    {
        // BrebPaymentStateMachine no conoce CONFIRMED y está acoplada al flujo
        // financiero de retiros: T6/T7 usa su propio clasificador aislado.
        var sinComentarios = QuitarComentarios(FuenteServicio());
        Assert.Contains("PassportInboundEventClassifier", sinComentarios);
        Assert.DoesNotContain("BrebPaymentStateMachine", sinComentarios);
    }

    [Fact]
    public void NuncaSeRegistraElCuerpoCrudo_NiElSecreto_NiLaFirma()
    {
        foreach (var fuente in new[] { FuenteServicio(), FuenteController() })
        {
            var sinComentarios = QuitarComentarios(fuente);

            // Se permite registrar la LONGITUD del cuerpo, nunca su contenido.
            Assert.DoesNotContain("{RawBody}", sinComentarios);
            Assert.DoesNotContain("rawBody)", sinComentarios.Replace("ReadToEndAsync", string.Empty)
                                                            .Replace("ComputePayloadHash(rawBody)", string.Empty)
                                                            .Replace("TryParse(rawBody", string.Empty)
                                                            .Replace("Canonicalize(rawBody", string.Empty));
            Assert.DoesNotContain("PayloadRawJson", sinComentarios);
        }
    }

    [Fact]
    public void ElCuerpoCrudo_NuncaSePersiste()
    {
        var sinComentarios = QuitarComentarios(FuenteServicio());

        // Sólo el hash y la proyección sanitizada llegan a la entidad.
        Assert.Contains("PayloadHash          = hash", sinComentarios);
        Assert.Contains("BuildSanitizedJson(payload)", sinComentarios);
        Assert.DoesNotContain("PayloadSanitizedJson = rawBody", sinComentarios);
    }

    [Fact]
    public void ElPagoEntrante_NuncaSeCorrelacionaConUnRetiro()
    {
        var sinComentarios = QuitarComentarios(FuenteServicio());
        Assert.Contains("IdBrebRetiro         = null", sinComentarios);
    }

    private static string QuitarComentarios(string fuente)
    {
        var sb = new System.Text.StringBuilder(fuente.Length);
        foreach (var linea in fuente.Split('\n'))
        {
            var t = linea.TrimStart();
            if (t.StartsWith("//") || t.StartsWith("///")) continue;
            sb.Append(linea).Append('\n');
        }
        return sb.ToString();
    }
}
