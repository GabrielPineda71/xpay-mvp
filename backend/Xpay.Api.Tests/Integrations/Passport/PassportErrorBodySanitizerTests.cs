using Xpay.Api.Integrations.Passport;
using Xunit;

namespace Xpay.Api.Tests.Integrations.Passport;

// XPAY-358 — tests unitarios PUROS de PassportErrorBodySanitizer, sin HTTP
// ni ningún otro colaborador (los tests de integración con
// PassportHttpClient real viven en PassportHttpClientTests.cs). Cubren cada
// rama fail-closed exhaustivamente.
public class PassportErrorBodySanitizerTests
{
    [Fact]
    public void Extract_NullOrEmptyBody_ReturnsEmpty()
    {
        Assert.Equal(PassportErrorBodySanitizer.SanitizedErrorInfo.Empty, PassportErrorBodySanitizer.Extract(null));
        Assert.Equal(PassportErrorBodySanitizer.SanitizedErrorInfo.Empty, PassportErrorBodySanitizer.Extract(""));
    }

    [Fact]
    public void Extract_ValidJsonWithSafeFields_ReturnsBothValues()
    {
        var result = PassportErrorBodySanitizer.Extract(
            """{ "code": "INVALID_INPUT", "message": "One or more fields are invalid." }""");

        Assert.Equal("INVALID_INPUT", result.SafeErrorCode);
        Assert.Equal("One or more fields are invalid.", result.SafeErrorMessage);
    }

    [Theory]
    [InlineData("code")]
    [InlineData("error_code")]
    public void Extract_CodeFieldNameVariants_AreRecognized(string fieldName)
    {
        var result = PassportErrorBodySanitizer.Extract($$"""{ "{{fieldName}}": "SOME_CODE" }""");
        Assert.Equal("SOME_CODE", result.SafeErrorCode);
    }

    [Theory]
    [InlineData("message")]
    [InlineData("error")]
    [InlineData("detail")]
    [InlineData("description")]
    public void Extract_MessageFieldNameVariants_AreRecognized(string fieldName)
    {
        var result = PassportErrorBodySanitizer.Extract($$"""{ "{{fieldName}}": "Some safe text." }""");
        Assert.Equal("Some safe text.", result.SafeErrorMessage);
    }

    [Fact]
    public void Extract_UnknownFieldNames_AreIgnored()
    {
        var result = PassportErrorBodySanitizer.Extract(
            """{ "reason": "not in the allowlist", "info": "also not in the allowlist" }""");

        Assert.Null(result.SafeErrorCode);
        Assert.Null(result.SafeErrorMessage);
    }

    [Fact]
    public void Extract_MalformedJson_ReturnsEmpty_NoException()
    {
        var result = PassportErrorBodySanitizer.Extract("{ this is not valid json ");
        Assert.Equal(PassportErrorBodySanitizer.SanitizedErrorInfo.Empty, result);
    }

    [Theory]
    [InlineData("[1,2,3]")]           // array en la raíz.
    [InlineData("\"just a string\"")] // string plano en la raíz.
    [InlineData("42")]                // número plano en la raíz.
    [InlineData("true")]              // booleano en la raíz.
    public void Extract_NonObjectRoot_ReturnsEmpty(string body)
    {
        var result = PassportErrorBodySanitizer.Extract(body);
        Assert.Equal(PassportErrorBodySanitizer.SanitizedErrorInfo.Empty, result);
    }

    [Fact]
    public void Extract_BodyExceedsSizeLimit_ReturnsEmpty_NeverParsed()
    {
        var oversized = new string('A', PassportErrorBodySanitizer.MaxBodyLengthForDiagnosticsBytes + 1);
        var body = $$"""{ "code": "X", "message": "{{oversized}}" }""";

        var result = PassportErrorBodySanitizer.Extract(body);

        Assert.Equal(PassportErrorBodySanitizer.SanitizedErrorInfo.Empty, result);
    }

    [Fact]
    public void Extract_FieldExceedsPerFieldLimit_IsTruncatedBeforePatternCheck()
    {
        var longValue = new string('B', PassportErrorBodySanitizer.MaxExtractedFieldLength + 25);
        var result = PassportErrorBodySanitizer.Extract($$"""{ "message": "{{longValue}}" }""");

        Assert.NotNull(result.SafeErrorMessage);
        Assert.Equal(PassportErrorBodySanitizer.MaxExtractedFieldLength, result.SafeErrorMessage!.Length);
    }

    [Theory]
    [InlineData("Authorization: Bearer x")]
    [InlineData("value contains bearer token")]
    [InlineData("access_token leaked")]
    [InlineData("client_secret exposed")]
    [InlineData("api_key present")]
    [InlineData("api_secret present")]
    [InlineData("key_id mismatch")]
    [InlineData("key_value invalid")]
    [InlineData("customer_id not found")]
    [InlineData("account_id missing")]
    [InlineData("identification_number invalid")]
    [InlineData("qr_code_data malformed")]
    [InlineData("qr_code_image missing")]
    [InlineData("email invalid")]
    [InlineData("phone invalid")]
    [InlineData("contact synthetic@example.invalid")]
    [InlineData("9999999999 not found")] // secuencia larga de dígitos.
    public void Extract_MessageContainingSensitivePattern_IsDiscarded(string sensitiveMessage)
    {
        var result = PassportErrorBodySanitizer.Extract($$"""{ "code": "SAFE_CODE", "message": "{{sensitiveMessage}}" }""");

        Assert.Equal("SAFE_CODE", result.SafeErrorCode); // el code no contenía el patrón.
        Assert.Null(result.SafeErrorMessage);            // el message sí — descartado por completo.
    }

    [Fact]
    public void Extract_NonStringFieldValues_AreIgnored()
    {
        var result = PassportErrorBodySanitizer.Extract(
            """{ "code": 123, "message": { "nested": "object" } }""");

        Assert.Null(result.SafeErrorCode);
        Assert.Null(result.SafeErrorMessage);
    }

    [Fact]
    public void Extract_BlankStringFieldValues_AreIgnored()
    {
        var result = PassportErrorBodySanitizer.Extract("""{ "code": "   ", "message": "" }""");

        Assert.Null(result.SafeErrorCode);
        Assert.Null(result.SafeErrorMessage);
    }

    [Fact]
    public void Extract_FirstMatchingCandidateWins_WhenMultiplePresent()
    {
        // "code" antes que "error_code" según el orden declarado de la allowlist.
        var result = PassportErrorBodySanitizer.Extract(
            """{ "code": "FIRST", "error_code": "SECOND" }""");

        Assert.Equal("FIRST", result.SafeErrorCode);
    }

    [Fact]
    public void Extract_SensitiveCandidateSkipsToNextFieldName()
    {
        // "message" contiene un patrón sensible y se descarta, pero "error"
        // (siguiente candidato en la allowlist) es seguro y SÍ se usa.
        var result = PassportErrorBodySanitizer.Extract(
            """{ "message": "leaked access_token here", "error": "Safe fallback message." }""");

        Assert.Equal("Safe fallback message.", result.SafeErrorMessage);
    }
}
