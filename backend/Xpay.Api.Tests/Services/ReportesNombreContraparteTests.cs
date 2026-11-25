using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

public sealed class ReportesNombreContraparteTests
{
    [Theory]
    [InlineData(true, "  María  José\t", " López\nMuñoz ", "Perfil", null, "Distinto", null, "María José López Muñoz")]
    [InlineData(false, "Nombre sin verificar", "Apellido sin verificar", " Gabriel ", " Andrés ", " Pineda ", " Rojas ", "Gabriel Andrés Pineda Rojas")]
    [InlineData(true, null, "No mezclar", "Ana", "María", "Torres", "Díaz", "Ana María Torres Díaz")]
    [InlineData(true, "   ", "No mezclar", "Ana", null, "Torres", null, "Ana Torres")]
    [InlineData(true, " Ana ", null, "Perfil", null, "Distinto", null, "Ana")]
    [InlineData(false, null, null, null, "María", "López", null, "María López")]
    [InlineData(false, null, null, null, null, "Solo apellido", null, null)]
    [InlineData(true, null, null, " \t ", null, null, null, null)]
    public void Nombre_personal_prefiere_identidad_verificada_y_normaliza_espacios(
        bool verificada, string? nombreVerificado, string? apellidoVerificado,
        string? primerNombre, string? segundoNombre, string? primerApellido, string? segundoApellido, string? esperado)
        => Assert.Equal(esperado, ReportesService.FormatearNombreContraparte(verificada,
            nombreVerificado, apellidoVerificado, primerNombre, segundoNombre, primerApellido, segundoApellido));

    [Theory]
    [InlineData("TRANSFERENCIA_ENTRADA", "wallets", 2L, 2L)]
    [InlineData("TRANSFERENCIA_SALIDA", "wallets", 3L, 3L)]
    [InlineData("RECARGA_WALLET", "wallets", 2L, null)]
    [InlineData("PAGO_QR", "wallets", 2L, null)]
    [InlineData("TRANSFERENCIA_ENTRADA", "ventas_qr", 2L, null)]
    [InlineData("TRANSFERENCIA_ENTRADA", null, 2L, null)]
    [InlineData("TRANSFERENCIA_ENTRADA", "wallets", null, null)]
    [InlineData("TRANSFERENCIA_ENTRADA", "wallets", 0L, null)]
    [InlineData("TRANSFERENCIA_ENTRADA", "wallets", -1L, null)]
    public void Solo_referencias_de_transferencias_wallet_validas_permiten_resolver_nombres(
        string tipo, string? referenciaTipo, long? referenciaId, long? esperado)
        => Assert.Equal(esperado, ReportesService.ReferenciaContraparte(tipo, referenciaTipo, referenciaId));
}
