using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Integrations.MiDecisor;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// ══════════════════════════════════════════════════════════════════════════
// Consentimiento durable V1 — integración SQL de la captura
// (CarteraAutorizacionConsultaRiesgoStore) y de la validación
// (AutorizacionConsultaRiesgoDurable). ACTA 001 §3 · XPAY-195/196/197.
// SIN red, SIN proveedor, SIN cédulas. Regla V1 ESTRICTA: la aceptación
// autoriza EXCLUSIVAMENTE la consulta de su misma solicitud (sin reutilización
// cross-request). Guard fail-closed idéntico a M2.4a/b/c: local sin
// ConnectionStrings__XpayConnection → early-return (PASS) ; en CI sin la
// variable → FALLA. Reutiliza la colección SqlIntegration.
// ══════════════════════════════════════════════════════════════════════════

[Collection("SqlIntegration")]
public sealed class CarteraAutorizacionConsultaRiesgoStoreTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";
    private const string V1 = "AUTZ_CONSULTA_RIESGO_2026_V1";

    // ── TEST 1 — registro correcto persiste todos los campos ─────────────
    [Fact]
    public async Task Registra_persiste_persona_usuario_solicitud_version_hash_snapshot_fecha()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us, so) = (new List<long>(), new List<long>(), new List<long>());
        try
        {
            var (idSol, idUsr, idPer) = await SembrarAsync(cs, "ACTIVO", "ACTIVA", pe, us, so);
            var antes = DateTime.UtcNow.AddSeconds(-2);

            await using (var c = NuevoContexto(cs))
                Assert.Equal(ResultadoRegistroAutorizacion.Registrada,
                    await new CarteraAutorizacionConsultaRiesgoStore(c)
                        .RegistrarAceptacionAsync(idSol, idUsr, V1, "corr-1", default));

            await using var v = NuevoContexto(cs);
            var fila = await v.CarteraAutorizacionesConsultaRiesgo.AsNoTracking()
                .SingleAsync(a => a.IdSolicitudOrigen == idSol);

            Assert.Equal(idPer, fila.IdPersona);
            Assert.Equal(idUsr, fila.IdUsuario);
            Assert.Equal(idSol, fila.IdSolicitudOrigen);
            Assert.Equal(V1, fila.VersionTexto);
            Assert.Equal(CarteraAutorizacionConsultaRiesgoTextos.V1_HashSha256, fila.HashTexto);
            Assert.Equal(CarteraAutorizacionConsultaRiesgoTextos.V1_Texto, fila.TextoSnapshot);
            Assert.True(fila.FechaAceptacionUtc >= antes && fila.FechaAceptacionUtc <= DateTime.UtcNow.AddSeconds(2));
            Assert.Equal("corr-1", fila.CorrelationId);
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    // ── TEST 2 — solicitud ajena → NoElegible, sin fila ──────────────────
    [Fact]
    public async Task Solicitud_de_otro_usuario_no_escribe()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us, so) = (new List<long>(), new List<long>(), new List<long>());
        try
        {
            var (idSol, _, _) = await SembrarAsync(cs, "ACTIVO", "ACTIVA", pe, us, so);
            long otroUsuario = idSol + 999_999; // no es el dueño

            await using (var c = NuevoContexto(cs))
                Assert.Equal(ResultadoRegistroAutorizacion.NoElegible,
                    await new CarteraAutorizacionConsultaRiesgoStore(c)
                        .RegistrarAceptacionAsync(idSol, otroUsuario, V1, null, default));

            await using var v = NuevoContexto(cs);
            Assert.False(await v.CarteraAutorizacionesConsultaRiesgo.AsNoTracking().AnyAsync(a => a.IdSolicitudOrigen == idSol));
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    // ── TEST 3 — identidad usuario/persona inconsistente → fail-closed ───
    [Fact]
    public async Task Persona_del_usuario_distinta_de_la_solicitud_fail_closed()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us, so) = (new List<long>(), new List<long>(), new List<long>());
        try
        {
            var (idSol, idUsr, _) = await SembrarAsync(cs, "ACTIVO", "ACTIVA", pe, us, so);

            // Corromper: apuntar la solicitud a otra persona sin tocar el usuario.
            var (_, _, idPerOtra) = await SembrarAsync(cs, "ACTIVO", "ACTIVA", pe, us, so);
            await using (var c = NuevoContexto(cs))
                await c.Database.ExecuteSqlAsync(
                    $"UPDATE dbo.cartera_solicitudes_cupo SET id_persona = {idPerOtra} WHERE id_solicitud = {idSol}");

            await using (var c = NuevoContexto(cs))
                await Assert.ThrowsAsync<CarteraAutorizacionConsultaRiesgoInvarianteException>(() =>
                    new CarteraAutorizacionConsultaRiesgoStore(c)
                        .RegistrarAceptacionAsync(idSol, idUsr, V1, null, default));

            await using var v = NuevoContexto(cs);
            Assert.False(await v.CarteraAutorizacionesConsultaRiesgo.AsNoTracking().AnyAsync(a => a.IdSolicitudOrigen == idSol));
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    // ── TEST 4 — versión recibida distinta → NoElegible, sin fila ────────
    [Fact]
    public async Task Version_distinta_de_la_vigente_NoElegible()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us, so) = (new List<long>(), new List<long>(), new List<long>());
        try
        {
            var (idSol, idUsr, _) = await SembrarAsync(cs, "ACTIVO", "ACTIVA", pe, us, so);

            await using (var c = NuevoContexto(cs))
                Assert.Equal(ResultadoRegistroAutorizacion.NoElegible,
                    await new CarteraAutorizacionConsultaRiesgoStore(c)
                        .RegistrarAceptacionAsync(idSol, idUsr, "AUTZ_CONSULTA_RIESGO_2026_V2", null, default));

            await using var v = NuevoContexto(cs);
            Assert.False(await v.CarteraAutorizacionesConsultaRiesgo.AsNoTracking().AnyAsync(a => a.IdSolicitudOrigen == idSol));
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    // ── TEST 5 — segunda aceptación misma solicitud+versión → YaRegistrada, 1 fila ──
    [Fact]
    public async Task Segunda_aceptacion_misma_solicitud_version_YaRegistrada_una_sola_fila()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us, so) = (new List<long>(), new List<long>(), new List<long>());
        try
        {
            var (idSol, idUsr, _) = await SembrarAsync(cs, "ACTIVO", "ACTIVA", pe, us, so);

            await using (var c = NuevoContexto(cs))
                Assert.Equal(ResultadoRegistroAutorizacion.Registrada,
                    await new CarteraAutorizacionConsultaRiesgoStore(c).RegistrarAceptacionAsync(idSol, idUsr, V1, null, default));
            await using (var c = NuevoContexto(cs))
                Assert.Equal(ResultadoRegistroAutorizacion.YaRegistrada,
                    await new CarteraAutorizacionConsultaRiesgoStore(c).RegistrarAceptacionAsync(idSol, idUsr, V1, "otra-corr", default));

            await using var v = NuevoContexto(cs);
            Assert.Equal(1, await v.CarteraAutorizacionesConsultaRiesgo.AsNoTracking().CountAsync(a => a.IdSolicitudOrigen == idSol));
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    // ── TEST 6 — concurrencia doble aceptación → exactamente una evidencia ──
    [Fact]
    public async Task Concurrencia_doble_aceptacion_una_sola_evidencia()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us, so) = (new List<long>(), new List<long>(), new List<long>());
        try
        {
            var (idSol, idUsr, _) = await SembrarAsync(cs, "ACTIVO", "ACTIVA", pe, us, so);

            await using var c1 = NuevoContexto(cs);
            await using var c2 = NuevoContexto(cs);
            var res = await Task.WhenAll(
                new CarteraAutorizacionConsultaRiesgoStore(c1).RegistrarAceptacionAsync(idSol, idUsr, V1, null, default),
                new CarteraAutorizacionConsultaRiesgoStore(c2).RegistrarAceptacionAsync(idSol, idUsr, V1, null, default));

            Assert.Equal(1, res.Count(x => x == ResultadoRegistroAutorizacion.Registrada));
            Assert.Equal(1, res.Count(x => x == ResultadoRegistroAutorizacion.YaRegistrada));

            await using var v = NuevoContexto(cs);
            Assert.Equal(1, await v.CarteraAutorizacionesConsultaRiesgo.AsNoTracking().CountAsync(a => a.IdSolicitudOrigen == idSol));
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    // ══════════════════ VALIDADOR DURABLE ══════════════════

    // ── TEST 7 — A1/S1 → true ───────────────────────────────────────────
    [Fact]
    public async Task Validador_A1_autoriza_su_propia_solicitud()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us, so) = (new List<long>(), new List<long>(), new List<long>());
        try
        {
            var (idSol, idUsr, _) = await SembrarAsync(cs, "ACTIVO", "ACTIVA", pe, us, so);
            await RegistrarAsync(cs, idSol, idUsr);

            await using var c = NuevoContexto(cs);
            Assert.True(await Validador(c).TieneAutorizacionVigenteAsync(idUsr, idSol));
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    // ── TEST 8 — ANTI-FUGA CRÍTICO: A1 originada en S1 NO autoriza S2 ─────
    [Fact]
    public async Task Validador_ANTI_FUGA_A1_de_S1_no_autoriza_S2()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us, so) = (new List<long>(), new List<long>(), new List<long>());
        try
        {
            var (idS1, idUsr, _) = await SembrarAsync(cs, "ACTIVO", "ACTIVA", pe, us, so);
            await RegistrarAsync(cs, idS1, idUsr);

            // S2: MISMO usuario/persona, otra solicitud (se libera la anterior).
            await using (var c = NuevoContexto(cs))
                await c.Database.ExecuteSqlAsync(
                    $"UPDATE dbo.cartera_solicitudes_cupo SET estado_solicitud = 'RECHAZADA' WHERE id_solicitud = {idS1}");
            var idS2 = await SembrarSolicitudExtraAsync(cs, idUsr, so);

            await using var v = NuevoContexto(cs);
            Assert.False(await Validador(v).TieneAutorizacionVigenteAsync(idUsr, idS2));
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    // ── TEST 9 — A2 originada en S2 → autoriza S2 ; A1 sigue retenida ────
    [Fact]
    public async Task Validador_A2_autoriza_S2_y_A1_sigue_retenida()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us, so) = (new List<long>(), new List<long>(), new List<long>());
        try
        {
            var (idS1, idUsr, _) = await SembrarAsync(cs, "ACTIVO", "ACTIVA", pe, us, so);
            await RegistrarAsync(cs, idS1, idUsr);
            await using (var c = NuevoContexto(cs))
                await c.Database.ExecuteSqlAsync(
                    $"UPDATE dbo.cartera_solicitudes_cupo SET estado_solicitud = 'RECHAZADA' WHERE id_solicitud = {idS1}");

            var idS2 = await SembrarSolicitudExtraAsync(cs, idUsr, so);
            await RegistrarAsync(cs, idS2, idUsr);

            await using var v = NuevoContexto(cs);
            Assert.True(await Validador(v).TieneAutorizacionVigenteAsync(idUsr, idS2));
            // A1 físicamente retenida.
            Assert.True(await v.CarteraAutorizacionesConsultaRiesgo.AsNoTracking().AnyAsync(a => a.IdSolicitudOrigen == idS1));
            Assert.Equal(2, await v.CarteraAutorizacionesConsultaRiesgo.AsNoTracking()
                .CountAsync(a => a.IdUsuario == idUsr));
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    // ── TEST 10 — hash inconsistente → false ────────────────────────────
    [Fact]
    public async Task Validador_hash_inconsistente_false()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us, so) = (new List<long>(), new List<long>(), new List<long>());
        try
        {
            var (idSol, idUsr, _) = await SembrarAsync(cs, "ACTIVO", "ACTIVA", pe, us, so);
            await RegistrarAsync(cs, idSol, idUsr);
            await using (var c = NuevoContexto(cs))
                await c.Database.ExecuteSqlAsync(
                    $"UPDATE dbo.cartera_autorizacion_consulta_riesgo SET hash_texto = REPLICATE('0', 64) WHERE id_solicitud_origen = {idSol}");

            await using var v = NuevoContexto(cs);
            Assert.False(await Validador(v).TieneAutorizacionVigenteAsync(idUsr, idSol));
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    // ── TEST 11 — snapshot con SHA que no coincide → false ──────────────
    [Fact]
    public async Task Validador_snapshot_manipulado_false()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us, so) = (new List<long>(), new List<long>(), new List<long>());
        try
        {
            var (idSol, idUsr, _) = await SembrarAsync(cs, "ACTIVO", "ACTIVA", pe, us, so);
            await RegistrarAsync(cs, idSol, idUsr);
            await using (var c = NuevoContexto(cs))
                await c.Database.ExecuteSqlAsync(
                    $"UPDATE dbo.cartera_autorizacion_consulta_riesgo SET texto_snapshot = N'texto alterado' WHERE id_solicitud_origen = {idSol}");

            await using var v = NuevoContexto(cs);
            Assert.False(await Validador(v).TieneAutorizacionVigenteAsync(idUsr, idSol));
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    // ── TEST 12 — persona inactiva → false ─────────────────────────────
    [Fact]
    public async Task Validador_persona_inactiva_false()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us, so) = (new List<long>(), new List<long>(), new List<long>());
        try
        {
            var (idSol, idUsr, idPer) = await SembrarAsync(cs, "ACTIVO", "ACTIVA", pe, us, so);
            await RegistrarAsync(cs, idSol, idUsr);
            await using (var c = NuevoContexto(cs))
                await c.Database.ExecuteSqlAsync($"UPDATE dbo.personas SET estado = 'INACTIVA' WHERE id_persona = {idPer}");

            await using var v = NuevoContexto(cs);
            Assert.False(await Validador(v).TieneAutorizacionVigenteAsync(idUsr, idSol));
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    // ── TEST 13 — usuario inactivo → false ────────────────────────────
    [Fact]
    public async Task Validador_usuario_inactivo_false()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us, so) = (new List<long>(), new List<long>(), new List<long>());
        try
        {
            var (idSol, idUsr, _) = await SembrarAsync(cs, "ACTIVO", "ACTIVA", pe, us, so);
            await RegistrarAsync(cs, idSol, idUsr);
            await using (var c = NuevoContexto(cs))
                await c.Database.ExecuteSqlAsync($"UPDATE dbo.usuarios SET estado = 'BLOQUEADO' WHERE id_usuario = {idUsr}");

            await using var v = NuevoContexto(cs);
            Assert.False(await Validador(v).TieneAutorizacionVigenteAsync(idUsr, idSol));
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    // ── TEST 14 — sin consentimiento → false (fail-closed) ─────────────
    [Fact]
    public async Task Validador_sin_consentimiento_false()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us, so) = (new List<long>(), new List<long>(), new List<long>());
        try
        {
            var (idSol, idUsr, _) = await SembrarAsync(cs, "ACTIVO", "ACTIVA", pe, us, so);
            await using var v = NuevoContexto(cs);
            Assert.False(await Validador(v).TieneAutorizacionVigenteAsync(idUsr, idSol));
        }
        finally { await LimpiarAsync(cs, pe, us, so); }
    }

    // ══════════════════ P2-2 (XPAY-199) — pruebas PURAS del validador ═══════
    // (sin SQL real: cancelación y fallo operacional se prueban sin fixtures)

    // A — la cancelación NO se convierte silenciosamente en false: se propaga.
    [Fact]
    public async Task Validador_cancelacion_se_propaga_no_es_false()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await using var c = NuevoContexto("Server=localhost,59999;Database=nope;User Id=x;Password=y;Connect Timeout=1;TrustServerCertificate=True");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new AutorizacionConsultaRiesgoDurable(c, NullLogger<AutorizacionConsultaRiesgoDurable>.Instance)
                .TieneAutorizacionVigenteAsync(1, 1, cts.Token));
    }

    // B — un error operacional inesperado → fail-closed false + log genérico SIN
    //     identificadores de cliente. Conexión deliberadamente inválida (no es
    //     acceso SQL real, no es producción).
    [Fact]
    public async Task Validador_error_operacional_fail_closed_false_y_log_sin_pii()
    {
        var log = new CapturingLogger<AutorizacionConsultaRiesgoDurable>();
        await using var c = NuevoContexto("Server=localhost,59999;Database=nope;User Id=x;Password=y;Connect Timeout=1;TrustServerCertificate=True");

        var r = await new AutorizacionConsultaRiesgoDurable(c, log)
            .TieneAutorizacionVigenteAsync(123456, 987654, default);

        Assert.False(r);
        Assert.NotEmpty(log.Mensajes);
        foreach (var m in log.Mensajes)
        {
            Assert.Contains("Error al validar autorización durable de consulta de riesgo", m);
            Assert.DoesNotContain("123456", m);
            Assert.DoesNotContain("987654", m);
            Assert.DoesNotContain(CarteraAutorizacionConsultaRiesgoTextos.V1_HashSha256, m);
            Assert.DoesNotContain("AUTORIZO", m);
        }
    }

    private static AutorizacionConsultaRiesgoDurable Validador(XpayDbContext c)
        => new(c, NullLogger<AutorizacionConsultaRiesgoDurable>.Instance);

    // ════════════════════ infraestructura de test ════════════════════════

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL del consentimiento durable.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    private static async Task RegistrarAsync(string cs, long idSolicitud, long idUsuario)
    {
        await using var c = NuevoContexto(cs);
        var r = await new CarteraAutorizacionConsultaRiesgoStore(c)
            .RegistrarAceptacionAsync(idSolicitud, idUsuario, V1, null, default);
        Assert.Equal(ResultadoRegistroAutorizacion.Registrada, r);
    }

    private static async Task<(long idSolicitud, long idUsuario, long idPersona)> SembrarAsync(
        string cs, string estadoUsuario, string estadoPersona,
        List<long> personas, List<long> usuarios, List<long> solicitudes)
    {
        await using var ctx = NuevoContexto(cs);
        var ahora = DateTime.UtcNow;
        var sufijo = Guid.NewGuid().ToString("N")[..12];
        var idUnidad = await ctx.Database.SqlQueryRaw<long>(
            "SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL").SingleAsync();
        var idPolitica = await ctx.CarteraPoliticasCredito.AsNoTracking()
            .Where(p => p.Estado == "ACTIVO").OrderBy(p => p.IdPolitica).Select(p => p.IdPolitica).FirstAsync();

        var persona = new Persona
        {
            IdUnidadNegocio = idUnidad, TipoDocumento = "CC",
            NumeroDocumento = $"76{(uint)Guid.NewGuid().GetHashCode() % 10_000_000:D7}",
            PrimerNombre = "AutzTest", PrimerApellido = "Sintetico", Celular = "3000000000",
            Pais = "Colombia", Estado = estadoPersona, FechaCreacion = ahora,
        };
        ctx.Personas.Add(persona);
        await ctx.SaveChangesAsync();
        personas.Add(persona.IdPersona);

        var usuario = new Usuario
        {
            IdPersona = persona.IdPersona, NombreUsuario = $"autz_test_{sufijo}",
            PasswordHash = "x", Estado = estadoUsuario, FechaCreacion = ahora,
        };
        ctx.Usuarios.Add(usuario);
        await ctx.SaveChangesAsync();
        usuarios.Add(usuario.IdUsuario);

        var solicitud = NuevaSolicitud(usuario.IdUsuario, persona.IdPersona, idPolitica, ahora, sufijo);
        ctx.CarteraSolicitudesCupo.Add(solicitud);
        await ctx.SaveChangesAsync();
        solicitudes.Add(solicitud.IdSolicitud);

        return (solicitud.IdSolicitud, usuario.IdUsuario, persona.IdPersona);
    }

    private static async Task<long> SembrarSolicitudExtraAsync(string cs, long idUsuario, List<long> solicitudes)
    {
        await using var ctx = NuevoContexto(cs);
        var ahora = DateTime.UtcNow;
        var sufijo = Guid.NewGuid().ToString("N")[..12];
        var idPersona = await ctx.Usuarios.AsNoTracking().Where(u => u.IdUsuario == idUsuario).Select(u => u.IdPersona).SingleAsync();
        var idPolitica = await ctx.CarteraPoliticasCredito.AsNoTracking()
            .Where(p => p.Estado == "ACTIVO").OrderBy(p => p.IdPolitica).Select(p => p.IdPolitica).FirstAsync();

        var solicitud = NuevaSolicitud(idUsuario, idPersona, idPolitica, ahora, sufijo);
        ctx.CarteraSolicitudesCupo.Add(solicitud);
        await ctx.SaveChangesAsync();
        solicitudes.Add(solicitud.IdSolicitud);
        return solicitud.IdSolicitud;
    }

    private static CarteraSolicitudCupo NuevaSolicitud(long idUsuario, long idPersona, long idPolitica, DateTime ahora, string sufijo)
        => new()
        {
            IdUsuario = idUsuario, IdPersona = idPersona, MontoSolicitado = 500_000m,
            EstadoSolicitud = CarteraSolicitudCupoEstados.Recibida,
            DecisionCrediticia = CarteraDecisionCrediticia.Pendiente,
            IdPoliticaAplicada = idPolitica, CupoMinimoAplicado = 0m, CupoMaximoAplicado = 1_000_000m,
            EdadMinimaAplicada = 18, EdadMaximaAplicada = 65, NumeroIntento = 1,
            CorrelationId = $"autz-sol-{sufijo}", FechaSolicitud = ahora, FechaActualizacion = ahora,
        };

    private static async Task LimpiarAsync(string cs, List<long> personas, List<long> usuarios, List<long> solicitudes)
    {
        try
        {
            await using var ctx = NuevoContexto(cs);
            foreach (var id in usuarios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.cartera_autorizacion_consulta_riesgo WHERE id_usuario = {id}");
            foreach (var id in usuarios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.cartera_solicitudes_cupo WHERE id_usuario = {id}");
            foreach (var id in usuarios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.usuarios WHERE id_usuario = {id}");
            foreach (var id in personas)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.personas WHERE id_persona = {id}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[CarteraAutorizacionConsultaRiesgoStoreTests] cleanup parcial falló ({ex.GetType().Name}). " +
                $"personas=[{string.Join(",", personas)}] usuarios=[{string.Join(",", usuarios)}] solicitudes=[{string.Join(",", solicitudes)}]");
        }
    }
}

// Logger genérico que captura los mensajes ya formateados, para verificar
// ausencia de identificadores de cliente (mismo criterio que CapturingLogger
// de CarteraConsultaRiesgoServiceTests).
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Mensajes { get; } = new();

    IDisposable? ILogger.BeginScope<TState>(TState state) => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Mensajes.Add(formatter(state, exception));
}
