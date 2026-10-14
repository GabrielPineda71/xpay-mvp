using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.Common;
using Xpay.Api.Controllers;
using Xpay.Api.DTOs;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// XPAY-676B — el cliente solicita ser evaluado para obtener un cupo; NO elige
// ni solicita un monto. XPAY determina el cupo con CarteraDecisionEngine +
// CarteraPolicyParameters.Vigente(). Tests OFFLINE (sin red, sin SQL). La
// persistencia real (monto_solicitado NULL, copia de política) se cubre en
// CarteraOrdinariaEstadosActivosTests (SQL, gated).
public sealed class CarteraSolicitudSinMontoTests
{
    // ── Contrato público ────────────────────────────────────────────────

    [Fact]
    public void SolicitarCupo_no_recibe_body_ni_parametros()
    {
        var m = typeof(CarteraOrdinariaController).GetMethod(nameof(CarteraOrdinariaController.SolicitarCupo));
        Assert.NotNull(m);
        Assert.Empty(m!.GetParameters());
    }

    [Fact]
    public void SolicitarCupoRequest_ya_no_existe()
        => Assert.Null(typeof(SolicitudCupoResponse).Assembly.GetType("Xpay.Api.DTOs.SolicitarCupoRequest"));

    [Fact]
    public void Response_no_expone_MontoSolicitado()
        => Assert.Null(typeof(SolicitudCupoResponse).GetProperty("MontoSolicitado"));

    [Fact]
    public void Modelo_de_dominio_no_tiene_MontoSolicitado()
        => Assert.Null(typeof(CarteraSolicitudCupo).GetProperty("MontoSolicitado"));

    [Fact]
    public void CrearSolicitudCupoAsync_no_recibe_monto()
    {
        var m = typeof(CarteraOrdinariaService).GetMethod(nameof(CarteraOrdinariaService.CrearSolicitudCupoAsync));
        Assert.NotNull(m);
        var ps = m!.GetParameters();
        Assert.Equal(new[] { "idUsuario", "idempotencyKey", "correlationId" }, ps.Select(p => p.Name));
        Assert.DoesNotContain(ps, p => p.ParameterType == typeof(decimal));
    }

    // ── Idempotencia ────────────────────────────────────────────────────

    [Fact]
    public void Replay_misma_key_mismo_usuario_devuelve_la_misma_solicitud()
    {
        var previa = Solicitud(idSolicitud: 77, idUsuario: 25);
        var r = CarteraOrdinariaService.ReplayValidadoOConflicto(previa, idUsuario: 25);
        Assert.Equal(77, r.IdSolicitud);
        Assert.Equal(CarteraSolicitudCupoEstados.Recibida, r.EstadoSolicitud);
    }

    [Fact]
    public void Replay_key_de_otro_usuario_es_conflicto_sin_exponer_datos()
    {
        var previa = Solicitud(idSolicitud: 77, idUsuario: 25);
        var ex = Assert.Throws<InvalidOperationException>(
            () => CarteraOrdinariaService.ReplayValidadoOConflicto(previa, idUsuario: 26));
        Assert.Equal("Idempotency-Key ya utilizada para otra solicitud.", ex.Message);
        Assert.DoesNotContain("77", ex.Message);
        Assert.DoesNotContain("25", ex.Message);
    }

    [Fact]
    public void Conflictos_409_ya_no_incluyen_parametros_diferentes()
    {
        var f = typeof(CarteraOrdinariaController).GetField(
            "MensajesConflictoSolicitudCupo", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(f);
        var mensajes = (string[])f!.GetValue(null)!;
        Assert.DoesNotContain(mensajes, x => x.Contains("parámetros diferentes", StringComparison.Ordinal));
        Assert.Contains("Idempotency-Key ya utilizada para otra solicitud.", mensajes);
        Assert.Contains("Ya tienes una solicitud de cupo en curso", mensajes);
    }

    // ── Política aplicada = política efectiva del motor ─────────────────

    [Fact]
    public void Politica_aplicada_coincide_con_los_parametros_del_motor()
    {
        var motor = CarteraPolicyParameters.Vigente();
        var aplicada = CarteraPoliticaAplicada.Vigente();

        Assert.Equal(motor.CupoMinimo, aplicada.CupoMinimo);
        Assert.Equal(motor.CupoMaximo, aplicada.CupoMaximo);
        Assert.Equal(200_000m, aplicada.CupoMinimo);
        Assert.Equal(700_000m, aplicada.CupoMaximo);
        Assert.Equal(18, aplicada.EdadMinimaElegible);
        Assert.Equal(65, aplicada.EdadMaximaElegible);
        Assert.Null(aplicada.ScoreDatacreditoMinimo);
    }

    [Fact]
    public void Politica_aplicada_no_copia_los_valores_historicos_de_la_tabla()
    {
        // Seed histórico de la migración 021 (no gobierna el motor).
        var aplicada = CarteraPoliticaAplicada.Vigente();
        Assert.NotEqual(100_000m, aplicada.CupoMinimo);
        Assert.NotEqual(5_000_000m, aplicada.CupoMaximo);
        Assert.NotEqual(70, aplicada.EdadMaximaElegible);
    }

    [Fact]
    public void Politica_aplicada_sigue_a_los_parametros_que_recibe()
    {
        var p = CarteraPolicyParameters.Vigente() with { CupoMinimo = 250_000m, CupoMaximo = 900_000m };
        var aplicada = CarteraPoliticaAplicada.Desde(p);
        Assert.Equal(250_000m, aplicada.CupoMinimo);
        Assert.Equal(900_000m, aplicada.CupoMaximo);
    }

    // ── PUT admin/politica fail-closed ──────────────────────────────────

    [Fact]
    public void Put_admin_politica_responde_409_sin_tocar_dependencias()
    {
        // Todas las dependencias en null: si la acción las usara, fallaría.
        var ctl = new CarteraOrdinariaController(null!, null!, null!, null!, null!, null!, null!);
        var r = Assert.IsType<ConflictObjectResult>(ctl.UpsertPolitica());
        Assert.Equal(409, r.StatusCode);
        Assert.Contains(CarteraOrdinariaController.MensajePoliticaNoEditable, r.Value!.ToString());
    }

    [Fact]
    public void Put_admin_politica_no_recibe_body_y_el_servicio_ya_no_muta_la_politica()
    {
        var m = typeof(CarteraOrdinariaController).GetMethod(nameof(CarteraOrdinariaController.UpsertPolitica));
        Assert.Empty(m!.GetParameters());
        Assert.Null(typeof(CarteraOrdinariaService).GetMethod("UpsertPoliticaAsync"));
        Assert.Null(typeof(SolicitudCupoResponse).Assembly.GetType("Xpay.Api.DTOs.UpsertPoliticaCreditoRequest"));
    }

    // ── Migración 045 (análisis estático del script; NO se ejecuta) ─────

    [Fact]
    public void Migracion_045_preserva_historico_y_permite_null()
    {
        var sql = LeerScript("045_cartera_solicitud_sin_monto_y_politica_aplicada.sql");

        Assert.Contains("ALTER TABLE dbo.cartera_solicitudes_cupo ALTER COLUMN monto_solicitado DECIMAL(18,2) NULL;", sql);
        Assert.Contains("CHECK (monto_solicitado IS NULL OR monto_solicitado > 0);", sql);
        Assert.Contains("ADD CONSTRAINT ck_cartera_solicitudes_cupo_monto_solicitado", sql);
        Assert.Contains("BEGIN TRANSACTION;", sql);
        Assert.Contains("ROLLBACK TRANSACTION;", sql);

        // Nunca destruye ni transforma datos de solicitudes.
        Assert.DoesNotContain("DROP COLUMN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE dbo.cartera_solicitudes_cupo", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migracion_045_alinea_solo_la_fila_activa_de_politica()
    {
        var sql = LeerScript("045_cartera_solicitud_sin_monto_y_politica_aplicada.sql");
        Assert.Contains("UPDATE dbo.cartera_politicas_credito", sql);
        Assert.Contains("WHERE estado = 'ACTIVO';", sql);
        Assert.Contains("cupo_minimo = 200000", sql);
        Assert.Contains("cupo_maximo = 700000", sql);
        Assert.Contains("edad_maxima = 65", sql);
        Assert.DoesNotContain("score_datacredito_minimo =", sql);
        Assert.DoesNotContain("requiere_veriff =", sql);
    }

    [Fact]
    public void Migracion_035_sigue_exigiendo_el_mismo_check_que_recrea_la_045()
    {
        var m035 = LeerScript("035_cartera_solicitud_cupo.sql");
        Assert.Contains("name = 'ck_cartera_solicitudes_cupo_monto_solicitado'", m035);
    }

    private static CarteraSolicitudCupo Solicitud(long idSolicitud, long idUsuario) => new()
    {
        IdSolicitud = idSolicitud, IdUsuario = idUsuario, IdPersona = 1,
        EstadoSolicitud = CarteraSolicitudCupoEstados.Recibida,
        DecisionCrediticia = CarteraDecisionCrediticia.Pendiente,
        FechaSolicitud = new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc),
    };

    private static string LeerScript(string nombre)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "database", nombre)))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "database", nombre));
    }
}
