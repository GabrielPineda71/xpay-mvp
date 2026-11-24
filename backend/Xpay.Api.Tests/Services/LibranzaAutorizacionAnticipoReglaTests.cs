using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Xpay.Api.Controllers;
using Xpay.Api.DTOs;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

public sealed class LibranzaAutorizacionAnticipoReglaTests
{
    private static readonly DateTime Ahora = new(2026, 11, 24, 20, 0, 0, DateTimeKind.Utc);
    private static LibranzaAutorizacionAnticipoService.Datos Datos(bool publico = true)
    {
        var revision = LibranzaVinculacionReglaTests.Datos();
        var vinculo = new LibranzaEmpleadoUsuario { IdEmpleadoUsuario = 7, IdEmpleado = 1, IdUsuario = 2,
            IdWallet = 6, Estado = "ACTIVO", CreatedAt = Ahora.AddDays(-1) };
        revision.Vinculos.Add(vinculo);
        return new() { Vinculo = vinculo, Revision = revision, OrigenPublico = publico };
    }
    private static void Autorizar(LibranzaAutorizacionAnticipoService.Datos d)
    {
        d.TieneHistorial = true;
        d.AutorizacionAuditada = true;
        d.Autorizacion = new() { IdEmpleadoUsuario = d.Vinculo.IdEmpleadoUsuario, IdUsuario = d.Vinculo.IdUsuario,
            IdEmpleado = d.Vinculo.IdEmpleado, IdWallet = d.Vinculo.IdWallet, IdConvenio = d.Revision.Empleado!.IdConvenio,
            Estado = "AUTORIZADO", Revision = 1, HuellaContexto = LibranzaAutorizacionAnticipoService.Contexto(d),
            FechaAutorizacion = DateTime.SpecifyKind(Ahora, DateTimeKind.Unspecified) };
    }

    [Fact]
    public void Registro_publico_vinculado_no_se_autoriza_por_identidad_empleo_o_Wallet()
    {
        var d = Datos(); var respuesta = LibranzaAutorizacionAnticipoService.Presentar(d, Ahora);
        Assert.Equal("PENDIENTE", respuesta.Estado); Assert.True(respuesta.PuedeAutorizar);
        Assert.False(LibranzaAutorizacionAnticipoService.AutorizadaVigente(d, Ahora));
        var json = JsonSerializer.Serialize(respuesta);
        Assert.DoesNotContain(d.Revision.Persona!.Email!, json); Assert.DoesNotContain(d.Revision.Persona.NumeroDocumento!, json);
        Assert.DoesNotContain("Salario", json); Assert.DoesNotContain("Cupo", json);
    }

    [Fact]
    public void Decision_explicita_da_solo_permiso_y_marca_fechas_UTC()
    {
        var d = Datos(); Autorizar(d);
        var r = LibranzaAutorizacionAnticipoService.Presentar(d, Ahora);
        Assert.Equal("AUTORIZADO", r.Estado); Assert.True(r.PuedeRevocar); Assert.False(r.PuedeAutorizar);
        Assert.True(LibranzaAutorizacionAnticipoService.AutorizadaVigente(d, Ahora));
        Assert.Equal(DateTimeKind.Utc, r.FechaAutorizacion!.Value.Kind);
    }

    [Theory]
    [InlineData("wallet")]
    [InlineData("usuario")]
    [InlineData("empleado")]
    [InlineData("convenio")]
    [InlineData("identidad")]
    [InlineData("correo")]
    [InlineData("vinculo-inactivo")]
    [InlineData("nuevo-vinculo")]
    public void Snapshot_cambiado_no_reutiliza_autorizacion(string campo)
    {
        var d = Datos(); Autorizar(d); var foto = LibranzaAutorizacionAnticipoService.Huella(d);
        switch (campo)
        {
            case "wallet": d.Vinculo.IdWallet = 99; break;
            case "usuario": d.Vinculo.IdUsuario = 99; break;
            case "empleado": d.Vinculo.IdEmpleado = 99; break;
            case "convenio": d.Revision.Empleado!.IdConvenio = 99; break;
            case "identidad": d.Revision.Kyc[0].EstadoKyc = "RECHAZADO"; break;
            case "correo": d.Revision.Usuario!.EmailVerificado = false; break;
            case "vinculo-inactivo": d.Vinculo.Estado = "INACTIVO"; break;
            case "nuevo-vinculo": d.Vinculo.IdEmpleadoUsuario = 99; break;
        }
        Assert.False(LibranzaAutorizacionAnticipoService.AutorizadaVigente(d, Ahora));
        Assert.NotEqual(foto, LibranzaAutorizacionAnticipoService.Huella(d));
    }

    [Fact]
    public void Revocacion_cambia_huella_y_conserva_cierre_aunque_requisitos_se_deterioren()
    {
        var d = Datos(); Autorizar(d); var foto = LibranzaAutorizacionAnticipoService.Huella(d);
        d.Revision.Usuario!.EmailVerificado = false; d.Vinculo.IdWallet = null;
        var antes = LibranzaAutorizacionAnticipoService.Presentar(d, Ahora);
        Assert.True(antes.PuedeRevocar); Assert.False(antes.PuedeAutorizar);
        d.Autorizacion!.Estado = "REVOCADO"; d.Autorizacion.Revision++;
        Assert.Equal("REVOCADO", LibranzaAutorizacionAnticipoService.Presentar(d, Ahora).Estado);
        Assert.NotEqual(foto, LibranzaAutorizacionAnticipoService.Huella(d));
        Assert.False(LibranzaAutorizacionAnticipoService.AutorizadaVigente(d, Ahora));
    }

    [Fact]
    public void Piloto_previo_puede_cerrarse_explicitamente_y_no_se_presenta_como_permiso_nuevo()
    {
        var d = Datos(publico: false); var inicial = LibranzaAutorizacionAnticipoService.Presentar(d, Ahora);
        Assert.Equal("FLUJO_PREVIO", inicial.Estado); Assert.True(inicial.PuedeRevocar);
        d.Vinculo.IdWallet = null; d.Revision.Usuario!.EmailVerificado = false;
        Assert.True(LibranzaAutorizacionAnticipoService.Presentar(d, Ahora).PuedeRevocar);
        d.TieneHistorial = true;
        Assert.NotEqual("FLUJO_PREVIO", LibranzaAutorizacionAnticipoService.Presentar(d, Ahora).Estado);
    }

    [Fact]
    public void Referencia_y_confirmacion_son_obligatorias_y_foto_es_exacta()
    {
        var r = new CambiarAutorizacionAnticipoRequest { IdEmpleadoUsuario = 7, Huella = new('a', 64), ReferenciaRevision = "Revisión administrativa" };
        Assert.Throws<InvalidOperationException>(() => LibranzaAutorizacionAnticipoService.ValidarSolicitud(r));
        r.ConfirmaAutorizacion = true; LibranzaAutorizacionAnticipoService.ValidarSolicitud(r);
        r.ReferenciaRevision = "no\nregistrar";
        Assert.Throws<InvalidOperationException>(() => LibranzaAutorizacionAnticipoService.ValidarSolicitud(r));
        r.ReferenciaRevision = "Revisión"; r.Huella = new('g', 64);
        Assert.Throws<InvalidOperationException>(() => LibranzaAutorizacionAnticipoService.ValidarSolicitud(r));
    }

    [Fact]
    public void Endpoint_restringido_a_XPAY_y_request_no_recibe_decision_crediticia_ni_montos()
    {
        var a = typeof(LibranzaAutorizacionAnticipoController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal("ADMIN_XPAY,SUPERUSUARIO", a!.Roles);
        Assert.DoesNotContain(typeof(CambiarAutorizacionAnticipoRequest).GetProperties(), p => p.Name is "IdUsuario" or "Monto" or "Cupo" or "IdConvenio");
    }
}
