using Xpay.Api.DTOs;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Common;

// Piloto PROD — corrección 1 (callback y vendorData de Veriff) y validación
// pura de la corrección 2b (alta controlada del usuario del piloto).
public sealed class PilotoProdConfiguracionTests
{
    // ── Corrección 1 ───────────────────────────────────────────────────────

    // VERIFF_CALLBACK_URL = retorno del navegador a una página del frontend.
    [Theory]
    [InlineData("https://app.ejemplo.com/mi-wallet")]
    [InlineData(" https://app.ejemplo.com/mi-wallet ")]
    [InlineData("https://app.ejemplo.com:8443/mi-wallet")]
    [InlineData("https://app.ejemplo.com/")]
    public void Callback_https_a_pagina_del_frontend_es_valido(string url) =>
        Assert.True(KycService.EsCallbackVeriffValido(url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://app.ejemplo.com/mi-wallet")]
    [InlineData("/mi-wallet")]
    [InlineData("https://api.ejemplo.com/api/kyc/veriff/webhook")] // el webhook no es un retorno de navegador
    [InlineData("https://api.ejemplo.com/API/kyc/veriff/webhook")]
    [InlineData("https://api.ejemplo.com/api")]
    [InlineData("https://app.ejemplo.com/mi-wallet?x=1")]
    [InlineData("https://app.ejemplo.com/mi-wallet#f")]
    [InlineData("https://user:pass@app.ejemplo.com/mi-wallet")]
    [InlineData("ftp://app.ejemplo.com/mi-wallet")]
    public void Callback_invalido_se_rechaza(string? url) =>
        Assert.False(KycService.EsCallbackVeriffValido(url));

    [Fact]
    public void VendorData_por_ambiente_sin_PII()
    {
        Assert.Equal("XPAY-PROD-USUARIO-42", KycService.VendorDataVeriff(42, esProductivo: true));
        Assert.Equal("XPAY-QA-USUARIO-42",   KycService.VendorDataVeriff(42, esProductivo: false));
    }

    // ── Corrección 2b: habilitación y validación ───────────────────────────

    [Theory]
    [InlineData("true", "1", 1)]
    [InlineData("true", "10", 10)]
    [InlineData(null, "10", null)]
    [InlineData("True", "10", null)]
    [InlineData("false", "10", null)]
    [InlineData("true", null, null)]
    [InlineData("true", "0", null)]
    [InlineData("true", "-1", null)]
    [InlineData("true", "x", null)]
    public void Alta_piloto_solo_habilitada_con_true_exacto_y_maximo_positivo(string? habilitada, string? maximo, int? esperado) =>
        Assert.Equal(esperado, AltaUsuarioPilotoService.LeerMaximoHabilitado(habilitada, maximo));

    private static readonly DateTime Ahora = new(2026, 10, 29, 15, 0, 0, DateTimeKind.Utc);

    private static AltaUsuarioPilotoRequest Valida() => new()
    {
        NombreUsuario = " Piloto.Uno ", Email = "Piloto@Ejemplo.com", Celular = " 3000000000 ",
        VersionTerminos = "1.0", VersionPoliticaDatos = "1.0", VersionAutorizacionKyc = "1.0",
        FechaAceptacionUtc = Ahora.AddHours(-1), ReferenciaEvidencia = "ACTA-PILOTO-2026/001",
    };

    [Fact]
    public void Solicitud_valida_se_normaliza()
    {
        var d = AltaUsuarioPilotoService.Validar(Valida(), AmbienteXpayTests.Ambiente("Production"), Ahora);
        Assert.Equal("piloto.uno", d.NombreUsuario);
        Assert.Equal("piloto@ejemplo.com", d.Email);
        Assert.Equal("3000000000", d.Celular);
        Assert.Equal("ACTA-PILOTO-2026/001", d.ReferenciaEvidencia);
    }

    public static TheoryData<string, Action<AltaUsuarioPilotoRequest>> Invalidas => new()
    {
        { "sin usuario",              r => r.NombreUsuario = " " },
        { "usuario qa en PROD",       r => r.NombreUsuario = "qa.piloto" },
        { "email inválido",           r => r.Email = "no-es-email" },
        { "sin celular",              r => r.Celular = null },
        { "sin versión T&C",          r => r.VersionTerminos = null },
        { "sin versión política",     r => r.VersionPoliticaDatos = "" },
        { "sin versión KYC",          r => r.VersionAutorizacionKyc = "  " },
        { "versión con espacios",     r => r.VersionTerminos = "1.0 final" },
        { "sin fecha de aceptación",  r => r.FechaAceptacionUtc = null },
        { "fecha futura",             r => r.FechaAceptacionUtc = Ahora.AddHours(1) },
        { "sin evidencia",            r => r.ReferenciaEvidencia = null },
        { "evidencia con PII libre",  r => r.ReferenciaEvidencia = "Firmado por Juan Pérez" },
        { "evidencia demasiado larga", r => r.ReferenciaEvidencia = new string('A', 201) },
    };

    [Theory]
    [MemberData(nameof(Invalidas))]
    public void Sin_identidad_minima_o_sin_consentimiento_no_hay_alta(string caso, Action<AltaUsuarioPilotoRequest> romper)
    {
        var r = Valida();
        romper(r);
        var ex = Record.Exception(() => AltaUsuarioPilotoService.Validar(r, AmbienteXpayTests.Ambiente("Production"), Ahora));
        Assert.True(ex is InvalidOperationException, $"Caso '{caso}' debió rechazarse.");
    }
}
