using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Integrations.Passport;
using Xunit;

namespace Xpay.Api.Tests.Integrations.Passport;

// XPAY-594 — verificación de la firma "X-Passport-Signature-V2".
//
// Todos los secretos aquí son sintéticos (XPAY_INTERNAL_TEST_VECTOR) y no son
// credenciales reales. La firma esperada se generó con la implementación de
// REFERENCIA del contrato (json.dumps de Python + HMAC-SHA256) y se fijó como
// literal; NO es un vector publicado por Passport.
public class PassportWebhookSignatureValidatorTests
{
    private const string Secret = "xpay-internal-test-secret";
    private const string Timestamp = "1758743276";

    // XPAY_INTERNAL_TEST_VECTOR — HMAC-SHA256(Secret, "<ts>.<canonical>") en hex.
    private const string ExpectedHmacHex =
        "3271c0a1bf5b80037f5bb28a7e08fbead60135e6a7cc17b287996323d65c4c4c";

    // Instante correspondiente al timestamp del vector, para que la ventana
    // anti-reenvío se satisfaga de forma determinista.
    private static readonly DateTimeOffset AtVectorInstant =
        DateTimeOffset.FromUnixTimeSeconds(1758743276);

    private static IConfiguration Config(string? secret = Secret, string? mode = null, string? tolerance = null)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [PassportWebhookOptions.EnvSecret] = secret,
                [PassportWebhookOptions.EnvSignatureMode] = mode,
                [PassportWebhookOptions.EnvTimestampToleranceSeconds] = tolerance,
            })
            .Build();

    private static PassportWebhookSignatureValidator Validator(
        IConfiguration config, DateTimeOffset? now = null)
        => new(config,
               new TestTimeProvider(now ?? AtVectorInstant),
               NullLogger<PassportWebhookSignatureValidator>.Instance);

    private static string Body => PassportWebhookCanonicalJsonTests.InboundPayloadRaw;

    // --- Firma válida -------------------------------------------------------

    [Fact]
    public void FirmaValida_Hex_EsAceptada()
    {
        var r = Validator(Config()).Validate(Body, ExpectedHmacHex, Timestamp);

        Assert.Equal(PassportWebhookSignatureOutcome.Valid, r.Outcome);
        Assert.True(r.SignatureValid);
        Assert.False(r.ShouldReject);
    }

    [Fact]
    public void FirmaValida_Base64_TambienEsAceptada()
    {
        var b64 = Convert.ToBase64String(Convert.FromHexString(ExpectedHmacHex));
        var r = Validator(Config()).Validate(Body, b64, Timestamp);

        Assert.Equal(PassportWebhookSignatureOutcome.Valid, r.Outcome);
        Assert.True(r.SignatureValid);
    }

    [Fact]
    public void FirmaValida_EsIndiferenteAlFormatoDelCuerpo()
    {
        // El mismo payload con otro espaciado produce la misma forma canónica,
        // así que la firma sigue validando. Es la razón de ser de V2.
        var reformateado = PassportWebhookCanonicalJson.Canonicalize(Body);
        var r = Validator(Config()).Validate(reformateado, ExpectedHmacHex, Timestamp);

        Assert.True(r.SignatureValid);
    }

    // --- Firma inválida ----------------------------------------------------

    [Fact]
    public void FirmaInvalida_EsRechazadaEnEnforce()
    {
        var alterada = ExpectedHmacHex.Replace('3', '4');
        var r = Validator(Config()).Validate(Body, alterada, Timestamp);

        Assert.Equal(PassportWebhookSignatureOutcome.SignatureInvalid, r.Outcome);
        Assert.False(r.SignatureValid);
        Assert.True(r.ShouldReject);
    }

    [Fact]
    public void CuerpoAlterado_InvalidaLaFirma()
    {
        var otro = Body.Replace("PROCESSING", "SETTLED");
        var r = Validator(Config()).Validate(otro, ExpectedHmacHex, Timestamp);

        Assert.False(r.SignatureValid);
        Assert.True(r.ShouldReject);
    }

    [Fact]
    public void SecretoDistinto_InvalidaLaFirma()
    {
        var r = Validator(Config(secret: "otro-secreto-sintetico"))
            .Validate(Body, ExpectedHmacHex, Timestamp);

        Assert.False(r.SignatureValid);
    }

    [Fact]
    public void CuerpoNoParseable_NoPuedeTenerFirmaValida()
    {
        var r = Validator(Config()).Validate("no-es-json", ExpectedHmacHex, Timestamp);

        Assert.Equal(PassportWebhookSignatureOutcome.SignatureInvalid, r.Outcome);
        Assert.False(r.SignatureValid);
    }

    // --- Ausencias y fail-closed -------------------------------------------

    [Fact]
    public void FirmaAusente_EnEnforce_Rechaza()
    {
        var r = Validator(Config()).Validate(Body, null, Timestamp);

        Assert.Equal(PassportWebhookSignatureOutcome.SignatureMissing, r.Outcome);
        Assert.True(r.ShouldReject);
    }

    [Fact]
    public void TimestampAusente_EnEnforce_Rechaza()
    {
        var r = Validator(Config()).Validate(Body, ExpectedHmacHex, null);

        Assert.Equal(PassportWebhookSignatureOutcome.TimestampMissing, r.Outcome);
        Assert.True(r.ShouldReject);
    }

    [Fact]
    public void SinSecretoConfigurado_FailClosed()
    {
        var r = Validator(Config(secret: null)).Validate(Body, ExpectedHmacHex, Timestamp);

        Assert.Equal(PassportWebhookSignatureOutcome.SecretNotConfigured, r.Outcome);
        Assert.False(r.SignatureValid);
        Assert.True(r.ShouldReject);
    }

    // --- Ventana anti-reenvío ----------------------------------------------

    [Fact]
    public void TimestampFueraDeVentana_Rechaza()
    {
        var tarde = AtVectorInstant.AddSeconds(
            PassportWebhookOptions.DefaultTimestampToleranceSeconds + 1);
        var r = Validator(Config(), tarde).Validate(Body, ExpectedHmacHex, Timestamp);

        Assert.Equal(PassportWebhookSignatureOutcome.TimestampOutOfWindow, r.Outcome);
        Assert.True(r.ShouldReject);
    }

    [Fact]
    public void TimestampEnElBordeDeLaVentana_SeAcepta()
    {
        var borde = AtVectorInstant.AddSeconds(
            PassportWebhookOptions.DefaultTimestampToleranceSeconds);
        var r = Validator(Config(), borde).Validate(Body, ExpectedHmacHex, Timestamp);

        Assert.True(r.SignatureValid);
    }

    [Fact]
    public void ToleranciaEsConfigurable()
    {
        var tarde = AtVectorInstant.AddSeconds(600);
        var r = Validator(Config(tolerance: "900"), tarde)
            .Validate(Body, ExpectedHmacHex, Timestamp);

        Assert.True(r.SignatureValid);
    }

    [Fact]
    public void TimestampNoInterpretable_Rechaza()
    {
        var r = Validator(Config()).Validate(Body, ExpectedHmacHex, "no-es-una-fecha");

        Assert.Equal(PassportWebhookSignatureOutcome.TimestampUnparseable, r.Outcome);
    }

    [Theory]
    [InlineData("1758743276")]        // epoch en segundos
    [InlineData("1758743276000")]     // epoch en milisegundos
    [InlineData("2025-09-24T20:27:56Z")] // ISO-8601
    public void FormatosDeTimestampAdmitidos(string raw)
        => Assert.True(PassportWebhookSignatureValidator.TryParseTimestamp(raw, out _));

    // --- Sandbox (OBSERVE) vs Producción (ENFORCE) -------------------------

    [Fact]
    public void Observe_NoRechaza_PeroRegistraLaFirmaComoNoValida()
    {
        // Sandbox: la documentación NO confirma que X-Passport-Signature-V2 se
        // envíe. OBSERVE permite recibir sin firma, dejando constancia de que
        // NO fue verificada.
        var r = Validator(Config(mode: PassportWebhookOptions.ModeObserve))
            .Validate(Body, null, null);

        Assert.False(r.ShouldReject);
        Assert.False(r.SignatureValid);
    }

    [Fact]
    public void Observe_ConFirmaValida_SigueReportandoValida()
    {
        var r = Validator(Config(mode: PassportWebhookOptions.ModeObserve))
            .Validate(Body, ExpectedHmacHex, Timestamp);

        Assert.True(r.SignatureValid);
        Assert.False(r.ShouldReject);
    }

    [Fact]
    public void ModoPorDefecto_EsEnforce()
    {
        var opciones = PassportWebhookOptions.FromConfiguration(Config(mode: null));
        Assert.True(opciones.IsEnforcing);
        Assert.Equal(PassportWebhookOptions.ModeEnforce, opciones.Mode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("observa")]
    [InlineData("off")]
    [InlineData("DISABLED")]
    public void ModoNoReconocido_CaeEnEnforce_NuncaEnPermisivo(string modo)
        => Assert.True(PassportWebhookOptions.FromConfiguration(Config(mode: modo)).IsEnforcing);

    [Fact]
    public void ToleranciaPorDefecto_EsDeclaradaPorXpay_No300Arbitrario()
    {
        // TIMESTAMP_TOLERANCE_SOURCE = XPAY_CONFIGURABLE_DEFAULT. Passport no
        // fija ningún valor; este test congela el default para que un cambio
        // silencioso sea visible.
        Assert.Equal(300, PassportWebhookOptions.DefaultTimestampToleranceSeconds);
        Assert.Equal(300, PassportWebhookOptions.FromConfiguration(Config()).TimestampToleranceSeconds);
    }
}
