using System.Net;
using System.Text.Json;
using Xpay.Api.Integrations.Passport;
using Xunit;

namespace Xpay.Api.Tests.Integrations.Passport;

// XPAY-298 — unit tests de PassportQrClient. SIN red real: se construye el
// PassportHttpClient de producción (XPAY-272) sobre un FakeHttpMessageHandler
// + FakePassportTokenProvider, exactamente igual al patrón ya usado en
// PassportKeyClientTests/PassportCustomerAccountClientTests. Todos los
// valores son sintéticos: NO se usan cédulas, teléfonos, emails ni IDs reales.
public class PassportQrClientTests
{
    private const string BaseUrl = "https://passport.test";

    private static Dictionary<string, string?> ValidConfig() => new()
    {
        [PassportOptions.EnvBaseUrl]      = BaseUrl,
        [PassportOptions.EnvClientId]     = "test-client-id",
        [PassportOptions.EnvClientSecret] = "test-client-secret",
    };

    private static PassportQrClient CreateClient(
        FakeHttpMessageHandler handler,
        FakePassportTokenProvider? tokenProvider = null)
    {
        var http = new PassportHttpClient(
            new FakeHttpClientFactory(handler),
            tokenProvider ?? new FakePassportTokenProvider("synthetic-bearer-token"),
            new FakeConfiguration(ValidConfig()),
            new CapturingLogger<PassportHttpClient>());
        return new PassportQrClient(http);
    }

    private static PassportQrAdditionalInfoRequest SyntheticAdditionalInfo(string transactionPurpose = "00") =>
        new(TransactionPurpose: transactionPurpose, TerminalLabel: "SYNTH-TERM-01");

    private static PassportQrVatRequest SyntheticVat(PassportQrVatType vatType = PassportQrVatType.FIXED) =>
        new(VatType: vatType, VatValue: "0.00", VatBaseValue: "0.00");

    // STATIC happy path: sin amount, sin inc.
    private static PassportCreateQrCodeRequest SyntheticStaticRequest(string? qrCodeReference = null) => new(
        KeyId: "synthetic-key-id-001",
        CustomerId: "synthetic-customer-id-001",
        Type: PassportQrType.STATIC,
        Channel: PassportQrChannel.POS,
        AdditionalInfo: SyntheticAdditionalInfo(),
        Vat: SyntheticVat())
    {
        QrCodeReference = qrCodeReference,
    };

    // DYNAMIC happy path: incluye amount + inc (inc condicionalmente
    // requerido por Passport cuando amount está presente).
    private static PassportCreateQrCodeRequest SyntheticDynamicRequest(string? qrCodeReference = "SYNTH12345") => new(
        KeyId: "synthetic-key-id-001",
        CustomerId: "synthetic-customer-id-001",
        Type: PassportQrType.DYNAMIC,
        Channel: PassportQrChannel.ECOMM,
        AdditionalInfo: SyntheticAdditionalInfo("06"),
        Vat: SyntheticVat())
    {
        QrCodeReference = qrCodeReference,
        Amount = new PassportQrAmountRequest("80000.57"),
        Inc = new PassportQrIncRequest(PassportQrVatType.FIXED, "10.00"),
    };

    private const string MinimalOkBody = "{\"id\":\"synthetic-qr-id-001\"}";

    // ── STATIC happy path (Fase 16) ──────────────────────────────────────

    [Fact]
    public async Task CreateQrCodeAsync_StaticHappyPath_PostsExactContractShape()
    {
        var tokenProvider = new FakePassportTokenProvider("synthetic-bearer-token");
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler, tokenProvider);

        await client.CreateQrCodeAsync(SyntheticStaticRequest());

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/v1/qrcodes", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal(BaseUrl, handler.LastRequest.RequestUri.GetLeftPart(UriPartial.Authority));
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal("synthetic-bearer-token", handler.LastRequest.Headers.Authorization!.Parameter);

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var root = doc.RootElement;

        Assert.Equal("synthetic-key-id-001", root.GetProperty("key_id").GetString());
        Assert.Equal("synthetic-customer-id-001", root.GetProperty("customer_id").GetString());
        Assert.Equal("STATIC", root.GetProperty("type").GetString());
        Assert.Equal("POS", root.GetProperty("channel").GetString());

        var additionalInfo = root.GetProperty("additional_info");
        // transaction_purpose conserva el cero inicial como STRING JSON.
        Assert.Equal(JsonValueKind.String, additionalInfo.GetProperty("transaction_purpose").ValueKind);
        Assert.Equal("00", additionalInfo.GetProperty("transaction_purpose").GetString());
        Assert.Equal("SYNTH-TERM-01", additionalInfo.GetProperty("terminal_label").GetString());

        var vat = root.GetProperty("vat");
        Assert.Equal("FIXED", vat.GetProperty("vat_type").GetString());
        Assert.Equal("0.00", vat.GetProperty("vat_value").GetString());
        Assert.Equal("0.00", vat.GetProperty("vat_base_value").GetString());

        // STATIC no debe enviar amount ni inc ni qr_code_reference (null en este caso).
        Assert.False(root.TryGetProperty("amount", out _));
        Assert.False(root.TryGetProperty("inc", out _));
        Assert.False(root.TryGetProperty("qr_code_reference", out _));

        // Sin campos inventados: exactamente 6 propiedades top-level.
        var topLevelNames = new List<string>();
        foreach (var prop in root.EnumerateObject())
            topLevelNames.Add(prop.Name);
        Assert.Equal(new[] { "key_id", "customer_id", "type", "channel", "additional_info", "vat" },
            topLevelNames);
    }

    // ── DYNAMIC happy path (Fase 16) ─────────────────────────────────────

    [Fact]
    public async Task CreateQrCodeAsync_DynamicHappyPath_PostsExactContractShapeWithAmount()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);

        await client.CreateQrCodeAsync(SyntheticDynamicRequest());

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/v1/qrcodes", handler.LastRequest.RequestUri!.AbsolutePath);

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var root = doc.RootElement;

        Assert.Equal("DYNAMIC", root.GetProperty("type").GetString());

        // amount.value debe ser JSON STRING, nunca número.
        var amount = root.GetProperty("amount");
        Assert.Equal(JsonValueKind.String, amount.GetProperty("value").ValueKind);
        Assert.Equal("80000.57", amount.GetProperty("value").GetString());
        Assert.Equal("COP", amount.GetProperty("currency").GetString());

        // inc presente (condicionalmente requerido cuando amount está presente).
        var inc = root.GetProperty("inc");
        Assert.Equal("FIXED", inc.GetProperty("inc_type").GetString());
        Assert.Equal("10.00", inc.GetProperty("inc_value").GetString());

        // qr_code_reference se conserva exactamente si se incluye.
        Assert.Equal("SYNTH12345", root.GetProperty("qr_code_reference").GetString());
    }

    [Fact]
    public async Task CreateQrCodeAsync_DynamicWithoutQrCodeReference_OmitsField()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);

        await client.CreateQrCodeAsync(SyntheticDynamicRequest(qrCodeReference: null));

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.False(doc.RootElement.TryGetProperty("qr_code_reference", out _));
    }

    // ── XPAY-300 — corrección hallazgo XPAY-299 FINDING_1 ────────────────
    // Passport documenta: "Required for Dynamic QR Codes if an Amount is
    // provided". amount presente + inc ausente en DYNAMIC debe fallar
    // localmente ANTES de HTTP.

    [Fact]
    public async Task CreateQrCodeAsync_DynamicWithAmountAndNullInc_ThrowsBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);
        var request = SyntheticDynamicRequest() with { Inc = null };

        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateQrCodeAsync(request));
        Assert.Equal(0, handler.CallCount);
    }

    // Regresión explícita: DYNAMIC SIN amount sigue permitido (el guard sólo
    // se activa cuando amount está presente) — llega hasta el happy path con
    // una respuesta fake válida.
    [Fact]
    public async Task CreateQrCodeAsync_DynamicWithoutAmount_IsAllowed()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);
        var request = SyntheticDynamicRequest() with { Amount = null, Inc = null };

        var result = await client.CreateQrCodeAsync(request);

        Assert.Equal("synthetic-qr-id-001", result.Id);
        Assert.Equal(1, handler.CallCount);
    }

    // Regresión explícita: inc SIN amount NO se bloquea artificialmente —
    // Passport no documenta esa dirección inversa (inc => amount obligatorio).
    [Fact]
    public async Task CreateQrCodeAsync_IncPresentWithoutAmount_IsAllowed()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);
        var request = SyntheticDynamicRequest() with
        {
            Amount = null,
            Inc = new PassportQrIncRequest(PassportQrVatType.FIXED, "10.00"),
        };

        var result = await client.CreateQrCodeAsync(request);

        Assert.Equal("synthetic-qr-id-001", result.Id);
        Assert.Equal(1, handler.CallCount);
    }

    // STATIC nunca se ve afectado por el guard DYNAMIC+amount=>inc, ni
    // siquiera si alguien forzara Amount en un request STATIC.
    [Fact]
    public async Task CreateQrCodeAsync_StaticWithAmountAndNoInc_IsNotBlockedByDynamicRule()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);
        var request = SyntheticStaticRequest() with { Amount = new PassportQrAmountRequest("100.00") };

        var result = await client.CreateQrCodeAsync(request);

        Assert.Equal("synthetic-qr-id-001", result.Id);
        Assert.Equal(1, handler.CallCount);
    }

    // ── Enum / code safety (Fase 17) — todos fallan ANTES de HTTP ────────

    [Fact]
    public async Task CreateQrCodeAsync_OutOfRangeQrType_ThrowsBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);
        var request = SyntheticStaticRequest() with { Type = (PassportQrType)999 };

        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateQrCodeAsync(request));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task CreateQrCodeAsync_OutOfRangeChannel_ThrowsBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);
        var request = SyntheticStaticRequest() with { Channel = (PassportQrChannel)999 };

        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateQrCodeAsync(request));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task CreateQrCodeAsync_OutOfRangeVatType_ThrowsBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);
        var request = SyntheticStaticRequest() with { Vat = new PassportQrVatRequest((PassportQrVatType)999, "0.00", "0.00") };

        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateQrCodeAsync(request));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task CreateQrCodeAsync_OutOfRangeIncType_ThrowsBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);
        var request = SyntheticDynamicRequest() with { Inc = new PassportQrIncRequest((PassportQrVatType)999, "10.00") };

        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateQrCodeAsync(request));
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData("01")]   // no está en el conjunto documentado (00,02-07)
    [InlineData("0")]    // pierde el cero inicial
    [InlineData("99")]
    [InlineData("")]
    public async Task CreateQrCodeAsync_InvalidTransactionPurpose_ThrowsBeforeHttp(string invalidPurpose)
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);
        var request = SyntheticStaticRequest() with { AdditionalInfo = SyntheticAdditionalInfo(invalidPurpose) };

        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateQrCodeAsync(request));
        Assert.Equal(0, handler.CallCount);
    }

    // ── Input guards (Fase 18) — todos fallan ANTES de HTTP ──────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateQrCodeAsync_BlankKeyId_ThrowsBeforeHttp(string? blank)
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);
        var request = SyntheticStaticRequest() with { KeyId = blank! };

        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateQrCodeAsync(request));
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateQrCodeAsync_BlankCustomerId_ThrowsBeforeHttp(string? blank)
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);
        var request = SyntheticStaticRequest() with { CustomerId = blank! };

        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateQrCodeAsync(request));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task CreateQrCodeAsync_BlankTerminalLabel_ThrowsBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);
        var request = SyntheticStaticRequest() with { AdditionalInfo = SyntheticAdditionalInfo() with { TerminalLabel = "  " } };

        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateQrCodeAsync(request));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task CreateQrCodeAsync_TerminalLabelTooLong_ThrowsBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);
        var tooLong = new string('X', 26);
        var request = SyntheticStaticRequest() with { AdditionalInfo = SyntheticAdditionalInfo() with { TerminalLabel = tooLong } };

        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateQrCodeAsync(request));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task CreateQrCodeAsync_NullAdditionalInfo_ThrowsBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);
        var request = SyntheticStaticRequest() with { AdditionalInfo = null! };

        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateQrCodeAsync(request));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task CreateQrCodeAsync_NullVat_ThrowsBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);
        var request = SyntheticStaticRequest() with { Vat = null! };

        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateQrCodeAsync(request));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task CreateQrCodeAsync_AmountPresentWithBlankValue_ThrowsBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);
        var request = SyntheticDynamicRequest() with { Amount = new PassportQrAmountRequest("   ") };

        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateQrCodeAsync(request));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task CreateQrCodeAsync_AmountWithNonCopCurrency_ThrowsBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);
        var request = SyntheticDynamicRequest() with { Amount = new PassportQrAmountRequest("100") { Currency = "USD" } };

        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateQrCodeAsync(request));
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData("")]                       // vacío explícito no es lo mismo que ausente
    [InlineData("TOOLONGREFERENCE123")]    // 19 caracteres > 17
    [InlineData("HASPUPPERCASE")]          // contiene 'P' mayúscula
    [InlineData("SYN-123")]                // XPAY-300 (FINDING_2): guión no es alfanumérico
    [InlineData("SYN 123")]                // XPAY-300 (FINDING_2): espacio no es alfanumérico
    public async Task CreateQrCodeAsync_InvalidQrCodeReference_ThrowsBeforeHttp(string invalidReference)
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);
        var request = SyntheticStaticRequest(qrCodeReference: invalidReference);

        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateQrCodeAsync(request));
        Assert.Equal(0, handler.CallCount);
    }

    // ── XPAY-300 — corrección hallazgo XPAY-299 FINDING_2 ────────────────

    // Puramente alfanumérico, sin 'P' mayúscula: debe pasar la validación.
    [Fact]
    public async Task CreateQrCodeAsync_ValidAlphanumericQrCodeReference_IsAllowed()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);
        var request = SyntheticStaticRequest(qrCodeReference: "SYN12345");

        var result = await client.CreateQrCodeAsync(request);

        Assert.Equal("synthetic-qr-id-001", result.Id);
        Assert.Equal(1, handler.CallCount);
    }

    // Decisión contractual fijada explícitamente: sólo 'P' mayúscula está
    // prohibida — la documentación no confirma una regla case-insensitive,
    // así que 'p' minúscula sigue permitida (no se inventa una restricción
    // adicional).
    [Fact]
    public async Task CreateQrCodeAsync_LowercaseP_IsStillAllowed()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);
        var request = SyntheticStaticRequest(qrCodeReference: "synth12345p");

        var result = await client.CreateQrCodeAsync(request);

        Assert.Equal("synthetic-qr-id-001", result.Id);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task CreateQrCodeAsync_NullRequest_ThrowsBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.CreateQrCodeAsync(null!));
        Assert.Equal(0, handler.CallCount);
    }

    // ── Response tests (Fase 19) ─────────────────────────────────────────

    [Fact]
    public async Task CreateQrCodeAsync_Http200_DeserializesStaticShapeAndReturnsId()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, """
                {
                  "id": "synthetic-qr-id-001",
                  "customer_id": "synthetic-customer-id-001",
                  "status": "ACTIVE",
                  "type": "STATIC",
                  "qr_code_data": "00020101...synthetic-emv-payload...6304ABCD",
                  "created_at": "2026-01-01T00:00:00.000000Z",
                  "key_id": "synthetic-key-id-001",
                  "channel": "POS",
                  "vat": { "vat_type": "FIXED", "vat_value": "0.00", "vat_base_value": "0.00" },
                  "additional_info": { "transaction_purpose": "00", "terminal_label": "SYNTH-TERM-01" }
                }
                """));
        var client = CreateClient(handler);

        var result = await client.CreateQrCodeAsync(SyntheticStaticRequest());

        Assert.Equal("synthetic-qr-id-001", result.Id);
        Assert.Equal("STATIC", result.Type);
        Assert.Equal("ACTIVE", result.Status);
        Assert.NotNull(result.QrCodeData);
        Assert.Null(result.Amount);
        Assert.Equal("00", result.AdditionalInfo!.TransactionPurpose);
    }

    [Fact]
    public async Task CreateQrCodeAsync_Http200_DeserializesDynamicShapeWithStringAmount()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, """
                {
                  "id": "synthetic-qr-id-002",
                  "customer_id": "synthetic-customer-id-001",
                  "status": "ACTIVE",
                  "type": "DYNAMIC",
                  "qr_code_data": "00020101...synthetic-emv-payload...6304EFGH",
                  "qr_code_image": "iVBORw0KGgoSyntheticBase64==",
                  "created_at": "2026-01-01T00:00:00.000000Z",
                  "key_id": "synthetic-key-id-001",
                  "channel": "ECOMM",
                  "amount": { "value": "80000.57", "currency": "COP" },
                  "vat": { "vat_type": "FIXED", "vat_value": "0.00", "vat_base_value": "0.00" },
                  "additional_info": { "transaction_purpose": "06", "terminal_label": "SYNTH-TERM-01" }
                }
                """));
        var client = CreateClient(handler);

        var result = await client.CreateQrCodeAsync(SyntheticDynamicRequest());

        Assert.Equal("synthetic-qr-id-002", result.Id);
        Assert.Equal("DYNAMIC", result.Type);
        Assert.NotNull(result.Amount);
        Assert.Equal("80000.57", result.Amount!.Value);
        Assert.Equal("COP", result.Amount.Currency);
        Assert.Equal("iVBORw0KGgoSyntheticBase64==", result.QrCodeImage);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"id\":\"\"}")]
    [InlineData("{\"id\":\"   \"}")]
    public async Task CreateQrCodeAsync_MissingOrBlankResponseId_ThrowsProtocolException(string body)
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, body));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<PassportProtocolException>(() => client.CreateQrCodeAsync(SyntheticStaticRequest()));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task CreateQrCodeAsync_MissingId_ExceptionMessageIsSanitized()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<PassportProtocolException>(
            () => client.CreateQrCodeAsync(SyntheticDynamicRequest()));

        Assert.DoesNotContain("synthetic-key-id-001", ex.Message);
        Assert.DoesNotContain("synthetic-customer-id-001", ex.Message);
        Assert.DoesNotContain("SYNTH12345", ex.Message);
        Assert.DoesNotContain("synthetic-bearer-token", ex.Message);
        Assert.DoesNotContain("test-client-secret", ex.Message);
    }

    // ── Error route (Fase 20) — reutiliza el manejo genérico existente ───

    [Fact]
    public async Task CreateQrCodeAsync_Http400_ThrowsTransportException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest, "{\"error\":\"bad_request\"}"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<PassportTransportException>(() => client.CreateQrCodeAsync(SyntheticStaticRequest()));
    }

    [Fact]
    public async Task CreateQrCodeAsync_Http401_ThrowsAuthenticationException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{\"error\":\"unauthorized\"}"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<PassportAuthenticationException>(() => client.CreateQrCodeAsync(SyntheticDynamicRequest()));
    }
}
