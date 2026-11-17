using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Data;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// Diseño A — integración SQL de la consolidación del documento legal en el
// Caso 1 de KYC Veriff (ProcessVeriffWebhookAsync → ProcesarDecisionVeriffAsync).
// SIN red, SIN Veriff real, SIN PII real: personas sintéticas creadas y
// borradas por el propio test. Guard fail-closed del repo: local sin
// ConnectionStrings__XpayConnection → early-return (PASS); en CI sin la
// variable → FALLA. Reutiliza la colección SqlIntegration.
[Collection("SqlIntegration")]
public sealed class KycDocumentoLegalSqlTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";
    private const string ApellidoVeriff = "PRUEBA SINTETICA";

    [Fact]
    public async Task Caso1_establece_CC_sin_tocar_PrimerApellido_y_audita_sin_numero_ni_apellido()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us) = (new List<long>(), new List<long>());
        try
        {
            var numero = NumeroAleatorio();
            var s = await SembrarAsync(cs, pe, us);

            Assert.True(await WebhookAsync(cs, s.SessionId, numero));

            await using var v = NuevoContexto(cs);
            var p = await v.Personas.AsNoTracking().SingleAsync(x => x.IdPersona == s.IdPersona);
            Assert.True(p.IdentidadVerificada);
            Assert.Equal("CC", p.TipoDocumento);
            Assert.Equal(numero, p.NumeroDocumento);
            Assert.Equal(ApellidoVeriff, p.ApellidoVerificadoCompleto);
            Assert.Null(p.PrimerApellido);   // no se infiere desde lastName

            var aud = Assert.Single(await AuditoriasAsync(v, s.IdPersona, "KYC_DOCUMENTO_LEGAL_ESTABLECIDO"));
            Assert.Equal("VACIO", aud.ValorAnterior);
            Assert.Equal("TIPO:CC", aud.ValorNuevo);
            await AssertSinPiiEnAuditoriaAsync(v, s.IdPersona, numero);
        }
        finally { await LimpiarAsync(cs, pe, us); }
    }

    [Fact]
    public async Task PrimerApellido_existente_no_se_sobrescribe()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us) = (new List<long>(), new List<long>());
        try
        {
            var s = await SembrarAsync(cs, pe, us, primerApellido: "Existente");

            Assert.True(await WebhookAsync(cs, s.SessionId, NumeroAleatorio()));

            await using var v = NuevoContexto(cs);
            var p = await v.Personas.AsNoTracking().SingleAsync(x => x.IdPersona == s.IdPersona);
            Assert.Equal("Existente", p.PrimerApellido);
        }
        finally { await LimpiarAsync(cs, pe, us); }
    }

    [Fact]
    public async Task Documento_declarado_distinto_no_se_sobrescribe()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us) = (new List<long>(), new List<long>());
        try
        {
            var declarado = NumeroAleatorio();
            var numero    = NumeroAleatorio();
            var s = await SembrarAsync(cs, pe, us, tipo: "CC", numero: declarado);

            Assert.True(await WebhookAsync(cs, s.SessionId, numero));

            await using var v = NuevoContexto(cs);
            var p = await v.Personas.AsNoTracking().SingleAsync(x => x.IdPersona == s.IdPersona);
            Assert.True(p.IdentidadVerificada);      // el Caso 1 se conserva
            Assert.Equal("CC", p.TipoDocumento);
            Assert.Equal(declarado, p.NumeroDocumento);
            Assert.Single(await AuditoriasAsync(v, s.IdPersona, "KYC_DOCUMENTO_DECLARADO_DIFIERE"));
            Assert.Empty(await AuditoriasAsync(v, s.IdPersona, "KYC_DOCUMENTO_LEGAL_ESTABLECIDO"));
            await AssertSinPiiEnAuditoriaAsync(v, s.IdPersona, numero, declarado);
        }
        finally { await LimpiarAsync(cs, pe, us); }
    }

    [Fact]
    public async Task Documento_declarado_parcial_no_se_completa()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us) = (new List<long>(), new List<long>());
        try
        {
            var s = await SembrarAsync(cs, pe, us, tipo: "CC");

            Assert.True(await WebhookAsync(cs, s.SessionId, NumeroAleatorio()));

            await using var v = NuevoContexto(cs);
            var p = await v.Personas.AsNoTracking().SingleAsync(x => x.IdPersona == s.IdPersona);
            Assert.Null(p.NumeroDocumento);
            Assert.Single(await AuditoriasAsync(v, s.IdPersona, "KYC_DOCUMENTO_DECLARADO_DIFIERE"));
        }
        finally { await LimpiarAsync(cs, pe, us); }
    }

    [Fact]
    public async Task Documento_legal_de_otra_cuenta_deja_la_segunda_en_revision()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us) = (new List<long>(), new List<long>());
        try
        {
            var numero = NumeroAleatorio();
            var otra = await SembrarAsync(cs, pe, us, tipo: "CC", numero: numero);
            var s    = await SembrarAsync(cs, pe, us);

            Assert.True(await WebhookAsync(cs, s.SessionId, numero));

            await using var v = NuevoContexto(cs);
            var p = await v.Personas.AsNoTracking().SingleAsync(x => x.IdPersona == s.IdPersona);
            Assert.False(p.IdentidadVerificada);
            Assert.Null(p.NumeroDocumentoVerificado);
            Assert.Null(p.TipoDocumento);
            Assert.Null(p.NumeroDocumento);
            Assert.Single(await AuditoriasAsync(v, s.IdPersona, "KYC_IDENTIDAD_DOCUMENTO_DUPLICADO"));
            var kyc = await v.KycVerificaciones.AsNoTracking().SingleAsync(x => x.SessionId == s.SessionId);
            Assert.Equal("EN_REVISION", kyc.EstadoKyc);
            Assert.Equal("DOCUMENTO_ASOCIADO_A_OTRA_CUENTA", kyc.Reason);
            Assert.Equal("EN_REVISION", (await v.Usuarios.AsNoTracking().SingleAsync(x => x.IdUsuario == s.IdUsuario)).EstadoKycActual);
            Assert.Equal(numero, (await v.Personas.AsNoTracking().SingleAsync(x => x.IdPersona == otra.IdPersona)).NumeroDocumento);
            await AssertSinPiiEnAuditoriaAsync(v, s.IdPersona, numero);
        }
        finally { await LimpiarAsync(cs, pe, us); }
    }

    [Fact]
    public async Task Duplicado_tardio_por_IX_personas_documento_conserva_la_aprobacion_KYC()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us) = (new List<long>(), new List<long>());
        try
        {
            var numero = NumeroAleatorio();
            var s = await SembrarAsync(cs, pe, us);

            // Otro escritor (sin lock de identidad) inserta el mismo CC entre la
            // consulta previa y el SaveChanges: solo el índice lo detecta.
            var interceptor = new InsertarDuplicadoAntesDeGuardar(cs, numero, pe);
            Assert.True(await WebhookAsync(cs, s.SessionId, numero, interceptor: interceptor));
            Assert.True(interceptor.Disparado);

            await using var v = NuevoContexto(cs);
            var p = await v.Personas.AsNoTracking().SingleAsync(x => x.IdPersona == s.IdPersona);
            Assert.True(p.IdentidadVerificada);
            Assert.Equal(numero, p.NumeroDocumentoVerificado);
            Assert.Null(p.TipoDocumento);
            Assert.Null(p.NumeroDocumento);

            var usuario = await v.Usuarios.AsNoTracking().SingleAsync(x => x.IdUsuario == s.IdUsuario);
            Assert.Equal("APROBADO", usuario.EstadoKycActual);
            var kyc = await v.KycVerificaciones.AsNoTracking().SingleAsync(x => x.SessionId == s.SessionId);
            Assert.Equal("APROBADO", kyc.EstadoKyc);

            Assert.Single(await AuditoriasAsync(v, s.IdPersona, "KYC_IDENTIDAD_CONSOLIDADA"));
            Assert.Single(await AuditoriasAsync(v, s.IdPersona, "KYC_DOCUMENTO_LEGAL_DUPLICADO"));
            Assert.Empty(await AuditoriasAsync(v, s.IdPersona, "KYC_DOCUMENTO_LEGAL_ESTABLECIDO"));
            await AssertSinPiiEnAuditoriaAsync(v, s.IdPersona, numero);
        }
        finally { await LimpiarAsync(cs, pe, us); }
    }

    [Theory]
    [InlineData("RESIDENCE_PERMIT", "CO", "1234567", "KYC_DOCUMENTO_TIPO_NO_MAPEABLE")]
    [InlineData("ID_CARD",          "VE", "1234567", "KYC_DOCUMENTO_TIPO_NO_MAPEABLE")]
    // numero_documento_verificado también es VARCHAR(30): un número > 30 no
    // llega a este punto en SQL (se cubre en TipoDocumentoVeriffMapperTests).
    [InlineData("ID_CARD",          "CO", "AB-",     "KYC_DOCUMENTO_NUMERO_INVALIDO")]
    public async Task Tipo_no_mapeable_o_numero_invalido_no_establece_documento(
        string tipo, string pais, string numeroBase, string accionEsperada)
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us) = (new List<long>(), new List<long>());
        try
        {
            // Sufijo aleatorio para no chocar con el índice de documento
            // verificado entre ejecuciones.
            var numero = numeroBase + NumeroAleatorio();
            var s = await SembrarAsync(cs, pe, us);

            Assert.True(await WebhookAsync(cs, s.SessionId, numero, tipo, pais));

            await using var v = NuevoContexto(cs);
            var p = await v.Personas.AsNoTracking().SingleAsync(x => x.IdPersona == s.IdPersona);
            Assert.True(p.IdentidadVerificada);
            Assert.Null(p.TipoDocumento);
            Assert.Null(p.NumeroDocumento);
            Assert.Single(await AuditoriasAsync(v, s.IdPersona, accionEsperada));
            await AssertSinPiiEnAuditoriaAsync(v, s.IdPersona, numero);
        }
        finally { await LimpiarAsync(cs, pe, us); }
    }

    [Fact]
    public async Task Reentrega_identica_es_idempotente()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us) = (new List<long>(), new List<long>());
        try
        {
            var numero = NumeroAleatorio();
            var attempt = Guid.NewGuid().ToString();
            var s = await SembrarAsync(cs, pe, us);

            Assert.True(await WebhookAsync(cs, s.SessionId, numero, attemptId: attempt));
            Assert.True(await WebhookAsync(cs, s.SessionId, numero, attemptId: attempt));

            await using var v = NuevoContexto(cs);
            Assert.Single(await AuditoriasAsync(v, s.IdPersona, "KYC_DOCUMENTO_LEGAL_ESTABLECIDO"));
        }
        finally { await LimpiarAsync(cs, pe, us); }
    }

    [Fact]
    public async Task Concurrencia_mismo_documento_en_dos_personas_establece_solo_una()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us) = (new List<long>(), new List<long>());
        try
        {
            var numero = NumeroAleatorio();
            var a = await SembrarAsync(cs, pe, us);
            var b = await SembrarAsync(cs, pe, us);

            await Task.WhenAll(
                Task.Run(() => WebhookAsync(cs, a.SessionId, numero)),
                Task.Run(() => WebhookAsync(cs, b.SessionId, numero)));

            await using var v = NuevoContexto(cs);
            var ids = new[] { a.IdPersona, b.IdPersona };
            var personas = await v.Personas.AsNoTracking().Where(x => ids.Contains(x.IdPersona)).ToListAsync();
            Assert.Single(personas, x => x.NumeroDocumento == numero && x.TipoDocumento == "CC");
            Assert.Single(personas, x => x.IdentidadVerificada);
            var estados = await v.Usuarios.AsNoTracking().Where(x => ids.Contains(x.IdPersona))
                .Select(x => x.EstadoKycActual).ToListAsync();
            Assert.Single(estados, x => x == "APROBADO");
            Assert.Single(estados, x => x == "EN_REVISION");
            Assert.Single(await v.Auditorias.AsNoTracking()
                .Where(x => ids.Contains(x.IdPersona!.Value) && x.Accion == "KYC_DOCUMENTO_LEGAL_ESTABLECIDO").ToListAsync());
        }
        finally { await LimpiarAsync(cs, pe, us); }
    }

    [Fact]
    public async Task Alta_y_Veriff_en_unidades_distintas_reservan_el_documento_para_una_sola_cuenta()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us) = (new List<long>(), new List<long>());
        var codigoUnidadAlta = $"DOC_TEST_{Guid.NewGuid():N}";
        try
        {
            var numero = NumeroAleatorio();
            var s = await SembrarAsync(cs, pe, us);
            await using var altaDb = NuevoContexto(cs);
            await altaDb.Database.ExecuteSqlAsync(
                $"INSERT INTO dbo.unidades_negocio (codigo, nombre) VALUES ({codigoUnidadAlta}, 'Documento test')");
            var unidad = await altaDb.Database.SqlQuery<long>(
                $"SELECT id_unidad_negocio AS Value FROM dbo.unidades_negocio WHERE codigo = {codigoUnidadAlta}").SingleAsync();
            var alta = new RegistroUsuarioFinalService(altaDb, Xpay.Api.Tests.Common.AmbienteXpayTests.Ambiente("QA"));
            var solicitud = new Xpay.Api.DTOs.RegistroUsuarioFinalRequest
            {
                IdUnidadNegocio = unidad, TipoDocumento = " cc ", NumeroDocumento = numero.Insert(2, "."),
                PrimerNombre = "ANA", PrimerApellido = "PRUEBA", Celular = "3000000000",
                Usuario = $"altadoc_{Guid.NewGuid():N}", Password = "UnaClaveSintetica1!",
            };

            var altaTask = alta.RegistrarAsync(solicitud);
            var veriffTask = WebhookAsync(cs, s.SessionId, numero);
            try
            {
                var id = await altaTask;
                us.Add(id);
                pe.Add((await altaDb.Usuarios.AsNoTracking().SingleAsync(u => u.IdUsuario == id)).IdPersona);
            }
            catch (InvalidOperationException ex)
            {
                Assert.Equal("Ya existe una persona registrada con este documento.", ex.Message);
            }
            Assert.True(await veriffTask);

            await using var v = NuevoContexto(cs);
            Assert.Equal(1, await v.Personas.AsNoTracking().CountAsync(p => p.NumeroDocumento == numero));
            var usuario = await v.Usuarios.AsNoTracking().SingleAsync(u => u.IdUsuario == s.IdUsuario);
            Assert.Equal(altaTask.IsCompletedSuccessfully ? "EN_REVISION" : "APROBADO", usuario.EstadoKycActual);
        }
        finally
        {
            await LimpiarAsync(cs, pe, us);
            await using var limpiar = NuevoContexto(cs);
            await limpiar.Database.ExecuteSqlAsync($"DELETE FROM dbo.unidades_negocio WHERE codigo = {codigoUnidadAlta}");
        }
    }

    [Fact]
    public async Task Documento_Veriff_sin_tipo_legal_reserva_el_numero_para_la_cuenta_verificada()
    {
        if (!TryConnString(out var cs)) return;
        var (pe, us) = (new List<long>(), new List<long>());
        try
        {
            var numero = NumeroAleatorio();
            var s = await SembrarAsync(cs, pe, us);
            Assert.True(await WebhookAsync(cs, s.SessionId, numero, tipo: "ID_CARD", pais: "VE"));

            await using var v = NuevoContexto(cs);
            var persona = await v.Personas.AsNoTracking().SingleAsync(p => p.IdPersona == s.IdPersona);
            Assert.True(persona.IdentidadVerificada);
            Assert.Null(persona.TipoDocumento);
            Assert.True(await DocumentoCuenta.ExisteOtraCuentaAsync(v, null, "CE", numero));
        }
        finally { await LimpiarAsync(cs, pe, us); }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private sealed record Semilla(long IdUsuario, long IdPersona, string SessionId);

    private static string NumeroAleatorio() => $"88{(uint)Guid.NewGuid().GetHashCode() % 100_000_000:D8}";

    private static async Task<bool> WebhookAsync(
        string cs, string sessionId, string numero, string tipo = "ID_CARD", string pais = "CO", string? attemptId = null,
        IInterceptor? interceptor = null)
    {
        var body = JsonSerializer.Serialize(new
        {
            status = "success",
            verification = new
            {
                id           = sessionId,
                attemptId    = attemptId ?? Guid.NewGuid().ToString(),
                status       = "approved",
                decisionTime = "2026-10-28T10:00:00Z",
                person       = new { firstName = "ANA MARIA", lastName = ApellidoVeriff, dateOfBirth = "1990-01-01" },
                document     = new { type = tipo, number = numero, country = pais },
            },
        });

        await using var db = NuevoContexto(cs, interceptor);
        var servicio = new KycService(db, new ConfigurationBuilder().Build(), new SinHttp(), NullLogger<KycService>.Instance,
            Xpay.Api.Tests.Common.AmbienteXpayTests.Ambiente("QA"));
        return (await servicio.ProcessVeriffWebhookAsync(body)).Processed;
    }

    private static Task<List<Auditoria>> AuditoriasAsync(XpayDbContext v, long idPersona, string accion) =>
        v.Auditorias.AsNoTracking().Where(a => a.IdPersona == idPersona && a.Accion == accion).ToListAsync();

    private static async Task AssertSinPiiEnAuditoriaAsync(XpayDbContext v, long idPersona, params string[] numeros)
    {
        var auds = await v.Auditorias.AsNoTracking().Where(a => a.IdPersona == idPersona).ToListAsync();
        foreach (var a in auds)
        {
            var texto = $"{a.ValorAnterior}|{a.ValorNuevo}|{a.Observacion}";
            foreach (var n in numeros) Assert.DoesNotContain(n, texto);
            Assert.DoesNotContain("PRUEBA", texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SINTETICA", texto, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class SinHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            throw new InvalidOperationException("Estas pruebas no deben llamar a Veriff.");
    }

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas SQL de documento legal KYC.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs, IInterceptor? interceptor = null)
    {
        var opciones = new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs);
        if (interceptor != null) opciones.AddInterceptors(interceptor);
        return new(opciones.Options);
    }

    // Inserta, una sola vez y por otra conexión, una Persona con el mismo CC
    // justo antes del primer SaveChanges del servicio.
    private sealed class InsertarDuplicadoAntesDeGuardar(string cs, string numero, List<long> personas) : SaveChangesInterceptor
    {
        public bool Disparado { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (!Disparado)
            {
                Disparado = true;
                await using var otro = NuevoContexto(cs);
                var idUnidad = await otro.Database.SqlQueryRaw<long>(
                    "SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL").SingleAsync(ct);
                var p = new Persona
                {
                    IdUnidadNegocio = idUnidad, TipoDocumento = "CC", NumeroDocumento = numero,
                    Celular = "3000000000", Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = DateTime.UtcNow,
                };
                otro.Personas.Add(p);
                await otro.SaveChangesAsync(ct);
                personas.Add(p.IdPersona);
            }
            return result;
        }
    }

    // Persona + usuario + sesión Veriff PENDIENTE, todo SINTÉTICO.
    private static async Task<Semilla> SembrarAsync(
        string cs, List<long> personas, List<long> usuarios,
        string? tipo = null, string? numero = null, string? primerApellido = null)
    {
        await using var ctx = NuevoContexto(cs);
        var ahora = DateTime.UtcNow;
        var sufijo = Guid.NewGuid().ToString("N")[..12];
        var idUnidad = await ctx.Database.SqlQueryRaw<long>(
            "SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL").SingleAsync();

        var persona = new Persona
        {
            IdUnidadNegocio = idUnidad, TipoDocumento = tipo, NumeroDocumento = numero,
            PrimerApellido = primerApellido, Celular = "3000000000",
            Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
        };
        ctx.Personas.Add(persona);
        await ctx.SaveChangesAsync();
        personas.Add(persona.IdPersona);

        var usuario = new Usuario
        {
            IdPersona = persona.IdPersona, NombreUsuario = $"kycdoc_test_{sufijo}",
            PasswordHash = "x", Estado = "ACTIVO", FechaCreacion = ahora,
        };
        ctx.Usuarios.Add(usuario);
        await ctx.SaveChangesAsync();
        usuarios.Add(usuario.IdUsuario);

        var sessionId = Guid.NewGuid().ToString();
        ctx.KycVerificaciones.Add(new KycVerificacion
        {
            IdUsuario = usuario.IdUsuario, IdPersona = persona.IdPersona, Proveedor = "VERIFF",
            EstadoKyc = "PENDIENTE", SessionId = sessionId, EsActual = true, FechaCreacion = ahora,
        });
        await ctx.SaveChangesAsync();

        return new Semilla(usuario.IdUsuario, persona.IdPersona, sessionId);
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
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.usuario_roles WHERE id_usuario = {id}");
            foreach (var id in personas)
            {
                await ctx.Database.ExecuteSqlAsync($"DELETE s FROM dbo.wallet_saldos s JOIN dbo.wallets w ON w.id_wallet = s.id_wallet WHERE w.id_persona = {id}");
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.wallets WHERE id_persona = {id}");
            }
            foreach (var id in usuarios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.usuarios WHERE id_usuario = {id}");
            foreach (var id in personas)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.personas WHERE id_persona = {id}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[KycDocumentoLegalSqlTests] cleanup parcial falló ({ex.GetType().Name}). " +
                $"personas=[{string.Join(",", personas)}] usuarios=[{string.Join(",", usuarios)}]");
        }
    }
}
