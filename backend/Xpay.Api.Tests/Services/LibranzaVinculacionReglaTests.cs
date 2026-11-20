using System.Text.Json;
using Xpay.Api.DTOs;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

public sealed class LibranzaVinculacionReglaTests
{
    private static readonly DateTime Ahora = new(2026, 11, 20, 17, 0, 0, DateTimeKind.Utc);

    internal static LibranzaVinculacionService.DatosRevision Datos() => new()
    {
        IdEmpleado = 1, IdUsuario = 2, RolUsuarioFinalActivo = true,
        Empleado = new() { IdEmpleado = 1, IdConvenio = 3, TipoDocumento = "CC", NumeroDocumento = "9990001234",
            Nombres = "Persona ficticia", Estado = "ACTIVO", SalarioMensual = 2_000_000m,
            CupoPreliminar = 600_000m, PeriodicidadPago = "MENSUAL", DiaPago1 = 30 },
        Convenio = new() { IdConvenio = 3, NombreEmpresa = "Empresa ficticia", Nit = "999001",
            Estado = "ACTIVO", FechaInicio = Ahora.AddDays(-10), PorcentajeMaximoCupo = 30m },
        Usuario = new() { IdUsuario = 2, IdPersona = 4, NombreUsuario = "ficticio", Estado = "ACTIVO",
            EmailVerificado = true, CelularVerificado = true, EstadoKycActual = "APROBADO" },
        Persona = new() { IdPersona = 4, IdUnidadNegocio = 1, Estado = "ACTIVA", TipoDocumento = "CC",
            NumeroDocumento = "9990001234", IdentidadVerificada = true, IdentidadVerificadaProveedor = "VERIFF",
            IdentidadVerificadaFecha = Ahora.AddDays(-1), NumeroDocumentoVerificado = "9990001234",
            TipoDocumentoVeriffRaw = "ID_CARD", Email = "ficticio@example.test", Celular = "3000000000" },
        Kyc = [new() { IdKycVerificacion = 5, IdUsuario = 2, IdPersona = 4, Proveedor = "VERIFF", EstadoKyc = "APROBADO",
            EsActual = true, Decision = "approved", FechaDecision = Ahora.AddDays(-1) }],
        Parametros = [new() { IdParametro = 6, IdConvenio = 3, Estado = "ACTIVO", PorcentajeMaximoCupo = 30m,
            MomentoCobroComision = "VENCIDO", MontoMinimoAnticipo = 100_000m }],
        Cortes = [new() { IdCortePago = 7, IdEmpleado = 1, NumeroCorte = 1, DiaPago = 30, Estado = "ACTIVO", ValorPagoProgramado = 2_000_000m }],
        EmpleadosMismoDocumento = [1], UsuariosMismoDocumento = [2],
    };

    [Fact]
    public void Coincidencia_verificada_unica_elegible_y_solo_documento_enmascarado()
    {
        var d = Datos();
        var r = LibranzaVinculacionService.Evaluar(d, Ahora);
        Assert.True(r.Elegible); Assert.Empty(r.Razones); Assert.Equal("PENDIENTE", r.Estado);
        var json = JsonSerializer.Serialize(r);
        Assert.DoesNotContain(d.Persona!.NumeroDocumentoVerificado!, json);
        Assert.DoesNotContain(d.Persona.Email!, json);
        Assert.DoesNotContain(d.Persona.Celular, json);
        Assert.EndsWith("1234", r.DocumentoMasked);
        Assert.Matches("^[0-9a-f]{64}$", r.Huella);
        Assert.Equal(600_000m, Assert.Single(r.CuposPorCorte).CupoBase);
    }

    [Theory]
    [InlineData("9990001234", "9990001234")]
    [InlineData("999.000.123-4", "9990001234")]
    [InlineData(" ab-123 ", "AB123")]
    public void Normalizacion_cerrada_admite_solo_separadores_conocidos(string entrada, string esperado) =>
        Assert.Equal(esperado, LibranzaVinculacionService.NormalizarNumero(entrada));

    [Theory]
    [InlineData("９９９０００１２３４")]
    [InlineData("999/0001234")]
    [InlineData("123\t456")]
    [InlineData("12")]
    [InlineData("")]
    public void Normalizacion_rechaza_confusables_y_simbolos(string entrada) =>
        Assert.Null(LibranzaVinculacionService.NormalizarNumero(entrada));

    [Fact]
    public void Mismo_nombre_o_contacto_no_suple_documento_distinto()
    {
        var d = Datos(); d.Empleado!.NumeroDocumento = "9990009999";
        Assert.False(LibranzaVinculacionService.Evaluar(d, Ahora).Elegible);
    }

    [Fact]
    public void Sin_email_o_whatsapp_no_habilita()
    {
        var d = Datos(); d.Usuario!.EmailVerificado = false; d.Usuario.CelularVerificado = false;
        var r = LibranzaVinculacionService.Evaluar(d, Ahora);
        Assert.False(r.Elegible); Assert.Contains(r.Razones, x => x.Contains("correo")); Assert.Contains(r.Razones, x => x.Contains("WhatsApp"));
    }

    [Fact]
    public void Simulacion_o_dos_sesiones_actuales_no_suplen_veriff()
    {
        var d = Datos(); d.Kyc[0].Proveedor = "SIMULACION_QA";
        Assert.False(LibranzaVinculacionService.Evaluar(d, Ahora).IdentidadVerificada);
        d.Kyc[0].Proveedor = "VERIFF"; d.Kyc.Add(new() { Proveedor = "VERIFF", EstadoKyc = "APROBADO" });
        Assert.False(LibranzaVinculacionService.Evaluar(d, Ahora).Elegible);
    }

    [Fact]
    public void Varios_usuarios_o_empleados_con_mismo_documento_requieren_aclaracion()
    {
        var d = Datos(); d.UsuariosMismoDocumento.Add(99); d.EmpleadosMismoDocumento.Add(100);
        var r = LibranzaVinculacionService.Evaluar(d, Ahora);
        Assert.False(r.Elegible); Assert.Contains(r.Razones, x => x.Contains("ambigua")); Assert.Contains(r.Razones, x => x.Contains("varios empleados"));
    }

    [Fact]
    public void Corte_incompleto_o_convenio_vencido_no_habilitan()
    {
        var d = Datos(); d.Cortes.Clear(); d.Convenio!.FechaFin = Ahora.AddDays(-1);
        var r = LibranzaVinculacionService.Evaluar(d, Ahora);
        Assert.False(r.Elegible); Assert.Contains(r.Razones, x => x.Contains("cortes")); Assert.Contains(r.Razones, x => x.Contains("vigente"));
    }

    [Fact]
    public void Huella_cambia_con_condiciones_e_identidad_no_con_login_o_version_seguridad()
    {
        var d = Datos(); var inicial = LibranzaVinculacionService.CalcularHuella(d);
        d.Usuario!.UltimoIngreso = Ahora; d.Usuario.VersionSeguridad++;
        Assert.Equal(inicial, LibranzaVinculacionService.CalcularHuella(d));
        d.Empleado!.SalarioMensual += 1;
        Assert.NotEqual(inicial, LibranzaVinculacionService.CalcularHuella(d));
        var salarial = LibranzaVinculacionService.CalcularHuella(d);
        d.Cortes[0].ValorPagoProgramado += 1;
        Assert.NotEqual(salarial, LibranzaVinculacionService.CalcularHuella(d));
        var corte = LibranzaVinculacionService.CalcularHuella(d); d.Persona!.NumeroDocumentoVerificado = "9990008888";
        Assert.NotEqual(corte, LibranzaVinculacionService.CalcularHuella(d));
    }

    [Fact]
    public void Revision_completa_vigente_se_muestra_aprobada_y_cambio_exige_nueva_revision()
    {
        var d = Datos(); d.Vinculos.Add(new() { IdEmpleado = 1, IdUsuario = 2, Estado = "ACTIVO", RevisionMetodo = "MANUAL",
            RevisionFecha = Ahora, RevisionPorUsuario = 9, RevisionReferencia = "RRHH prueba ficticia",
            RevisionHuella = LibranzaVinculacionService.CalcularHuella(d) });
        var aprobada = LibranzaVinculacionService.Evaluar(d, Ahora);
        Assert.Equal("APROBADO", aprobada.Estado); Assert.False(aprobada.Elegible);
        d.Empleado!.SalarioMensual += 1;
        var cambiada = LibranzaVinculacionService.Evaluar(d, Ahora);
        Assert.Equal("REVISION_REQUERIDA", cambiada.Estado); Assert.True(cambiada.Elegible);
    }

    [Fact]
    public void Metadata_parcial_no_es_legacy()
    {
        var a = new LibranzaEmpleadoUsuario(); Assert.True(LibranzaVinculacionService.EsLegacy(a));
        a.RevisionMetodo = "MANUAL"; Assert.False(LibranzaVinculacionService.EsLegacy(a));
    }

    [Fact]
    public void Pago_de_corte_inflado_no_habilita_cupo_por_encima_del_salario()
    {
        var d = Datos(); d.Cortes[0].ValorPagoProgramado = 20_000_000m;
        var r = LibranzaVinculacionService.Evaluar(d, Ahora);
        Assert.False(r.Elegible); Assert.Contains(r.Razones, x => x.Contains("corresponder al salario"));
        Assert.Equal(6_000_000m, Assert.Single(r.CuposPorCorte).CupoBase);
    }

    [Fact]
    public void Preliminar_respeta_porcentaje_y_redondeo_comercial_de_SQL()
    {
        var d = Datos(); d.Empleado!.SalarioMensual = 2_000_000.17m; d.Cortes[0].ValorPagoProgramado = 2_000_000.17m;
        d.Parametros[0].PorcentajeMaximoCupo = 33.33m;
        d.Empleado.CupoPreliminar = decimal.Round(d.Empleado.SalarioMensual * 33.33m / 100m, 2, MidpointRounding.AwayFromZero);
        Assert.True(LibranzaVinculacionService.Evaluar(d, Ahora).Elegible);
        d.Empleado.CupoPreliminar += 0.01m;
        Assert.False(LibranzaVinculacionService.Evaluar(d, Ahora).Elegible);
    }

    [Fact]
    public void Seleccion_exige_confirmacion_referencia_y_no_repite_usuarios()
    {
        var r = new AprobarVinculacionesRequest
        {
            ReferenciaRevision = "RRHH caso ficticio", Seleccion = [new() { IdEmpleado = 1, IdUsuario = 2, Huella = new string('a', 64) }],
        };
        Assert.Throws<InvalidOperationException>(() => LibranzaVinculacionService.ValidarSeleccion(r));
        r.ConfirmaPertenenciaLaboral = true; LibranzaVinculacionService.ValidarSeleccion(r);
        r.Seleccion.Add(new() { IdEmpleado = 3, IdUsuario = 2, Huella = new string('b', 64) });
        Assert.Throws<InvalidOperationException>(() => LibranzaVinculacionService.ValidarSeleccion(r));
    }
}
