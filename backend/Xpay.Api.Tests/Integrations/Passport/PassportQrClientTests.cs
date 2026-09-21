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

    // STATIC happy path: sin amount, sin inc, sin vat (XPAY-357 — vat es
    // opcional a nivel de DTO; este fixture ya NO lo incluye por defecto
    // porque el ejemplo oficial STATIC vigente no lo muestra. Los tests que
    // necesiten ejercitar STATIC+vat presente lo agregan explícitamente vía
    // `with { Vat = SyntheticVat() }`).
    // XPAY-458 — AdditionalInfo se movió del constructor posicional a
    // propiedad opcional (ver PassportCreateQrCodeRequest); este fixture
    // sigue incluyéndolo por defecto (comportamiento general no-M4-T1 sin
    // cambios), sólo cambia SU sintaxis de asignación.
    private static PassportCreateQrCodeRequest SyntheticStaticRequest(string? qrCodeReference = null) => new(
        KeyId: "synthetic-key-id-001",
        CustomerId: "synthetic-customer-id-001",
        Type: PassportQrType.STATIC,
        Channel: PassportQrChannel.POS)
    {
        AdditionalInfo = SyntheticAdditionalInfo(),
        QrCodeReference = qrCodeReference,
    };

    // DYNAMIC happy path: incluye amount + inc (inc condicionalmente
    // requerido por Passport cuando amount está presente) + vat (SIGUE
    // SIENDO REQUERIDO para DYNAMIC tras XPAY-357 — sin cambios de
    // comportamiento respecto a antes).
    private static PassportCreateQrCodeRequest SyntheticDynamicRequest(string? qrCodeReference = "SYNTH12345") => new(
        KeyId: "synthetic-key-id-001",
        CustomerId: "synthetic-customer-id-001",
        Type: PassportQrType.DYNAMIC,
        Channel: PassportQrChannel.ECOMM)
    {
        AdditionalInfo = SyntheticAdditionalInfo("06"),
        Vat = SyntheticVat(),
        QrCodeReference = qrCodeReference,
        Amount = new PassportQrAmountRequest("80000.57"),
        Inc = new PassportQrIncRequest(PassportQrVatType.FIXED, "10.00"),
    };

    // XPAY-458 — fixture dedicado al contrato M4-T1 confirmado por Passport
    // (Gustavo, 2026-09-21): STATIC + MPOS + vat/inc/tip/qr_code_reference,
    // SIN amount ni additional_info. Ningún ID/valor copiado del correo de
    // Gustavo — todos sintéticos.
    // XPAY-460 — vat/inc/tip actualizados a los valores del FIXTURE DE
    // CERTIFICACIÓN que Passport confirmó explícitamente (Gustavo aclaró
    // que son informativos, no reglas productivas — ver
    // CreateQrStaticExecutor para el razonamiento completo). Reemplazan los
    // valores de XPAY-458 (elegidos entonces deliberadamente DISTINTOS del
    // ejemplo de Gustavo, antes de que este ticket confirmara que adoptarlos
    // es lo correcto).
    private static PassportCreateQrCodeRequest SyntheticM4T1ConfirmedRequest(string qrCodeReference = "SYNM4T1REF01") => new(
        KeyId: "synthetic-key-id-001",
        CustomerId: "synthetic-customer-id-001",
        Type: PassportQrType.STATIC,
        Channel: PassportQrChannel.MPOS)
    {
        Vat = new PassportQrVatRequest(PassportQrVatType.FIXED, "100.00", "100.00"),
        Inc = new PassportQrIncRequest(PassportQrVatType.FIXED, "10.00"),
        Tip = new PassportQrTipRequest(PassportQrVatType.FIXED, "100.00"),
        QrCodeReference = qrCodeReference,
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

        // XPAY-357 — STATIC ya NO envía vat por defecto (el ejemplo oficial
        // STATIC vigente no lo muestra); tampoco amount, inc ni
        // qr_code_reference (null en este caso).
        Assert.False(root.TryGetProperty("vat", out _));
        Assert.False(root.TryGetProperty("amount", out _));
        Assert.False(root.TryGetProperty("inc", out _));
        Assert.False(root.TryGetProperty("qr_code_reference", out _));

        // Sin campos inventados: exactamente 5 propiedades top-level.
        var topLevelNames = new List<string>();
        foreach (var prop in root.EnumerateObject())
            topLevelNames.Add(prop.Name);
        Assert.Equal(new[] { "key_id", "customer_id", "type", "channel", "additional_info" },
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

    // ── XPAY-458 — tip (nuevo campo, mismo criterio de validación que vat/inc) ──

    [Fact]
    public async Task CreateQrCodeAsync_OutOfRangeTipType_ThrowsBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);
        var request = SyntheticStaticRequest() with { Tip = new PassportQrTipRequest((PassportQrVatType)999, "50.00") };

        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateQrCodeAsync(request));
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateQrCodeAsync_TipPresentWithBlankValue_ThrowsBeforeHttp(string? blank)
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);
        var request = SyntheticStaticRequest() with { Tip = new PassportQrTipRequest(PassportQrVatType.FIXED, blank!) };

        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateQrCodeAsync(request));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task CreateQrCodeAsync_WithoutTip_OmitsField()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);

        await client.CreateQrCodeAsync(SyntheticStaticRequest());

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.False(doc.RootElement.TryGetProperty("tip", out _));
    }

    [Fact]
    public async Task CreateQrCodeAsync_WithTip_SerializesCorrectJsonNames()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);
        var request = SyntheticStaticRequest() with
        {
            Tip = new PassportQrTipRequest(PassportQrVatType.FIXED, "100.00"),
        };

        var result = await client.CreateQrCodeAsync(request);

        Assert.Equal("synthetic-qr-id-001", result.Id);
        Assert.Equal(1, handler.CallCount);

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var tip = doc.RootElement.GetProperty("tip");
        Assert.Equal("FIXED", tip.GetProperty("tip_type").GetString());
        Assert.Equal(JsonValueKind.String, tip.GetProperty("tip_value").ValueKind);
        Assert.Equal("100.00", tip.GetProperty("tip_value").GetString());
    }

    // ── XPAY-458 — contrato M4-T1 confirmado por Passport (Gustavo,
    // 2026-09-21): STATIC + MPOS + vat/inc/tip/qr_code_reference presentes,
    // SIN amount/additional_info/transaction_purpose/terminal_label. ──────

    [Fact]
    public async Task CreateQrCodeAsync_M4T1ConfirmedContract_PostsExactContractShape()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);

        var result = await client.CreateQrCodeAsync(SyntheticM4T1ConfirmedRequest());

        Assert.Equal("synthetic-qr-id-001", result.Id);
        Assert.Equal(1, handler.CallCount);

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var root = doc.RootElement;

        Assert.Equal("STATIC", root.GetProperty("type").GetString());
        Assert.Equal("MPOS", root.GetProperty("channel").GetString());

        // Presentes — XPAY-460: valores exactos del fixture de
        // certificación confirmado por Passport (Gustavo, 2026-09-21).
        Assert.True(root.TryGetProperty("vat", out var vat));
        Assert.Equal("FIXED", vat.GetProperty("vat_type").GetString());
        Assert.Equal(JsonValueKind.String, vat.GetProperty("vat_value").ValueKind);
        Assert.Equal("100.00", vat.GetProperty("vat_value").GetString());
        Assert.Equal(JsonValueKind.String, vat.GetProperty("vat_base_value").ValueKind);
        Assert.Equal("100.00", vat.GetProperty("vat_base_value").GetString());

        Assert.True(root.TryGetProperty("inc", out var inc));
        Assert.Equal("FIXED", inc.GetProperty("inc_type").GetString());
        Assert.Equal(JsonValueKind.String, inc.GetProperty("inc_value").ValueKind);
        Assert.Equal("10.00", inc.GetProperty("inc_value").GetString());

        Assert.True(root.TryGetProperty("tip", out var tip));
        Assert.Equal("FIXED", tip.GetProperty("tip_type").GetString());
        Assert.Equal(JsonValueKind.String, tip.GetProperty("tip_value").ValueKind);
        Assert.Equal("100.00", tip.GetProperty("tip_value").GetString());

        Assert.True(root.TryGetProperty("qr_code_reference", out var qrCodeReference));
        Assert.Equal(JsonValueKind.String, qrCodeReference.ValueKind);

        // Ausentes — el contrato confirmado por Passport para M4-T1 NO los incluye.
        Assert.False(root.TryGetProperty("amount", out _));
        Assert.False(root.TryGetProperty("additional_info", out _));

        // Sin campos inventados: exactamente 8 propiedades top-level (orden
        // = orden de declaración de propiedades en PassportCreateQrCodeRequest).
        var topLevelNames = new List<string>();
        foreach (var prop in root.EnumerateObject())
            topLevelNames.Add(prop.Name);
        Assert.Equal(
            new[] { "key_id", "customer_id", "type", "channel", "vat", "qr_code_reference", "inc", "tip" },
            topLevelNames);
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

    // XPAY-458 — CAMBIO DE CONTRATO: additional_info pasó de incondicionalmente
    // requerido a OPCIONAL (Passport confirmó, para M4-T1, un request
    // funcional que no lo incluye en absoluto). Este test reemplaza al
    // histórico "CreateQrCodeAsync_NullAdditionalInfo_ThrowsBeforeHttp"
    // (aserción inversa, ya no válida bajo el contrato confirmado).
    [Fact]
    public async Task CreateQrCodeAsync_NullAdditionalInfo_IsAllowed()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);
        var request = SyntheticStaticRequest() with { AdditionalInfo = null };

        var result = await client.CreateQrCodeAsync(request);

        Assert.Equal("synthetic-qr-id-001", result.Id);
        Assert.Equal(1, handler.CallCount);

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.False(doc.RootElement.TryGetProperty("additional_info", out _));
    }

    // XPAY-357 — vat SIGUE siendo requerido para DYNAMIC (comportamiento
    // preservado sin cambios respecto a antes de XPAY-357). Renombrado desde
    // "CreateQrCodeAsync_NullVat_ThrowsBeforeHttp" (que usaba STATIC, cuya
    // semántica cambió — ver el nuevo test STATIC a continuación).
    [Fact]
    public async Task CreateQrCodeAsync_DynamicNullVat_ThrowsBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);
        var request = SyntheticDynamicRequest() with { Vat = null! };

        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateQrCodeAsync(request));
        Assert.Equal(0, handler.CallCount);
    }

    // XPAY-357 — regla contractual deliberada: STATIC ya NO requiere vat.
    // Llega hasta el happy path con una respuesta fake válida, y el body
    // real enviado no debe contener "vat" en absoluto (serialización
    // condicional ya existente vía JsonIgnore(WhenWritingNull)).
    [Fact]
    public async Task CreateQrCodeAsync_StaticWithoutVat_IsAllowed()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);
        var request = SyntheticStaticRequest(); // Vat ya ausente por defecto tras XPAY-357.

        var result = await client.CreateQrCodeAsync(request);

        Assert.Equal("synthetic-qr-id-001", result.Id);
        Assert.Equal(1, handler.CallCount);

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.False(doc.RootElement.TryGetProperty("vat", out _));
    }

    // Regresión explícita: STATIC SÍ puede seguir incluyendo vat si el
    // caller lo agrega explícitamente (vat es OPCIONAL, no PROHIBIDO, para
    // STATIC) — y sus subcampos se siguen validando cuando está presente.
    [Fact]
    public async Task CreateQrCodeAsync_StaticWithVatExplicitlyIncluded_IsAllowed()
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, MinimalOkBody));
        var client = CreateClient(handler);
        var request = SyntheticStaticRequest() with { Vat = SyntheticVat() };

        var result = await client.CreateQrCodeAsync(request);

        Assert.Equal("synthetic-qr-id-001", result.Id);
        Assert.Equal(1, handler.CallCount);

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.True(doc.RootElement.TryGetProperty("vat", out _));
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

    // XPAY-458 — deserialización de inc/tip/qr_code_reference en la
    // respuesta de Create QR Code (campos nuevos). Datos COMPLETAMENTE
    // SINTÉTICOS — ningún ID/key/customer_id/valor real de Gustavo.
    [Fact]
    public async Task CreateQrCodeAsync_Http200_DeserializesIncTipAndQrCodeReference()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, """
                {
                  "id": "synthetic-qr-id-003",
                  "customer_id": "synthetic-customer-id-001",
                  "status": "ACTIVE",
                  "type": "STATIC",
                  "qr_code_data": "00020101...synthetic-emv-payload...6304IJKL",
                  "qr_code_image": "iVBORw0KGgoSyntheticBase64Two==",
                  "created_at": "2026-01-01T00:00:00.000000Z",
                  "key_id": "synthetic-key-id-001",
                  "channel": "MPOS",
                  "vat": { "vat_type": "FIXED", "vat_value": "0.00", "vat_base_value": "0.00" },
                  "inc": { "inc_type": "FIXED", "inc_value": "0.00" },
                  "tip": { "tip_type": "FIXED", "tip_value": "0.00" },
                  "qr_code_reference": "SYNM4T1REF01"
                }
                """));
        var client = CreateClient(handler);

        var result = await client.CreateQrCodeAsync(SyntheticM4T1ConfirmedRequest());

        Assert.Equal("synthetic-qr-id-003", result.Id);
        Assert.Equal("STATIC", result.Type);
        Assert.Equal("ACTIVE", result.Status);
        Assert.Equal("MPOS", result.Channel);
        Assert.Equal("00020101...synthetic-emv-payload...6304IJKL", result.QrCodeData);
        Assert.Equal("iVBORw0KGgoSyntheticBase64Two==", result.QrCodeImage);

        Assert.NotNull(result.Vat);
        Assert.Equal("FIXED", result.Vat!.VatType);

        Assert.NotNull(result.Inc);
        Assert.Equal("FIXED", result.Inc!.IncType);
        Assert.Equal("0.00", result.Inc.IncValue);

        Assert.NotNull(result.Tip);
        Assert.Equal("FIXED", result.Tip!.TipType);
        Assert.Equal("0.00", result.Tip.TipValue);

        Assert.Equal("SYNM4T1REF01", result.QrCodeReference);
        Assert.Null(result.AdditionalInfo);
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

    // ══════════════════════════════════════════════════════════════════════
    // XPAY-305 — DecodeQrCodeAsync (POST /v1/qrcodes/decode)
    // ══════════════════════════════════════════════════════════════════════

    private static PassportDecodeQrCodeRequest SyntheticDecodeRequest(
        string customerId = "synthetic-customer-id-001",
        string qrCodeData = "00020101...synthetic-emv-payload...6304ABCD") =>
        new(CustomerId: customerId, QrCodeData: qrCodeData);

    // Ejemplo sintético construido con exactamente el shape confirmado en
    // XPAY-304 (docs.passportfintech.com/EN/decode-qr-code): todos los
    // campos root documentados presentes, ningún valor real (ningún
    // teléfono/cédula/QR de un cliente real, ningún key_id/customer_id real).
    private const string FullDecodeResponseBody = """
        {
          "amount": { "currency": "COP", "value": "80000.57" },
          "additional_info": { "transaction_purpose": "PURCHASE" },
          "inc": { "inc_type": "FIXED", "inc_value": "10.00" },
          "key": { "key_value": "synthetic-key-value-001", "key_type": "PHONE" },
          "qr_code_data": "00020101...synthetic-emv-payload...6304ABCD",
          "status": "ACTIVE",
          "acquirer_network_identifier": "SYNTH-NETWORK",
          "merchant": {
            "merchant_category_code": "0412",
            "merchant_country": "CO",
            "merchant_name": "Synthetic Merchant",
            "merchant_city": "Synthetic City",
            "merchant_post_code": "000000"
          },
          "channel": "MPOS",
          "vat": { "vat_type": "FIXED", "vat_value": "0.00", "vat_base_value": "0.00" },
          "qr_code_reference": "SYNTH12345",
          "type": "DYNAMIC"
        }
        """;

    // ── Fase 12: request happy path ─────────────────────────────────────

    [Fact]
    public async Task DecodeQrCodeAsync_HappyPath_PostsExactContractShape()
    {
        var tokenProvider = new FakePassportTokenProvider("synthetic-bearer-token");
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, FullDecodeResponseBody));
        var client = CreateClient(handler, tokenProvider);

        await client.DecodeQrCodeAsync(SyntheticDecodeRequest());

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/v1/qrcodes/decode", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal(BaseUrl, handler.LastRequest.RequestUri.GetLeftPart(UriPartial.Authority));
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal("synthetic-bearer-token", handler.LastRequest.Headers.Authorization!.Parameter);

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var root = doc.RootElement;

        Assert.Equal("synthetic-customer-id-001", root.GetProperty("customer_id").GetString());
        Assert.Equal(JsonValueKind.String, root.GetProperty("qr_code_data").ValueKind);
        Assert.Equal("00020101...synthetic-emv-payload...6304ABCD", root.GetProperty("qr_code_data").GetString());

        // Sin campos inventados: exactamente 2 propiedades top-level.
        var topLevelNames = new List<string>();
        foreach (var prop in root.EnumerateObject())
            topLevelNames.Add(prop.Name);
        Assert.Equal(new[] { "customer_id", "qr_code_data" }, topLevelNames);
    }

    // ── Fase 13: input guards ───────────────────────────────────────────

    [Fact]
    public async Task DecodeQrCodeAsync_NullRequest_ThrowsBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, FullDecodeResponseBody));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.DecodeQrCodeAsync(null!));
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DecodeQrCodeAsync_MissingCustomerId_ThrowsBeforeHttp(string? customerId)
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, FullDecodeResponseBody));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.DecodeQrCodeAsync(SyntheticDecodeRequest(customerId: customerId!)));
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DecodeQrCodeAsync_MissingQrCodeData_ThrowsBeforeHttp(string? qrCodeData)
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, FullDecodeResponseBody));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.DecodeQrCodeAsync(SyntheticDecodeRequest(qrCodeData: qrCodeData!)));
        Assert.Equal(0, handler.CallCount);
    }

    // ── Fase 14: response deserialization ───────────────────────────────

    [Fact]
    public async Task DecodeQrCodeAsync_Http200_DeserializesFullShapeDefensively()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, FullDecodeResponseBody));
        var client = CreateClient(handler);

        var result = await client.DecodeQrCodeAsync(SyntheticDecodeRequest());

        Assert.Equal(1, handler.CallCount);

        // amount.value se conserva como String (NO decimal).
        Assert.NotNull(result.Amount);
        Assert.Equal("80000.57", result.Amount!.Value);
        Assert.Equal("COP", result.Amount.Currency);

        // additional_info.transaction_purpose = "PURCHASE" se conserva tal
        // cual, como string defensivo — NO validado contra
        // ValidTransactionPurposes (ese conjunto sólo aplica al request de
        // Create QR), NO traducido.
        Assert.NotNull(result.AdditionalInfo);
        Assert.Equal("PURCHASE", result.AdditionalInfo!.TransactionPurpose);
        Assert.Null(result.AdditionalInfo.TerminalLabel);

        // inc — nuevo DTO de respuesta, ambos subcampos string.
        Assert.NotNull(result.Inc);
        Assert.Equal("FIXED", result.Inc!.IncType);
        Assert.Equal("10.00", result.Inc.IncValue);

        // key — reutiliza PassportKeyResponseDetail (exact match confirmado XPAY-304/305).
        Assert.NotNull(result.Key);
        Assert.Equal("PHONE", result.Key!.KeyType);
        Assert.Equal("synthetic-key-value-001", result.Key.KeyValue);

        Assert.Equal("00020101...synthetic-emv-payload...6304ABCD", result.QrCodeData);
        Assert.Equal("ACTIVE", result.Status);
        Assert.Equal("SYNTH-NETWORK", result.AcquirerNetworkIdentifier);

        Assert.NotNull(result.Merchant);
        Assert.Equal("0412", result.Merchant!.MerchantCategoryCode);
        Assert.Equal("CO", result.Merchant.MerchantCountry);
        Assert.Equal("Synthetic Merchant", result.Merchant.MerchantName);
        Assert.Equal("Synthetic City", result.Merchant.MerchantCity);
        Assert.Equal("000000", result.Merchant.MerchantPostCode);

        // type/channel/status nunca intentan un enum estricto — string plano.
        Assert.Equal("MPOS", result.Channel);
        Assert.Equal("DYNAMIC", result.Type);

        Assert.NotNull(result.Vat);
        Assert.Equal("FIXED", result.Vat!.VatType);
        Assert.Equal("0.00", result.Vat.VatValue);
        Assert.Equal("0.00", result.Vat.VatBaseValue);

        Assert.Equal("SYNTH12345", result.QrCodeReference);
    }

    // ── Fase 15: respuesta defensiva / parcial ──────────────────────────

    [Fact]
    public async Task DecodeQrCodeAsync_Http200_EmptyBody_DeserializesWithoutException()
    {
        // "{}" NO se declara aquí como respuesta contractual real del
        // proveedor — sólo demuestra que este DTO, a diferencia de
        // PassportQrCodeResponse (que exige `id` vía RequireQrId), no exige
        // ningún campo que Passport no documente como requerido de forma
        // exhaustiva en Decode (XPAY-304 FASE 11).
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var client = CreateClient(handler);

        var result = await client.DecodeQrCodeAsync(SyntheticDecodeRequest());

        Assert.Equal(1, handler.CallCount);
        Assert.NotNull(result);
        Assert.Null(result.Amount);
        Assert.Null(result.AdditionalInfo);
        Assert.Null(result.Inc);
        Assert.Null(result.Key);
        Assert.Null(result.QrCodeData);
        Assert.Null(result.Status);
        Assert.Null(result.AcquirerNetworkIdentifier);
        Assert.Null(result.Merchant);
        Assert.Null(result.Channel);
        Assert.Null(result.Vat);
        Assert.Null(result.QrCodeReference);
        Assert.Null(result.Type);
    }

    // ── Fase 16: error route — reutiliza el manejo genérico existente ────

    [Fact]
    public async Task DecodeQrCodeAsync_Http400_ThrowsTransportException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest, "{\"error\":\"bad_request\"}"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<PassportTransportException>(() => client.DecodeQrCodeAsync(SyntheticDecodeRequest()));
    }

    [Fact]
    public async Task DecodeQrCodeAsync_Http401_ThrowsAuthenticationException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{\"error\":\"unauthorized\"}"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<PassportAuthenticationException>(() => client.DecodeQrCodeAsync(SyntheticDecodeRequest()));
    }
}
