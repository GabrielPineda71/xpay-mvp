using System.Security.Cryptography;
using System.Text;
using Xpay.Api.Integrations.Passport;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// XPAY-371 — tests offline/puros de BrebKeyResolutionRequestBuilder. CERO
// I/O, CERO red, CERO base de datos: PassportBrebLlave se construye a mano
// en memoria (no vía EF/DbContext — este proyecto de tests no referencia
// ningún provider InMemory/Sqlite; ningún test existente construye
// XpayDbContext, mismo criterio se preserva aquí).
public class BrebKeyResolutionRequestBuilderTests
{
    private const string OperationalCustomerId = "xpay-operational-customer-synthetic";
    private const string RawKeyValue            = "3001234567";

    // Mismo algoritmo EXACTO que BrebService.ComputeKeyHash / el método
    // privado duplicado en BrebKeyResolutionRequestBuilder — usado aquí
    // sólo para construir fixtures de prueba, no para probar el hash en sí.
    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static PassportBrebLlave LlaveActiva(string keyType = "PHONE", string? rawValue = null)
    {
        var value = rawValue ?? RawKeyValue;
        return new PassportBrebLlave
        {
            IdBrebLlave    = 42,
            IdWallet       = 7,
            TipoSujeto     = "USUARIO",
            IdUsuario      = 99,
            KeyType        = keyType,
            KeyValueMasked = "***4567",
            KeyValueHash   = Hash(value),
            Estado         = "PENDIENTE_VALIDACION",
            EsActiva       = true,
            FechaRegistro  = DateTime.UtcNow,
        };
    }

    // ── 1. Caso feliz — usa exactamente la llave activa ──────────────────

    [Fact]
    public void Build_LlaveActivaYConfirmacionCorrecta_DevuelveRequestCorrecto()
    {
        var llave = LlaveActiva(keyType: "PHONE");

        var result = BrebKeyResolutionRequestBuilder.Build(llave, RawKeyValue, OperationalCustomerId);

        Assert.Equal(OperationalCustomerId, result.CustomerId);
        Assert.Equal(PassportKeyType.PHONE, result.Key.KeyType);
        Assert.Equal(RawKeyValue, result.Key.KeyValue);
    }

    [Theory]
    [InlineData("id", PassportKeyType.ID)]
    [InlineData("EMAIL", PassportKeyType.EMAIL)]
    [InlineData("bcode", PassportKeyType.BCODE)]
    [InlineData("Alpha", PassportKeyType.ALPHA)]
    public void Build_KeyTypeLocalCaseInsensitivo_MapeaAlEnumPassportCorrecto(string localKeyType, PassportKeyType expected)
    {
        var llave = LlaveActiva(keyType: localKeyType);

        var result = BrebKeyResolutionRequestBuilder.Build(llave, RawKeyValue, OperationalCustomerId);

        Assert.Equal(expected, result.Key.KeyType);
    }

    // ── 2. Sin llave activa → rechazo antes de Passport ──────────────────

    [Fact]
    public void Build_LlaveActivaNull_LanzaLlaveNoConfirmadaException()
    {
        var ex = Assert.Throws<BrebKeyResolutionRequestBuilder.LlaveNoConfirmadaException>(
            () => BrebKeyResolutionRequestBuilder.Build(null, RawKeyValue, OperationalCustomerId));

        Assert.Contains("no tienes una llave", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── 3. No puede suministrar llave arbitraria desde el request ────────

    [Fact]
    public void Build_ValorConfirmacionNoCoincideConHashRegistrado_LanzaLlaveNoConfirmadaException()
    {
        var llave = LlaveActiva(rawValue: RawKeyValue);

        // Un valor DISTINTO al registrado — simula el intento de "resolver"
        // una llave arbitraria en vez de la propia ya registrada.
        var ex = Assert.Throws<BrebKeyResolutionRequestBuilder.LlaveNoConfirmadaException>(
            () => BrebKeyResolutionRequestBuilder.Build(llave, "3009999999", OperationalCustomerId));

        Assert.Contains("no coincide", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_KeyValueConfirmacionAusente_LanzaLlaveNoConfirmadaException(string? keyValue)
    {
        var llave = LlaveActiva();

        Assert.Throws<BrebKeyResolutionRequestBuilder.LlaveNoConfirmadaException>(
            () => BrebKeyResolutionRequestBuilder.Build(llave, keyValue, OperationalCustomerId));
    }

    // ── 4. Config de cuenta operativa ausente → fail-closed ──────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Build_OperationalCustomerIdAusente_LanzaPassportConfigurationException(string? customerId)
    {
        var llave = LlaveActiva();

        var ex = Assert.Throws<PassportConfigurationException>(
            () => BrebKeyResolutionRequestBuilder.Build(llave, RawKeyValue, customerId!));

        Assert.Contains(PassportOptions.EnvOperationalCustomerId, ex.Message);
    }

    // ── 5. KeyType local no mapeable al enum Passport ────────────────────

    [Fact]
    public void Build_KeyTypeLocalInvalido_LanzaInvalidOperationException()
    {
        var llave = LlaveActiva(keyType: "TIPO_INEXISTENTE");

        Assert.Throws<InvalidOperationException>(
            () => BrebKeyResolutionRequestBuilder.Build(llave, RawKeyValue, OperationalCustomerId));
    }

    // ── 6. El KeyValue nunca se filtra en el mensaje de excepción ────────

    [Fact]
    public void Build_ValorNoCoincide_MensajeDeExcepcionNuncaContieneElValorEnviado()
    {
        var llave = LlaveActiva();
        const string valorArbitrario = "3009999999-SECRETO";

        var ex = Assert.Throws<BrebKeyResolutionRequestBuilder.LlaveNoConfirmadaException>(
            () => BrebKeyResolutionRequestBuilder.Build(llave, valorArbitrario, OperationalCustomerId));

        Assert.DoesNotContain(valorArbitrario, ex.Message);
        Assert.DoesNotContain(RawKeyValue, ex.Message);
    }
}
