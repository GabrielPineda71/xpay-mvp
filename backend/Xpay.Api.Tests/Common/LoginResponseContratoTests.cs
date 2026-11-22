using System.Text.Json;
using Xpay.Api.DTOs;
using Xunit;

namespace Xpay.Api.Tests.Common;

public sealed class LoginResponseContratoTests
{
    [Fact]
    public void Sesion_habilitada_envia_false_explicito_para_que_el_cliente_la_pueda_validar()
    {
        var respuesta = new LoginResponse { IdUsuario = 1, IdPersona = 1, Usuario = "sintetico",
            Estado = "ACTIVO", Roles = ["USUARIO_FINAL"], Token = "token-sintetico", RequierePerfil = false };
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { success = true, data = respuesta },
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(JsonValueKind.False, json.RootElement.GetProperty("data").GetProperty("requierePerfil").ValueKind);
    }
}
