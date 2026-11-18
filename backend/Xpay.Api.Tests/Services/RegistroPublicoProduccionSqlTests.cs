using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Services;
using Xpay.Api.Tests.Common;
using Xunit;

namespace Xpay.Api.Tests.Services;

[Collection("SqlIntegration")]
public sealed class RegistroPublicoProduccionSqlTests
{
    [Fact]
    public async Task Alta_real_production_guarda_aceptaciones_envia_tras_commit_y_activa_solo_el_perfil()
    {
        if (!TryConexion(out var cs)) return;
        var nombre = "prod_limited_" + Guid.NewGuid().ToString("N")[..12];
        var correo = nombre + "@example.test";
        try
        {
            await using var db = Contexto(cs);
            var movimientosAntes = await db.WalletMovimientos.CountAsync();
            var asientosAntes = await db.LedgerTransacciones.CountAsync();
            var saldosAntes = await db.WalletSaldos.CountAsync();
            var config = RegistroPublicoProduccionTests.Configuracion();
            config["Jwt:Key"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
            config["Jwt:Issuer"] = "registro-production-test";
            config["Jwt:Audience"] = "registro-production-test";
            var ambiente = AmbienteXpayTests.Ambiente("Production");
            var transporte = new EmailSenderAcsTests.Transporte(HttpStatusCode.Accepted);
            var observador = new ObservadorCommit(transporte, db, cs, nombre);
            var servicio = new RegistroPublicoService(db, ambiente,
                new TokenUsuarioService(db, config, TimeProvider.System), EmailSenderAcsTests.Sender(observador),
                new PlantillasEmailIdentidad(config, ambiente), TimeProvider.System,
                NullLogger<RegistroPublicoService>.Instance, config);
            var solicitud = RegistroPublicoProduccionTests.Solicitud(RegistroPublicoProduccionTests.Consentimiento)
                with { NombreUsuario = nombre, Email = correo };

            await servicio.RegistrarAsync(solicitud, "203.0.113.25");

            await using var consulta = Contexto(cs);
            var usuario = await consulta.Usuarios.AsNoTracking().SingleAsync(u => u.NombreUsuario == nombre);
            Assert.Equal(EstadosUsuario.PendienteVerificacion, usuario.Estado);
            Assert.False(usuario.EmailVerificado);
            Assert.False(await consulta.Wallets.AnyAsync(w => w.IdPersona == usuario.IdPersona));
            Assert.Equal(movimientosAntes, await consulta.WalletMovimientos.CountAsync());
            Assert.Equal(asientosAntes, await consulta.LedgerTransacciones.CountAsync());
            Assert.Equal(saldosAntes, await consulta.WalletSaldos.CountAsync());
            Assert.False(observador.TransaccionAbierta);
            Assert.True(observador.UsuarioConfirmado);
            Assert.Equal(1, transporte.Envios);
            var roles = await (from ur in consulta.UsuarioRoles join r in consulta.Roles on ur.IdRol equals r.IdRol
                where ur.IdUsuario == usuario.IdUsuario && ur.Estado == "ACTIVO" select r.Codigo).ToListAsync();
            Assert.Equal("USUARIO_FINAL", Assert.Single(roles));

            var evidencias = await consulta.Auditorias.AsNoTracking().Where(a => a.IdUsuario == usuario.IdUsuario
                && (a.Accion == RegistroPublicoService.AccionAceptarTerminos || a.Accion == RegistroPublicoService.AccionAutorizarDatos)).ToListAsync();
            Assert.Equal(2, evidencias.Count);
            Assert.All(evidencias, evidencia =>
            {
                Assert.Equal("EXITOSO", evidencia.Resultado);
                Assert.Equal("203.0.113.25", evidencia.Ip);
                Assert.Equal(usuario.IdPersona, evidencia.IdPersona);
                using var json = JsonDocument.Parse(evidencia.ValorNuevo!);
                Assert.Equal("1.2", json.RootElement.GetProperty("version").GetString());
                var termino = evidencia.Accion == RegistroPublicoService.AccionAceptarTerminos;
                Assert.Equal(new string(termino ? 'a' : 'b', 64), json.RootElement.GetProperty("sha256").GetString());
                Assert.Equal("WEB_AUTORREGISTRO", json.RootElement.GetProperty("canal").GetString());
            });

            var auth = new AuthService(consulta, config, ambiente);
            await Assert.ThrowsAsync<CredencialesInvalidasException>(() => auth.LoginAsync(new LoginRequest
                { Usuario = nombre, Password = solicitud.Password }));
            using var envio = JsonDocument.Parse(transporte.Cuerpo!);
            var texto = envio.RootElement.GetProperty("content").GetProperty("plainText").GetString()!;
            var coincidencia = Regex.Match(texto, "#token=([A-Za-z0-9_-]+)");
            Assert.True(coincidencia.Success);
            Assert.Contains("https://app.xpaycol.com/verificar-email#token=", texto);
            Assert.DoesNotContain(coincidencia.Groups[1].Value, transporte.Url!);
            var tokens = await consulta.UsuarioTokens.AsNoTracking().Where(t => t.IdUsuario == usuario.IdUsuario).ToListAsync();
            Assert.Single(tokens);
            Assert.Null(tokens[0].FechaUso);

            Assert.True(await servicio.VerificarEmailAsync(coincidencia.Groups[1].Value));
            Assert.False(await servicio.VerificarEmailAsync(coincidencia.Groups[1].Value));
            // Cada petición HTTP usa un scope nuevo. La consulta del intento
            // previo conserva en seguimiento el estado pendiente anterior.
            await using var consultaLogin = Contexto(cs);
            var login = await new AuthService(consultaLogin, config, ambiente)
                .LoginAsync(new LoginRequest { Usuario = nombre, Password = solicitud.Password });
            Assert.Equal(EstadosUsuario.Activo, login.Estado);
            Assert.True(login.RequierePerfil);
            Assert.False(await consulta.Wallets.AnyAsync(w => w.IdPersona == usuario.IdPersona));
            Assert.Equal(movimientosAntes, await consulta.WalletMovimientos.CountAsync());
            Assert.Equal(asientosAntes, await consulta.LedgerTransacciones.CountAsync());
            Assert.Equal(saldosAntes, await consulta.WalletSaldos.CountAsync());
            Assert.Equal(1, await consulta.Auditorias.CountAsync(a => a.IdUsuario == usuario.IdUsuario && a.Accion == "VERIFICAR_EMAIL"));
        }
        finally { await LimpiarAsync(cs, nombre, correo); }
    }

    private sealed class ObservadorCommit(HttpMessageHandler transporte, XpayDbContext registro, string cs, string nombre)
        : DelegatingHandler(transporte), IHttpClientFactory
    {
        public bool TransaccionAbierta { get; private set; }
        public bool UsuarioConfirmado { get; private set; }
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            TransaccionAbierta = registro.Database.CurrentTransaction is not null;
            await using var otraConexion = Contexto(cs);
            UsuarioConfirmado = await otraConexion.Usuarios.AnyAsync(u => u.NombreUsuario == nombre, ct);
            return await base.SendAsync(request, ct);
        }
    }

    private static bool TryConexion(out string cs)
    {
        cs = Environment.GetEnvironmentVariable("ConnectionStrings__XpayConnection") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        Assert.False(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")),
            "ConnectionStrings__XpayConnection es obligatoria en CI para el registro productivo.");
        return false;
    }

    private static XpayDbContext Contexto(string cs) => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    private static async Task LimpiarAsync(string cs, string nombre, string correo)
    {
        await using var db = Contexto(cs);
        var ids = await db.Usuarios.Where(u => u.NombreUsuario == nombre).Select(u => u.IdUsuario).ToArrayAsync();
        var personas = await db.Personas.Where(p => p.Email == correo).Select(p => p.IdPersona).ToArrayAsync();
        await db.UsuarioTokens.Where(t => ids.Contains(t.IdUsuario)).ExecuteDeleteAsync();
        await db.Auditorias.Where(a => a.IdUsuario != null && ids.Contains(a.IdUsuario.Value)).ExecuteDeleteAsync();
        await db.UsuarioRoles.Where(r => ids.Contains(r.IdUsuario)).ExecuteDeleteAsync();
        await db.Usuarios.Where(u => ids.Contains(u.IdUsuario)).ExecuteDeleteAsync();
        await db.Personas.Where(p => personas.Contains(p.IdPersona)).ExecuteDeleteAsync();
    }
}
