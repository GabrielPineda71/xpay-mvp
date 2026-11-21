using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Xpay.Api.Authorization;
using Xpay.Api.Common;
using Xpay.Api.Controllers;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Common;

// HTTP real en loopback, JWT emitido por AuthService y SQL Server sintético.
// Se usan las políticas de autorización y revocación del servidor. Ningún
// transporte externo se registra: si una acción bloqueada intenta activarse,
// el request no puede terminar como el 403 esperado de CuentaLimitadaFilter.
[Collection("SqlIntegration")]
public sealed class CuentaLimitadaSqlHttpTests
{
    private const string Clave = "Limitada#2026x";
    private const string EnvConnString = "ConnectionStrings__XpayConnection";

    [Fact]
    public async Task Http_cuenta_limitada_bloquea_operaciones_antes_del_body_sin_interrumpir_piloto()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Alta>();
        try
        {
            // También con evidencia KYC y contactos aprobados: ni esos estados
            // ni un JWT válido liberan una cuenta marcada como limitada.
            var limitada = await SembrarAsync(cs, creados, limitada: true);
            var piloto = await SembrarAsync(cs, creados, limitada: false);
            var config = Configuracion();
            var loginLimitada = await LoginAsync(cs, config, limitada);
            var loginPiloto = await LoginAsync(cs, config, piloto);
            Assert.True(loginLimitada.RequierePerfil);
            Assert.False(loginPiloto.RequierePerfil);

            var sonda = new SondaCuentaLimitada();
            await using var app = CrearHost(cs, config, sonda);
            await app.StartAsync();
            using var cliente = new HttpClient
            {
                BaseAddress = new Uri(Assert.Single(app.Services.GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()!.Addresses)),
            };
            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginLimitada.Token);

            foreach (var ruta in new[] { "/api/usuarios/mi-perfil", "/api/contactos/estado", "/api/kyc/mi-estado" })
            {
                using var respuesta = await cliente.GetAsync(ruta);
                Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            }
            using (var cambio = await cliente.PatchAsync("/api/usuarios/mi-perfil",
                JsonContent.Create(new { ciudad = "Bogotá" })))
                Assert.Equal(HttpStatusCode.OK, cambio.StatusCode);

            // El cuerpo es deliberadamente inválido: un rechazo por model
            // binding sería 400 y demostraría que el filtro llegó demasiado tarde.
            foreach (var ruta in new[]
            {
                "/api/wallets/transferencia", "/api/qr/pagar",
                "/api/breb/mi-llave", "/api/breb/mi-llave/resolver",
                "/api/breb/retiros/real", "/api/cartera-ordinaria/solicitar-cupo",
                "/api/wallets/mi-wallet/recarga-breb",
                "/api/prueba-cuenta-limitada/operacion",
            })
            {
                using var respuesta = await cliente.PostAsync(ruta, new StringContent("{invalido", Encoding.UTF8, "application/json"));
                Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
                var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(CuentaLimitadaFilter.CodigoCuentaLimitada, cuerpo.GetProperty("codigo").GetString());
            }
            // KYC es una excepción deliberada al filtro financiero: permite
            // verificar identidad conservando la cuenta limitada. Su servicio
            // exige la nueva autorización C y no puede abrirse por esa excepción.
            using (var kycSinConsentimiento = await cliente.PostAsync("/api/kyc/veriff/session", null))
            {
                Assert.Equal(HttpStatusCode.Forbidden, kycSinConsentimiento.StatusCode);
                var cuerpo = await kycSinConsentimiento.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(KycService.MensajeConsentimientoKycPendiente, cuerpo.GetProperty("message").GetString());
            }
            using (var noPermitida = await cliente.GetAsync("/api/prueba-cuenta-limitada/sin-excepcion"))
                Assert.Equal(HttpStatusCode.Forbidden, noPermitida.StatusCode);
            Assert.Equal(0, sonda.Ejecuciones);

            // Login/callbacks son anónimos, incluso si el navegador manda un
            // bearer limitado. Se conserva la validación propia de cada callback.
            using (var callback = await cliente.PostAsync("/api/prueba-cuenta-limitada/callback", null))
                Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
            Assert.Equal(1, sonda.Callbacks);

            // Las vías legacy nunca se abren en PROD, tampoco como alternativa
            // anónima a las verificaciones del registro nuevo.
            cliente.DefaultRequestHeaders.Authorization = null;
            foreach (var ruta in new[] { "/api/usuarios/registro-inicial", "/api/usuarios/registro-final" })
            {
                using var respuesta = await cliente.PostAsync(ruta, new StringContent("{invalido", Encoding.UTF8, "application/json"));
                Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
            }

            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginPiloto.Token);
            using (var operacion = await cliente.PostAsync("/api/prueba-cuenta-limitada/operacion", JsonContent.Create(new { valor = 1 })))
                Assert.Equal(HttpStatusCode.OK, operacion.StatusCode);
            Assert.Equal(1, sonda.Ejecuciones); // interruptor global sigue true.

            // El marcador se relee en vivo: un JWT emitido como piloto tampoco
            // evade la restricción si la cuenta queda limitada posteriormente.
            await using (var db = Contexto(cs))
            {
                db.Auditorias.Add(Marcador(piloto));
                await db.SaveChangesAsync();
            }
            using (var bloqueada = await cliente.PostAsync("/api/prueba-cuenta-limitada/operacion", JsonContent.Create(new { valor = 1 })))
                Assert.Equal(HttpStatusCode.Forbidden, bloqueada.StatusCode);
            Assert.Equal(1, sonda.Ejecuciones);

            await using var consulta = Contexto(cs);
            var idsPersona = creados.Select(a => a.IdPersona).ToArray();
            var idsUsuario = creados.Select(a => a.IdUsuario).ToArray();
            Assert.False(await consulta.Wallets.AnyAsync(w => w.IdPersona != null && idsPersona.Contains(w.IdPersona.Value)));
            Assert.False(await consulta.LedgerTransacciones.AnyAsync(t => t.CreadoPor != null && idsUsuario.Contains(t.CreadoPor.Value)));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Theory]
    [InlineData("QA")]
    [InlineData("Production")]
    public async Task Servicio_kyc_bloquea_cuenta_limitada_sin_crear_sesion_ni_transporte(string nombreAmbiente)
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Alta>();
        try
        {
            var alta = await SembrarAsync(cs, creados, limitada: true);
            await using var db = Contexto(cs);
            var http = new SinHttp();
            var kyc = new KycService(db, Configuracion(), http,
                NullLogger<KycService>.Instance, AmbienteXpayTests.Ambiente(nombreAmbiente));
            var sesionesAntes = await db.KycVerificaciones.CountAsync(k => k.IdUsuario == alta.IdUsuario);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => kyc.CreateVeriffSessionAsync(alta.IdUsuario));

            Assert.Equal(KycService.MensajeConsentimientoKycPendiente, error.Message);
            Assert.Equal(0, http.Intentos);
            Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
            Assert.Equal(sesionesAntes, await db.KycVerificaciones.CountAsync(k => k.IdUsuario == alta.IdUsuario));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Cambio_obligatorio_de_clave_en_prod_conserva_requiere_perfil_y_marcador()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Alta>();
        try
        {
            var alta = await SembrarAsync(cs, creados, limitada: true);
            await using var db = Contexto(cs);
            await db.Usuarios.Where(u => u.IdUsuario == alta.IdUsuario)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.RequiereCambioClave, true));
            var auth = new AuthService(db, Configuracion(), AmbienteXpayTests.Ambiente("Production"));
            var respuesta = await auth.CambiarClaveObligatoriaAsync(alta.IdUsuario, new CambiarClaveObligatoriaRequest
            {
                ClaveActual = Clave,
                ClaveNueva = "NuevaLimitada#2026x",
                ConfirmacionClaveNueva = "NuevaLimitada#2026x",
            });

            Assert.True(respuesta.RequierePerfil);
            Assert.False(respuesta.RequiereCambioClave);
            Assert.True(await RegistroPublicoService.EsCuentaLimitadaAsync(db, alta.IdUsuario));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task Alta_publica_no_consume_Veriff_sin_flag_correo_o_autorizacion_vigente(
        bool habilitado, bool correoVerificado, bool consentimiento)
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Alta>();
        try
        {
            var alta = await SembrarInicioPublicoAsync(cs, creados);
            var config = ConfiguracionKycPublico();
            if (consentimiento)
            {
                await using var dbConsentimiento = Contexto(cs);
                await new ConsentimientoBiometricoService(dbConsentimiento, config,
                    AmbienteXpayTests.Ambiente("Production"), TimeProvider.System)
                    .AutorizarAsync(alta.IdUsuario, new(true, "1.2"), "203.0.113.8");
            }
            config[ConsentimientoBiometricoService.ClaveHabilitada] = habilitado ? "true" : "false";
            await using (var cambio = Contexto(cs))
                await cambio.Usuarios.Where(u => u.IdUsuario == alta.IdUsuario)
                    .ExecuteUpdateAsync(u => u.SetProperty(x => x.EmailVerificado, correoVerificado));
            await using var db = Contexto(cs);
            var http = new SinHttp();
            var servicio = new KycService(db, config, http, NullLogger<KycService>.Instance,
                AmbienteXpayTests.Ambiente("Production"));

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.CreateVeriffSessionAsync(alta.IdUsuario));

            Assert.Equal(KycService.MensajeConsentimientoKycPendiente, error.Message);
            Assert.Equal(0, http.Intentos);
            Assert.False(await db.KycVerificaciones.AnyAsync(k => k.IdUsuario == alta.IdUsuario));
            Assert.True(await RegistroPublicoService.EsCuentaLimitadaAsync(db, alta.IdUsuario));
            Assert.False(await db.Wallets.AnyAsync(w => w.IdPersona == alta.IdPersona));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Correo_no_verificado_no_permite_otorgar_C_ni_crea_evidencia()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Alta>();
        try
        {
            var alta = await SembrarInicioPublicoAsync(cs, creados);
            await using (var cambio = Contexto(cs))
                await cambio.Usuarios.Where(u => u.IdUsuario == alta.IdUsuario)
                    .ExecuteUpdateAsync(u => u.SetProperty(x => x.EmailVerificado, false));
            await using var db = Contexto(cs);
            var servicio = new ConsentimientoBiometricoService(db, ConfiguracionKycPublico(),
                AmbienteXpayTests.Ambiente("Production"), TimeProvider.System);

            await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.AutorizarAsync(alta.IdUsuario, new(true, "1.2"), null));

            Assert.False(await db.Auditorias.AnyAsync(a => a.IdUsuario == alta.IdUsuario
                && a.Accion == ConsentimientoBiometricoService.AccionAutorizar));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Http_correo_y_C_permiten_Veriff_sin_WhatsApp_y_aprobacion_conserva_Perfil_sin_Wallet()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Alta>();
        try
        {
            var alta = await SembrarInicioPublicoAsync(cs, creados);
            var config = ConfiguracionKycPublico();
            var proveedor = new VeriffFicticio();
            var login = await LoginAsync(cs, config, alta);
            Assert.True(login.RequierePerfil);
            var sonda = new SondaCuentaLimitada();
            await using var app = CrearHost(cs, config, sonda, proveedor);
            await app.StartAsync();
            using var cliente = new HttpClient { BaseAddress = new Uri(Assert.Single(app.Services
                .GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses)) };
            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
            using (var sinActo = await cliente.PostAsJsonAsync("/api/usuarios/alta-publica/consentimiento-kyc",
                new { autorizaBiometria = false, versionDocumento = "1.2" }))
                Assert.Equal(HttpStatusCode.BadRequest, sinActo.StatusCode);
            using (var aceptar = await cliente.PostAsJsonAsync("/api/usuarios/alta-publica/consentimiento-kyc",
                new { autorizaBiometria = true, versionDocumento = "1.2" }))
                Assert.Equal(HttpStatusCode.OK, aceptar.StatusCode);
            using (var iniciar = await cliente.PostAsync("/api/kyc/veriff/session", null))
            {
                Assert.Equal(HttpStatusCode.OK, iniciar.StatusCode);
                var cuerpo = await iniciar.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(proveedor.SessionId, cuerpo.GetProperty("data").GetProperty("sessionId").GetString());
            }
            using (var retomar = await cliente.PostAsync("/api/kyc/veriff/session", null))
            {
                Assert.Equal(HttpStatusCode.OK, retomar.StatusCode);
                var cuerpo = await retomar.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(proveedor.SessionId, cuerpo.GetProperty("data").GetProperty("sessionId").GetString());
            }
            using (var recuperar = await cliente.GetAsync("/api/kyc/mi-estado"))
            {
                Assert.Equal(HttpStatusCode.OK, recuperar.StatusCode);
                var cuerpo = await recuperar.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("PENDIENTE", cuerpo.GetProperty("data").GetProperty("estadoKyc").GetString());
                Assert.Equal("https://saas.veriff.com/v/solo-prueba-sintetica",
                    cuerpo.GetProperty("data").GetProperty("sessionUrl").GetString());
            }
            Assert.Equal(1, proveedor.Solicitudes);
            var numeroSintetico = $"88{(uint)Guid.NewGuid().GetHashCode() % 100_000_000:D8}";
            var body = JsonSerializer.Serialize(new { status = "success", verification = new
            {
                id = proveedor.SessionId, attemptId = Guid.NewGuid().ToString(), status = "approved",
                decisionTime = "2026-11-21T12:00:00Z",
                person = new { firstName = "ANA", lastName = "PRUEBA SINTETICA", dateOfBirth = "1990-01-01" },
                document = new { type = "ID_CARD", number = numeroSintetico, country = "CO" },
            } });
            using var webhook = new HttpRequestMessage(HttpMethod.Post, "/api/kyc/veriff/webhook")
            { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            webhook.Headers.Add("x-hmac-signature", Convert.ToHexString(HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(config["VERIFF_SHARED_SECRET"]!), Encoding.UTF8.GetBytes(body))).ToLowerInvariant());
            using (var decision = await cliente.SendAsync(webhook)) Assert.Equal(HttpStatusCode.OK, decision.StatusCode);

            await using var db = Contexto(cs);
            var usuario = await db.Usuarios.AsNoTracking().SingleAsync(u => u.IdUsuario == alta.IdUsuario);
            Assert.Equal("APROBADO", usuario.EstadoKycActual);
            Assert.True(usuario.EmailVerificado);
            Assert.False(usuario.CelularVerificado);
            Assert.True((await db.Personas.AsNoTracking().SingleAsync(p => p.IdPersona == alta.IdPersona)).IdentidadVerificada);
            Assert.True(await RegistroPublicoService.EsCuentaLimitadaAsync(db, alta.IdUsuario));
            Assert.True((await LoginAsync(cs, config, alta)).RequierePerfil);
            Assert.False(await db.Wallets.AnyAsync(w => w.IdPersona == alta.IdPersona));
            Assert.False(await db.LibranzaEmpleadoUsuarios.AnyAsync(v => v.IdUsuario == alta.IdUsuario));
            Assert.False(await db.LibranzaAnticipos.AnyAsync(a => a.IdUsuario == alta.IdUsuario));
            Assert.False(await db.LedgerTransacciones.AnyAsync(t => t.CreadoPor == alta.IdUsuario));
            using (var operar = await cliente.PostAsync("/api/prueba-cuenta-limitada/operacion", JsonContent.Create(new { valor = 1 })))
                Assert.Equal(HttpStatusCode.Forbidden, operar.StatusCode);
            Assert.Equal(0, sonda.Ejecuciones);
            var consentimiento = Assert.Single(await db.Auditorias.AsNoTracking().Where(a => a.IdUsuario == alta.IdUsuario
                && a.Accion == ConsentimientoBiometricoService.AccionAutorizar).ToListAsync());
            Assert.Contains("\"autorizaBiometria\":true", consentimiento.ValorNuevo);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Theory]
    [InlineData("EN_REVISION")]
    [InlineData("APROBADO")]
    [InlineData("RECHAZADO")]
    [InlineData("EXPIRADO")]
    [InlineData("ERROR")]
    public async Task Alta_publica_no_reinicia_procesos_con_estado_previo(string estado)
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Alta>();
        try
        {
            var alta = await SembrarInicioPublicoAsync(cs, creados);
            var config = ConfiguracionKycPublico();
            await using (var autorizar = Contexto(cs))
            {
                await new ConsentimientoBiometricoService(autorizar, config,
                    AmbienteXpayTests.Ambiente("Production"), TimeProvider.System)
                    .AutorizarAsync(alta.IdUsuario, new(true, "1.2"), null);
                await autorizar.Usuarios.Where(u => u.IdUsuario == alta.IdUsuario)
                    .ExecuteUpdateAsync(u => u.SetProperty(x => x.EstadoKycActual, estado));
            }
            await using var db = Contexto(cs);
            var http = new SinHttp();
            var servicio = new KycService(db, config, http, NullLogger<KycService>.Instance,
                AmbienteXpayTests.Ambiente("Production"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.CreateVeriffSessionAsync(alta.IdUsuario));

            Assert.Equal(0, http.Intentos);
            Assert.False(await db.KycVerificaciones.AnyAsync(k => k.IdUsuario == alta.IdUsuario));
            Assert.Equal(estado, (await db.Usuarios.AsNoTracking().SingleAsync(u => u.IdUsuario == alta.IdUsuario)).EstadoKycActual);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    private static async Task<Alta> SembrarInicioPublicoAsync(string cs, List<Alta> creados)
    {
        var alta = await SembrarAsync(cs, creados, limitada: true);
        await using var db = Contexto(cs);
        await db.KycVerificaciones.Where(k => k.IdUsuario == alta.IdUsuario).ExecuteDeleteAsync();
        await db.Usuarios.Where(u => u.IdUsuario == alta.IdUsuario).ExecuteUpdateAsync(u => u
            .SetProperty(x => x.EstadoKycActual, "NO_INICIADO").SetProperty(x => x.CelularVerificado, false));
        await db.Personas.Where(p => p.IdPersona == alta.IdPersona).ExecuteUpdateAsync(p => p
            .SetProperty(x => x.IdentidadVerificada, false).SetProperty(x => x.IdentidadVerificadaProveedor, (string?)null));
        return alta;
    }

    [Fact]
    public async Task Dos_POST_publicos_simultaneos_crean_una_sola_sesion_y_retoman_el_mismo_enlace()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Alta>();
        var liberarProveedor = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var alta = await SembrarInicioPublicoAsync(cs, creados);
            var config = ConfiguracionKycPublico();
            await using (var db = Contexto(cs))
                await new ConsentimientoBiometricoService(db, config,
                    AmbienteXpayTests.Ambiente("Production"), TimeProvider.System)
                    .AutorizarAsync(alta.IdUsuario, new(true, "1.2"), null);
            var proveedor = new VeriffFicticio { Espera = liberarProveedor.Task };
            var login = await LoginAsync(cs, config, alta);
            await using var app = CrearHost(cs, config, new SondaCuentaLimitada(), proveedor);
            await app.StartAsync();
            using var cliente = new HttpClient { BaseAddress = new Uri(Assert.Single(app.Services
                .GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses)) };
            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);

            var primera = cliente.PostAsync("/api/kyc/veriff/session", null);
            await proveedor.Iniciada.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var segunda = cliente.PostAsync("/api/kyc/veriff/session", null);
            // El proveedor está detenido intencionalmente: una segunda petición
            // que traspasara el bloqueo ya habría incrementado este contador.
            await Task.Delay(250);
            Assert.Equal(1, proveedor.Solicitudes);
            Assert.False(primera.IsCompleted);
            Assert.False(segunda.IsCompleted);
            liberarProveedor.TrySetResult();
            var respuestas = await Task.WhenAll(primera, segunda);
            foreach (var respuesta in respuestas)
            {
                using (respuesta)
                {
                    Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
                    var body = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
                    Assert.Equal(proveedor.SessionId, body.GetProperty("data").GetProperty("sessionId").GetString());
                }
            }
            Assert.Equal(1, proveedor.Solicitudes);
            await using var consulta = Contexto(cs);
            var sesiones = await consulta.KycVerificaciones.AsNoTracking().Where(k => k.IdUsuario == alta.IdUsuario).ToListAsync();
            Assert.Equal(proveedor.SessionId, Assert.Single(sesiones).SessionId);
            Assert.True(sesiones[0].EsActual);
            Assert.True(await RegistroPublicoService.EsCuentaLimitadaAsync(consulta, alta.IdUsuario));
        }
        finally
        {
            liberarProveedor.TrySetResult();
            await LimpiarAsync(cs, creados);
        }
    }

    [Fact]
    public async Task Piloto_legacy_conserva_callback_global_sin_flag_C_o_correo_nuevos()
    {
        if (!TryConnString(out var cs)) return;
        var creados = new List<Alta>();
        try
        {
            var alta = await SembrarAsync(cs, creados, limitada: false);
            var config = new ConfigurationBuilder().AddConfiguration(Configuracion()).Build();
            config["VERIFF_CALLBACK_URL"] = "https://app.xpaycol.com/mi-wallet";
            var proveedor = new VeriffFicticio { CallbackEsperado = "https://app.xpaycol.com/mi-wallet" };
            await using var db = Contexto(cs);
            var servicio = new KycService(db, config, proveedor, NullLogger<KycService>.Instance,
                AmbienteXpayTests.Ambiente("Production"));

            var respuesta = await servicio.CreateVeriffSessionAsync(alta.IdUsuario);

            Assert.Equal(proveedor.SessionId, respuesta.SessionId);
            Assert.Equal(1, proveedor.Solicitudes);
            Assert.False(await RegistroPublicoService.EsCuentaLimitadaAsync(db, alta.IdUsuario));
            Assert.Equal("https://app.xpaycol.com/mi-wallet", config["VERIFF_CALLBACK_URL"]);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    private static IConfigurationRoot ConfiguracionKycPublico()
    {
        var config = new ConfigurationBuilder().AddConfiguration(Configuracion()).Build();
        config[ConsentimientoBiometricoService.ClaveHabilitada] = "true";
        config["Identidad:UrlFrontend"] = "https://app.xpaycol.com/";
        config["XPAY_AUTORIZACION_KYC_VERSION"] = "1.2";
        config["XPAY_AUTORIZACION_KYC_FECHA"] = "2026-11-18";
        config["XPAY_AUTORIZACION_KYC_URL"] = "https://app.xpaycol.com/legal/v1.2/autorizaciones.pdf";
        config["XPAY_AUTORIZACION_KYC_SHA256"] = ConsentimientoBiometricoService.ShaDocumentoAprobado;
        return config;
    }

    private sealed class VeriffFicticio : IHttpClientFactory
    {
        public string SessionId { get; } = Guid.NewGuid().ToString();
        public string CallbackEsperado { get; init; } = "https://app.xpaycol.com/mi-wallet/perfil";
        public Task? Espera { get; init; }
        public TaskCompletionSource Iniciada { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Solicitudes;
        public HttpClient CreateClient(string name) => new(new Respuesta(this));
        private sealed class Respuesta(VeriffFicticio proveedor) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("https://veriff.example.test/v1/sessions", request.RequestUri!.AbsoluteUri);
                var body = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(cancellationToken));
                Assert.Equal(proveedor.CallbackEsperado, body.GetProperty("verification").GetProperty("callback").GetString());
                Interlocked.Increment(ref proveedor.Solicitudes);
                proveedor.Iniciada.TrySetResult();
                if (proveedor.Espera is not null) await proveedor.Espera.WaitAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { status = "success", verification = new
                    { id = proveedor.SessionId, url = "https://saas.veriff.com/v/solo-prueba-sintetica" } }),
                };
            }
        }
    }

    private static WebApplication CrearHost(string cs, IConfiguration config, SondaCuentaLimitada sonda, IHttpClientFactory? http = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddConfiguration(config);
        builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
        builder.Services.AddDbContext<XpayDbContext>(o => o.UseSqlServer(cs));
        builder.Services.AddSingleton(AmbienteXpayTests.Ambiente("Production"));
        builder.Services.AddSingleton<ControlOperacionesFinancieras>();
        builder.Services.AddSingleton(sonda);
        builder.Services.AddSingleton<IHttpClientFactory>(http ?? new SinHttp());
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<PerfilService>();
        builder.Services.AddScoped<RegistroUsuarioFinalService>();
        builder.Services.AddScoped<RegistroInicialService>();
        builder.Services.AddScoped<ContactoVerificacionService>();
        builder.Services.AddSingleton<RequisitoContactosVerificados>();
        builder.Services.AddScoped<AuditLogService>();
        builder.Services.AddScoped<KycService>();
        builder.Services.AddScoped<ConsentimientoBiometricoService>();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!)),
                ValidateIssuer = true, ValidIssuer = config["Jwt:Issuer"],
                ValidateAudience = true, ValidAudience = config["Jwt:Audience"],
                ValidateLifetime = true, ClockSkew = TimeSpan.Zero,
            };
            ValidadorVersionSeguridad.Registrar(o);
        });
        builder.Services.AddScoped<IAuthorizationHandler, ClaveVigenteAuthorizationHandler>();
        builder.Services.AddScoped<IAuthorizationHandler, KycAprobadoAuthorizationHandler>();
        builder.Services.AddAuthorization(AutorizacionPoliticas.Configurar);
        builder.Services.AddRateLimiter(o =>
        {
            foreach (var nombre in new[] { "LoginPolicy", "CambiarClavePolicy", "ContactosCodigoPolicy", "IdentidadPublicaPolicy", "DestinatarioWalletPolicy" })
                o.AddFixedWindowLimiter(nombre, limite =>
                {
                    limite.PermitLimit = 1000;
                    limite.Window = TimeSpan.FromMinutes(1);
                });
        });
        builder.Services.AddControllers(o => o.Filters.Add<CuentaLimitadaFilter>(CuentaLimitadaFilter.Orden))
            .AddApplicationPart(typeof(UsuariosController).Assembly)
            .AddApplicationPart(typeof(CuentaLimitadaProbeController).Assembly);
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapControllers();
        return app;
    }

    private sealed record Alta(long IdUsuario, long IdPersona, string Nombre);

    private static async Task<Alta> SembrarAsync(string cs, List<Alta> creados, bool limitada)
    {
        await using var db = Contexto(cs);
        var unidad = await db.Database.SqlQueryRaw<long>(
            "SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL").SingleAsync();
        var nombre = "limited_" + Guid.NewGuid().ToString("N")[..12];
        var ahora = DateTime.UtcNow;
        var persona = new Persona
        {
            IdUnidadNegocio = unidad, Email = nombre + "@example.test", Celular = "3000000000",
            PrimerNombre = "Prueba", PrimerApellido = "Sintetica", Estado = "ACTIVA", FechaCreacion = ahora,
            IdentidadVerificada = true, IdentidadVerificadaProveedor = "VERIFF",
        };
        db.Personas.Add(persona);
        await db.SaveChangesAsync();
        var usuario = new Usuario
        {
            IdPersona = persona.IdPersona, NombreUsuario = nombre, PasswordHash = BCrypt.Net.BCrypt.HashPassword(Clave, workFactor: 4),
            Estado = "ACTIVO", FechaCreacion = ahora, EmailVerificado = true, CelularVerificado = true, EstadoKycActual = "APROBADO",
        };
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        var alta = new Alta(usuario.IdUsuario, persona.IdPersona, nombre);
        creados.Add(alta);
        db.UsuarioRoles.Add(new UsuarioRol
        {
            IdUsuario = usuario.IdUsuario,
            IdRol = await db.Roles.Where(r => r.Codigo == "USUARIO_FINAL" && r.Estado == "ACTIVO").Select(r => r.IdRol).SingleAsync(),
            Estado = "ACTIVO", FechaAsignacion = ahora,
        });
        db.KycVerificaciones.Add(new KycVerificacion
        {
            IdUsuario = usuario.IdUsuario, IdPersona = persona.IdPersona, Proveedor = "VERIFF",
            EstadoKyc = "APROBADO", EsActual = true, FechaCreacion = ahora,
        });
        if (limitada) db.Auditorias.Add(Marcador(alta));
        await db.SaveChangesAsync();
        return alta;
    }

    private static Auditoria Marcador(Alta alta) => new()
    {
        IdUsuario = alta.IdUsuario, IdPersona = alta.IdPersona, Modulo = "USUARIOS",
        Accion = RegistroPublicoService.AccionAceptarTerminos, Resultado = "EXITOSO", FechaEvento = DateTime.UtcNow,
        Entidad = "usuarios", IdEntidad = alta.IdUsuario.ToString(),
    };

    private static async Task<LoginResponse> LoginAsync(string cs, IConfiguration config, Alta alta)
    {
        await using var db = Contexto(cs);
        return await new AuthService(db, config, AmbienteXpayTests.Ambiente("Production"))
            .LoginAsync(new LoginRequest { Usuario = alta.Nombre, Password = Clave });
    }

    private static IConfiguration Configuracion() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Jwt:Key"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
        ["Jwt:Issuer"] = "cuenta-limitada-pruebas", ["Jwt:Audience"] = "cuenta-limitada-pruebas",
        [ControlOperacionesFinancieras.ClaveInterruptor] = "true",
        ["VERIFF_API_KEY"] = "sintetica", ["VERIFF_SHARED_SECRET"] = "sintetica",
        ["VERIFF_BASE_URL"] = "https://veriff.example.test", ["VERIFF_CALLBACK_URL"] = "https://app.example.test/perfil",
    }).Build();

    private static XpayDbContext Contexto(string cs) => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        Assert.False(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")),
            EnvConnString + " es obligatoria en CI para las pruebas de aislamiento de cuentas.");
        return false;
    }

    private static async Task LimpiarAsync(string cs, List<Alta> creados)
    {
        await using var db = Contexto(cs);
        var ids = creados.Select(a => a.IdUsuario).ToArray();
        var personas = creados.Select(a => a.IdPersona).ToArray();
        await db.Auditorias.Where(a => a.IdUsuario != null && ids.Contains(a.IdUsuario.Value)).ExecuteDeleteAsync();
        await db.KycVerificaciones.Where(k => ids.Contains(k.IdUsuario)).ExecuteDeleteAsync();
        await db.UsuarioRoles.Where(r => ids.Contains(r.IdUsuario)).ExecuteDeleteAsync();
        await db.Usuarios.Where(u => ids.Contains(u.IdUsuario)).ExecuteDeleteAsync();
        await db.Personas.Where(p => personas.Contains(p.IdPersona)).ExecuteDeleteAsync();
    }

    private sealed class SinHttp : IHttpClientFactory
    {
        public int Intentos { get; private set; }
        public HttpClient CreateClient(string name)
        {
            Intentos++;
            throw new InvalidOperationException("No se debe invocar un proveedor externo en estas pruebas.");
        }
    }
}

public sealed class SondaCuentaLimitada
{
    public int Ejecuciones;
    public int Callbacks;
}

[ApiController]
[Authorize]
[Route("api/prueba-cuenta-limitada")]
public sealed class CuentaLimitadaProbeController(SondaCuentaLimitada sonda) : ControllerBase
{
    [HttpPost("operacion")]
    [OperacionFinanciera]
    public IActionResult Operacion([FromBody] OperacionPrueba body)
    {
        Interlocked.Increment(ref sonda.Ejecuciones);
        return Ok(new { success = true });
    }

    [HttpGet("sin-excepcion")]
    public IActionResult SinExcepcion()
    {
        Interlocked.Increment(ref sonda.Ejecuciones);
        return Ok();
    }

    [HttpPost("callback")]
    [AllowAnonymous]
    public IActionResult Callback()
    {
        Interlocked.Increment(ref sonda.Callbacks);
        return Ok();
    }

    public sealed record OperacionPrueba(decimal Valor);
}
