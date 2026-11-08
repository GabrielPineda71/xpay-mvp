using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xpay.Api.Authorization;
using Xpay.Api.Data;
using Xunit;

namespace Xpay.Api.Tests.Common;

// P0-B — ValidadorVersionSeguridad sin BD. El RequestServices de los contextos
// está vacío: si el validador intentara resolver XpayDbContext lanzaría, así
// que un "Token no vigente." prueba que el rechazo ocurrió antes de la BD.
public class ValidadorVersionSeguridadTests
{
    private const string Mensaje = ValidadorVersionSeguridad.MensajeFallo;

    private static ClaimsPrincipal Principal(params (string Tipo, string Valor)[] claims)
        => new(new ClaimsIdentity(claims.Select(c => new Claim(c.Tipo, c.Valor)), "Bearer"));

    private static TokenValidatedContext Contexto(ClaimsPrincipal principal, IServiceProvider? servicios = null)
    {
        var http    = new DefaultHttpContext { RequestServices = servicios ?? new ServiceCollection().BuildServiceProvider() };
        var esquema = new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));
        return new TokenValidatedContext(http, esquema, new JwtBearerOptions()) { Principal = principal };
    }

    // ── TryLeerClaims ───────────────────────────────────────────────────

    [Theory]
    [InlineData("42", "0", 42L, 0)]
    [InlineData("1", "2147483647", 1L, int.MaxValue)]
    [InlineData("9223372036854775807", "7", long.MaxValue, 7)]
    public void TryLeerClaims_Valido(string id, string vs, long idEsperado, int vsEsperado)
    {
        Assert.True(ValidadorVersionSeguridad.TryLeerClaims(Principal(("idUsuario", id), ("vs", vs)), out var i, out var v));
        Assert.Equal(idEsperado, i);
        Assert.Equal(vsEsperado, v);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("1.0")]
    [InlineData("01")]
    [InlineData("9223372036854775808")]
    [InlineData("１")]
    public void TryLeerClaims_IdUsuarioInvalido(string id)
        => Assert.False(ValidadorVersionSeguridad.TryLeerClaims(Principal(("idUsuario", id), ("vs", "0")), out _, out _));

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("1.0")]
    [InlineData("01")]
    [InlineData("2147483648")]
    [InlineData("١")]
    public void TryLeerClaims_VsInvalido(string vs)
        => Assert.False(ValidadorVersionSeguridad.TryLeerClaims(Principal(("idUsuario", "42"), ("vs", vs)), out _, out _));

    [Fact]
    public void TryLeerClaims_IdUsuarioAusente()
        => Assert.False(ValidadorVersionSeguridad.TryLeerClaims(Principal(("vs", "0")), out _, out _));

    [Fact]
    public void TryLeerClaims_VsAusente()
        => Assert.False(ValidadorVersionSeguridad.TryLeerClaims(Principal(("idUsuario", "42")), out _, out _));

    [Fact]
    public void TryLeerClaims_IdUsuarioDuplicado()
        => Assert.False(ValidadorVersionSeguridad.TryLeerClaims(
            Principal(("idUsuario", "42"), ("idUsuario", "42"), ("vs", "0")), out _, out _));

    [Fact]
    public void TryLeerClaims_VsDuplicado()
        => Assert.False(ValidadorVersionSeguridad.TryLeerClaims(
            Principal(("idUsuario", "42"), ("vs", "0"), ("vs", "0")), out _, out _));

    [Fact]
    public void TryLeerClaims_PrincipalNulo()
        => Assert.False(ValidadorVersionSeguridad.TryLeerClaims(null, out _, out _));

    // ── ValidarAsync sin BD ─────────────────────────────────────────────

    [Fact]
    public async Task Validar_SinVs_FallaSinTocarBd()
    {
        var ctx = Contexto(Principal(("idUsuario", "42")));
        await ValidadorVersionSeguridad.ValidarAsync(ctx);
        Assert.Equal(Mensaje, ctx.Result?.Failure?.Message);
    }

    [Fact]
    public async Task Validar_VsNegativo_FallaSinTocarBd()
    {
        var ctx = Contexto(Principal(("idUsuario", "42"), ("vs", "-1")));
        await ValidadorVersionSeguridad.ValidarAsync(ctx);
        Assert.Equal(Mensaje, ctx.Result?.Failure?.Message);
    }

    [Fact]
    public async Task Validar_IdUsuarioInvalido_FallaSinTocarBd()
    {
        var ctx = Contexto(Principal(("idUsuario", "abc"), ("vs", "0")));
        await ValidadorVersionSeguridad.ValidarAsync(ctx);
        Assert.Equal(Mensaje, ctx.Result?.Failure?.Message);
    }

    // Un error de infraestructura no se disfraza de token inválido: se propaga
    // y el contexto queda sin resultado (nunca Success).
    [Fact]
    public async Task Validar_ErrorDeBd_SePropaga_NoSeConvierteEnFallo()
    {
        var servicios = new ServiceCollection()
            .AddDbContext<XpayDbContext>(o => o.UseSqlServer("Server=127.0.0.1,1;Database=NoExiste;Connect Timeout=1;"))
            .BuildServiceProvider();
        var ctx = Contexto(Principal(("idUsuario", "42"), ("vs", "0")), servicios.CreateScope().ServiceProvider);

        await Assert.ThrowsAnyAsync<Exception>(() => ValidadorVersionSeguridad.ValidarAsync(ctx));
        Assert.Null(ctx.Result);
    }

    // Token histórico (sin vs) firmado y validado como en el pipeline (JsonWebTokenHandler
    // con MapInboundClaims): la firma es válida pero el validador lo rechaza.
    [Fact]
    public async Task TokenHistoricoSinVs_ConFirmaValida_SeRechaza()
    {
        var ctx = Contexto(await PrincipalDeTokenAsync(new Dictionary<string, object> { ["idUsuario"] = "42", ["usuario"] = "historico" }));
        await ValidadorVersionSeguridad.ValidarAsync(ctx);
        Assert.Equal(Mensaje, ctx.Result?.Failure?.Message);
    }

    // Control: idUsuario y vs sobreviven al mapeo de entrada del pipeline.
    [Fact]
    public async Task TokenConVs_ConservaClaimsTrasMapeoDeEntrada()
    {
        var principal = await PrincipalDeTokenAsync(new Dictionary<string, object> { ["idUsuario"] = "42", ["vs"] = "3" });
        Assert.True(ValidadorVersionSeguridad.TryLeerClaims(principal, out var id, out var vs));
        Assert.Equal(42L, id);
        Assert.Equal(3, vs);
    }

    // ── Registrar ───────────────────────────────────────────────────────

    [Fact]
    public async Task Registrar_SinEventosPrevios_AplicaElValidador()
    {
        var opts = new JwtBearerOptions();
        ValidadorVersionSeguridad.Registrar(opts);
        var ctx = Contexto(Principal(("idUsuario", "42")));
        await opts.Events.OnTokenValidated(ctx);
        Assert.Equal(Mensaje, ctx.Result?.Failure?.Message);
    }

    [Fact]
    public async Task Registrar_PrevioSinResultado_SeEncadenaYLuegoValida()
    {
        var llamadas = 0;
        var opts = new JwtBearerOptions
        {
            Events = new JwtBearerEvents { OnTokenValidated = _ => { llamadas++; return Task.CompletedTask; } },
        };
        ValidadorVersionSeguridad.Registrar(opts);
        var ctx = Contexto(Principal(("idUsuario", "42")));
        await opts.Events.OnTokenValidated(ctx);
        Assert.Equal(1, llamadas);
        Assert.Equal(Mensaje, ctx.Result?.Failure?.Message);
    }

    [Fact]
    public async Task Registrar_PrevioConResultado_NoSePisa()
    {
        var llamadas = 0;
        var opts = new JwtBearerOptions
        {
            Events = new JwtBearerEvents { OnTokenValidated = c => { llamadas++; c.Fail("previo"); return Task.CompletedTask; } },
        };
        ValidadorVersionSeguridad.Registrar(opts);
        // Claims válidos: si el validador corriera, intentaría resolver la BD y lanzaría.
        var ctx = Contexto(Principal(("idUsuario", "42"), ("vs", "0")));
        await opts.Events.OnTokenValidated(ctx);
        Assert.Equal(1, llamadas);
        Assert.Equal("previo", ctx.Result?.Failure?.Message);
    }

    private static async Task<ClaimsPrincipal> PrincipalDeTokenAsync(Dictionary<string, object> claims)
    {
        var clave   = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(48));
        var handler = new JsonWebTokenHandler { MapInboundClaims = true };
        var token   = handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "xpay-test-issuer", Audience = "xpay-test-audience",
            Expires = DateTime.UtcNow.AddHours(1), Claims = claims,
            SigningCredentials = new SigningCredentials(clave, SecurityAlgorithms.HmacSha256),
        });
        var resultado = await handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true, IssuerSigningKey = clave,
            ValidateIssuer = true, ValidIssuer = "xpay-test-issuer",
            ValidateAudience = true, ValidAudience = "xpay-test-audience",
            ValidateLifetime = true,
        });
        Assert.True(resultado.IsValid);
        return new ClaimsPrincipal(resultado.ClaimsIdentity);
    }
}
