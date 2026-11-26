using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Integrations.MiDecisor;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xpay.Api.Tests.Common;
using Xunit;

namespace Xpay.Api.Tests.Services;

// Actual SQL stores/resolver and typed client; only Veriff HTTP and the token
// wait are synthetic. Never connects to Veriff or the bureau. CI must provide
// its disposable integration database; this file is not run against live SQL.
[Collection("SqlIntegration")]
public sealed class CarteraConsultaRiesgoRevalidacionSqlTests
{
    [Theory]
    [InlineData("CUENTA_INACTIVA")]
    [InlineData("KYC_REVOCADO")]
    [InlineData("CONSENTIMIENTO_V1_RETIRADO")]
    [InlineData("BIOMETRIA_RETIRADA")]
    [InlineData("MEMBRESIA_RETIRADA")]
    public async Task CambioMientrasEsperaToken_FinalizaPreCallSinPostCrediticio(string cambio)
    {
        if (!Conexion(out var cs)) return;
        await using var datos = new PreparacionWalletSqlTests.Semillero(cs);
        var config = PreparacionWalletSqlTests.Configuracion();
        config[MiDecisorProduccionConfig.EnvProductionEnabled] = "true";
        config[ControlOperacionesFinancieras.ClaveInterruptor] = "true";
        config[MiDecisorOptions.EnvBaseUrl] = "https://midecisor.test";
        config["VERIFF_BASE_URL"] = "https://api-saas.veriff.com";
        config["VERIFF_API_KEY"] = "synthetic-key";
        config["VERIFF_SHARED_SECRET"] = "synthetic-secret";
        var cuenta = await datos.ListaAsync(config);
        long solicitud = 0;
        WaitingToken? token = null;
        try
        {
            string decision;
            await using (var preparar = Contexto(cs))
            {
                await PreparacionWalletSqlTests.Servicio(preparar, config).ActivarAsync(cuenta.Usuario, new(), null);
                var p = await preparar.Personas.SingleAsync(p => p.IdPersona == cuenta.Persona);
                var k = await preparar.KycVerificaciones.SingleAsync(k => k.IdUsuario == cuenta.Usuario && k.EsActual);
                var numero = "8" + Random.Shared.NextInt64(100_000_000, 999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var fecha = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
                p.TipoDocumento = "CC"; p.NumeroDocumento = numero; p.NumeroDocumentoVerificado = numero;
                p.TipoDocumentoVeriffRaw = "ID_CARD"; p.PrimerApellido = "PRUEBA"; p.SegundoApellido = "SINTETICA";
                p.NombreVerificadoCompleto = "ANA"; p.ApellidoVerificadoCompleto = "PRUEBA SINTETICA";
                p.FechaNacimiento = new DateTime(1990, 1, 1);
                k.AttemptIdVeriff = "attempt-" + Guid.NewGuid().ToString("N"); k.DecisionTimeVeriff = fecha;
                k.VendorData = "synthetic-" + cuenta.Usuario;
                await preparar.SaveChangesAsync();
                decision = JsonSerializer.Serialize(new { status = "success", verification = new
                {
                    id = k.SessionId, attemptId = k.AttemptIdVeriff, status = "approved", decisionTime = "2026-11-01T00:00:00Z", vendorData = k.VendorData,
                    person = new { firstName = "ANA", lastName = "PRUEBA SINTETICA", dateOfBirth = "1990-01-01" },
                    document = new { type = "ID_CARD", number = numero, country = "CO" },
                } });
                Assert.True((await Piloto(preparar, config).AsegurarHabilitacionAsync(cuenta.Usuario)).Habilitado);
                solicitud = (await new CarteraOrdinariaService(preparar, null!, NullLogger<CarteraOrdinariaService>.Instance)
                    .CrearSolicitudCupoAsync(cuenta.Usuario, Guid.NewGuid(), "synthetic-revalidation")).IdSolicitud;
                Assert.Equal(ResultadoRegistroAutorizacion.Registrada, await new CarteraAutorizacionConsultaRiesgoStore(preparar)
                    .RegistrarAceptacionAsync(solicitud, cuenta.Usuario, CarteraAutorizacionConsultaRiesgoTextos.V1_Version, "synthetic-revalidation"));
            }

            var handler = new Provider(decision);
            var factory = new Factory(handler);
            await using var db = Contexto(cs);
            var resolver = new MiDecisorIdentidadResolver(db, factory, config, NullLogger<MiDecisorIdentidadResolver>.Instance);
            var riesgo = new RiesgoProduccionStore(db, resolver, new MiDecisorConsultaExternaStore(db, TimeProvider.System));
            var durable = new AutorizacionConsultaRiesgoDurable(db, NullLogger<AutorizacionConsultaRiesgoDurable>.Instance);
            var store = new CarteraConsultaRiesgoStore(db, resolver, riesgo, durable, Piloto(db, config),
                new ControlOperacionesFinancieras(config, AmbienteXpayTests.Ambiente("Production")));
            token = new WaitingToken();
            var client = new MiDecisorClient(factory, token, config, NullLogger<MiDecisorClient>.Instance);
            var gate = new AutorizacionConsultaRiesgoProduccion(durable, riesgo, config, TimeProvider.System,
                NullLogger<AutorizacionConsultaRiesgoProduccion>.Instance);
            var service = new CarteraConsultaRiesgoService(store, client, gate, TimeProvider.System, NullLogger<CarteraConsultaRiesgoService>.Instance);
            var execution = service.EjecutarConsultaRiesgoAsync(solicitud, cuenta.Usuario, "synthetic-revalidation");
            await token.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));

            await using (var revocar = Contexto(cs))
            {
                if (cambio == "CUENTA_INACTIVA")
                    await revocar.Usuarios.Where(u => u.IdUsuario == cuenta.Usuario).ExecuteUpdateAsync(s => s.SetProperty(u => u.Estado, "INACTIVO"));
                if (cambio == "KYC_REVOCADO")
                    await revocar.KycVerificaciones.Where(k => k.IdUsuario == cuenta.Usuario).ExecuteUpdateAsync(s => s.SetProperty(k => k.EsActual, false));
                if (cambio == "CONSENTIMIENTO_V1_RETIRADO")
                    await revocar.CarteraAutorizacionesConsultaRiesgo.Where(a => a.IdSolicitudOrigen == solicitud).ExecuteDeleteAsync();
                if (cambio == "BIOMETRIA_RETIRADA")
                    await revocar.Auditorias.Where(a => a.IdUsuario == cuenta.Usuario && a.Accion == ConsentimientoBiometricoService.AccionAutorizar)
                        .ExecuteUpdateAsync(s => s.SetProperty(a => a.Resultado, "REVOCADO"));
                if (cambio == "MEMBRESIA_RETIRADA")
                    await revocar.Database.ExecuteSqlInterpolatedAsync($"DELETE dbo.cartera_piloto_habilitaciones WHERE id_usuario={cuenta.Usuario}");
            }
            token.Release.TrySetResult();
            var result = await execution.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(CarteraConsultaRiesgoResultados.ErrorValidacionLocal, result.ResultadoTecnico);
            Assert.Equal(1, token.Calls); Assert.Equal(0, handler.CreditPosts); Assert.Equal(1, handler.VeriffGets);
            await using var verificar = Contexto(cs);
            var intento = await verificar.CarteraSolicitudCupoIntentos.AsNoTracking().SingleAsync(i => i.IdSolicitud == solicitud);
            Assert.Equal(CarteraIntentoFases.Finalizado, intento.FaseIntento);
            Assert.Equal(CarteraConsultaRiesgoResultados.ErrorValidacionLocal, intento.ResultadoTecnico);
            Assert.False(intento.EsIntentoConResultadoUtil); Assert.Null(intento.HttpStatusObservado);
            Assert.Null(intento.ScoreRaw); Assert.Null(intento.MontoSugeridoRaw); Assert.Null(intento.P0ProviderRawJson);
            Assert.Equal(CarteraSolicitudCupoEstados.ErrorProveedor, (await verificar.CarteraSolicitudesCupo.AsNoTracking().SingleAsync(s => s.IdSolicitud == solicitud)).EstadoSolicitud);
            Assert.False(await verificar.CarteraCuposOrdinarios.AnyAsync(c => c.IdUsuario == cuenta.Usuario));

            if (cambio is "CUENTA_INACTIVA" or "KYC_REVOCADO")
            {
                await verificar.Usuarios.Where(u => u.IdUsuario == cuenta.Usuario).ExecuteUpdateAsync(s => s.SetProperty(u => u.Estado, "ACTIVO"));
                await verificar.KycVerificaciones.Where(k => k.IdUsuario == cuenta.Usuario).ExecuteUpdateAsync(s => s.SetProperty(k => k.EsActual, true));
                Assert.NotNull(await resolver.ResolverAsync(cuenta.Usuario));
                Assert.Equal(2, handler.VeriffGets); // Changed evidence invalidated the original scoped cache.
                Assert.Equal(0, handler.CreditPosts);
            }
        }
        finally
        {
            token?.Release.TrySetResult();
            await using var limpiar = Contexto(cs);
            await limpiar.CarteraAutorizacionesConsultaRiesgo.Where(a => a.IdUsuario == cuenta.Usuario).ExecuteDeleteAsync();
            await limpiar.CarteraSolicitudCupoIntentos.Where(i => i.IdSolicitud == solicitud).ExecuteDeleteAsync();
            await limpiar.CarteraSolicitudesCupo.Where(s => s.IdSolicitud == solicitud).ExecuteDeleteAsync();
            await limpiar.Database.ExecuteSqlInterpolatedAsync($"DELETE dbo.cartera_piloto_habilitaciones WHERE id_usuario={cuenta.Usuario}");
        }
    }

    private static CarteraPilotoService Piloto(XpayDbContext db, Microsoft.Extensions.Configuration.IConfiguration config) => new(db, config,
        new ConsentimientoBiometricoService(db, config, AmbienteXpayTests.Ambiente("Production"), TimeProvider.System), new PerfilService(db), TimeProvider.System);
    private sealed class WaitingToken : IMiDecisorTokenProvider
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls;
        public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
        { Calls++; Entered.TrySetResult(); await Release.Task.WaitAsync(ct); return "synthetic-token"; }
    }
    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, disposeHandler: false); }
    private sealed class Provider(string decision) : HttpMessageHandler
    {
        internal int VeriffGets, CreditPosts;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.Host == "api-saas.veriff.com")
            {
                Interlocked.Increment(ref VeriffGets);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(decision, Encoding.UTF8, "application/json") });
            }
            Interlocked.Increment(ref CreditPosts);
            throw new InvalidOperationException("No credit request is allowed after revocation.");
        }
    }
    private static XpayDbContext Contexto(string cs) => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);
    private static bool Conexion(out string cs)
    {
        cs = Environment.GetEnvironmentVariable("ConnectionStrings__XpayConnection") ?? "";
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        Assert.False(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")),
            "CI exige SQL sintético para revalidar la autorización antes del envío de MiDecisor.");
        return false;
    }
}
