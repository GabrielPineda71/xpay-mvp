using Xpay.Api.Integrations.Passport;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// XPAY-371 — tests offline/puros de BrebKeyResolutionResponseMapper. CERO
// I/O, CERO red, CERO base de datos.
public class BrebKeyResolutionResponseMapperTests
{
    private static PassportResolveKeyResponse FullResponse() => new()
    {
        Id           = "synthetic-resolution-id-001",
        ReceptorNode = "SYNTH-NODE",
        ResolvedAt   = "2026-01-01T00:00:00.000000Z",
        ExpiresAt    = "2026-01-01T00:30:00.000000Z",
        CustomerId   = "xpay-operational-customer-synthetic",
        Owner = new PassportResolveKeyOwnerResponse
        {
            FirstName            = "Synthetic",
            SecondName           = "Test",
            FirstLastName        = "Owner",
            SecondLastName       = "Fixture",
            IdentificationType   = "CC",
            IdentificationNumber = "1000000000",
            Type                 = "PERSON",
        },
        Key = new PassportKeyResponseDetail { KeyType = "BCODE", KeyValue = "0000000000" },
        Participant = new PassportResolveKeyParticipantResponse
        {
            Name                 = "Synthetic Participant Bank",
            IdentificationNumber = "900000000",
        },
        Account = new PassportResolveKeyAccountResponse
        {
            AccountNumber = "2222220000",
            AccountType   = "SAVINGS",
        },
    };

    private static PassportBrebLlave Llave() => new()
    {
        IdBrebLlave    = 42,
        IdWallet       = 7,
        TipoSujeto     = "USUARIO",
        IdUsuario      = 99,
        KeyType        = "PHONE",
        KeyValueMasked = "***4567",
        KeyValueHash   = "irrelevant-for-these-tests",
        Estado         = "PENDIENTE_VALIDACION",
        EsActiva       = true,
        FechaRegistro  = DateTime.UtcNow,
    };

    // ── IsComplete ────────────────────────────────────────────────────────

    [Fact]
    public void IsComplete_RespuestaCompleta_DevuelveTrue()
    {
        Assert.True(BrebKeyResolutionResponseMapper.IsComplete(FullResponse()));
    }

    [Fact]
    public void IsComplete_SinOwner_DevuelveFalse()
    {
        var response = FullResponse();
        response.Owner = null;
        Assert.False(BrebKeyResolutionResponseMapper.IsComplete(response));
    }

    [Fact]
    public void IsComplete_SinParticipant_DevuelveFalse()
    {
        var response = FullResponse();
        response.Participant = null;
        Assert.False(BrebKeyResolutionResponseMapper.IsComplete(response));
    }

    [Fact]
    public void IsComplete_SinAccount_DevuelveFalse()
    {
        var response = FullResponse();
        response.Account = null;
        Assert.False(BrebKeyResolutionResponseMapper.IsComplete(response));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsComplete_IdAusente_DevuelveFalse(string? id)
    {
        var response = FullResponse();
        response.Id = id;
        Assert.False(BrebKeyResolutionResponseMapper.IsComplete(response));
    }

    // ── ApplyToLlave ──────────────────────────────────────────────────────

    [Fact]
    public void ApplyToLlave_RespuestaCompleta_PueblaCamposEsperadosYMarcaValidada()
    {
        var llave = Llave();
        var now = new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);

        BrebKeyResolutionResponseMapper.ApplyToLlave(llave, FullResponse(), now, updatedByUsuario: 7);

        Assert.Equal("VALIDADA", llave.Estado);
        Assert.Equal(now, llave.FechaValidacion);
        Assert.Equal(now, llave.FechaActualizacion);
        Assert.Equal(7, llave.UpdatedByUsuario);
        Assert.Equal("CC", llave.OwnerIdentificationType);
        Assert.Equal("Synthetic Participant Bank", llave.ParticipantName);
        Assert.Equal("SAVINGS", llave.AccountType);
    }

    [Fact]
    public void ApplyToLlave_EnmascaraIdentificacionYCuenta_NuncaValorCompleto()
    {
        var llave = Llave();
        BrebKeyResolutionResponseMapper.ApplyToLlave(llave, FullResponse(), DateTime.UtcNow, updatedByUsuario: 7);

        Assert.NotNull(llave.OwnerIdentificationNumberMasked);
        Assert.DoesNotContain("1000000000", llave.OwnerIdentificationNumberMasked);
        Assert.EndsWith("0000", llave.OwnerIdentificationNumberMasked);

        Assert.NotNull(llave.AccountNumberMasked);
        Assert.DoesNotContain("2222220000", llave.AccountNumberMasked);
        Assert.EndsWith("0000", llave.AccountNumberMasked);

        Assert.NotNull(llave.ParticipantIdentificationNumber);
        Assert.DoesNotContain("900000000", llave.ParticipantIdentificationNumber);
    }

    [Fact]
    public void ApplyToLlave_NombreTitular_MuestraSoloPrimerNombreYEnmascaraElResto()
    {
        var llave = Llave();
        BrebKeyResolutionResponseMapper.ApplyToLlave(llave, FullResponse(), DateTime.UtcNow, updatedByUsuario: 7);

        Assert.Equal("Synthetic ***", llave.OwnerNameMasked);
        Assert.DoesNotContain("Owner", llave.OwnerNameMasked!);
        Assert.DoesNotContain("Fixture", llave.OwnerNameMasked!);
    }

    [Fact]
    public void ApplyToLlave_BusinessName_UsaRazonSocialEnmascarada()
    {
        var llave = Llave();
        var response = FullResponse();
        response.Owner!.BusinessName = "Comercio Synthetic SAS";
        response.Owner!.FirstName    = null;

        BrebKeyResolutionResponseMapper.ApplyToLlave(llave, response, DateTime.UtcNow, updatedByUsuario: 7);

        Assert.Equal("Comercio ***", llave.OwnerNameMasked);
    }

    // XPAY-371 FASE 3 — "no asumir equivalencias que el DTO no demuestre":
    // PassportKeyId/PassportCustomerId/PassportAccountId NUNCA se pueblan
    // desde Resolve Key, porque ese contrato no los provee con la misma
    // semántica (ver comentario de clase). Regresión explícita.
    // XPAY-373 — la resolución exitosa debe cachear su id y vencimiento en
    // la llave, para que un retiro posterior pueda copiarlos a su propio
    // snapshot inmutable (ver PassportBrebRetiro.PassportResolutionExpiresAtUtc
    // y BrebPaymentRequestBuilder).
    [Fact]
    public void ApplyToLlave_PueblaResolutionIdYExpiresAt()
    {
        var llave = Llave();
        var response = FullResponse();

        BrebKeyResolutionResponseMapper.ApplyToLlave(llave, response, DateTime.UtcNow, updatedByUsuario: 7);

        Assert.Equal("synthetic-resolution-id-001", llave.PassportResolutionId);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 30, 0, DateTimeKind.Utc), llave.PassportResolutionExpiresAtUtc);
    }

    [Fact]
    public void ApplyToLlave_ExpiresAtNoParseable_QuedaNullFailClosed()
    {
        var llave = Llave();
        var response = FullResponse();
        response.ExpiresAt = "no-es-una-fecha";

        BrebKeyResolutionResponseMapper.ApplyToLlave(llave, response, DateTime.UtcNow, updatedByUsuario: 7);

        Assert.Null(llave.PassportResolutionExpiresAtUtc);
    }

    [Fact]
    public void ApplyToLlave_NuncaPueblaKeyIdCustomerIdNiAccountId()
    {
        var llave = Llave();
        BrebKeyResolutionResponseMapper.ApplyToLlave(llave, FullResponse(), DateTime.UtcNow, updatedByUsuario: 7);

        Assert.Null(llave.PassportKeyId);
        Assert.Null(llave.PassportCustomerId);
        Assert.Null(llave.PassportAccountId);
    }

    [Fact]
    public void ApplyToLlave_RespuestaIncompleta_LanzaInvalidOperationException()
    {
        var llave = Llave();
        var response = FullResponse();
        response.Owner = null;

        Assert.Throws<InvalidOperationException>(
            () => BrebKeyResolutionResponseMapper.ApplyToLlave(llave, response, DateTime.UtcNow, updatedByUsuario: 7));

        // La llave NO debe quedar marcada como validada si el guard falla.
        Assert.Equal("PENDIENTE_VALIDACION", llave.Estado);
    }

    // ── ToSanitizedResponse ───────────────────────────────────────────────

    [Fact]
    public void ToSanitizedResponse_NuncaExponeIdentificacionOCuentaCompleta()
    {
        var llave = Llave();
        var response = FullResponse();
        BrebKeyResolutionResponseMapper.ApplyToLlave(llave, response, DateTime.UtcNow, updatedByUsuario: 7);

        var dto = BrebKeyResolutionResponseMapper.ToSanitizedResponse(llave, response);

        Assert.True(dto.ResolucionVerificadaPassport);
        Assert.DoesNotContain("1000000000", dto.TitularIdentificacionMasked ?? string.Empty);
        Assert.DoesNotContain("2222220000", dto.CuentaMasked ?? string.Empty);
        Assert.Equal("Synthetic ***", dto.TitularNombreMasked);
        Assert.Equal("Synthetic Participant Bank", dto.EntidadFinanciera);
        Assert.Equal("SAVINGS", dto.TipoCuenta);
        Assert.Equal(response.ExpiresAt, dto.VigenteHasta);
    }

    // ── WasResolvedByPassport (real vs. simulada) ────────────────────────

    [Fact]
    public void WasResolvedByPassport_LlaveSinOwnerNameMasked_DevuelveFalse()
    {
        // Equivale a una llave VALIDADA sólo por
        // POST /api/breb/admin/simular-validacion-llave — nunca puebla
        // OwnerNameMasked.
        var llave = Llave();
        llave.Estado = "VALIDADA";

        Assert.False(BrebKeyResolutionResponseMapper.WasResolvedByPassport(llave));
    }

    [Fact]
    public void WasResolvedByPassport_LlaveConOwnerNameMasked_DevuelveTrue()
    {
        var llave = Llave();
        BrebKeyResolutionResponseMapper.ApplyToLlave(llave, FullResponse(), DateTime.UtcNow, updatedByUsuario: 7);

        Assert.True(BrebKeyResolutionResponseMapper.WasResolvedByPassport(llave));
    }
}
