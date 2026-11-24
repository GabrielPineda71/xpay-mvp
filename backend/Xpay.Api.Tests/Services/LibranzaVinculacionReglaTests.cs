using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Xpay.Api.Controllers;
using Xpay.Api.DTOs;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

public sealed class LibranzaVinculacionReglaTests
{
    private static readonly DateTime Ahora = new(2026, 11, 23, 20, 0, 0, DateTimeKind.Utc);
    internal static LibranzaVinculacionService.DatosRevision Datos() => new()
    {
        IdEmpleado = 1, IdUsuario = 2, RolUsuarioFinalActivo = true,
        Empleado = new() { IdEmpleado = 1, IdConvenio = 3, TipoDocumento = "CC", NumeroDocumento = "9990001234",
            Nombres = "Persona ficticia", Estado = "ACTIVO", SalarioMensual = 2_000_000m, PeriodicidadPago = "MENSUAL" },
        Convenio = new() { IdConvenio = 3, NombreEmpresa = "Empresa ficticia", Nit = "999001",
            Estado = "ACTIVO", FechaInicio = Ahora.AddDays(-10) },
        Usuario = new() { IdUsuario = 2, IdPersona = 4, NombreUsuario = "ficticio", Estado = "ACTIVO",
            EmailVerificado = true, CelularVerificado = false, EstadoKycActual = "APROBADO" },
        Persona = new() { IdPersona = 4, IdUnidadNegocio = 1, Estado = "ACTIVA", TipoDocumento = "CC",
            NumeroDocumento = "9990001234", IdentidadVerificada = true, IdentidadVerificadaProveedor = "VERIFF",
            IdentidadVerificadaFecha = Ahora.AddDays(-1), NumeroDocumentoVerificado = "9.990.001.234",
            TipoDocumentoVeriffRaw = "ID_CARD", Email = "ficticio@example.test", Celular = "3000000000" },
        Kyc = [new() { IdKycVerificacion = 5, IdUsuario = 2, IdPersona = 4, Proveedor = "VERIFF", EstadoKyc = "APROBADO",
            EsActual = true, Decision = "approved", FechaDecision = Ahora.AddDays(-1) }],
        Wallets = [new() { IdWallet = 6, IdPersona = 4, IdUnidadNegocio = 1, TipoWallet = "PERSONA", Estado = "ACTIVA" }],
        EmpleadosMismoDocumento = [1], UsuariosMismoDocumento = [2], PersonasMismoDocumento = [4],
    };

    [Fact]
    public void Coincidencia_verificada_y_Wallet_existente_sin_WhatsApp_es_elegible_y_no_expone_contactos()
    {
        var d = Datos(); var r = LibranzaVinculacionService.Evaluar(d, Ahora);
        Assert.True(r.Elegible); Assert.Empty(r.Razones); Assert.Equal("PENDIENTE", r.Estado);
        var json = JsonSerializer.Serialize(r);
        Assert.DoesNotContain(d.Persona!.NumeroDocumentoVerificado!, json);
        Assert.DoesNotContain(d.Persona.NumeroDocumento!, json);
        Assert.DoesNotContain(d.Persona.Email!, json); Assert.DoesNotContain(d.Persona.Celular, json);
        Assert.DoesNotContain("WhatsApp", json, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("1234", r.DocumentoMasked); Assert.Matches("^[0-9a-f]{64}$", r.Huella);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("  ", "\t")]
    public void Historico_con_ambos_legales_vacios_lee_Veriff_aprobado_sin_consolidar(string? tipo, string? numero)
    {
        var d = Datos(); d.Persona!.TipoDocumento = tipo; d.Persona.NumeroDocumento = numero;
        var raw = d.Persona.NumeroDocumentoVerificado;
        var r = LibranzaVinculacionService.Evaluar(d, Ahora);
        Assert.True(r.IdentidadVerificada); Assert.True(r.Elegible); Assert.Empty(r.Razones);
        Assert.Equal("PENDIENTE", r.Estado); // Aún requiere la acción expresa de XPAY.
        Assert.Empty(d.Vinculos);
        Assert.Equal(tipo, d.Persona.TipoDocumento); Assert.Equal(numero, d.Persona.NumeroDocumento);
        Assert.Equal(raw, d.Persona.NumeroDocumentoVerificado);
    }

    [Fact]
    public void Vinculo_historico_activo_sin_legales_conserva_su_estado_sin_reaprobar()
    {
        var d = Datos(); d.Persona!.TipoDocumento = null; d.Persona.NumeroDocumento = null;
        d.Vinculos.Add(new() { IdEmpleado = 1, IdUsuario = 2, IdWallet = 6, Estado = "ACTIVO" });
        var r = LibranzaVinculacionService.Evaluar(d, Ahora);
        Assert.Equal("APROBADO", r.Estado); Assert.False(r.Elegible); Assert.Empty(r.Razones);
        Assert.Single(d.Vinculos); Assert.Null(d.Persona.TipoDocumento); Assert.Null(d.Persona.NumeroDocumento);
    }

    [Theory]
    [InlineData("CC", null)]
    [InlineData(null, "9990001234")]
    [InlineData("CC", "  ")]
    [InlineData("\t", "9990001234")]
    public void Documento_legal_parcial_no_usa_fallback_Veriff(string? tipo, string? numero)
    {
        var d = Datos(); d.Persona!.TipoDocumento = tipo; d.Persona.NumeroDocumento = numero;
        var r = LibranzaVinculacionService.Evaluar(d, Ahora);
        Assert.False(r.Elegible); Assert.Contains(r.Razones, x => x.Contains("legal está incompleto"));
    }

    [Theory]
    [InlineData("CC", "9990001111")]
    [InlineData("PASAPORTE", "9990001234")]
    [InlineData("CC", "999-0001234")]
    [InlineData("CC", "9.99.0001.234")]
    public void Documento_legal_distinto_o_invalido_no_se_ignora(string tipo, string numero)
    {
        var d = Datos(); d.Persona!.TipoDocumento = tipo; d.Persona.NumeroDocumento = numero;
        var r = LibranzaVinculacionService.Evaluar(d, Ahora);
        Assert.False(r.Elegible); Assert.Contains(r.Razones, x => x.Contains("no coinciden"));
    }

    [Fact]
    public void Cedula_legal_y_Veriff_con_miles_estrictos_son_equivalentes()
    {
        var d = Datos(); d.Persona!.NumeroDocumento = "9.990.001.234";
        var r = LibranzaVinculacionService.Evaluar(d, Ahora);
        Assert.True(r.Elegible); Assert.Empty(r.Razones);
    }

    [Theory]
    [InlineData("999/0001234")]
    [InlineData("999-0001234")]
    [InlineData("9.99.0001.234")]
    [InlineData("９９９０００１２３４")]
    public void Sin_legales_Veriff_con_numero_invalido_no_habilita_la_revision(string raw)
    {
        var d = Datos(); d.Persona!.TipoDocumento = null; d.Persona.NumeroDocumento = null;
        d.Persona.NumeroDocumentoVerificado = raw;
        var r = LibranzaVinculacionService.Evaluar(d, Ahora);
        Assert.False(r.Elegible); Assert.Contains(r.Razones, x => x.Contains("verificado no coincide"));
    }

    [Theory]
    [InlineData("sin-identidad")]
    [InlineData("otro-proveedor")]
    [InlineData("sin-sesion-actual")]
    [InlineData("sesion-distinta-persona")]
    [InlineData("decision-no-aprobada")]
    public void Sin_legales_sigue_exigiendo_prueba_Veriff_actual_aprobada(string caso)
    {
        var d = Datos(); d.Persona!.TipoDocumento = null; d.Persona.NumeroDocumento = null;
        switch (caso)
        {
            case "sin-identidad": d.Persona.IdentidadVerificada = false; break;
            case "otro-proveedor": d.Persona.IdentidadVerificadaProveedor = "SIMULACION_QA"; break;
            case "sin-sesion-actual": d.Kyc.Clear(); break;
            case "sesion-distinta-persona": d.Kyc[0].IdPersona = 77; break;
            case "decision-no-aprobada": d.Kyc[0].Decision = "declined"; break;
        }
        var r = LibranzaVinculacionService.Evaluar(d, Ahora);
        Assert.False(r.IdentidadVerificada); Assert.False(r.Elegible);
        Assert.Contains(r.Razones, x => x.Contains("identidad", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Sin_legales_no_omite_correo_Wallet_ni_duplicados()
    {
        var d = Datos(); d.Persona!.TipoDocumento = null; d.Persona.NumeroDocumento = null;
        d.Usuario!.EmailVerificado = false; d.Wallets.Clear(); d.PersonasMismoDocumento.Add(77);
        var r = LibranzaVinculacionService.Evaluar(d, Ahora);
        Assert.False(r.Elegible); Assert.False(r.EmailVerificado); Assert.False(r.WalletActiva);
        Assert.Contains(r.Razones, x => x.Contains("correo")); Assert.Contains(r.Razones, x => x.Contains("Wallet"));
        Assert.Contains(r.Razones, x => x.Contains("ambigua"));
    }

    [Theory]
    [InlineData("9990001234", "CC:9990001234")]
    [InlineData("9.990.001.234", "CC:9990001234")]
    [InlineData(" 9990001234 ", "CC:9990001234")]
    public void Cedula_canonica_o_miles_estrictos(string entrada, string esperado) =>
        Assert.Equal(esperado, LibranzaVinculacionService.ClaveDocumento("CC", entrada));

    [Theory]
    [InlineData("９９９０００１２３４")]
    [InlineData("999/0001234")]
    [InlineData("999-0001234")]
    [InlineData("9.99.0001.234")]
    [InlineData("123\t456")]
    [InlineData("ABC123")]
    public void Cedula_rechaza_confusables_y_separadores_arbitrarios(string entrada) =>
        Assert.Null(LibranzaVinculacionService.ClaveDocumento("CC", entrada));

    [Fact]
    public void Pasaporte_admite_letras_no_suplanta_tipo_cedula()
    {
        Assert.Equal("PASAPORTE:AB123", LibranzaVinculacionService.ClaveDocumento("PASAPORTE", "ab123"));
        Assert.Null(LibranzaVinculacionService.ClaveDocumento("CE", "123456"));
        Assert.Null(LibranzaVinculacionService.ClaveDocumento("PASAPORTE", "1.234"));
        var d = Datos(); d.Empleado!.TipoDocumento = "PASAPORTE";
        Assert.False(LibranzaVinculacionService.Evaluar(d, Ahora).Elegible);
    }

    [Fact]
    public void Correo_y_Wallet_pendientes_se_informan_sin_aprobar()
    {
        var d = Datos(); d.Usuario!.EmailVerificado = false; d.Wallets.Clear();
        var r = LibranzaVinculacionService.Evaluar(d, Ahora);
        Assert.False(r.Elegible); Assert.False(r.WalletActiva); Assert.False(r.EmailVerificado);
        Assert.Contains(r.Razones, x => x.Contains("correo")); Assert.Contains(r.Razones, x => x.Contains("Wallet"));
    }

    [Theory]
    [InlineData("SIMULACION_QA", "APROBADO", "approved")]
    [InlineData("VERIFF", "RECHAZADO", "approved")]
    [InlineData("VERIFF", "APROBADO", "declined")]
    public void Solo_Veriff_actual_aprobado(string proveedor, string estado, string decision)
    {
        var d = Datos(); d.Kyc[0].Proveedor = proveedor; d.Kyc[0].EstadoKyc = estado; d.Kyc[0].Decision = decision;
        Assert.False(LibranzaVinculacionService.Evaluar(d, Ahora).IdentidadVerificada);
    }

    [Fact]
    public void Multiples_sesiones_o_documento_legal_distinto_bloquean()
    {
        var d = Datos(); d.Kyc.Add(new() { IdPersona = 4, Proveedor = "VERIFF", EstadoKyc = "APROBADO", Decision = "approved" });
        Assert.False(LibranzaVinculacionService.Evaluar(d, Ahora).Elegible);
        d.Kyc.RemoveAt(1); d.Persona!.NumeroDocumento = "9990001111";
        Assert.Contains(LibranzaVinculacionService.Evaluar(d, Ahora).Razones, x => x.Contains("legal"));
    }

    [Fact]
    public void Duplicados_de_persona_cuenta_o_empleado_no_se_eligen_automaticamente()
    {
        var d = Datos(); d.PersonasMismoDocumento.Add(77); d.UsuariosMismoDocumento.Add(88); d.EmpleadosMismoDocumento.Add(99);
        var r = LibranzaVinculacionService.Evaluar(d, Ahora);
        Assert.False(r.Elegible); Assert.Contains(r.Razones, x => x.Contains("ambigua")); Assert.Contains(r.Razones, x => x.Contains("varios empleados"));
    }

    [Fact]
    public void Estados_perfil_y_vigencia_se_revalidan()
    {
        var d = Datos(); d.Empleado!.Estado = "INACTIVO"; d.Usuario!.FechaBloqueo = Ahora;
        d.Convenio!.FechaFin = Ahora.AddDays(-1); d = new()
        {
            IdEmpleado = d.IdEmpleado, IdUsuario = d.IdUsuario, Empleado = d.Empleado, Usuario = d.Usuario,
            Convenio = d.Convenio, Persona = d.Persona, Kyc = d.Kyc, Wallets = d.Wallets,
            EmpleadosMismoDocumento = d.EmpleadosMismoDocumento, PersonasMismoDocumento = d.PersonasMismoDocumento,
            UsuariosMismoDocumento = d.UsuariosMismoDocumento, RolUsuarioFinalActivo = false,
        };
        var r = LibranzaVinculacionService.Evaluar(d, Ahora);
        Assert.False(r.Elegible); Assert.Contains(r.Razones, x => x.Contains("empleado"));
        Assert.Contains(r.Razones, x => x.Contains("vigente")); Assert.Contains(r.Razones, x => x.Contains("perfil"));
    }

    [Fact]
    public void Propio_vigente_es_idempotente_pero_cruzado_o_Wallet_distinta_bloquea()
    {
        var d = Datos(); var huella = LibranzaVinculacionService.CalcularHuella(d);
        d.Vinculos.Add(new() { IdEmpleado = 1, IdUsuario = 2, IdWallet = 6, Estado = "ACTIVO" });
        var r = LibranzaVinculacionService.Evaluar(d, Ahora);
        Assert.Equal("APROBADO", r.Estado); Assert.False(r.Elegible); Assert.Equal(huella, r.Huella);
        d.Vinculos[0].IdWallet = 99;
        Assert.Equal("REVISION_REQUERIDA", LibranzaVinculacionService.Evaluar(d, Ahora).Estado);
        d.Vinculos.Clear(); d.Vinculos.Add(new() { IdEmpleado = 9, IdUsuario = 2, Estado = "ACTIVO" });
        Assert.False(LibranzaVinculacionService.Evaluar(d, Ahora).Elegible);
    }

    [Fact]
    public void Huella_no_contiene_y_no_depende_de_sesion_WhatsApp_saldo_o_password()
    {
        var d = Datos(); var h = LibranzaVinculacionService.CalcularHuella(d);
        d.Usuario!.UltimoIngreso = Ahora; d.Usuario.CelularVerificado = true; d.Usuario.PasswordHash = "cambiada";
        Assert.Equal(h, LibranzaVinculacionService.CalcularHuella(d));
        d.Persona!.NumeroDocumentoVerificado = "9990001235";
        Assert.NotEqual(h, LibranzaVinculacionService.CalcularHuella(d));
    }

    [Fact]
    public void Seleccion_exige_confirmacion_referencia_limite_y_una_pareja_por_persona()
    {
        var r = new AprobarVinculacionesRequest { ReferenciaRevision = "RRHH prueba", Seleccion = [new() { IdEmpleado = 1, IdUsuario = 2, Huella = new('a', 64) }] };
        Assert.Throws<InvalidOperationException>(() => LibranzaVinculacionService.ValidarSeleccion(r));
        r.ConfirmaPertenenciaLaboral = true; LibranzaVinculacionService.ValidarSeleccion(r);
        r.Seleccion.Add(new() { IdEmpleado = 3, IdUsuario = 2, Huella = new('b', 64) });
        Assert.Throws<InvalidOperationException>(() => LibranzaVinculacionService.ValidarSeleccion(r));
        r.Seleccion = Enumerable.Range(1, 51).Select(n => new SeleccionVinculacionRequest { IdEmpleado = n, IdUsuario = n, Huella = new('a', 64) }).ToList();
        Assert.Throws<InvalidOperationException>(() => LibranzaVinculacionService.ValidarSeleccion(r));
        r.Seleccion.RemoveAt(50); LibranzaVinculacionService.ValidarSeleccion(r);
        r.ReferenciaRevision = "no\nregistrar"; Assert.Throws<InvalidOperationException>(() => LibranzaVinculacionService.ValidarSeleccion(r));
    }

    [Fact]
    public void Controlador_restringido_solo_a_administrador_XPAY()
    {
        var a = typeof(LibranzaVinculacionController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal("ADMIN_XPAY,SUPERUSUARIO", a!.Roles);
    }
}
