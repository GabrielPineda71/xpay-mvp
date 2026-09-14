using System.Text.Json;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// 6. request sintético válido se construye correctamente (offline, sin HTTP).
public class SyntheticCustomerRequestFactoryTests
{
    [Fact]
    public void BuildSynthetic_ProducesValidPassportLinkMerchantRequest()
    {
        var request = SyntheticCustomerRequestFactory.BuildSynthetic();

        Assert.IsType<PassportLinkMerchantRequest>(request);
        Assert.False(string.IsNullOrWhiteSpace(request.BusinessName));
        Assert.False(string.IsNullOrWhiteSpace(request.Email));
        Assert.False(string.IsNullOrWhiteSpace(request.MobilePhoneNumber));
        Assert.False(string.IsNullOrWhiteSpace(request.IdentificationNumber));
        Assert.False(string.IsNullOrWhiteSpace(request.MerchantCategoryCode));
        Assert.NotNull(request.Address);
        Assert.Equal("BUSINESS", request.Type);
        Assert.Equal("NIT", request.IdentificationType);
    }

    [Fact]
    public void BuildSynthetic_SerializesWithoutException_UsingRealContractShape()
    {
        var request = SyntheticCustomerRequestFactory.BuildSynthetic();

        var json = JsonSerializer.Serialize(request);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("business_name", out _));
        Assert.True(root.TryGetProperty("email", out _));
        Assert.True(root.TryGetProperty("mobile_phone_number", out _));
        Assert.True(root.TryGetProperty("identification_number", out _));
        Assert.True(root.TryGetProperty("merchant_category_code", out _));
        Assert.True(root.TryGetProperty("address", out _));
        Assert.True(root.TryGetProperty("type", out _));
        Assert.True(root.TryGetProperty("identification_type", out _));
    }

    [Fact]
    public void BuildSynthetic_NeverUsesRealTestIdentificationNumberEnvVar()
    {
        // XPAY-312 FASE 5 — confirma que el factory NUNCA lee
        // PASSPORT_TEST_IDENTIFICATION_NUMBER del entorno, incluso si esa
        // variable está presente en el proceso (simulado aquí).
        Environment.SetEnvironmentVariable("PASSPORT_TEST_IDENTIFICATION_NUMBER", "999999999-9");
        try
        {
            var request = SyntheticCustomerRequestFactory.BuildSynthetic();
            Assert.Equal("000000000", request.IdentificationNumber);
            Assert.NotEqual("999999999-9", request.IdentificationNumber);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PASSPORT_TEST_IDENTIFICATION_NUMBER", null);
        }
    }
}
