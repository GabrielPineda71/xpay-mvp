using System.Data.Common;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Xpay.Api.Authorization;
using Xpay.Api.Common;
using Xpay.Api.Controllers;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xpay.Api.Tests.Common;
using Xunit;

namespace Xpay.Api.Tests.Services;

// Datos exclusivamente sintéticos. Los proveedores no se construyen: se
// ejercitan SQL Server, los servicios propios, la evidencia C y los JWT.
[Collection("SqlIntegration")]
public sealed class PreparacionWalletSqlTests
{
    private const string Clave = "Preparacion#2026Prueba";
    private const string Pin = "648291";
    private static readonly ActivarWalletRequest Aceptacion = new(true, CondicionesWalletPiloto.Version);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Progresion_ExigeCadaPaso_YActivaSoloWalletConSaldoCero(bool omitirLlave)
    {
        if (!Conexion(out var cs)) return;
        await using var datos = new Semillero(cs);
        var cuenta = await datos.CuentaAsync();
        var config = Configuracion();
        await using var db = Contexto(cs);
        var servicio = Servicio(db, config);
        Assert.Equal("CORREO", (await servicio.ObtenerAsync(cuenta.Usuario)).SiguientePaso);
        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.ExigirAccesoPinAsync(cuenta.Usuario));
        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.GuardarLlaveAsync(cuenta.Usuario, new(true, null, null, false)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.ActivarAsync(cuenta.Usuario, Aceptacion, null));
        Assert.True(await RegistroPublicoService.EsCuentaLimitadaAsync(db, cuenta.Usuario));

        await datos.ConfirmarCorreoAsync(cuenta);
        Assert.Equal("IDENTIDAD", (await servicio.ObtenerAsync(cuenta.Usuario)).SiguientePaso);
        await datos.AutorizarBiometriaAsync(cuenta, config);
        // La casilla C precede a la verificación y no la sustituye.
        Assert.Equal("IDENTIDAD", (await servicio.ObtenerAsync(cuenta.Usuario)).SiguientePaso);
        await datos.AprobarIdentidadAsync(cuenta);
        Assert.Equal("DATOS", (await servicio.ObtenerAsync(cuenta.Usuario)).SiguientePaso);
        Assert.False(await db.Wallets.AnyAsync(w => w.IdPersona == cuenta.Persona));

        var lugar = await datos.LugarAsync();
        var conDatos = await servicio.GuardarDatosAsync(cuenta.Usuario, new("Calle sintética 12", lugar.Departamento, lugar.Ciudad));
        Assert.Equal("LLAVE", conDatos.SiguientePaso);
        Assert.Equal(lugar.Ciudad, conDatos.IdCiudad);
        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.ExigirAccesoPinAsync(cuenta.Usuario));
        var alias = "@qa" + Guid.NewGuid().ToString("N")[..10];
        if (omitirLlave)
            await servicio.GuardarLlaveAsync(cuenta.Usuario, new(true, null, null, false));
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.GuardarLlaveAsync(cuenta.Usuario, new(false, "ALPHA", alias, false)));
            await servicio.GuardarLlaveAsync(cuenta.Usuario, new(false, "ALPHA", alias, true));
        }
        var conLlave = await servicio.ObtenerAsync(cuenta.Usuario);
        Assert.Equal("PIN", conLlave.SiguientePaso);
        Assert.Equal(omitirLlave ? "OMITIDA" : "REGISTRADA", conLlave.Llave.Opcion);
        Assert.DoesNotContain(alias, JsonSerializer.Serialize(conLlave));
        Assert.False(await db.PassportBrebLlaves.AnyAsync(l => l.IdUsuario == cuenta.Usuario));
        await servicio.ExigirAccesoPinAsync(cuenta.Usuario);
        var pines = new PinTransaccionalService(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => pines.CrearAsync(cuenta.Usuario,
            new() { ClaveActual = Clave, Pin = "12345", ConfirmacionPin = "12345" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => pines.CrearAsync(cuenta.Usuario,
            new() { ClaveActual = Clave, Pin = Pin, ConfirmacionPin = Pin[..5] }));
        await pines.CrearAsync(cuenta.Usuario, new() { ClaveActual = Clave, Pin = Pin, ConfirmacionPin = Pin });
        Assert.Equal("ACTIVAR", (await servicio.ObtenerAsync(cuenta.Usuario)).SiguientePaso);
        Assert.True((await servicio.ObtenerAsync(cuenta.Usuario)).PuedeActivar);

        foreach (var rechazo in new[] { new ActivarWalletRequest(false, CondicionesWalletPiloto.Version),
                     new(true, null), new(true, "0.9"), new(true, CondicionesWalletPiloto.Version + " ") })
            await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.ActivarAsync(cuenta.Usuario, rechazo, "203.0.113.41"));
        Assert.False(await db.Wallets.AnyAsync(w => w.IdPersona == cuenta.Persona));
        Assert.False(await db.Auditorias.AnyAsync(a => a.IdUsuario == cuenta.Usuario && a.Accion == PreparacionWalletService.AccionActivar));

        var antes = await FotoSinDineroAsync(db, cuenta);
        var sesionPrevia = await new AuthService(db, config).LoginAsync(new() { Usuario = cuenta.Nombre, Password = Clave });
        Assert.True(sesionPrevia.RequierePerfil);
        Assert.Equal(0, Version(sesionPrevia.Token, config));
        var respuesta = await servicio.ActivarAsync(cuenta.Usuario, Aceptacion, "203.0.113.41");
        Assert.Equal(cuenta.Usuario, respuesta.IdUsuario);
        Assert.False(respuesta.RequierePerfil);
        Assert.False(respuesta.EsUsuarioEmpresa);
        Assert.Equal(1, Version(respuesta.Token, config));
        Assert.False(await ValidadorVersionSeguridad.EsVigenteAsync(db, cuenta.Usuario, 0, default));
        Assert.True(await ValidadorVersionSeguridad.EsVigenteAsync(db, cuenta.Usuario, 1, default));
        Assert.False(await RegistroPublicoService.EsCuentaLimitadaAsync(db, cuenta.Usuario));
        Assert.True(await RegistroPublicoService.EsAutorregistroAsync(db, cuenta.Usuario));
        var activa = await servicio.ObtenerAsync(cuenta.Usuario);
        Assert.Equal("WALLET", activa.SiguientePaso);
        Assert.True(activa.WalletHabilitada);
        Assert.False(activa.PuedeActivar);

        var wallet = await db.Wallets.AsNoTracking().SingleAsync(w => w.IdPersona == cuenta.Persona);
        Assert.Equal("ACTIVA", wallet.Estado);
        Assert.Equal("PERSONA", wallet.TipoWallet);
        var saldo = await db.WalletSaldos.AsNoTracking().SingleAsync(s => s.IdWallet == wallet.IdWallet);
        Assert.Equal((0m, 0m, 0m, 0m), (saldo.SaldoDisponible, saldo.SaldoRetenido, saldo.SaldoTransito, saldo.SaldoContingencia));
        Assert.Equal(antes, await FotoSinDineroAsync(db, cuenta));
        var preparacion = await db.WalletPreparaciones.AsNoTracking().SingleAsync(p => p.IdUsuario == cuenta.Usuario);
        Assert.Equal(wallet.IdWallet, preparacion.IdWallet);
        Assert.Equal(CondicionesWalletPiloto.Sha256, preparacion.CondicionesSha256);
        Assert.Equal(PreparacionWalletService.CriteriosVersion, preparacion.CriteriosVersion);
        var evidencia = await db.Auditorias.AsNoTracking().SingleAsync(a => a.IdUsuario == cuenta.Usuario && a.Accion == PreparacionWalletService.AccionActivar);
        Assert.Equal("203.0.113.41", evidencia.Ip);
        using (var json = JsonDocument.Parse(evidencia.ValorNuevo!))
        {
            Assert.True(json.RootElement.GetProperty("aceptaCondiciones").GetBoolean());
            Assert.Equal(CondicionesWalletPiloto.Sha256, json.RootElement.GetProperty("condicionesSha256").GetString());
            Assert.True(json.RootElement.GetProperty("whatsAppPendiente").GetBoolean());
            Assert.Equal(6, json.RootElement.GetProperty("longitudPin").GetInt32());
        }
        Assert.False((await db.Usuarios.AsNoTracking().SingleAsync(u => u.IdUsuario == cuenta.Usuario)).CelularVerificado);
        var llaves = await db.PassportBrebLlaves.AsNoTracking().Where(l => l.IdUsuario == cuenta.Usuario).ToListAsync();
        if (omitirLlave) Assert.Empty(llaves);
        else
        {
            var llave = Assert.Single(llaves);
            Assert.Equal("ALPHA", llave.KeyType);
            Assert.Equal("PENDIENTE_VALIDACION", llave.Estado);
            Assert.Equal(BrebKeyValue.ComputeHash(BrebService.NormalizeKeyValue("ALPHA", alias)), llave.KeyValueHash);
            Assert.Null(llave.FechaValidacion);
            Assert.Null(llave.OwnerIdentificationType);
            Assert.Null(llave.KeyValueEncrypted);
        }

        var repetida = await servicio.ActivarAsync(cuenta.Usuario, Aceptacion, null);
        Assert.Equal(1, Version(repetida.Token, config));
        Assert.Single(await db.Wallets.Where(w => w.IdPersona == cuenta.Persona).ToListAsync());
        Assert.Single(await db.Auditorias.Where(a => a.IdUsuario == cuenta.Usuario && a.Accion == PreparacionWalletService.AccionActivar).ToListAsync());
        Assert.Equal(antes, await FotoSinDineroAsync(db, cuenta));
    }

    [Theory]
    [InlineData("correo", "CORREO")]
    [InlineData("consentimiento_obsoleto", "REVISION")]
    [InlineData("sin_consentimiento", "REVISION")]
    [InlineData("kyc_simulado", "REVISION")]
    [InlineData("kyc_decision_distinta", "REVISION")]
    [InlineData("documento_no_consolidado", "REVISION")]
    [InlineData("documento_duplicado", "REVISION")]
    [InlineData("pin_cinco", "PIN")]
    [InlineData("pin_bloqueado", "REVISION")]
    [InlineData("domicilio_sin_dane", "DATOS")]
    public async Task RequisitosInvalidos_NoCreanWalletNiRevocanSesion(string caso, string pasoEsperado)
    {
        if (!Conexion(out var cs)) return;
        await using var datos = new Semillero(cs);
        var config = Configuracion();
        var cuenta = await datos.ListaAsync(config);
        await using (var cambiar = Contexto(cs))
        {
            if (caso == "correo") await cambiar.Usuarios.Where(u => u.IdUsuario == cuenta.Usuario).ExecuteUpdateAsync(s => s.SetProperty(u => u.EmailVerificado, false));
            if (caso == "sin_consentimiento") await cambiar.Auditorias.Where(a => a.IdUsuario == cuenta.Usuario && a.Accion == ConsentimientoBiometricoService.AccionAutorizar).ExecuteDeleteAsync();
            if (caso == "consentimiento_obsoleto")
                cambiar.Auditorias.Add(new() { IdUsuario = cuenta.Usuario, IdPersona = cuenta.Persona, Modulo = "USUARIOS", Accion = ConsentimientoBiometricoService.AccionAutorizar,
                    Entidad = "usuarios", IdEntidad = cuenta.Usuario.ToString(CultureInfo.InvariantCulture), Resultado = "REVOCADO", ValorNuevo = "{}", FechaEvento = DateTime.UtcNow });
            if (caso == "kyc_simulado") await cambiar.KycVerificaciones.Where(k => k.IdUsuario == cuenta.Usuario).ExecuteUpdateAsync(s => s.SetProperty(k => k.Proveedor, "SIMULACION_QA"));
            if (caso == "kyc_decision_distinta") await cambiar.KycVerificaciones.Where(k => k.IdUsuario == cuenta.Usuario).ExecuteUpdateAsync(s => s.SetProperty(k => k.Decision, "declined"));
            if (caso == "documento_no_consolidado") await cambiar.Personas.Where(p => p.IdPersona == cuenta.Persona).ExecuteUpdateAsync(s => s.SetProperty(p => p.TipoDocumento, (string?)null).SetProperty(p => p.NumeroDocumento, (string?)null));
            if (caso == "pin_cinco") await cambiar.PinesTransaccionales.Where(p => p.IdUsuario == cuenta.Usuario).ExecuteUpdateAsync(s => s.SetProperty(p => p.LongitudPin, (byte?)5));
            if (caso == "pin_bloqueado") await cambiar.PinesTransaccionales.Where(p => p.IdUsuario == cuenta.Usuario).ExecuteUpdateAsync(s => s.SetProperty(p => p.Bloqueado, true));
            if (caso == "domicilio_sin_dane") await cambiar.Personas.Where(p => p.IdPersona == cuenta.Persona).ExecuteUpdateAsync(s => s.SetProperty(p => p.IdCiudadDane, (long?)null));
            await cambiar.SaveChangesAsync();
        }
        if (caso == "documento_duplicado") await datos.PersonaDuplicadaAsync(cuenta);
        await using var db = Contexto(cs);
        var servicio = Servicio(db, config);
        Assert.Equal(pasoEsperado, (await servicio.ObtenerAsync(cuenta.Usuario)).SiguientePaso);
        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.ActivarAsync(cuenta.Usuario, Aceptacion, null));
        Assert.False(await db.Wallets.AnyAsync(w => w.IdPersona == cuenta.Persona));
        Assert.True(await RegistroPublicoService.EsCuentaLimitadaAsync(db, cuenta.Usuario));
        Assert.False(await db.Auditorias.AnyAsync(a => a.IdUsuario == cuenta.Usuario && a.Accion == PreparacionWalletService.AccionActivar));
        Assert.Equal(0, await db.Usuarios.Where(u => u.IdUsuario == cuenta.Usuario).Select(u => u.VersionSeguridad).SingleAsync());
    }

    [Fact]
    public async Task DosActivacionesSimultaneasMismaCuenta_CreanUnaWalletUnaPlazaYUnaRevocacion()
    {
        if (!Conexion(out var cs)) return;
        await using var datos = new Semillero(cs);
        var config = Configuracion();
        var cuenta = await datos.ListaAsync(config);
        await datos.OcuparHastaAsync(49, cuenta.Usuario);
        var inicio = new AbrirJuntos(2);
        var resultados = await Task.WhenAll(ActivarConcurrenteAsync(cs, config, cuenta, inicio), ActivarConcurrenteAsync(cs, config, cuenta, inicio));
        Assert.All(resultados, r => Assert.Null(r.Error));
        await using var db = Contexto(cs);
        Assert.Equal(50, await PreparacionWalletService.ContarPlazasAsync(db));
        Assert.Single(await db.Wallets.Where(w => w.IdPersona == cuenta.Persona).ToListAsync());
        Assert.Single(await db.Auditorias.Where(a => a.IdUsuario == cuenta.Usuario && a.Accion == PreparacionWalletService.AccionActivar && a.Observacion != Semillero.Capacidad).ToListAsync());
        Assert.Equal(1, await db.Usuarios.Where(u => u.IdUsuario == cuenta.Usuario).Select(u => u.VersionSeguridad).SingleAsync());
        Assert.All(resultados, r => Assert.Equal(1, Version(r.Sesion!.Token, config)));
    }

    [Fact]
    public async Task DosTitularesCompitenPorUltimaPlaza_ElTotalConjuntoNoSuperaCincuenta()
    {
        if (!Conexion(out var cs)) return;
        await using var datos = new Semillero(cs);
        var config = Configuracion();
        var primera = await datos.ListaAsync(config);
        var segunda = await datos.ListaAsync(config);
        await datos.OcuparHastaAsync(49, primera.Usuario);
        var inicio = new AbrirJuntos(2);
        var resultados = await Task.WhenAll(ActivarConcurrenteAsync(cs, config, primera, inicio), ActivarConcurrenteAsync(cs, config, segunda, inicio));
        var ganador = Assert.Single(resultados, r => r.Error is null);
        var perdedor = Assert.Single(resultados, r => r.Error is not null);
        Assert.Contains(AltaUsuarioPilotoService.MensajeMaximoAlcanzado, perdedor.Error);
        await using var db = Contexto(cs);
        Assert.Equal(50, await PreparacionWalletService.ContarPlazasAsync(db));
        Assert.True(await db.Wallets.AnyAsync(w => w.IdPersona == ganador.Cuenta.Persona));
        Assert.False(await db.Wallets.AnyAsync(w => w.IdPersona == perdedor.Cuenta.Persona));
        Assert.True(await RegistroPublicoService.EsCuentaLimitadaAsync(db, perdedor.Cuenta.Usuario));
        Assert.Equal(0, await db.Usuarios.Where(u => u.IdUsuario == perdedor.Cuenta.Usuario).Select(u => u.VersionSeguridad).SingleAsync());
        // Tampoco se libera automáticamente capacidad por cerrar la Wallet.
        await db.Wallets.Where(w => w.IdPersona == ganador.Cuenta.Persona).ExecuteUpdateAsync(s => s.SetProperty(w => w.Estado, "INACTIVA"));
        Assert.Equal(50, await PreparacionWalletService.ContarPlazasAsync(db));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Servicio(db, config).ActivarAsync(perdedor.Cuenta.Usuario, Aceptacion, null));
    }

    [Fact]
    public async Task AltaManualYPúblicaCompartenElMismoLimiteBajoConcurrencia()
    {
        if (!Conexion(out var cs)) return;
        await using var datos = new Semillero(cs);
        var config = Configuracion();
        var publica = await datos.ListaAsync(config);
        var admin = await datos.CuentaAsync(publica: false, rol: "SUPERUSUARIO");
        await datos.OcuparHastaAsync(49, admin.Usuario);
        var solicitud = datos.NuevaSolicitudManual();
        var inicio = new AbrirJuntos(2);
        async Task<ResultadoConcurrente> ManualAsync()
        {
            await using var db = Contexto(cs, inicio);
            try
            {
                var resultado = await new AltaUsuarioPilotoService(db, config, AmbienteXpayTests.Ambiente("Production")).CrearAsync(solicitud, admin.Usuario);
                return new(new(resultado.IdUsuario, 0, solicitud.NombreUsuario!, ""), null, null);
            }
            catch (InvalidOperationException ex) { return new(admin, null, ex.Message); }
        }
        var resultados = await Task.WhenAll(ActivarConcurrenteAsync(cs, config, publica, inicio), ManualAsync());
        Assert.Single(resultados, r => r.Error is null);
        Assert.Contains(AltaUsuarioPilotoService.MensajeMaximoAlcanzado, Assert.Single(resultados, r => r.Error is not null).Error);
        await using var consulta = Contexto(cs);
        Assert.Equal(50, await PreparacionWalletService.ContarPlazasAsync(consulta));
        var manualExiste = await consulta.Usuarios.AnyAsync(u => u.NombreUsuario == solicitud.NombreUsuario);
        var publicaTieneWallet = await consulta.Wallets.AnyAsync(w => w.IdPersona == publica.Persona);
        Assert.NotEqual(manualExiste, publicaTieneWallet);
    }

    [Fact]
    public async Task FalloDeRevocacion_RevierteWalletSaldoAceptacionYCupoDelPiloto()
    {
        if (!Conexion(out var cs)) return;
        await using var datos = new Semillero(cs);
        var config = Configuracion();
        var cuenta = await datos.ListaAsync(config);
        int plazasAntes;
        await using (var consulta = Contexto(cs)) plazasAntes = await PreparacionWalletService.ContarPlazasAsync(consulta);
        await using (var falla = Contexto(cs, new FallarRevocacion()))
            await Assert.ThrowsAsync<RevocacionFallida>(() => Servicio(falla, config).ActivarAsync(cuenta.Usuario, Aceptacion, null));
        await using var db = Contexto(cs);
        Assert.False(await db.Wallets.AnyAsync(w => w.IdPersona == cuenta.Persona));
        Assert.False(await db.Auditorias.AnyAsync(a => a.IdUsuario == cuenta.Usuario && a.Accion == PreparacionWalletService.AccionActivar));
        Assert.Equal(plazasAntes, await PreparacionWalletService.ContarPlazasAsync(db));
        Assert.Equal(0, await db.Usuarios.Where(u => u.IdUsuario == cuenta.Usuario).Select(u => u.VersionSeguridad).SingleAsync());
        var preparacion = await db.WalletPreparaciones.AsNoTracking().SingleAsync(p => p.IdUsuario == cuenta.Usuario);
        Assert.Null(preparacion.IdWallet);
        Assert.Null(preparacion.FechaActivacion);
        Assert.Null(preparacion.CondicionesSha256);
        Assert.True(await RegistroPublicoService.EsCuentaLimitadaAsync(db, cuenta.Usuario));
        Assert.Equal("ACTIVAR", (await Servicio(db, config).ObtenerAsync(cuenta.Usuario)).SiguientePaso);
    }

    [Theory]
    [InlineData("inactivar", true)]
    [InlineData("clave", true)]
    [InlineData("rol", true)]
    [InlineData("inactivar", false)]
    [InlineData("clave", false)]
    [InlineData("rol", false)]
    public async Task RevocacionAdministrativa_SeOrdenaRespectoDeLaActivacion(string operacion, bool adminPrimero)
    {
        if (!Conexion(out var cs)) return;
        await using var datos = new Semillero(cs);
        var config = Configuracion();
        var cuenta = await datos.ListaAsync(config);
        var admin = await datos.CuentaAsync(publica: false, rol: "SUPERUSUARIO");
        async Task CambiarAdminAsync(XpayDbContext db)
        {
            var servicio = new UsuarioAdminService(db);
            if (operacion == "inactivar") await servicio.InactivarAsync(cuenta.Usuario, admin.Usuario);
            if (operacion == "clave") await servicio.RestablecerClaveAsync(cuenta.Usuario, admin.Usuario, "Prueba sintética de concurrencia");
            if (operacion == "rol") await servicio.AsignarRolAsync(cuenta.Usuario, admin.Usuario, "CARTERA_XPAY", "Prueba sintética de concurrencia");
        }
        if (adminPrimero)
        {
            await using (var cambio = Contexto(cs)) await CambiarAdminAsync(cambio);
            await using var intento = Contexto(cs);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Servicio(intento, config).ActivarAsync(cuenta.Usuario, Aceptacion, null));
            Assert.False(await intento.Wallets.AnyAsync(w => w.IdPersona == cuenta.Persona));
            Assert.False(await intento.Auditorias.AnyAsync(a => a.IdUsuario == cuenta.Usuario && a.Accion == PreparacionWalletService.AccionActivar));
            Assert.Equal(1, await intento.Usuarios.Where(u => u.IdUsuario == cuenta.Usuario).Select(u => u.VersionSeguridad).SingleAsync());
            return;
        }

        // La activación ya comprobó los requisitos. Se pausa antes del INSERT;
        // una revocación administrativa no puede modificar su lectura estable.
        var pausa = new PausarAntesDeCrearWallet();
        var escritor = new ObservarEscritura();
        await using var activacionDb = Contexto(cs, pausa);
        await using var adminDb = Contexto(cs, escritor);
        LoginResponse? sesion = null;
        var activar = CapturarErrorAsync(async () =>
            sesion = await Servicio(activacionDb, config).ActivarAsync(cuenta.Usuario, Aceptacion, null));
        Task<Exception?>? revocar = null;
        try
        {
            var sesionActivacion = await pausa.Detenida.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(IsolationLevel.Serializable, pausa.Aislamiento);
            revocar = CapturarErrorAsync(() => CambiarAdminAsync(adminDb));
            var sesionAdmin = await escritor.PrimeraEscritura.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await using var monitor = Contexto(cs);
            var reloj = Stopwatch.StartNew();
            var bloqueada = false;
            while (reloj.Elapsed < TimeSpan.FromSeconds(5))
            {
                var esperando = await monitor.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*) AS Value FROM sys.dm_exec_requests WHERE session_id={0} AND blocking_session_id={1} AND wait_type LIKE 'LCK_M_%'",
                    sesionAdmin, sesionActivacion).SingleAsync();
                if (esperando == 1) { bloqueada = true; break; }
                if (revocar.IsCompleted) break;
                await Task.Delay(40);
            }
            Assert.True(bloqueada, "La revocación debe esperar los bloqueos de la activación, sin modificar sus requisitos a mitad de la transacción.");
        }
        finally
        {
            pausa.Continuar.TrySetResult();
            await activar.WaitAsync(TimeSpan.FromSeconds(30));
            if (revocar is not null) await revocar.WaitAsync(TimeSpan.FromSeconds(30));
        }
        var errorActivar = await activar;
        var errorAdmin = await revocar!;
        Assert.Null(errorAdmin);
        // La reemisión puede llegar después de una inactivación/reset ya
        // confirmados y denegarse; nunca vuelve vigente una sesión antigua.
        Assert.True(errorActivar is null or InvalidOperationException,
            "La serialización no debe producir un error técnico al activar: " + errorActivar?.GetType().Name);
        await using var consulta = Contexto(cs);
        var usuario = await consulta.Usuarios.AsNoTracking().SingleAsync(u => u.IdUsuario == cuenta.Usuario);
        Assert.True(await consulta.Wallets.AnyAsync(w => w.IdPersona == cuenta.Persona));
        Assert.Equal(2, usuario.VersionSeguridad);
        if (operacion == "inactivar") Assert.Equal("INACTIVO", usuario.Estado);
        if (operacion == "clave") Assert.True(usuario.RequiereCambioClave);
        if (operacion == "rol") Assert.True(await consulta.UsuarioRoles.CountAsync(r => r.IdUsuario == cuenta.Usuario && r.Estado == "ACTIVO") > 1);
        Assert.False(await ValidadorVersionSeguridad.EsVigenteAsync(consulta, cuenta.Usuario, 0, default));
        Assert.False(await ValidadorVersionSeguridad.EsVigenteAsync(consulta, cuenta.Usuario, 1, default));
        if (sesion is not null && operacion != "rol")
            Assert.False(await ValidadorVersionSeguridad.EsVigenteAsync(consulta, cuenta.Usuario, Version(sesion.Token, config), default));
    }

    [Fact]
    public async Task EndpointIgnoraIdsAjenosDelJson_YUsaSoloElTitularDelToken()
    {
        if (!Conexion(out var cs)) return;
        await using var datos = new Semillero(cs);
        var config = Configuracion();
        var titular = await datos.CuentaAsync();
        var ajeno = await datos.CuentaAsync();
        await datos.ConfirmarCorreoAsync(titular);
        await datos.AutorizarBiometriaAsync(titular, config);
        await datos.AprobarIdentidadAsync(titular);
        var lugar = await datos.LugarAsync();
        var body = JsonSerializer.Deserialize<PreparacionDatosRequest>(JsonSerializer.Serialize(new
        {
            direccion = "Dirección del titular", idDepartamento = lugar.Departamento, idCiudad = lugar.Ciudad,
            idUsuario = ajeno.Usuario, idPersona = ajeno.Persona, estado = "ACTIVO", identidadVerificada = true
        }), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        await using var db = Contexto(cs);
        var controller = new PreparacionWalletController(Servicio(db, config))
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext
            { User = new(new ClaimsIdentity([new Claim("idUsuario", titular.Usuario.ToString(CultureInfo.InvariantCulture))], "test")) } }
        };
        Assert.IsType<OkObjectResult>(await controller.Datos(body, default));
        Assert.Equal("Dirección del titular", await db.Personas.Where(p => p.IdPersona == titular.Persona).Select(p => p.Direccion).SingleAsync());
        Assert.Null(await db.Personas.Where(p => p.IdPersona == ajeno.Persona).Select(p => p.Direccion).SingleAsync());
        controller.HttpContext.User = new(new ClaimsIdentity());
        Assert.IsType<UnauthorizedObjectResult>(await controller.Activar(Aceptacion, default));
        Assert.NotNull(typeof(PreparacionWalletController).GetCustomAttribute<AuthorizeAttribute>());
        foreach (var tipo in new[] { typeof(PreparacionDatosRequest), typeof(PreparacionLlaveRequest), typeof(ActivarWalletRequest) })
            Assert.DoesNotContain(tipo.GetProperties(), p => p.Name is "IdUsuario" or "IdPersona" or "IdWallet");
    }

    [Theory]
    [InlineData("XPAY_ALTA_WALLET_GUIADA_HABILITADA", "false")]
    [InlineData("XPAY_CONDICIONES_WALLET_VERSION", "0.9")]
    [InlineData("XPAY_CONDICIONES_WALLET_SHA256", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("XPAY_CONDICIONES_WALLET_URL", "https://otro.example.test/legal/wallet/v1.0/condiciones.pdf")]
    public async Task DocumentoOConfiguracionNoVigente_CierraElFlujoAntesDeConsultarDatos(string clave, string valor)
    {
        var config = Configuracion();
        config[clave] = valor;
        await using var db = new XpayDbContext(new DbContextOptionsBuilder<XpayDbContext>().Options);
        await Assert.ThrowsAsync<PreparacionWalletNoDisponibleException>(() => Servicio(db, config).ObtenerAsync(1));
        await Assert.ThrowsAsync<PreparacionWalletNoDisponibleException>(() => Servicio(db, config).ActivarAsync(1, Aceptacion, null));
    }

    internal sealed record Cuenta(long Usuario, long Persona, string Nombre, string Documento);
    internal sealed record Lugar(long Departamento, long Ciudad);
    private sealed record ResultadoConcurrente(Cuenta Cuenta, LoginResponse? Sesion, string? Error);
    private sealed record FotoDinero(int Movimientos, int Ledger, int Cupos, int Creditos, int Solicitudes, int Anticipos, int Vinculos, int Empresas);

    private static async Task<FotoDinero> FotoSinDineroAsync(XpayDbContext db, Cuenta cuenta) => new(
        await db.WalletMovimientos.CountAsync(), await db.LedgerTransacciones.CountAsync(),
        await db.CarteraCuposOrdinarios.CountAsync(c => c.IdUsuario == cuenta.Usuario),
        await db.CarteraUtilizaciones.CountAsync(c => c.IdUsuario == cuenta.Usuario),
        await db.CarteraSolicitudesCupo.CountAsync(c => c.IdUsuario == cuenta.Usuario),
        await db.LibranzaAnticipos.CountAsync(c => c.IdUsuario == cuenta.Usuario),
        await db.LibranzaEmpleadoUsuarios.CountAsync(c => c.IdUsuario == cuenta.Usuario),
        await db.LibranzaUsuariosEmpresa.CountAsync(c => c.IdUsuario == cuenta.Usuario));

    internal static IConfigurationRoot Configuracion()
    {
        var config = ConsentimientoBiometricoTests.Configuracion();
        config[CondicionesWalletPiloto.ClaveHabilitada] = "true";
        config[CondicionesWalletPiloto.Prefijo + "_VERSION"] = CondicionesWalletPiloto.Version;
        config[CondicionesWalletPiloto.Prefijo + "_FECHA"] = CondicionesWalletPiloto.Fecha;
        config[CondicionesWalletPiloto.Prefijo + "_SHA256"] = CondicionesWalletPiloto.Sha256;
        config[CondicionesWalletPiloto.Prefijo + "_URL"] = "https://app.xpaycol.com" + CondicionesWalletPiloto.Ruta;
        config[AltaUsuarioPilotoService.ClaveHabilitada] = "true";
        config[AltaUsuarioPilotoService.ClaveMaxUsuarios] = "50";
        config["Jwt:Key"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        config["Jwt:Issuer"] = "wallet-preparacion-local";
        config["Jwt:Audience"] = "wallet-preparacion-local";
        return config;
    }

    internal static PreparacionWalletService Servicio(XpayDbContext db, IConfiguration config) =>
        new(db, config, new(config, AmbienteXpayTests.Ambiente("Production")),
            new(db, config, AmbienteXpayTests.Ambiente("Production"), TimeProvider.System),
            new(db), new(db, config), TimeProvider.System);

    private static XpayDbContext Contexto(string cs, IInterceptor? interceptor = null)
    {
        var opciones = new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs);
        if (interceptor is not null) opciones.AddInterceptors(interceptor);
        return new(opciones.Options);
    }

    private static bool Conexion(out string cs)
    {
        cs = Environment.GetEnvironmentVariable("ConnectionStrings__XpayConnection") ?? "";
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        Assert.False(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) ||
                     !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")),
            "Se requiere la conexión SQL de pruebas para PreparacionWalletSqlTests.");
        return false;
    }

    private static int Version(string token, IConfiguration config)
    {
        var principal = new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!)),
            ValidateIssuer = true, ValidIssuer = config["Jwt:Issuer"], ValidateAudience = true, ValidAudience = config["Jwt:Audience"],
            ValidateLifetime = true, ClockSkew = TimeSpan.Zero
        }, out _);
        return int.Parse(principal.FindFirst(VersionSeguridadUsuario.Claim)!.Value, CultureInfo.InvariantCulture);
    }

    private static async Task<ResultadoConcurrente> ActivarConcurrenteAsync(string cs, IConfiguration config, Cuenta cuenta, AbrirJuntos inicio)
    {
        await using var db = Contexto(cs, inicio);
        try { return new(cuenta, await Servicio(db, config).ActivarAsync(cuenta.Usuario, Aceptacion, null), null); }
        catch (InvalidOperationException ex) { return new(cuenta, null, ex.Message); }
    }

    // Las dos conexiones llegan a BeginTransaction simultáneamente; no se usan
    // esperas arbitrarias ni se simula el bloqueo SQL del servicio.
    private sealed class AbrirJuntos(int participantes) : DbConnectionInterceptor
    {
        private int _abiertos;
        private readonly TaskCompletionSource _todos = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _abiertos) >= participantes) _todos.TrySetResult();
            await _todos.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        }
    }

    private sealed class RevocacionFallida : Exception;
    private sealed class FallarRevocacion : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("[version_seguridad] + 1", StringComparison.Ordinal))
                throw new RevocacionFallida();
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private static async Task<Exception?> CapturarErrorAsync(Func<Task> accion)
    {
        try { await accion(); return null; }
        catch (Exception ex) { return ex; }
    }

    private sealed class PausarAntesDeCrearWallet : DbCommandInterceptor
    {
        public TaskCompletionSource<int> Detenida { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continuar { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IsolationLevel Aislamiento { get; private set; }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO [wallets]", StringComparison.Ordinal))
            {
                Aislamiento = command.Transaction!.IsolationLevel;
                Detenida.TrySetResult(((SqlConnection)command.Connection!).ServerProcessId);
                await Continuar.Task.WaitAsync(TimeSpan.FromSeconds(25), cancellationToken);
            }
            return result;
        }
    }

    private sealed class ObservarEscritura : DbCommandInterceptor
    {
        public TaskCompletionSource<int> PrimeraEscritura { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private void Registrar(DbCommand command)
        {
            if (command.CommandText.Contains("INSERT ", StringComparison.Ordinal) || command.CommandText.Contains("UPDATE ", StringComparison.Ordinal))
                PrimeraEscritura.TrySetResult(((SqlConnection)command.Connection!).ServerProcessId);
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Registrar(command); return new(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Registrar(command); return new(result); }
    }

    internal sealed class Semillero(string cs) : IAsyncDisposable
    {
        public const string Capacidad = "PREPARACION_WALLET_TEST_CAPACIDAD";
        private readonly List<long> _personas = [];
        private readonly List<long> _usuarios = [];
        private readonly List<string> _nombres = [];
        private readonly string _sufijo = Guid.NewGuid().ToString("N")[..12];
        private int _secuencia;

        public async Task<Cuenta> CuentaAsync(bool publica = true, string rol = "USUARIO_FINAL")
        {
            await using var db = Contexto(cs);
            var n = ++_secuencia;
            var nombre = $"prep_{_sufijo}_{n}";
            var numero = "9" + Guid.NewGuid().ToString("N")[..15];
            var unidad = await db.Database.SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo={0}", "XPAY_COL").SingleAsync();
            var persona = new Persona { IdUnidadNegocio = unidad, Celular = "3000000000", Email = nombre + "@example.test", FechaCreacion = DateTime.UtcNow };
            db.Personas.Add(persona); await db.SaveChangesAsync(); _personas.Add(persona.IdPersona);
            var usuario = new Usuario { IdPersona = persona.IdPersona, NombreUsuario = nombre, PasswordHash = BCrypt.Net.BCrypt.HashPassword(Clave, 4), FechaCreacion = DateTime.UtcNow };
            db.Usuarios.Add(usuario); await db.SaveChangesAsync(); _usuarios.Add(usuario.IdUsuario); _nombres.Add(nombre);
            // Las migraciones antiguas de demo contienen asociaciones sin FK
            // reservadas al id_usuario 12 aunque el fixture CI no crea esa
            // cuenta. No se borran ni se atribuyen a un titular de esta prueba.
            if (await db.LibranzaUsuariosEmpresa.AnyAsync(a => a.IdUsuario == usuario.IdUsuario)
                || await db.LibranzaEmpleadoUsuarios.AnyAsync(a => a.IdUsuario == usuario.IdUsuario))
                return await CuentaAsync(publica, rol);
            var idRol = await db.Roles.Where(r => r.Codigo == rol && r.Estado == "ACTIVO").Select(r => r.IdRol).SingleAsync();
            db.UsuarioRoles.Add(new() { IdUsuario = usuario.IdUsuario, IdRol = idRol, Estado = "ACTIVO", FechaAsignacion = DateTime.UtcNow });
            if (publica) db.Auditorias.Add(new() { IdUsuario = usuario.IdUsuario, IdPersona = persona.IdPersona, Modulo = "USUARIOS",
                Accion = RegistroPublicoService.AccionAceptarTerminos, Entidad = "usuarios", IdEntidad = usuario.IdUsuario.ToString(CultureInfo.InvariantCulture),
                Resultado = "EXITOSO", ValorNuevo = "{\"canal\":\"WEB_AUTORREGISTRO\"}", FechaEvento = DateTime.UtcNow });
            await db.SaveChangesAsync();
            return new(usuario.IdUsuario, persona.IdPersona, nombre, numero);
        }

        public async Task ConfirmarCorreoAsync(Cuenta cuenta)
        {
            await using var db = Contexto(cs);
            await db.Usuarios.Where(u => u.IdUsuario == cuenta.Usuario).ExecuteUpdateAsync(s => s.SetProperty(u => u.EmailVerificado, true));
        }

        public async Task AutorizarBiometriaAsync(Cuenta cuenta, IConfiguration config)
        {
            await using var db = Contexto(cs);
            var respuesta = await new ConsentimientoBiometricoService(db, config, AmbienteXpayTests.Ambiente("Production"), TimeProvider.System)
                .AutorizarAsync(cuenta.Usuario, new(true, "1.2"), "203.0.113.41");
            Assert.True(respuesta.Aceptado);
        }

        public async Task AprobarIdentidadAsync(Cuenta cuenta)
        {
            await using var db = Contexto(cs);
            await db.Usuarios.Where(u => u.IdUsuario == cuenta.Usuario).ExecuteUpdateAsync(s => s.SetProperty(u => u.EstadoKycActual, "APROBADO"));
            await db.Personas.Where(p => p.IdPersona == cuenta.Persona).ExecuteUpdateAsync(s => s
                .SetProperty(p => p.IdentidadVerificada, true).SetProperty(p => p.IdentidadVerificadaProveedor, "VERIFF")
                .SetProperty(p => p.IdentidadVerificadaFecha, DateTime.UtcNow).SetProperty(p => p.TipoDocumento, "PASAPORTE")
                .SetProperty(p => p.NumeroDocumento, cuenta.Documento).SetProperty(p => p.NumeroDocumentoVerificado, cuenta.Documento)
                .SetProperty(p => p.TipoDocumentoVeriffRaw, "PASSPORT"));
            db.KycVerificaciones.Add(new() { IdUsuario = cuenta.Usuario, IdPersona = cuenta.Persona,
                Proveedor = "VERIFF", EstadoKyc = "APROBADO", Decision = "approved", EsActual = true,
                SessionId = "sesion-sintetica-" + Guid.NewGuid().ToString("N"), FechaCreacion = DateTime.UtcNow,
                FechaDecision = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        public async Task<Lugar> LugarAsync()
        {
            await using var db = Contexto(cs);
            return await (from c in db.CatalogoCiudades join d in db.CatalogoDepartamentos on c.IdDepartamento equals d.IdDepartamento
                join p in db.CatalogoPaises on d.IdPais equals p.IdPais
                where c.Estado == "ACTIVO" && d.Estado == "ACTIVO" && p.Estado == "ACTIVO" && p.Codigo == "COL"
                orderby c.IdCiudad select new Lugar(d.IdDepartamento, c.IdCiudad)).FirstAsync();
        }

        public async Task<Cuenta> ListaAsync(IConfiguration config)
        {
            var cuenta = await CuentaAsync();
            await ConfirmarCorreoAsync(cuenta);
            await AutorizarBiometriaAsync(cuenta, config);
            await AprobarIdentidadAsync(cuenta);
            var lugar = await LugarAsync();
            await using var db = Contexto(cs);
            var servicio = Servicio(db, config);
            await servicio.GuardarDatosAsync(cuenta.Usuario, new("Calle sintética 12", lugar.Departamento, lugar.Ciudad));
            await servicio.GuardarLlaveAsync(cuenta.Usuario, new(true, null, null, false));
            await new PinTransaccionalService(db).CrearAsync(cuenta.Usuario, new() { ClaveActual = Clave, Pin = Pin, ConfirmacionPin = Pin });
            Assert.Equal("ACTIVAR", (await servicio.ObtenerAsync(cuenta.Usuario)).SiguientePaso);
            return cuenta;
        }

        public async Task PersonaDuplicadaAsync(Cuenta cuenta)
        {
            await using var db = Contexto(cs);
            var unidad = await db.Personas.Where(p => p.IdPersona == cuenta.Persona).Select(p => p.IdUnidadNegocio).SingleAsync();
            var persona = new Persona { IdUnidadNegocio = unidad, Celular = "3000000000", FechaCreacion = DateTime.UtcNow,
                TipoDocumentoVeriffRaw = "PASSPORT", NumeroDocumentoVerificado = cuenta.Documento };
            db.Personas.Add(persona); await db.SaveChangesAsync(); _personas.Add(persona.IdPersona);
        }

        public async Task OcuparHastaAsync(int total, long usuario)
        {
            await using var db = Contexto(cs);
            var previas = await PreparacionWalletService.ContarPlazasAsync(db);
            Assert.True(previas <= total, "La base de pruebas ya excede la capacidad prevista por el caso.");
            for (var i = previas; i < total; i++) db.Auditorias.Add(new() {
                IdUsuario = usuario, Modulo = "USUARIOS", Entidad = "usuarios", IdEntidad = $"fixture-{_sufijo}-{i}",
                Accion = i % 2 == 0 ? AltaUsuarioPilotoService.AccionAuditoria : PreparacionWalletService.AccionActivar,
                Resultado = "EXITOSO", FechaEvento = DateTime.UtcNow, Observacion = Capacidad });
            await db.SaveChangesAsync();
        }

        public AltaUsuarioPilotoRequest NuevaSolicitudManual()
        {
            var nombre = "prep_manual_" + _sufijo;
            _nombres.Add(nombre);
            return new() { NombreUsuario = nombre, Email = nombre + "@example.test", Celular = "3000000000",
                VersionTerminos = "1.2", VersionPoliticaDatos = "1.2", VersionAutorizacionKyc = "1.2",
                FechaAceptacionUtc = DateTime.UtcNow.AddMinutes(-5), ReferenciaEvidencia = "ACTA-PRUEBA-" + _sufijo };
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = Contexto(cs);
            var manuales = await db.Usuarios.Where(u => _nombres.Contains(u.NombreUsuario)).Select(u => new { u.IdUsuario, u.IdPersona }).ToListAsync();
            var usuarios = _usuarios.Concat(manuales.Select(u => u.IdUsuario)).Distinct().ToArray();
            var personas = _personas.Concat(manuales.Select(u => u.IdPersona)).Distinct().ToArray();
            var wallets = await db.Wallets.Where(w => w.IdPersona != null && personas.Contains(w.IdPersona.Value)).Select(w => w.IdWallet).ToArrayAsync();
            await db.PassportBrebLlaves.Where(l => l.IdUsuario != null && usuarios.Contains(l.IdUsuario.Value)).ExecuteDeleteAsync();
            await db.WalletPreparaciones.Where(p => usuarios.Contains(p.IdUsuario)).ExecuteDeleteAsync();
            await db.WalletSaldos.Where(s => wallets.Contains(s.IdWallet)).ExecuteDeleteAsync();
            await db.Wallets.Where(w => wallets.Contains(w.IdWallet)).ExecuteDeleteAsync();
            await db.PinesTransaccionales.Where(p => usuarios.Contains(p.IdUsuario)).ExecuteDeleteAsync();
            await db.KycVerificaciones.Where(k => usuarios.Contains(k.IdUsuario)).ExecuteDeleteAsync();
            await db.Auditorias.Where(a => a.IdUsuario != null && usuarios.Contains(a.IdUsuario.Value)).ExecuteDeleteAsync();
            await db.UsuarioRoles.Where(r => usuarios.Contains(r.IdUsuario)).ExecuteDeleteAsync();
            await db.Usuarios.Where(u => usuarios.Contains(u.IdUsuario)).ExecuteDeleteAsync();
            await db.Personas.Where(p => personas.Contains(p.IdPersona)).ExecuteDeleteAsync();
        }
    }
}
