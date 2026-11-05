using System.Text.Json;
using Xpay.Api.Integrations.Passport;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-531 §18/§2/§4 — pruebas puramente OFFLINE que demuestran (sin
// ejecutar ningún Payment real, sin OAuth, sin HTTP) que
// PassportCreatePaymentRequest, TAL COMO YA EXISTE en producción, puede
// representar exactamente lo que el director confirmó como contrato
// vigente para M5-T2:
//   - account_id / resolution_id (ya existentes, sin cambios)
//   - amount.value = "1.00" (ejemplo oficial vigente, NO un requisito
//     universal — M5_T2_AMOUNT_VALUE_CLASSIFICATION=OFFICIAL_CURRENT_API_EXAMPLE)
//   - amount.currency = "COP"
//   - display_name omitido (DISPLAY_NAME_PLAN=OMIT)
//   - NINGÚN campo `sender` — PAYMENT_REQUEST_SENDER_FIELD_REQUIRED=NO
//
// Estas pruebas NO modifican PassportCreatePaymentRequest/
// PassportPaymentAmountRequest — su único propósito es demostrar que el DTO
// productivo YA satisface el contrato aclarado, sin necesidad de ningún
// cambio. Si alguna de estas pruebas fallara, señalaría que el DTO SÍ
// necesitaría un cambio — pero XPAY-531 confirmó que no es el caso.
public class M5T2PaymentContractAlignmentTests
{
    private const string SyntheticAccountId = "synthetic-m5-t2-account-id";
    private const string SyntheticResolutionId = "synthetic-m5-t2-resolution-id";

    [Fact]
    public void Request_CanRepresentOfficialCurrentApiExampleAmount_1_00_COP()
    {
        var request = new PassportCreatePaymentRequest(
            AccountId: SyntheticAccountId,
            ResolutionId: SyntheticResolutionId,
            Amount: new PassportPaymentAmountRequest(Value: "1.00", Currency: PassportPaymentClient.CopCurrency));

        Assert.Equal("1.00", request.Amount.Value);
        Assert.Equal("COP", request.Amount.Currency);
    }

    [Fact]
    public void Request_DisplayName_CanBeOmitted_DefaultsToNull()
    {
        var request = new PassportCreatePaymentRequest(
            AccountId: SyntheticAccountId,
            ResolutionId: SyntheticResolutionId,
            Amount: new PassportPaymentAmountRequest(Value: "1.00", Currency: PassportPaymentClient.CopCurrency));

        Assert.Null(request.DisplayName);
    }

    [Fact]
    public void Request_WithDisplayNameOmitted_SerializesWithoutDisplayNameField()
    {
        // DISPLAY_NAME_PLAN=OMIT — el DTO ya soporta esto vía
        // JsonIgnoreCondition.WhenWritingNull (confirmado en el código
        // existente, XPAY-373); esta prueba lo demuestra empíricamente
        // serializando el request real.
        var request = new PassportCreatePaymentRequest(
            AccountId: SyntheticAccountId,
            ResolutionId: SyntheticResolutionId,
            Amount: new PassportPaymentAmountRequest(Value: "1.00", Currency: PassportPaymentClient.CopCurrency));

        var json = JsonSerializer.Serialize(request);

        Assert.DoesNotContain("display_name", json);
    }

    [Fact]
    public void Request_SerializedJson_NeverContainsSenderField()
    {
        // PAYMENT_REQUEST_SENDER_FIELD_REQUIRED=NO — el DTO nunca declaró
        // ni declara un campo `sender`; esta prueba lo fija como
        // comportamiento esperado y detectaría una futura regresión si
        // alguien lo agregara sin pasar por una nueva aclaración
        // contractual explícita.
        var request = new PassportCreatePaymentRequest(
            AccountId: SyntheticAccountId,
            ResolutionId: SyntheticResolutionId,
            Amount: new PassportPaymentAmountRequest(Value: "1.00", Currency: PassportPaymentClient.CopCurrency));

        var json = JsonSerializer.Serialize(request);

        Assert.DoesNotContain("sender", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequestType_HasNoSenderProperty()
    {
        // Verificación estructural — independiente de la serialización:
        // el tipo mismo nunca declaró un miembro `Sender`/`sender`.
        var properties = typeof(PassportCreatePaymentRequest).GetProperties();
        Assert.DoesNotContain(properties, p => p.Name.Contains("Sender", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Request_SerializedJson_ContainsExactlyTheExpectedTopLevelFields()
    {
        var request = new PassportCreatePaymentRequest(
            AccountId: SyntheticAccountId,
            ResolutionId: SyntheticResolutionId,
            Amount: new PassportPaymentAmountRequest(Value: "1.00", Currency: PassportPaymentClient.CopCurrency));

        var json = JsonSerializer.Serialize(request);
        using var doc = JsonDocument.Parse(json);
        var topLevelFieldNames = doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();

        // account_id, resolution_id, amount — display_name omitido (null),
        // ningún campo sender/receiver/direction/end_to_end_identification
        // (esos son documentados por Passport sólo en la RESPUESTA, no en
        // el request — ver PassportPaymentResponse.cs).
        Assert.Equal(new[] { "account_id", "amount", "resolution_id" }, topLevelFieldNames);
    }

    [Fact]
    public void Request_AccountId_IsDistinctFieldFromResolutionId_BothRequired()
    {
        // Confirmación estructural simple: ambos son parámetros posicionales
        // requeridos del record, no opcionales — un intento de omitir
        // cualquiera de los dos es un error de compilación, no un valor null
        // silencioso.
        var constructor = typeof(PassportCreatePaymentRequest).GetConstructors().Single();
        var requiredParamNames = constructor.GetParameters().Select(p => p.Name).ToArray();

        Assert.Contains("AccountId", requiredParamNames);
        Assert.Contains("ResolutionId", requiredParamNames);
        Assert.Contains("Amount", requiredParamNames);
    }

    [Fact]
    public void Amount_Currency_IsFixedProtocolConstant_COP()
    {
        // La constante ya usada por PassportPaymentClient/CreateBrebPaymentCertificationExecutor
        // — nunca configurable por entorno (mismo criterio ya aplicado al
        // amount de QR DYNAMIC).
        Assert.Equal("COP", PassportPaymentClient.CopCurrency);
    }
}
