using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Authorization;
using Xpay.Api.Data;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xpay.Api.Tests.Common;
using Xpay.Api.Tests.Integrations.MiDecisor;
using Xunit;

namespace Xpay.Api.Tests.Services;

// XPAY-669 — integración SQL de la preparación QA de identidad de prueba
// (QaIdentidadPruebaStore + QaIdentidadPruebaService + KycAprobado). SIN red,
// SIN Veriff, SIN MiDecisor, SIN PII real: personas sintéticas creadas y
// borradas por el propio test. Guard fail-closed del repo: local sin
// ConnectionStrings__XpayConnection → early-return (PASS); en CI sin la
// variable → FALLA. Reutiliza la colección SqlIntegration.
[Collection("SqlIntegration")]
public sealed class QaIdentidadPruebaStoreTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";

    [Fact]
    public async Task Caso_feliz_escribe_procedencia_qa_kyc_y_auditoria_sin_datos_veriff()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us) = (new List<long>(), new List<long>());
        try
        {
            var (idDestino, idPersona) = await SembrarAsync(cs, pe, us);
            var (idAdmin, _) = await SembrarAsync(cs, pe, us);

            await using (var c = NuevoContexto(cs))
                Assert.Equal(ResultadoPreparacionIdentidadQa.Preparada,
                    await Servicio(c, idDestino).PrepararAsync(idDestino, idAdmin));

            await using var v = NuevoContexto(cs);
            var persona = await v.Personas.AsNoTracking().SingleAsync(p => p.IdPersona == idPersona);
            Assert.True(persona.IdentidadVerificada);
            Assert.Equal("SIMULACION_QA", persona.IdentidadVerificadaProveedor);
            Assert.NotNull(persona.IdentidadVerificadaFecha);
            Assert.Null(persona.NombreVerificadoCompleto);
            Assert.Null(persona.ApellidoVerificadoCompleto);
            Assert.Null(persona.TipoDocumentoVeriffRaw);
            Assert.Null(persona.NumeroDocumentoVerificado);

            var usuario = await v.Usuarios.AsNoTracking().SingleAsync(u => u.IdUsuario == idDestino);
            Assert.Equal("APROBADO", usuario.EstadoKycActual);
            Assert.NotNull(usuario.FechaKycActualizacion);

            var kyc = await v.KycVerificaciones.AsNoTracking().Where(k => k.IdUsuario == idDestino).ToListAsync();
            var actual = Assert.Single(kyc, k => k.EsActual);
            Assert.Equal("SIMULACION_QA", actual.Proveedor);
            Assert.Equal("APROBADO", actual.EstadoKyc);
            Assert.Equal("SIMULACION_QA", actual.Decision);
            Assert.Equal(QaIdentidadPruebaConstantes.Reason, actual.Reason);

            var aud = Assert.Single(await v.Auditorias.AsNoTracking()
                .Where(a => a.IdPersona == idPersona && a.Accion == "QA_PREPARAR_IDENTIDAD_PRUEBA").ToListAsync());
            Assert.Equal(idAdmin, aud.IdUsuario);
            Assert.Equal("KYC", aud.Modulo);
            Assert.Equal("personas", aud.Entidad);
            Assert.Equal(idPersona.ToString(), aud.IdEntidad);
            Assert.Contains("SIMULACION_QA", aud.ValorNuevo);
            Assert.DoesNotContain(persona.NumeroDocumento!, $"{aud.ValorAnterior}{aud.ValorNuevo}{aud.Observacion}");
            Assert.DoesNotContain(persona.PrimerApellido!, $"{aud.ValorAnterior}{aud.ValorNuevo}{aud.Observacion}");
        }
        finally { await LimpiarAsync(cs, pe, us); }
    }

    [Fact]
    public async Task Repeticion_es_idempotente_sin_filas_ni_timestamps_nuevos()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us) = (new List<long>(), new List<long>());
        try
        {
            var (idDestino, idPersona) = await SembrarAsync(cs, pe, us);
            var (idAdmin, _) = await SembrarAsync(cs, pe, us);

            await using (var c = NuevoContexto(cs))
                Assert.Equal(ResultadoPreparacionIdentidadQa.Preparada, await Servicio(c, idDestino).PrepararAsync(idDestino, idAdmin));

            DateTime? fechaAntes;
            await using (var v1 = NuevoContexto(cs))
                fechaAntes = (await v1.Personas.AsNoTracking().SingleAsync(p => p.IdPersona == idPersona)).IdentidadVerificadaFecha;

            await using (var c2 = NuevoContexto(cs))
                Assert.Equal(ResultadoPreparacionIdentidadQa.YaPreparada, await Servicio(c2, idDestino).PrepararAsync(idDestino, idAdmin));

            await using var v = NuevoContexto(cs);
            Assert.Equal(1, await v.KycVerificaciones.CountAsync(k => k.IdUsuario == idDestino));
            Assert.Equal(1, await v.Auditorias.CountAsync(a => a.IdPersona == idPersona && a.Accion == "QA_PREPARAR_IDENTIDAD_PRUEBA"));
            Assert.Equal(fechaAntes, (await v.Personas.AsNoTracking().SingleAsync(p => p.IdPersona == idPersona)).IdentidadVerificadaFecha);
        }
        finally { await LimpiarAsync(cs, pe, us); }
    }

    [Fact]
    public async Task Identidad_veriff_existente_rechazo_sin_cambios()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us) = (new List<long>(), new List<long>());
        try
        {
            var (idDestino, idPersona) = await SembrarAsync(cs, pe, us, veriff: true);
            var (idAdmin, _) = await SembrarAsync(cs, pe, us);

            await using (var c = NuevoContexto(cs))
                Assert.Equal(ResultadoPreparacionIdentidadQa.IdentidadProtegida, await Servicio(c, idDestino).PrepararAsync(idDestino, idAdmin));

            await using var v = NuevoContexto(cs);
            var persona = await v.Personas.AsNoTracking().SingleAsync(p => p.IdPersona == idPersona);
            Assert.Equal("VERIFF", persona.IdentidadVerificadaProveedor);
            Assert.Equal(0, await v.KycVerificaciones.CountAsync(k => k.IdUsuario == idDestino));
            Assert.Equal(0, await v.Auditorias.CountAsync(a => a.IdPersona == idPersona && a.Accion == "QA_PREPARAR_IDENTIDAD_PRUEBA"));
        }
        finally { await LimpiarAsync(cs, pe, us); }
    }

    [Fact]
    public async Task Revalidacion_en_transaccion_rechaza_sesion_veriff_sin_escrituras()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us) = (new List<long>(), new List<long>());
        try
        {
            var (idDestino, idPersona) = await SembrarAsync(cs, pe, us);
            var (idAdmin, _) = await SembrarAsync(cs, pe, us);
            await using (var s = NuevoContexto(cs))
            {
                s.KycVerificaciones.Add(new KycVerificacion
                {
                    IdUsuario = idDestino, IdPersona = idPersona, Proveedor = "VERIFF", EstadoKyc = "PENDIENTE",
                    EsActual = true, FechaCreacion = DateTime.UtcNow,
                });
                await s.SaveChangesAsync();
            }

            await using (var c = NuevoContexto(cs))
                Assert.False(await new QaIdentidadPruebaStore(c).AplicarPreparacionAsync(
                    new AplicacionPreparacionIdentidadQa(idDestino, idPersona, idAdmin, DateTime.UtcNow), default));

            await using var v = NuevoContexto(cs);
            Assert.False((await v.Personas.AsNoTracking().SingleAsync(p => p.IdPersona == idPersona)).IdentidadVerificada);
            var kyc = Assert.Single(await v.KycVerificaciones.AsNoTracking().Where(k => k.IdUsuario == idDestino).ToListAsync());
            Assert.True(kyc.EsActual);
            Assert.Equal("VERIFF", kyc.Proveedor);
        }
        finally { await LimpiarAsync(cs, pe, us); }
    }

    [Fact]
    public async Task KycAprobado_tiene_exito_despues_de_preparar()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us) = (new List<long>(), new List<long>());
        try
        {
            var (idDestino, _) = await SembrarAsync(cs, pe, us);
            var (idAdmin, _) = await SembrarAsync(cs, pe, us);

            await using (var antes = NuevoContexto(cs))
                Assert.False(await KycAprobadoAsync(antes, idDestino));

            await using (var c = NuevoContexto(cs))
                Assert.Equal(ResultadoPreparacionIdentidadQa.Preparada, await Servicio(c, idDestino).PrepararAsync(idDestino, idAdmin));

            await using var despues = NuevoContexto(cs);
            Assert.True(await KycAprobadoAsync(despues, idDestino));
        }
        finally { await LimpiarAsync(cs, pe, us); }
    }

    // XPAY-717 - en ambiente productivo la identidad SIMULACION_QA no satisface
    // KycAprobado (exige VERIFF), aunque en QA sí lo haga.
    [Fact]
    public async Task KycAprobado_en_produccion_rechaza_identidad_simulacion_qa()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us) = (new List<long>(), new List<long>());
        try
        {
            var (idDestino, _) = await SembrarAsync(cs, pe, us);
            var (idAdmin, _) = await SembrarAsync(cs, pe, us);

            await using (var c = NuevoContexto(cs))
                Assert.Equal(ResultadoPreparacionIdentidadQa.Preparada, await Servicio(c, idDestino).PrepararAsync(idDestino, idAdmin));

            await using (var qa = NuevoContexto(cs))
                Assert.True(await KycAprobadoAsync(qa, idDestino, "QA"));

            await using var prod = NuevoContexto(cs);
            Assert.False(await KycAprobadoAsync(prod, idDestino, "Production"));
        }
        finally { await LimpiarAsync(cs, pe, us); }
    }

    private static async Task<bool> KycAprobadoAsync(XpayDbContext db, long idUsuario, string ambiente = "QA")
    {
        var req = new KycAprobadoRequirement();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("idUsuario", idUsuario.ToString()) }, "test"));
        var ctx = new AuthorizationHandlerContext(new[] { req }, user, resource: null);
        await new KycAprobadoAuthorizationHandler(db, AmbienteXpayTests.Ambiente(ambiente)).HandleAsync(ctx);
        return ctx.HasSucceeded;
    }

    private static QaIdentidadPruebaService Servicio(XpayDbContext db, long idPermitido) =>
        new(new QaIdentidadPruebaStore(db),
            new FakeConfiguration(new Dictionary<string, string?>
            {
                [QaIdentidadPruebaConfig.EnvEnabled]          = "true",
                [QaIdentidadPruebaConfig.EnvAllowedUsuarioId] = idPermitido.ToString(),
            }),
            TimeProvider.System,
            AmbienteXpayTests.Ambiente("QA"));

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL de preparación QA de identidad.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    // Persona + usuario SINTÉTICOS (documento aleatorio, apellido ficticio).
    private static async Task<(long idUsuario, long idPersona)> SembrarAsync(
        string cs, List<long> personas, List<long> usuarios, bool veriff = false)
    {
        await using var ctx = NuevoContexto(cs);
        var ahora = DateTime.UtcNow;
        var sufijo = Guid.NewGuid().ToString("N")[..12];
        var idUnidad = await ctx.Database.SqlQueryRaw<long>(
            "SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL").SingleAsync();

        var persona = new Persona
        {
            IdUnidadNegocio = idUnidad, TipoDocumento = "CC",
            NumeroDocumento = $"77{(uint)Guid.NewGuid().GetHashCode() % 10_000_000:D7}",
            PrimerNombre = "QaIdTest", PrimerApellido = "Sintetico", Celular = "3000000000",
            Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
        };
        if (veriff)
        {
            persona.IdentidadVerificada          = true;
            persona.IdentidadVerificadaProveedor = "VERIFF";
            persona.IdentidadVerificadaFecha     = ahora;
            persona.NumeroDocumentoVerificado    = persona.NumeroDocumento;
        }
        ctx.Personas.Add(persona);
        await ctx.SaveChangesAsync();
        personas.Add(persona.IdPersona);

        var usuario = new Usuario
        {
            IdPersona = persona.IdPersona, NombreUsuario = $"qaid_test_{sufijo}",
            PasswordHash = "x", Estado = "ACTIVO", FechaCreacion = ahora,
        };
        ctx.Usuarios.Add(usuario);
        await ctx.SaveChangesAsync();
        usuarios.Add(usuario.IdUsuario);

        return (usuario.IdUsuario, persona.IdPersona);
    }

    private static async Task LimpiarAsync(string cs, List<long> personas, List<long> usuarios)
    {
        try
        {
            await using var ctx = NuevoContexto(cs);
            foreach (var id in personas)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.auditoria WHERE id_persona = {id}");
            foreach (var id in usuarios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.auditoria WHERE id_usuario = {id}");
            foreach (var id in usuarios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.kyc_verificaciones WHERE id_usuario = {id}");
            foreach (var id in usuarios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.usuarios WHERE id_usuario = {id}");
            foreach (var id in personas)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.personas WHERE id_persona = {id}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[QaIdentidadPruebaStoreTests] cleanup parcial falló ({ex.GetType().Name}). " +
                $"personas=[{string.Join(",", personas)}] usuarios=[{string.Join(",", usuarios)}]");
        }
    }
}
