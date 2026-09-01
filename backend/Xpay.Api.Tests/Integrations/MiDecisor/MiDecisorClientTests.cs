using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Integrations.MiDecisor;
using Xunit;

namespace Xpay.Api.Tests.Integrations.MiDecisor;

// M2.2 — unit tests del query client. SIN red real, SIN credenciales, SIN
// cédulas. Valores "test-*" / documentos cortos son sintéticos y ficticios.
public class MiDecisorClientTests
{
    private const string BaseUrl      = "https://midecisor.test";
    private const string ExpectedPath = "/co/cs/midecisor/v1/client";

    private static Dictionary<string, string?> ValidConfig() => new()
    {
        [MiDecisorOptions.EnvBaseUrl] = BaseUrl,
    };

    private static MiDecisorConsultaRequest ValidRequest() =>
        new(TipoIdentificacion: "CC", NumeroIdentificacion: "1234567", ApellidoRazonSocial: "Rodriguez");

    private static MiDecisorClient CreateClient(
        FakeHttpMessageHandler handler,
        Dictionary<string, string?> config,
        FakeMiDecisorTokenProvider tokenProvider)
        => new(
            new FakeHttpClientFactory(handler),
            tokenProvider,
            new FakeConfiguration(config),
            NullLogger<MiDecisorClient>.Instance);

    private static string AcceptedWithRisk() =>
        """
        {"status":"ACCEPTED","content":{"status":"202 ACCEPTED","respuesta":{"informacionRiesgo":
        {"conInformacion":true,"score":"853","viabilidad":"ALTA","ratingRecaudos":"A",
        "montoSugerido":"13809492","alertas":[{"alerta":"a"},{"alerta":"b"}]}}}}
        """;

    // ── A. Happy path ──────────────────────────────────────────────────────
    [Fact]
    public async Task ValidRequest_PostsQueryExactlyOnce()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, AcceptedWithRisk()));
        var token = new FakeMiDecisorTokenProvider();
        var client = CreateClient(handler, ValidConfig(), token);

        var result = await client.ConsultarPersonaNaturalAsync(ValidRequest());

        Assert.NotNull(result);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(1, token.CallCount);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
    }

    [Fact]
    public async Task Query_UsesConfiguredUri()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, AcceptedWithRisk()));
        var client = CreateClient(handler, ValidConfig(), new FakeMiDecisorTokenProvider());

        await client.ConsultarPersonaNaturalAsync(ValidRequest());

        Assert.Equal(ExpectedPath, handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Equal(BaseUrl, handler.LastRequest.RequestUri.GetLeftPart(UriPartial.Authority));
    }

    [Fact]
    public async Task Query_RespectsConfiguredQueryPathOverride()
    {
        var config = ValidConfig();
        config[MiDecisorOptions.EnvQueryPath] = "/co/cs/midecisor/v1/pn";
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, AcceptedWithRisk()));
        var client = CreateClient(handler, config, new FakeMiDecisorTokenProvider());

        await client.ConsultarPersonaNaturalAsync(ValidRequest());

        Assert.Equal("/co/cs/midecisor/v1/pn", handler.LastRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Query_SendsBearerAuthorization()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, AcceptedWithRisk()));
        var client = CreateClient(handler, ValidConfig(), new FakeMiDecisorTokenProvider());

        await client.ConsultarPersonaNaturalAsync(ValidRequest());

        var auth = handler.LastRequest!.Headers.Authorization;
        Assert.NotNull(auth);
        Assert.Equal("Bearer", auth!.Scheme);
        Assert.Equal(FakeMiDecisorTokenProvider.SyntheticToken, auth.Parameter);
    }

    [Fact]
    public async Task Query_DoesNotSendClientCredentialHeaders()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, AcceptedWithRisk()));
        var client = CreateClient(handler, ValidConfig(), new FakeMiDecisorTokenProvider());

        await client.ConsultarPersonaNaturalAsync(ValidRequest());

        Assert.False(handler.LastRequest!.Headers.Contains("Client_id"));
        Assert.False(handler.LastRequest.Headers.Contains("Client_secret"));
    }

    [Fact]
    public async Task Query_RequestJsonFieldNamesExact_AndMapsType()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, AcceptedWithRisk()));
        var client = CreateClient(handler, ValidConfig(), new FakeMiDecisorTokenProvider());

        await client.ConsultarPersonaNaturalAsync(ValidRequest());

        var body = handler.LastRequestBody!;
        Assert.Contains("\"tipoIdentificacion\":\"1\"", body);   // CC → "1"
        Assert.Contains("\"numeroIdentificacion\":\"1234567\"", body);
        Assert.Contains("\"apellidoRazonSocial\":\"Rodriguez\"", body);
        Assert.DoesNotContain(FakeMiDecisorTokenProvider.SyntheticToken, body);
    }

    [Fact]
    public async Task AcceptedWithRiskInfo_MapsRawFields()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, AcceptedWithRisk()));
        var client = CreateClient(handler, ValidConfig(), new FakeMiDecisorTokenProvider());

        var r = await client.ConsultarPersonaNaturalAsync(ValidRequest());

        Assert.Equal("ACCEPTED", r.EstadoEnvelope);
        Assert.Equal("202 ACCEPTED", r.ContentStatus);
        Assert.True(r.ConInformacion);
        Assert.Equal("853", r.ScoreRaw);
        Assert.Equal("ALTA", r.Viabilidad);
        Assert.Equal("A", r.RatingRecaudos);
        Assert.Equal("13809492", r.MontoSugeridoRaw);
        Assert.Equal(2, r.AlertasCount);
    }

    // ── B. Valid "no information" ──────────────────────────────────────────
    [Fact]
    public async Task AcceptedConInformacionFalse_ReturnsResultado()
    {
        const string body =
            """{"status":"ACCEPTED","content":{"status":"202 ACCEPTED","respuesta":{"informacionRiesgo":{"conInformacion":false,"score":"-","montoSugerido":"-"}}}}""";
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, body));
        var client = CreateClient(handler, ValidConfig(), new FakeMiDecisorTokenProvider());

        var r = await client.ConsultarPersonaNaturalAsync(ValidRequest());

        Assert.False(r.ConInformacion);
        Assert.Equal("-", r.ScoreRaw);
        Assert.Equal(0, r.AlertasCount);
    }

    [Fact]
    public async Task AcceptedMissingInformacionRiesgo_ReturnsResultadoWithNulls()
    {
        const string body =
            """{"status":"ACCEPTED","content":{"status":"202 ACCEPTED","respuesta":{"validacion":{"conInformacion":true}}}}""";
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, body));
        var client = CreateClient(handler, ValidConfig(), new FakeMiDecisorTokenProvider());

        var r = await client.ConsultarPersonaNaturalAsync(ValidRequest());

        Assert.Equal("ACCEPTED", r.EstadoEnvelope);
        Assert.Null(r.ConInformacion);
        Assert.Null(r.ScoreRaw);
        Assert.Null(r.MontoSugeridoRaw);
        Assert.Equal(0, r.AlertasCount);
    }

    // ── C. Protocol / rejection ───────────────────────────────────────────
    [Fact]
    public async Task MalformedJson_ThrowsProtocolException()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "no-json"));
        var client = CreateClient(handler, ValidConfig(), new FakeMiDecisorTokenProvider());

        await Assert.ThrowsAsync<MiDecisorProtocolException>(() => client.ConsultarPersonaNaturalAsync(ValidRequest()));
    }

    [Fact]
    public async Task UnknownEnvelopeStatus_ThrowsProtocolException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, """{"status":"WEIRD","content":{}}"""));
        var client = CreateClient(handler, ValidConfig(), new FakeMiDecisorTokenProvider());

        await Assert.ThrowsAsync<MiDecisorProtocolException>(() => client.ConsultarPersonaNaturalAsync(ValidRequest()));
    }

    [Fact]
    public async Task AcceptedNullContent_ThrowsProtocolException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, """{"status":"ACCEPTED","content":null}"""));
        var client = CreateClient(handler, ValidConfig(), new FakeMiDecisorTokenProvider());

        await Assert.ThrowsAsync<MiDecisorProtocolException>(() => client.ConsultarPersonaNaturalAsync(ValidRequest()));
    }

    [Fact]
    public async Task AcceptedNullRespuesta_ThrowsProtocolException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, """{"status":"ACCEPTED","content":{"status":"202 ACCEPTED"}}"""));
        var client = CreateClient(handler, ValidConfig(), new FakeMiDecisorTokenProvider());

        await Assert.ThrowsAsync<MiDecisorProtocolException>(() => client.ConsultarPersonaNaturalAsync(ValidRequest()));
    }

    [Fact]
    public async Task PreconditionFailed_ThrowsQueryRejected_Sanitized()
    {
        const string body =
            """{"status":"PRECONDITION_FAILED","content":{"status":"","infoTransaccion":{"msjExcepcion":"numeroIdentificacion es requerido"}}}""";
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, body));
        var client = CreateClient(handler, ValidConfig(), new FakeMiDecisorTokenProvider());

        var ex = await Assert.ThrowsAsync<MiDecisorQueryRejectedException>(
            () => client.ConsultarPersonaNaturalAsync(ValidRequest()));
        Assert.DoesNotContain("numeroIdentificacion", ex.Message);
    }

    // ── D. HTTP status ────────────────────────────────────────────────────
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData((HttpStatusCode)429)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task NonAuthErrorStatus_ThrowsTransport_NoRetry(HttpStatusCode code)
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(code, "irrelevant"));
        var client = CreateClient(handler, ValidConfig(), new FakeMiDecisorTokenProvider());

        await Assert.ThrowsAsync<MiDecisorTransportException>(() => client.ConsultarPersonaNaturalAsync(ValidRequest()));
        Assert.Equal(1, handler.CallCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task AuthErrorStatus_ThrowsAuthentication_NoRetry(HttpStatusCode code)
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(code, """{"errors":[{"code":"401"}],"success":false}"""));
        var token = new FakeMiDecisorTokenProvider();
        var client = CreateClient(handler, ValidConfig(), token);

        await Assert.ThrowsAsync<MiDecisorAuthenticationException>(() => client.ConsultarPersonaNaturalAsync(ValidRequest()));
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(1, token.CallCount); // no re-auth
    }

    // ── E. Transport / cancellation ───────────────────────────────────────
    [Fact]
    public async Task HttpRequestException_ThrowsTransport_OneAttempt()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            throw new HttpRequestException("boom"));
        var client = CreateClient(handler, ValidConfig(), new FakeMiDecisorTokenProvider());

        await Assert.ThrowsAsync<MiDecisorTransportException>(() => client.ConsultarPersonaNaturalAsync(ValidRequest()));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task SendTimeout_ThrowsTransport()
    {
        var config = ValidConfig();
        config[MiDecisorOptions.EnvTimeoutSeconds] = "1";
        var handler = new FakeHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return FakeHttpMessageHandler.Json(HttpStatusCode.OK, AcceptedWithRisk());
        });
        var client = CreateClient(handler, config, new FakeMiDecisorTokenProvider());

        await Assert.ThrowsAsync<MiDecisorTransportException>(() => client.ConsultarPersonaNaturalAsync(ValidRequest()));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task BodyReadTimeout_ThrowsTransport()
    {
        var config = ValidConfig();
        config[MiDecisorOptions.EnvTimeoutSeconds] = "1";
        var handler = new FakeHttpMessageHandler(
            () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StallingHttpContent() });
        var client = CreateClient(handler, config, new FakeMiDecisorTokenProvider());

        await Assert.ThrowsAsync<MiDecisorTransportException>(() => client.ConsultarPersonaNaturalAsync(ValidRequest()));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task CallerCancellation_PropagatesOperationCanceled()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, AcceptedWithRisk()));
        var client = CreateClient(handler, ValidConfig(), new FakeMiDecisorTokenProvider());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.ConsultarPersonaNaturalAsync(ValidRequest(), cts.Token));
        Assert.IsNotType<MiDecisorException>(ex);
        Assert.Equal(0, handler.CallCount);
    }

    // ── F. Local validation — token = 0, HTTP = 0 ─────────────────────────
    [Theory]
    [InlineData("12")]            // too short
    [InlineData("12345678901234")]// 14 digits, too long
    [InlineData("12345a7")]       // non-digit
    [InlineData("123 456")]       // internal space -> non-digit
    public async Task InvalidDocumentNumber_ThrowsRequestValidation_NoHttpNoToken(string numero)
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, AcceptedWithRisk()));
        var token = new FakeMiDecisorTokenProvider();
        var client = CreateClient(handler, ValidConfig(), token);

        await Assert.ThrowsAsync<MiDecisorRequestValidationException>(() =>
            client.ConsultarPersonaNaturalAsync(new("CC", numero, "Rodriguez")));
        Assert.Equal(0, handler.CallCount);
        Assert.Equal(0, token.CallCount);
    }

    [Fact]
    public async Task UnsupportedDocumentType_ThrowsRequestValidation_NoHttpNoToken()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, AcceptedWithRisk()));
        var token = new FakeMiDecisorTokenProvider();
        var client = CreateClient(handler, ValidConfig(), token);

        await Assert.ThrowsAsync<MiDecisorRequestValidationException>(() =>
            client.ConsultarPersonaNaturalAsync(new("NIT", "1234567", "Rodriguez")));
        Assert.Equal(0, handler.CallCount);
        Assert.Equal(0, token.CallCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankSurname_ThrowsRequestValidation_NoHttp(string apellido)
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, AcceptedWithRisk()));
        var client = CreateClient(handler, ValidConfig(), new FakeMiDecisorTokenProvider());

        await Assert.ThrowsAsync<MiDecisorRequestValidationException>(() =>
            client.ConsultarPersonaNaturalAsync(new("CC", "1234567", apellido)));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SurnameWithAccentAndHyphen_IsAccepted()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, AcceptedWithRisk()));
        var client = CreateClient(handler, ValidConfig(), new FakeMiDecisorTokenProvider());

        var r = await client.ConsultarPersonaNaturalAsync(new("CC", "1234567", "Núñez-Peña"));

        Assert.Equal(1, handler.CallCount);
        Assert.Equal("ACCEPTED", r.EstadoEnvelope);
        // El apellido con tilde/guion se acepta y se envía tal cual (System.Text.Json
        // lo escapa en el alambre; al des-serializar vuelve al valor original).
        using var sent = System.Text.Json.JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("Núñez-Peña", sent.RootElement.GetProperty("apellidoRazonSocial").GetString());
    }

    [Fact]
    public async Task NullRequest_ThrowsArgumentNull()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, AcceptedWithRisk()));
        var client = CreateClient(handler, ValidConfig(), new FakeMiDecisorTokenProvider());

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.ConsultarPersonaNaturalAsync(null!));
        Assert.Equal(0, handler.CallCount);
    }

    // ── G. Config ─────────────────────────────────────────────────────────
    [Fact]
    public async Task NonHttpBaseUrl_ThrowsConfiguration_NoHttpNoToken()
    {
        var config = ValidConfig();
        config[MiDecisorOptions.EnvBaseUrl] = "ftp://midecisor.test";
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, AcceptedWithRisk()));
        var token = new FakeMiDecisorTokenProvider();
        var client = CreateClient(handler, config, token);

        await Assert.ThrowsAsync<MiDecisorConfigurationException>(() => client.ConsultarPersonaNaturalAsync(ValidRequest()));
        Assert.Equal(0, handler.CallCount);
        Assert.Equal(0, token.CallCount);
    }

    [Fact]
    public async Task MissingBaseUrl_ThrowsConfiguration_NoHttpNoToken()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, AcceptedWithRisk()));
        var token = new FakeMiDecisorTokenProvider();
        var client = CreateClient(handler, new Dictionary<string, string?>(), token);

        await Assert.ThrowsAsync<MiDecisorConfigurationException>(() => client.ConsultarPersonaNaturalAsync(ValidRequest()));
        Assert.Equal(0, handler.CallCount);
        Assert.Equal(0, token.CallCount);
    }

    // ── H. Token provider ─────────────────────────────────────────────────
    [Fact]
    public async Task TokenProviderThrows_PropagatesWithoutHttp()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, AcceptedWithRisk()));
        var token = new FakeMiDecisorTokenProvider(
            toThrow: new MiDecisorAuthenticationException("token fail"));
        var client = CreateClient(handler, ValidConfig(), token);

        await Assert.ThrowsAsync<MiDecisorAuthenticationException>(() => client.ConsultarPersonaNaturalAsync(ValidRequest()));
        Assert.Equal(0, handler.CallCount);
    }
}
