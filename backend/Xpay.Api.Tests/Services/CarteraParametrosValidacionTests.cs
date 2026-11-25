using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Common;
using Xpay.Api.Controllers;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

public sealed class CarteraParametrosValidacionTests
{
    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);
    private static UpsertParametroUtilizacionRequest Valida() =>
        new(2m, 0m, 0m, false, 0m, 1, 12, "MENSUAL", 0m, 500000m);

    [Theory]
    [InlineData("tasaEmv")]
    [InlineData("porcAval")]
    [InlineData("porcAdmin")]
    [InlineData("aplicaIva")]
    [InlineData("porcIva")]
    [InlineData("plazoMin")]
    [InlineData("plazoMax")]
    [InlineData("frecuencia")]
    [InlineData("montoMin")]
    [InlineData("montoMax")]
    public void Json_exige_todos_los_campos_sin_convertir_omisiones_en_cero(string campo)
    {
        var objeto = JsonSerializer.SerializeToNode(Valida(), JsonWeb)!.AsObject();
        Assert.True(objeto.Remove(campo));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<UpsertParametroUtilizacionRequest>(objeto.ToJsonString(), JsonWeb));
    }

    [Theory]
    [InlineData("tasaEmv", "null")]
    [InlineData("tasaEmv", "\"sin valor\"")]
    [InlineData("aplicaIva", "null")]
    [InlineData("aplicaIva", "\"false\"")]
    [InlineData("plazoMin", "1.5")]
    [InlineData("frecuencia", "true")]
    public void Json_rechaza_tipos_incompatibles(string campo, string json)
    {
        var objeto = JsonSerializer.SerializeToNode(Valida(), JsonWeb)!.AsObject();
        objeto[campo] = JsonNode.Parse(json);
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<UpsertParametroUtilizacionRequest>(objeto.ToJsonString(), JsonWeb));
    }

    [Fact]
    public void Cero_en_cargos_e_IVA_es_una_decision_explicita_valida()
    {
        var result = CarteraParametrosValidacion.Validar(
            JsonSerializer.Deserialize<UpsertParametroUtilizacionRequest>(JsonSerializer.Serialize(Valida(), JsonWeb), JsonWeb)!);
        Assert.Equal(0m, result.PorcAval);
        Assert.Equal(0m, result.PorcAdmin);
        Assert.Equal(0m, result.PorcIva);
        Assert.False(result.AplicaIva);
        Assert.Equal(0m, result.MontoMin);
    }

    [Theory]
    [InlineData("tasa_cero")]
    [InlineData("tasa_negativa")]
    [InlineData("tasa_precision")]
    [InlineData("tasa_sql")]
    [InlineData("aval_negativo")]
    [InlineData("admin_precision")]
    [InlineData("iva_sql")]
    [InlineData("plazo_cero")]
    [InlineData("plazos_invertidos")]
    [InlineData("plazo_overflow")]
    [InlineData("monto_min_negativo")]
    [InlineData("monto_max_cero")]
    [InlineData("monto_precision")]
    [InlineData("monto_sql")]
    [InlineData("montos_invertidos")]
    [InlineData("frecuencia_ajena")]
    [InlineData("frecuencia_vacia")]
    [InlineData("frecuencia_nula")]
    public void Rechaza_rangos_precision_signos_y_opciones_invalidas(string caso)
    {
        var req = caso switch
        {
            "tasa_cero" => Valida() with { TasaEmv = 0m },
            "tasa_negativa" => Valida() with { TasaEmv = -1m },
            "tasa_precision" => Valida() with { TasaEmv = 0.12345m },
            "tasa_sql" => Valida() with { TasaEmv = 100000m },
            "aval_negativo" => Valida() with { PorcAval = -0.0001m },
            "admin_precision" => Valida() with { PorcAdmin = 0.12345m },
            "iva_sql" => Valida() with { PorcIva = 100000m },
            "plazo_cero" => Valida() with { PlazoMin = 0 },
            "plazos_invertidos" => Valida() with { PlazoMin = 13, PlazoMax = 12 },
            "plazo_overflow" => Valida() with { PlazoMax = int.MaxValue },
            "monto_min_negativo" => Valida() with { MontoMin = -0.01m },
            "monto_max_cero" => Valida() with { MontoMax = 0m },
            "monto_precision" => Valida() with { MontoMin = 0.001m },
            "monto_sql" => Valida() with { MontoMax = 10000000000000000m },
            "montos_invertidos" => Valida() with { MontoMin = 500001m },
            "frecuencia_ajena" => Valida() with { Frecuencia = "ANUAL" },
            "frecuencia_vacia" => Valida() with { Frecuencia = " " },
            "frecuencia_nula" => Valida() with { Frecuencia = null! },
            _ => throw new ArgumentException("Caso de prueba desconocido."),
        };
        Assert.Throws<ArgumentException>(() => CarteraParametrosValidacion.Validar(req));
    }

    [Fact]
    public void Normaliza_frecuencia_y_respeta_limites_SQL_sin_crear_topes_comerciales()
    {
        var req = Valida() with
        {
            TasaEmv = 0.0001m, PorcAval = 99999.9999m, PorcIva = 19.0000m,
            MontoMax = 9999999999999999.99m, PlazoMax = 48, Frecuencia = " quincenal ",
        };
        var result = CarteraParametrosValidacion.Validar(req);
        Assert.Equal("QUINCENAL", result.Frecuencia);
        Assert.Equal(req.PorcAval, result.PorcAval);
        Assert.Equal(req.MontoMax, result.MontoMax);
        Assert.Equal(48, result.PlazoMax);
        Assert.Equal("COMPRA_COMERCIO", CarteraParametrosValidacion.Tipo(" compra_comercio "));
        Assert.Equal("AVANCE_WALLET", CarteraParametrosValidacion.Tipo("avance_wallet"));
    }

    [Fact]
    public async Task Crear_y_editar_validan_antes_de_intentar_acceder_a_DB()
    {
        await using var db = new XpayDbContext(new DbContextOptionsBuilder<XpayDbContext>().Options);
        var svc = new CarteraOrdinariaService(db, null!, NullLogger<CarteraOrdinariaService>.Instance);
        await Assert.ThrowsAsync<ArgumentException>(() => svc.CrearParametroAsync("AVANCE_WALLET", Valida() with { TasaEmv = 0m }, 9));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.UpsertParametroAsync("AVANCE_WALLET", Valida() with { MontoMax = -1m }, 9));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.CrearParametroAsync("OTRO", Valida(), 9));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.UpsertParametroAsync("OTRO", Valida(), 9));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("USUARIO_FINAL", false)]
    [InlineData("OPERADOR_XPAY", false)]
    [InlineData("ADMIN_EMPRESA", false)]
    [InlineData("ADMIN_XPAY", true)]
    [InlineData("SUPERUSUARIO", true)]
    public async Task Politica_de_autorizacion_de_POST_y_PUT_conserva_roles_administrativos(string? rol, bool permitido)
    {
        var servicios = new ServiceCollection().AddLogging().AddAuthorization().BuildServiceProvider();
        await using var disposable = servicios;
        var proveedor = servicios.GetRequiredService<IAuthorizationPolicyProvider>();
        var autorizacion = servicios.GetRequiredService<IAuthorizationService>();
        foreach (var nombre in new[] { nameof(CarteraOrdinariaController.CrearParametro), nameof(CarteraOrdinariaController.UpsertParametro) })
        {
            var metodo = typeof(CarteraOrdinariaController).GetMethod(nombre)!;
            var atributos = typeof(CarteraOrdinariaController).GetCustomAttributes<AuthorizeAttribute>()
                .Concat(metodo.GetCustomAttributes<AuthorizeAttribute>()).ToArray();
            var politica = await AuthorizationPolicy.CombineAsync(proveedor, atributos);
            Assert.NotNull(politica);
            Assert.Empty(metodo.GetCustomAttributes<AllowAnonymousAttribute>());
            var identidad = rol is null ? new ClaimsIdentity() : new ClaimsIdentity([new Claim(ClaimTypes.Role, rol)], "test");
            Assert.Equal(permitido, (await autorizacion.AuthorizeAsync(new ClaimsPrincipal(identidad), null, politica!)).Succeeded);
        }
    }

    [Fact]
    public async Task HTTP_400_seguro_en_validacion_y_error_tecnico_propagado_al_middleware()
    {
        await using var db = new XpayDbContext(new DbContextOptionsBuilder<XpayDbContext>().Options);
        var svc = new CarteraOrdinariaService(db, null!, NullLogger<CarteraOrdinariaService>.Instance);
        var c = new CarteraOrdinariaController(svc, db, null!, null!, null!, null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("idUsuario", "9")], "test")),
            } },
        };
        var invalida = Valida() with { TasaEmv = 0m };
        foreach (var resultado in new[]
        {
            Assert.IsType<BadRequestObjectResult>(await c.CrearParametro("AVANCE_WALLET", invalida)),
            Assert.IsType<BadRequestObjectResult>(await c.UpsertParametro("AVANCE_WALLET", invalida)),
        })
        {
            var body = JsonSerializer.SerializeToNode(resultado.Value)!.AsObject();
            Assert.Equal(body["error"]!.GetValue<string>(), body["message"]!.GetValue<string>());
        }
        // No hay proveedor SQL: la excepción técnica no debe filtrarse en un
        // BadRequest con ex.Message. La tratará el middleware genérico.
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.CrearParametro("AVANCE_WALLET", Valida()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.UpsertParametro("AVANCE_WALLET", Valida()));
    }
}
