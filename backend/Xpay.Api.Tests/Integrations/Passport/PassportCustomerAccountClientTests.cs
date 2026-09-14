using System.Net;
using Xpay.Api.Integrations.Passport;
using Xunit;

namespace Xpay.Api.Tests.Integrations.Passport;

// XPAY-279 — unit tests de PassportCustomerAccountClient. SIN red real: se
// construye el PassportHttpClient de producción (XPAY-272) sobre un
// FakeHttpMessageHandler + FakePassportTokenProvider, exactamente igual al
// patrón ya usado en PassportHttpClientTests — esto ejercita la cadena
// completa (Bearer + BaseUrl + JSON) tal como se ejecutará en producción,
// no un doble de IPassportHttpClient. Todos los valores son sintéticos:
// NO se usan NIT, email, teléfono ni IDs reales.
public class PassportCustomerAccountClientTests
{
    private const string BaseUrl = "https://passport.test";

    private static Dictionary<string, string?> ValidConfig() => new()
    {
        [PassportOptions.EnvBaseUrl]      = BaseUrl,
        [PassportOptions.EnvClientId]     = "test-client-id",
        [PassportOptions.EnvClientSecret] = "test-client-secret",
    };

    private static PassportCustomerAccountClient CreateClient(
        FakeHttpMessageHandler handler,
        FakePassportTokenProvider? tokenProvider = null)
    {
        var http = new PassportHttpClient(
            new FakeHttpClientFactory(handler),
            tokenProvider ?? new FakePassportTokenProvider("synthetic-bearer-token"),
            new FakeConfiguration(ValidConfig()),
            new CapturingLogger<PassportHttpClient>());
        return new PassportCustomerAccountClient(http);
    }

    private static PassportLinkMerchantRequest SyntheticMerchantRequest(
        string? line2 = "Suite 100", string? line3 = null) => new(
        BusinessName: "Synthetic Test Business SAS",
        Email: "synthetic-test@example-sandbox.test",
        MobilePhoneNumber: "+5730000000",
        IdentificationNumber: "900000000",
        MerchantCategoryCode: "5999",
        Address: new PassportMerchantAddressRequest(
            Line1: "Synthetic Street 123",
            City: "Bogota",
            State: "Cundinamarca",
            PostCode: "110111",
            Country: "CO")
        {
            Line2 = line2,
            Line3 = line3,
        });

    // ── Link Merchant ─────────────────────────────────────────────────────

    // 1/2 — POST correcto al path exacto contractual.
    [Fact]
    public async Task LinkMerchantAsync_PostsToExactContractPath()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"id\":\"synthetic-customer-id\"}"));
        var client = CreateClient(handler);

        await client.LinkMerchantAsync(SyntheticMerchantRequest());

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/v1/customers/business/link", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal(BaseUrl, handler.LastRequest.RequestUri.GetLeftPart(UriPartial.Authority));
    }

    // 3 — Bearer presente vía la infraestructura Passport existente.
    [Fact]
    public async Task LinkMerchantAsync_SendsBearerFromExistingInfrastructure()
    {
        var tokenProvider = new FakePassportTokenProvider("synthetic-bearer-token");
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"id\":\"synthetic-customer-id\"}"));
        var client = CreateClient(handler, tokenProvider);

        await client.LinkMerchantAsync(SyntheticMerchantRequest());

        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("synthetic-bearer-token", handler.LastRequest.Headers.Authorization!.Parameter);
        Assert.Equal(1, tokenProvider.CallCount);
    }

    // 4/5/6 — JSON request contiene exactamente los campos contractuales,
    // address se serializa correctamente, line_2/line_3 opcionales se omiten
    // cuando son null.
    //
    // Se parsea el body con JsonDocument en vez de comparar substrings crudos:
    // System.Text.Json escapa por defecto ciertos caracteres ASCII "seguros"
    // pero no HTML-safe (p.ej. '+' → "+") — la comparación por substring
    // literal es frágil ante ese detalle de encoding aunque el JSON producido
    // sea perfectamente válido; parsear y comparar valores ya decodificados
    // es la forma correcta de verificar el contenido real transmitido.
    [Fact]
    public async Task LinkMerchantAsync_SerializesContractualFieldsAndAddress()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"id\":\"synthetic-customer-id\"}"));
        var client = CreateClient(handler);

        await client.LinkMerchantAsync(SyntheticMerchantRequest(line2: "Suite 100", line3: null));

        using var doc = System.Text.Json.JsonDocument.Parse(handler.LastRequestBody!);
        var root = doc.RootElement;

        Assert.Equal("BUSINESS", root.GetProperty("type").GetString());
        Assert.Equal("Synthetic Test Business SAS", root.GetProperty("business_name").GetString());
        Assert.Equal("synthetic-test@example-sandbox.test", root.GetProperty("email").GetString());
        Assert.Equal("+5730000000", root.GetProperty("mobile_phone_number").GetString());
        Assert.Equal("NIT", root.GetProperty("identification_type").GetString());
        Assert.Equal("900000000", root.GetProperty("identification_number").GetString());
        Assert.Equal("5999", root.GetProperty("merchant_category_code").GetString());

        var address = root.GetProperty("address");
        Assert.Equal("Synthetic Street 123", address.GetProperty("line_1").GetString());
        Assert.Equal("Suite 100", address.GetProperty("line_2").GetString());
        Assert.Equal("Bogota", address.GetProperty("city").GetString());
        Assert.Equal("CO", address.GetProperty("country").GetString());
        // line_3 es null en este test — debe omitirse, no enviarse como "line_3":null.
        Assert.False(address.TryGetProperty("line_3", out _));
    }

    // 6 (complemento) — ambos opcionales ausentes simultáneamente.
    [Fact]
    public async Task LinkMerchantAsync_OmitsBothOptionalAddressLinesWhenNull()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"id\":\"synthetic-customer-id\"}"));
        var client = CreateClient(handler);

        await client.LinkMerchantAsync(SyntheticMerchantRequest(line2: null, line3: null));

        var body = handler.LastRequestBody!;
        Assert.DoesNotContain("line_2", body);
        Assert.DoesNotContain("line_3", body);
    }

    // 7/8 — HTTP 200 documentado se deserializa y `id` se recupera.
    [Fact]
    public async Task LinkMerchantAsync_Http200_DeserializesIdAsCustomerId()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                "{\"id\":\"synthetic-customer-id-001\",\"status\":\"ACTIVE\",\"type\":\"BUSINESS\"}"));
        var client = CreateClient(handler);

        var result = await client.LinkMerchantAsync(SyntheticMerchantRequest());

        Assert.Equal("synthetic-customer-id-001", result.Id);
        Assert.Equal("ACTIVE", result.Status);
        Assert.Equal("BUSINESS", result.Type);
    }

    // Complemento Fase 5 — HTTP 201 (el valor del anexo de certificación,
    // distinto de la doc oficial) también se acepta como éxito, porque
    // IPassportHttpClient usa IsSuccessStatusCode genérico (2xx), sin
    // codificar ningún workaround específico para esta discrepancia.
    [Fact]
    public async Task LinkMerchantAsync_Http201_AlsoAcceptedAsSuccess_NoWorkaroundNeeded()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.Created, "{\"id\":\"synthetic-customer-id-201\"}"));
        var client = CreateClient(handler);

        var result = await client.LinkMerchantAsync(SyntheticMerchantRequest());

        Assert.Equal("synthetic-customer-id-201", result.Id);
    }

    // ── Retrieve Customer ─────────────────────────────────────────────────

    // 9/10/11/12 — GET, path con customer_id correcto, sin body, response
    // documentada se deserializa.
    [Fact]
    public async Task RetrieveCustomerAsync_UsesGetWithCorrectPathAndNoBody()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                "{\"id\":\"synthetic-customer-id-001\",\"business_name\":\"Synthetic Test Business SAS\"}"));
        var client = CreateClient(handler);

        var result = await client.RetrieveCustomerAsync("synthetic-customer-id-001");

        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal("/v1/customers/synthetic-customer-id-001", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Null(handler.LastRequestBody);
        Assert.Equal("synthetic-customer-id-001", result.Id);
        Assert.Equal("Synthetic Test Business SAS", result.BusinessName);
    }

    // 13 — error HTTP no se interpreta como éxito.
    [Fact]
    public async Task RetrieveCustomerAsync_Http404_ThrowsTransportException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.NotFound, "{\"error\":\"not_found\"}"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<PassportTransportException>(
            () => client.RetrieveCustomerAsync("synthetic-unknown-id"));
    }

    [Fact]
    public async Task LinkMerchantAsync_Http400_ThrowsTransportException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest, "{\"error\":\"bad_request\"}"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<PassportTransportException>(
            () => client.LinkMerchantAsync(SyntheticMerchantRequest()));
    }

    [Fact]
    public async Task RetrieveCustomerAsync_EmptyId_ThrowsBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"id\":\"x\"}"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => client.RetrieveCustomerAsync(""));
        Assert.Equal(0, handler.CallCount);
    }

    // ── Link Account ──────────────────────────────────────────────────────

    private static PassportLinkAccountRequest SyntheticAccountRequest() => new(
        CustomerId: "synthetic-customer-id-001",
        AccountNumber: "0000000000000001");

    // 1/2/3 — POST correcto al path exacto, con customer_id/account_type/
    // account_number sintéticos.
    [Fact]
    public async Task LinkAccountAsync_PostsToExactContractPathWithRequiredFields()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"id\":\"synthetic-account-id\"}"));
        var client = CreateClient(handler);

        await client.LinkAccountAsync(SyntheticAccountRequest());

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/v1/accounts/link", handler.LastRequest.RequestUri!.AbsolutePath);

        var body = handler.LastRequestBody!;
        Assert.Contains("\"customer_id\":\"synthetic-customer-id-001\"", body);
        Assert.Contains("\"account_type\":\"ORDINARY\"", body);
        Assert.Contains("\"account_number\":\"0000000000000001\"", body);
    }

    // 4/5 — HTTP 200 se deserializa, `id` se recupera como account_id.
    [Fact]
    public async Task LinkAccountAsync_Http200_DeserializesIdAsAccountId()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                "{\"id\":\"synthetic-account-id-001\",\"customer_id\":\"synthetic-customer-id-001\",\"status\":\"ACTIVE\"}"));
        var client = CreateClient(handler);

        var result = await client.LinkAccountAsync(SyntheticAccountRequest());

        Assert.Equal("synthetic-account-id-001", result.Id);
        Assert.Equal("synthetic-customer-id-001", result.CustomerId);
        Assert.Equal("ACTIVE", result.Status);
    }

    // 6/7/8 — available_balance/pending_balance se deserializan, value/currency
    // se preservan.
    [Fact]
    public async Task LinkAccountAsync_DeserializesAvailableAndPendingBalance()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, """
                {
                  "id": "synthetic-account-id-001",
                  "available_balance": { "value": 150000, "currency": "COP" },
                  "pending_balance": { "value": 0, "currency": "COP" }
                }
                """));
        var client = CreateClient(handler);

        var result = await client.LinkAccountAsync(SyntheticAccountRequest());

        Assert.NotNull(result.AvailableBalance);
        Assert.Equal(150000m, result.AvailableBalance!.Value);
        Assert.Equal("COP", result.AvailableBalance.Currency);

        Assert.NotNull(result.PendingBalance);
        Assert.Equal(0m, result.PendingBalance!.Value);
        Assert.Equal("COP", result.PendingBalance.Currency);
    }

    // ── Retrieve Account ──────────────────────────────────────────────────

    // 9/10/11 — GET, path con account_id correcto, sin body.
    [Fact]
    public async Task RetrieveAccountAsync_UsesGetWithCorrectPathAndNoBody()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                "{\"id\":\"synthetic-account-id-001\",\"customer_id\":\"synthetic-customer-id-001\"}"));
        var client = CreateClient(handler);

        var result = await client.RetrieveAccountAsync("synthetic-account-id-001");

        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal("/v1/accounts/synthetic-account-id-001", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Null(handler.LastRequestBody);
        Assert.Equal("synthetic-account-id-001", result.Id);
        Assert.Equal("synthetic-customer-id-001", result.CustomerId);
    }

    // 12 — error HTTP no se interpreta como éxito.
    [Fact]
    public async Task RetrieveAccountAsync_Http404_ThrowsTransportException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.NotFound, "{\"error\":\"not_found\"}"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<PassportTransportException>(
            () => client.RetrieveAccountAsync("synthetic-unknown-id"));
    }

    [Fact]
    public async Task LinkAccountAsync_Http401_ThrowsAuthenticationException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{\"error\":\"unauthorized\"}"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<PassportAuthenticationException>(
            () => client.LinkAccountAsync(SyntheticAccountRequest()));
    }

    [Fact]
    public async Task RetrieveAccountAsync_EmptyId_ThrowsBeforeHttp()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"id\":\"x\"}"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => client.RetrieveAccountAsync("  "));
        Assert.Equal(0, handler.CallCount);
    }

    // ── Forward compatibility (Fase 8) ───────────────────────────────────

    // Un campo JSON adicional, desconocido por XPAY, no debe romper la
    // deserialización de ninguno de los 4 DTOs de respuesta — System.Text.Json
    // ignora propiedades no mapeadas por defecto; no se requiere ningún
    // converter adicional.
    [Fact]
    public async Task LinkMerchantAsync_UnknownAdditionalField_DoesNotBreakDeserialization()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, """
                {
                  "id": "synthetic-customer-id-001",
                  "business_name": "Synthetic Test Business SAS",
                  "risk_score_v2_experimental": { "nested": ["a", "b"], "flag": true }
                }
                """));
        var client = CreateClient(handler);

        var result = await client.LinkMerchantAsync(SyntheticMerchantRequest());

        Assert.Equal("synthetic-customer-id-001", result.Id);
        Assert.Equal("Synthetic Test Business SAS", result.BusinessName);
    }

    [Fact]
    public async Task LinkAccountAsync_UnknownAdditionalField_DoesNotBreakDeserialization()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, """
                {
                  "id": "synthetic-account-id-001",
                  "customer_id": "synthetic-customer-id-001",
                  "new_field_not_yet_documented": "some-future-value"
                }
                """));
        var client = CreateClient(handler);

        var result = await client.LinkAccountAsync(SyntheticAccountRequest());

        Assert.Equal("synthetic-account-id-001", result.Id);
        Assert.Equal("synthetic-customer-id-001", result.CustomerId);
    }

    // ── XPAY-281 — Corrección 1: PassportBalance.Value acepta número JSON o
    // string numérico, y sigue rechazando strings no numéricos ─────────────

    [Theory]
    [InlineData("150000", 150000)]              // A. NUMBER_JSON
    [InlineData("\"150000\"", 150000)]          // B. NUMERIC_STRING_JSON
    [InlineData("\"150000.25\"", 150000.25)]    // C. DECIMAL_STRING_JSON
    public async Task LinkAccountAsync_BalanceValue_AcceptsNumberOrNumericString(string rawValueJson, double expected)
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, $$"""
                {
                  "id": "synthetic-account-id-001",
                  "available_balance": { "value": {{rawValueJson}}, "currency": "COP" }
                }
                """));
        var client = CreateClient(handler);

        var result = await client.LinkAccountAsync(SyntheticAccountRequest());

        Assert.Equal((decimal)expected, result.AvailableBalance!.Value);
    }

    // D. INVALID_STRING_JSON — un string no numérico falla de forma
    // controlada (JsonException, nunca se convierte silenciosamente en
    // null/0).
    [Fact]
    public async Task LinkAccountAsync_BalanceValue_InvalidStringRejectedControlled()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, """
                {
                  "id": "synthetic-account-id-001",
                  "available_balance": { "value": "not-a-number", "currency": "COP" }
                }
                """));
        var client = CreateClient(handler);

        // El fallo de deserialización se propaga como PassportProtocolException
        // (mismo tratamiento que cualquier JSON no interpretable en la base
        // HTTP compartida — ver PassportHttpClient).
        await Assert.ThrowsAsync<PassportProtocolException>(
            () => client.LinkAccountAsync(SyntheticAccountRequest()));
    }

    // ── XPAY-281 — Corrección 2: id remoto obligatorio en las 4 operaciones ─
    //
    // Cada Theory usa un body 2xx bien formado pero sin un `id` utilizable
    // (ausente / "" / sólo whitespace) — el HTTP sí ocurre (handler.CallCount
    // == 1), y el fallo proviene de la validación de PROTOCOLO sobre la
    // respuesta, no de una validación previa del request.

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"id\":\"\"}")]
    [InlineData("{\"id\":\"   \"}")]
    public async Task LinkMerchantAsync_MissingOrBlankId_ThrowsProtocolException(string body)
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, body));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<PassportProtocolException>(
            () => client.LinkMerchantAsync(SyntheticMerchantRequest()));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task RetrieveCustomerAsync_MissingId_ThrowsProtocolException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"business_name\":\"Synthetic\"}"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<PassportProtocolException>(
            () => client.RetrieveCustomerAsync("synthetic-customer-id-001"));
        Assert.Equal(1, handler.CallCount);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"id\":\"\"}")]
    public async Task LinkAccountAsync_MissingOrBlankId_ThrowsProtocolException(string body)
    {
        var handler = new FakeHttpMessageHandler(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, body));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<PassportProtocolException>(
            () => client.LinkAccountAsync(SyntheticAccountRequest()));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task RetrieveAccountAsync_MissingId_ThrowsProtocolException()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"customer_id\":\"synthetic-customer-id-001\"}"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<PassportProtocolException>(
            () => client.RetrieveAccountAsync("synthetic-account-id-001"));
        Assert.Equal(1, handler.CallCount);
    }

    // El mensaje de protocolo es estático/saneado — nunca incluye body,
    // token, Authorization ni PII del request.
    [Fact]
    public async Task LinkMerchantAsync_MissingId_ExceptionMessageIsSanitized()
    {
        var handler = new FakeHttpMessageHandler(
            () => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<PassportProtocolException>(
            () => client.LinkMerchantAsync(SyntheticMerchantRequest()));

        Assert.DoesNotContain("synthetic-bearer-token", ex.Message);
        Assert.DoesNotContain("test-client-secret", ex.Message);
        Assert.DoesNotContain("Synthetic Test Business SAS", ex.Message);
        Assert.DoesNotContain("900000000", ex.Message);
    }
}
