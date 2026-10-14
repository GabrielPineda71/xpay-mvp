using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Common;
using Xpay.Api.Controllers;
using Xpay.Api.Data;
using Xpay.Api.Integrations.MiDecisor;
using Xpay.Api.Models;
using Xunit;

namespace Xpay.Api.Tests.Services;

// XPAY-702 - GET mi-solicitud-actual: recuperacion del estado tras refresh.
// Sin score ni motivos; monto solo si APROBADA.
[Collection("SqlIntegration")]
public sealed class CarteraSolicitudClienteControllerTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";

    [Fact]
    public void Endpoint_ExigeKycAprobado()
    {
        var attr = Assert.Single(typeof(CarteraSolicitudClienteController)
            .GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal("KycAprobado", attr.Policy);
    }

    [Fact]
    public void Respuesta_NoExponeDatosDeRiesgo()
    {
        var props = typeof(MiSolicitudActualResponse).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain(props, n => n.Contains("Score") || n.Contains("Motivo")
                                       || n.Contains("Viabilidad") || n.Contains("Rating"));
    }

    private static CarteraSolicitudClienteController Controller(XpayDbContext c, long idUsuario)
    {
        var ctl = new CarteraSolicitudClienteController(c,
            new AutorizacionConsultaRiesgoDurable(c, NullLogger<AutorizacionConsultaRiesgoDurable>.Instance));
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("idUsuario", idUsuario.ToString()) }));
        ctl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };
        return ctl;
    }

    [Fact]
    public async Task SinSolicitud_204_LuegoUltima_MontoSoloSiAprobada()
    {
        if (!TryConnString(out var cs)) return;
        var doc = $"79{(uint)Guid.NewGuid().GetHashCode() % 100_000_000:D8}";
        try
        {
            await using var c = NuevoContexto(cs);
            var ahora = DateTime.UtcNow;
            var idUnidad = await c.Database.SqlQueryRaw<long>(
                "SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL").SingleAsync();
            var persona = new Persona
            {
                IdUnidadNegocio = idUnidad, TipoDocumento = "CC", NumeroDocumento = doc,
                PrimerNombre = "MsaTest", PrimerApellido = "Sintetico", Celular = "3000000000",
                Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
            };
            c.Personas.Add(persona);
            await c.SaveChangesAsync();
            var usuario = new Usuario
            {
                IdPersona = persona.IdPersona, NombreUsuario = $"msa_{Guid.NewGuid().ToString("N")[..12]}",
                PasswordHash = "x", Estado = "ACTIVO", FechaCreacion = ahora,
            };
            c.Usuarios.Add(usuario);
            await c.SaveChangesAsync();

            Assert.Null(Assert.IsType<MiSolicitudActualEnvelope>(Assert.IsType<OkObjectResult>(await Controller(c, usuario.IdUsuario).MiSolicitudActual()).Value).Solicitud);

            var idPolitica = await c.CarteraPoliticasCredito.AsNoTracking()
                .Where(p => p.Estado == "ACTIVO").OrderBy(p => p.IdPolitica).Select(p => p.IdPolitica).FirstAsync();
            CarteraSolicitudCupo Sol(string estado, decimal? monto, DateTime fecha) => new()
            {
                IdUsuario = usuario.IdUsuario, IdPersona = persona.IdPersona,
                EstadoSolicitud = estado, DecisionCrediticia = estado == CarteraSolicitudCupoEstados.Aprobada
                    ? CarteraDecisionCrediticia.Aprobada : CarteraDecisionCrediticia.Rechazada,
                MontoAprobado = monto, FechaDecision = fecha,
                IdPoliticaAplicada = idPolitica, CupoMinimoAplicado = 0m, CupoMaximoAplicado = 1_000_000m,
                EdadMinimaAplicada = 18, EdadMaximaAplicada = 99, NumeroIntento = 1,
                CorrelationId = $"msa-{Guid.NewGuid().ToString("N")[..12]}",
                FechaSolicitud = fecha, FechaActualizacion = fecha,
            };

            c.CarteraSolicitudesCupo.Add(Sol(CarteraSolicitudCupoEstados.Rechazada, 0m, ahora.AddMinutes(-10)));
            await c.SaveChangesAsync();
            var r1 = Assert.IsType<OkObjectResult>(await Controller(c, usuario.IdUsuario).MiSolicitudActual());
            var b1 = Assert.IsType<MiSolicitudActualEnvelope>(r1.Value).Solicitud!;
            Assert.Equal(CarteraSolicitudCupoEstados.Rechazada, b1.EstadoSolicitud);
            Assert.Null(b1.MontoAprobado);
            Assert.False(b1.ConsentimientoRegistrado);

            c.CarteraSolicitudesCupo.Add(Sol(CarteraSolicitudCupoEstados.Aprobada, 384_000m, ahora));
            await c.SaveChangesAsync();
            var r2 = Assert.IsType<OkObjectResult>(await Controller(c, usuario.IdUsuario).MiSolicitudActual());
            var b2 = Assert.IsType<MiSolicitudActualEnvelope>(r2.Value).Solicitud!;
            Assert.Equal(CarteraSolicitudCupoEstados.Aprobada, b2.EstadoSolicitud);
            Assert.Equal(384_000m, b2.MontoAprobado);
            Assert.False(b2.RequiereRevisionManual);
        }
        finally { await LimpiarAsync(cs, doc); }
    }

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL de XPAY-702.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    private static async Task LimpiarAsync(string cs, string doc)
    {
        await using var ctx = NuevoContexto(cs);
        var db = ctx.Database;
        await db.ExecuteSqlAsync($"DELETE s FROM dbo.cartera_solicitudes_cupo s JOIN dbo.personas p ON p.id_persona = s.id_persona WHERE p.numero_documento = {doc}");
        await db.ExecuteSqlAsync($"DELETE u FROM dbo.usuarios u JOIN dbo.personas p ON p.id_persona = u.id_persona WHERE p.numero_documento = {doc}");
        await db.ExecuteSqlAsync($"DELETE FROM dbo.personas WHERE numero_documento = {doc}");
    }
}
