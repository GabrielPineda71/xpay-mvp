using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-344 — confirma que InvalidKeyValueGenerator sólo soporta BCODE
// (único tipo con contrato de formato confirmado sin ambigüedad — ver
// comentario de clase en InvalidKeyValueGenerator.cs) y que el valor
// generado viola el formato de forma determinista/clasificable sin
// coincidir nunca con un BCODE real válido.
public class InvalidKeyValueGeneratorTests
{
    [Fact]
    public void TryGenerate_Bcode_ReturnsTrue_WithInvalidFormatValue()
    {
        var ok = InvalidKeyValueGenerator.TryGenerate(PassportKeyType.BCODE, out var value, out var dimension);

        Assert.True(ok);
        Assert.NotNull(value);
        Assert.Equal("key_value_format", dimension);

        // Mismo largo/prefijo que un BCODE real (^00[0-9]{8}$) — la única
        // violación es el contenido no-numérico, para que la invalidez sea
        // inequívocamente de formato, no de longitud/prefijo.
        Assert.Equal(10, value!.Length);
        Assert.StartsWith("00", value);
        Assert.False(value.All(char.IsDigit));
    }

    [Fact]
    public void TryGenerate_Bcode_NeverProducesAValidBcodeByAccident()
    {
        for (var i = 0; i < 200; i++)
        {
            InvalidKeyValueGenerator.TryGenerate(PassportKeyType.BCODE, out var value, out _);
            Assert.DoesNotMatch("^00[0-9]{8}$", value!);
        }
    }

    [Theory]
    [InlineData(PassportKeyType.ID)]
    [InlineData(PassportKeyType.PHONE)]
    [InlineData(PassportKeyType.EMAIL)]
    [InlineData(PassportKeyType.ALPHA)]
    public void TryGenerate_UnsupportedType_ReturnsFalse(PassportKeyType unsupportedType)
    {
        var ok = InvalidKeyValueGenerator.TryGenerate(unsupportedType, out var value, out var dimension);

        Assert.False(ok);
        Assert.Null(value);
        Assert.Equal("unsupported_key_type_for_invalid_generation", dimension);
    }

    [Fact]
    public void SupportedKeyTypes_ContainsOnlyBcode()
    {
        Assert.Single(InvalidKeyValueGenerator.SupportedKeyTypes);
        Assert.Contains(PassportKeyType.BCODE, InvalidKeyValueGenerator.SupportedKeyTypes);
    }
}
