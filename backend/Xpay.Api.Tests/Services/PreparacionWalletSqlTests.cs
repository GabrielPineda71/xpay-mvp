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
    internal static readonly ActivarWalletRequest Aceptacion = new();
    private static readonly ActivarWalletRequest AceptacionActualizada = new(true, PreparacionWalletConfiguracion.Version, true, PreparacionWalletConfiguracion.Version);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Progresion_ExigeCadaPaso_YActivaSoloWalletConSaldoCero(bool aceptadasEnRegistro)
    {
        if (!Conexion(out var cs)) return;
        await using var datos = new Semillero(cs);
        var cuenta = await datos.CuentaAsync(aVigente: aceptadasEnRegistro, bVigente: aceptadasEnRegistro);
        var config = Configuracion();
        await using var db = Contexto(cs);
        var servicio = Servicio(db, config);
        Assert.Equal("CORREO", (await servicio.ObtenerAsync(cuenta.Usuario)).SiguientePaso);
        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.ExigirAccesoPinAsync(cuenta.Usuario));
        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.ActivarAsync(cuenta.Usuario, Aceptacion, null));
        Assert.True(await RegistroPublicoService.EsCuentaLimitadaAsync(db, cuenta.Usuario));
        await datos.ConfirmarCorreoAsync(cuenta);
        Assert.Equal("IDENTIDAD", (await servicio.ObtenerAsync(cuenta.Usuario)).SiguientePaso);
        await datos.AutorizarBiometriaAsync(cuenta, config);
        Assert.Equal("IDENTIDAD", (await servicio.ObtenerAsync(cuenta.Usuario)).SiguientePaso);
        await datos.AprobarIdentidadAsync(cuenta);
        Assert.Equal("DATOS", (await servicio.ObtenerAsync(cuenta.Usuario)).SiguientePaso);
        Assert.False(await db.Wallets.AnyAsync(w => w.IdPersona == cuenta.Persona));
        var lugar = await datos.LugarAsync();
        var conDatos = await servicio.GuardarDatosAsync(cuenta.Usuario, new("Calle sintética 12", lugar.Departamento, lugar.Ciudad));
        Assert.Equal("PIN", conDatos.SiguientePaso);
        Assert.Equal(lugar.Ciudad, conDatos.IdCiudad);
        Assert.Equal(aceptadasEnRegistro, conDatos.Terminos.Aceptados);
        Assert.Equal(aceptadasEnRegistro, conDatos.PoliticaDatos.Aceptados);
        Assert.Equal(PreparacionWalletConfiguracion.Version, conDatos.Terminos.Version);
        Assert.False(await db.WalletPreparaciones.AnyAsync(p => p.IdUsuario == cuenta.Usuario));
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
        if (!aceptadasEnRegistro)
            await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.ActivarAsync(cuenta.Usuario, Aceptacion, null));
        var antes = await FotoSinDineroAsync(db, cuenta);
        var evidenciasAntes = await db.Auditorias.AsNoTracking().Where(a => a.IdUsuario == cuenta.Usuario &&
            (a.Accion == RegistroPublicoService.AccionAceptarTerminos || a.Accion == RegistroPublicoService.AccionAutorizarDatos))
            .Select(a => new { a.IdAuditoria, a.ValorNuevo }).ToListAsync();
        var sesionPrevia = await new AuthService(db, config).LoginAsync(new() { Usuario = cuenta.Nombre, Password = Clave });
        Assert.True(sesionPrevia.RequierePerfil);
        Assert.Equal(0, Version(sesionPrevia.Token, config));
        var respuesta = await servicio.ActivarAsync(cuenta.Usuario,
            aceptadasEnRegistro ? Aceptacion : AceptacionActualizada, "203.0.113.41");
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
        Assert.Equal(PreparacionWalletConfiguracion.TerminosSha256, preparacion.TerminosSha256);
        Assert.Equal(PreparacionWalletConfiguracion.PoliticaDatosSha256, preparacion.PoliticaDatosSha256);
        Assert.Equal(PreparacionWalletService.CriteriosVersion, preparacion.CriteriosVersion);
        var evidencia = await db.Auditorias.AsNoTracking().SingleAsync(a => a.IdUsuario == cuenta.Usuario && a.Accion == PreparacionWalletService.AccionActivar);
        Assert.Equal("203.0.113.41", evidencia.Ip);
        using (var json = JsonDocument.Parse(evidencia.ValorNuevo!))
        {
            Assert.Equal(PreparacionWalletConfiguracion.TerminosSha256, json.RootElement.GetProperty("terminosSha256").GetString());
            Assert.Equal(PreparacionWalletConfiguracion.PoliticaDatosSha256, json.RootElement.GetProperty("politicaDatosSha256").GetString());
            Assert.Equal(6, json.RootElement.GetProperty("longitudPin").GetInt32());
            Assert.False(json.RootElement.TryGetProperty("condicionesSha256", out _));
        }
        var usuario = await db.Usuarios.AsNoTracking().SingleAsync(u => u.IdUsuario == cuenta.Usuario);
        Assert.False(usuario.CelularVerificado);
        Assert.False(await db.PassportBrebLlaves.AnyAsync(l => l.IdUsuario == cuenta.Usuario));
        var nuevasAb = await db.Auditorias.AsNoTracking().Where(a => a.IdUsuario == cuenta.Usuario &&
            (a.Accion == RegistroPublicoService.AccionAceptarTerminos || a.Accion == RegistroPublicoService.AccionAutorizarDatos)).ToListAsync();
        Assert.Equal(aceptadasEnRegistro ? 2 : 4, nuevasAb.Count);
        foreach (var previa in evidenciasAntes)
            Assert.Equal(previa.ValorNuevo, nuevasAb.Single(a => a.IdAuditoria == previa.IdAuditoria).ValorNuevo);
        var repetida = await servicio.ActivarAsync(cuenta.Usuario, new(), null);
        Assert.Equal(1, Version(repetida.Token, config));
        Assert.Single(await db.Wallets.Where(w => w.IdPersona == cuenta.Persona).ToListAsync());
        Assert.Single(await db.Auditorias.Where(a => a.IdUsuario == cuenta.Usuario && a.Accion == PreparacionWalletService.AccionActivar).ToListAsync());
        Assert.Equal(nuevasAb.Count, await db.Auditorias.CountAsync(a => a.IdUsuario == cuenta.Usuario &&
            (a.Accion == RegistroPublicoService.AccionAceptarTerminos || a.Accion == RegistroPublicoService.AccionAutorizarDatos)));
        Assert.Equal(antes, await FotoSinDineroAsync(db, cuenta));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SoloSolicitaLasCategoriasAbPendientes_SinOtraAceptacion(bool aVigente, bool bVigente)
    {
        if (!Conexion(out var cs)) return;
        await using var datos = new Semillero(cs);
        var config = Configuracion();
        var cuenta = await datos.ListaAsync(config, aVigente, bVigente);
        await using var db = Contexto(cs);
        var servicio = Servicio(db, config);
        var estado = await servicio.ObtenerAsync(cuenta.Usuario);
        Assert.Equal(aVigente, estado.Terminos.Aceptados);
        Assert.Equal(bVigente, estado.PoliticaDatos.Aceptados);
        var vacio = JsonSerializer.Deserialize<ActivarWalletRequest>("{}", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.False(vacio.AceptaTerminos);
        Assert.False(vacio.AutorizaDatos);
        Assert.Null(vacio.VersionTerminos);
        Assert.Null(vacio.VersionPoliticaDatos);
        if (!aVigente || !bVigente)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.ActivarAsync(cuenta.Usuario, vacio, null));
            Assert.False(await db.WalletPreparaciones.AnyAsync(p => p.IdUsuario == cuenta.Usuario));
            Assert.False(await db.Wallets.AnyAsync(w => w.IdPersona == cuenta.Persona));
        }
        var request = new ActivarWalletRequest(!aVigente, aVigente ? null : "1.3", !bVigente, bVigente ? null : "1.3");
        await servicio.ActivarAsync(cuenta.Usuario, request, null);
        Assert.Equal(aVigente ? 1 : 2, await db.Auditorias.CountAsync(a => a.IdUsuario == cuenta.Usuario && a.Accion == RegistroPublicoService.AccionAceptarTerminos));
        Assert.Equal(bVigente ? 1 : 2, await db.Auditorias.CountAsync(a => a.IdUsuario == cuenta.Usuario && a.Accion == RegistroPublicoService.AccionAutorizarDatos));
        Assert.True(await AceptacionesRegistroWallet.VigenteAsync(db, cuenta.Usuario, cuenta.Persona, true));
        Assert.True(await AceptacionesRegistroWallet.VigenteAsync(db, cuenta.Usuario, cuenta.Persona, false));
    }

    public static TheoryData<bool, string> EvidenciasAbInvalidas()
    {
        var casos = new TheoryData<bool, string>();
        foreach (var terminos in new[] { true, false })
        foreach (var problema in new[] { "version", "sha", "canal", "tipo", "falta_sha", "falta_version", "falta_canal",
                     "actor", "persona", "entidad", "id_entidad", "modulo", "json" }) casos.Add(terminos, problema);
        return casos;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CuentasConAb12_ExigenAmbasManifestacionesExpresas13(bool aceptaTerminos, bool autorizaDatos)
    {
        if (!Conexion(out var cs)) return;
        await using var datos = new Semillero(cs);
        var config = Configuracion();
        var cuenta = await datos.ListaAsync(config, aVigente: false, bVigente: false);
        await using var db = Contexto(cs);
        var request = new ActivarWalletRequest(aceptaTerminos, aceptaTerminos ? "1.3" : null,
            autorizaDatos, autorizaDatos ? "1.3" : null);
        if (aceptaTerminos && autorizaDatos)
        {
            await Servicio(db, config).ActivarAsync(cuenta.Usuario, request, null);
            Assert.True(await db.Wallets.AnyAsync(w => w.IdPersona == cuenta.Persona));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Servicio(db, config).ActivarAsync(cuenta.Usuario, request, null));
            Assert.False(await db.Wallets.AnyAsync(w => w.IdPersona == cuenta.Persona));
            Assert.Equal(2, await db.Auditorias.CountAsync(a => a.IdUsuario == cuenta.Usuario &&
                (a.Accion == RegistroPublicoService.AccionAceptarTerminos || a.Accion == RegistroPublicoService.AccionAutorizarDatos)));
            Assert.False(await AceptacionesRegistroWallet.VigenteAsync(db, cuenta.Usuario, cuenta.Persona, true));
            Assert.False(await AceptacionesRegistroWallet.VigenteAsync(db, cuenta.Usuario, cuenta.Persona, false));
        }
    }

    [Theory]
    [MemberData(nameof(EvidenciasAbInvalidas))]
    public async Task EvidenciaAjenaIncompletaODeOtroDocumento_NoEquivaleAAceptacion(bool terminos, string problema)
    {
        if (!Conexion(out var cs)) return;
        await using var datos = new Semillero(cs);
        var config = Configuracion();
        var cuenta = await datos.ListaAsync(config, aVigente: !terminos, bVigente: terminos);
        var otra = await datos.CuentaAsync(publica: false);
        var evidencia = EvidenciaRegistro(cuenta, terminos, "1.3");
        var payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(evidencia.ValorNuevo!)!;
        switch (problema)
        {
            case "version": payload["version"] = "1.2"; break;
            case "sha": payload["sha256"] = new string('0', 64); break;
            case "canal": payload["canal"] = "ADMIN"; break;
            case "tipo": payload["tipoDocumento"] = "OTRO_DOCUMENTO"; break;
            case "falta_sha": payload.Remove("sha256"); break;
            case "falta_version": payload.Remove("version"); break;
            case "falta_canal": payload.Remove("canal"); break;
            case "actor": evidencia.IdUsuario = otra.Usuario; break;
            case "persona": evidencia.IdPersona = otra.Persona; break;
            case "entidad": evidencia.Entidad = "otra_entidad"; break;
            case "id_entidad": evidencia.IdEntidad = otra.Usuario.ToString(CultureInfo.InvariantCulture); break;
            case "modulo": evidencia.Modulo = "OTRO_MODULO"; break;
        }
        evidencia.ValorNuevo = problema == "json" ? "{" : JsonSerializer.Serialize(payload);
        await using var db = Contexto(cs);
        db.Auditorias.Add(evidencia); await db.SaveChangesAsync();
        var estado = await Servicio(db, config).ObtenerAsync(cuenta.Usuario);
        Assert.False(terminos ? estado.Terminos.Aceptados : estado.PoliticaDatos.Aceptados);
        Assert.True(terminos ? estado.PoliticaDatos.Aceptados : estado.Terminos.Aceptados);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Servicio(db, config).ActivarAsync(cuenta.Usuario, new(), null));
        Assert.False(await db.Wallets.AnyAsync(w => w.IdPersona == cuenta.Persona));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UltimaRevocacionAb_NoRescataAceptacionAnterior(bool terminos)
    {
        if (!Conexion(out var cs)) return;
        await using var datos = new Semillero(cs);
        var config = Configuracion();
        var cuenta = await datos.ListaAsync(config);
        await using var db = Contexto(cs);
        var ultima = EvidenciaRegistro(cuenta, terminos, "1.3");
        ultima.Resultado = "REVOCADO";
        db.Auditorias.Add(ultima); await db.SaveChangesAsync();
        Assert.False(await AceptacionesRegistroWallet.VigenteAsync(db, cuenta.Usuario, cuenta.Persona, terminos));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Servicio(db, config).ActivarAsync(cuenta.Usuario, new(), null));
        Assert.False(await db.Wallets.AnyAsync(w => w.IdPersona == cuenta.Persona));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task VersionExpresaEquivocada_NoSeAceptaAunqueExistaEvidenciaVigente(bool terminos)
    {
        if (!Conexion(out var cs)) return;
        await using var datos = new Semillero(cs);
        var config = Configuracion();
        var cuenta = await datos.ListaAsync(config);
        await using var db = Contexto(cs);
        foreach (var version in new string?[] { null, "1.2", "1.3 " })
        {
            var request = terminos ? new ActivarWalletRequest(true, version) : new(false, null, true, version);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Servicio(db, config).ActivarAsync(cuenta.Usuario, request, null));
        }
        Assert.False(await db.Wallets.AnyAsync(w => w.IdPersona == cuenta.Persona));
    }

    [Fact]
    public async Task C12VigenteBajoConfiguracion13_SeConservaSinNuevaAceptacionNiCaptura()
    {
        if (!Conexion(out var cs)) return;
        await using var datos = new Semillero(cs);
        var config = Configuracion();
        var cuenta = await datos.ListaAsync(config);
        await using var db = Contexto(cs);
        var antes = await db.Auditorias.AsNoTracking().SingleAsync(a => a.IdUsuario == cuenta.Usuario && a.Accion == ConsentimientoBiometricoService.AccionAutorizar);
        var sesiones = await db.KycVerificaciones.CountAsync(k => k.IdUsuario == cuenta.Usuario);
        Assert.Contains("\"version\":\"1.2\"", antes.ValorNuevo);
        config[ConsentimientoBiometricoService.PrefijoDocumento + "_VERSION"] = "1.3";
        config[ConsentimientoBiometricoService.PrefijoDocumento + "_FECHA"] = PreparacionWalletConfiguracion.Fecha;
        config[ConsentimientoBiometricoService.PrefijoDocumento + "_SHA256"] = PreparacionWalletConfiguracion.AutorizacionSha256;
        config[ConsentimientoBiometricoService.PrefijoDocumento + "_URL"] = "https://app.xpaycol.com/legal/v1.3/autorizaciones.pdf";
        Assert.True(await new ConsentimientoBiometricoService(db, config, AmbienteXpayTests.Ambiente("Production"), TimeProvider.System)
            .TieneConsentimientoVigenteAsync(cuenta.Usuario));
        await Servicio(db, config).ActivarAsync(cuenta.Usuario, new(), null);
        var despues = await db.Auditorias.AsNoTracking().SingleAsync(a => a.IdUsuario == cuenta.Usuario && a.Accion == ConsentimientoBiometricoService.AccionAutorizar);
        Assert.Equal(antes.IdAuditoria, despues.IdAuditoria);
        Assert.Equal(antes.ValorNuevo, despues.ValorNuevo);
        Assert.Equal(sesiones, await db.KycVerificaciones.CountAsync(k => k.IdUsuario == cuenta.Usuario));
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
        Assert.Contains("activación de Wallet no está disponible", perdedor.Error);
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
        var error = Assert.Single(resultados, r => r.Error is not null).Error;
        Assert.True(error == AltaUsuarioPilotoService.MensajeMaximoAlcanzado || error?.Contains("activación de Wallet no está disponible", StringComparison.Ordinal) == true);
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
        var cuenta = await datos.ListaAsync(config, aVigente: false, bVigente: false);
        int plazasAntes;
        await using (var consulta = Contexto(cs)) plazasAntes = await PreparacionWalletService.ContarPlazasAsync(consulta);
        await using (var falla = Contexto(cs, new FallarRevocacion()))
            await Assert.ThrowsAsync<RevocacionFallida>(() => Servicio(falla, config).ActivarAsync(cuenta.Usuario, AceptacionActualizada, null));
        await using var db = Contexto(cs);
        Assert.False(await db.Wallets.AnyAsync(w => w.IdPersona == cuenta.Persona));
        Assert.False(await db.Auditorias.AnyAsync(a => a.IdUsuario == cuenta.Usuario && a.Accion == PreparacionWalletService.AccionActivar));
        Assert.Equal(plazasAntes, await PreparacionWalletService.ContarPlazasAsync(db));
        Assert.Equal(0, await db.Usuarios.Where(u => u.IdUsuario == cuenta.Usuario).Select(u => u.VersionSeguridad).SingleAsync());
        Assert.False(await db.WalletPreparaciones.AnyAsync(p => p.IdUsuario == cuenta.Usuario));
        Assert.False(await AceptacionesRegistroWallet.VigenteAsync(db, cuenta.Usuario, cuenta.Persona, true));
        Assert.False(await AceptacionesRegistroWallet.VigenteAsync(db, cuenta.Usuario, cuenta.Persona, false));
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
        foreach (var tipo in new[] { typeof(PreparacionDatosRequest), typeof(ActivarWalletRequest) })
            Assert.DoesNotContain(tipo.GetProperties(), p => p.Name is "IdUsuario" or "IdPersona" or "IdWallet");
        Assert.Null(typeof(PreparacionWalletController).GetMethod("Llave"));
        Assert.DoesNotContain(typeof(PreparacionWalletResponse).GetProperties(), p => p.Name is "Llave" or "Condiciones");
    }

    [Theory]
    [InlineData("XPAY_ALTA_WALLET_GUIADA_HABILITADA", "false")]
    [InlineData("XPAY_REGISTRO_PUBLICO_TERMINOS_VERSION", "0.9")]
    [InlineData("XPAY_REGISTRO_PUBLICO_TERMINOS_SHA256", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("XPAY_REGISTRO_PUBLICO_POLITICA_DATOS_URL", "https://otro.example.test/legal/v1.3/politica.pdf")]
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

    private static Auditoria EvidenciaRegistro(Cuenta cuenta, bool terminos, string version)
    {
        var sha = version == "1.3"
            ? terminos ? PreparacionWalletConfiguracion.TerminosSha256 : PreparacionWalletConfiguracion.PoliticaDatosSha256
            : terminos ? "f5d7450394b3c64284dfdf0d8461341ad2d59f5ac9e907d10f39de245d9fb680"
                       : "6dba8d16e11168e073b13047ac01fd0df06bc4cb2b57aeec904144add442e534";
        return new() { IdUsuario = cuenta.Usuario, IdPersona = cuenta.Persona, Modulo = "USUARIOS",
            Accion = terminos ? RegistroPublicoService.AccionAceptarTerminos : RegistroPublicoService.AccionAutorizarDatos,
            Entidad = "usuarios", IdEntidad = cuenta.Usuario.ToString(CultureInfo.InvariantCulture),
            Resultado = "EXITOSO", FechaEvento = DateTime.UtcNow,
            ValorNuevo = JsonSerializer.Serialize(new {
                tipoDocumento = terminos ? "TERMINOS_Y_CONDICIONES" : "POLITICA_TRATAMIENTO_DATOS",
                version, sha256 = sha, canal = "WEB_AUTORREGISTRO"
            }) };
    }

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
        config[PreparacionWalletConfiguracion.ClaveHabilitada] = "true";
        foreach (var (prefijo, archivo, sha) in new[] {
            (DocumentosRegistroPublico.PrefijoTerminos, "terminos.pdf", PreparacionWalletConfiguracion.TerminosSha256),
            (DocumentosRegistroPublico.PrefijoPoliticaDatos, "politica.pdf", PreparacionWalletConfiguracion.PoliticaDatosSha256) })
        {
            config[prefijo + "_VERSION"] = PreparacionWalletConfiguracion.Version;
            config[prefijo + "_FECHA"] = PreparacionWalletConfiguracion.Fecha;
            config[prefijo + "_SHA256"] = sha;
            config[prefijo + "_URL"] = "https://app.xpaycol.com/legal/v1.3/" + archivo;
        }
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

        public async Task<Cuenta> CuentaAsync(bool publica = true, string rol = "USUARIO_FINAL", bool aVigente = false, bool bVigente = false)
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
                return await CuentaAsync(publica, rol, aVigente, bVigente);
            var idRol = await db.Roles.Where(r => r.Codigo == rol && r.Estado == "ACTIVO").Select(r => r.IdRol).SingleAsync();
            db.UsuarioRoles.Add(new() { IdUsuario = usuario.IdUsuario, IdRol = idRol, Estado = "ACTIVO", FechaAsignacion = DateTime.UtcNow });
            if (publica)
            {
                var cuenta = new Cuenta(usuario.IdUsuario, persona.IdPersona, nombre, numero);
                db.Auditorias.Add(EvidenciaRegistro(cuenta, true, aVigente ? "1.3" : "1.2"));
                db.Auditorias.Add(EvidenciaRegistro(cuenta, false, bVigente ? "1.3" : "1.2"));
            }
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
                .AutorizarAsync(cuenta.Usuario, new(true, config[ConsentimientoBiometricoService.PrefijoDocumento + "_VERSION"]), "203.0.113.41");
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

        public async Task<Cuenta> ListaAsync(IConfiguration config, bool aVigente = true, bool bVigente = true)
        {
            var cuenta = await CuentaAsync(aVigente: aVigente, bVigente: bVigente);
            await ConfirmarCorreoAsync(cuenta);
            await AutorizarBiometriaAsync(cuenta, config);
            await AprobarIdentidadAsync(cuenta);
            var lugar = await LugarAsync();
            await using var db = Contexto(cs);
            var servicio = Servicio(db, config);
            await servicio.GuardarDatosAsync(cuenta.Usuario, new("Calle sintética 12", lugar.Departamento, lugar.Ciudad));
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
                // Capacidad por titular distinto; el actor administrador no
                // es el usuario dado de alta. IDs sintéticos sin datos reales.
                IdUsuario = usuario, Modulo = "USUARIOS", Entidad = "usuarios", IdEntidad = (long.MaxValue-i).ToString(CultureInfo.InvariantCulture),
                Accion = AltaUsuarioPilotoService.AccionAuditoria,
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
