using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Integrations.MiDecisor;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// Real SQL only in the project's disposable integration database. There is no
// HTTP client, provider/token, host or production connection in this fixture.
// A missing connection is fatal in CI; local source/build checks do not run SQL.
[Collection("SqlIntegration")]
public sealed class MiDecisorConsultaExternaSqlTests
{
    private static readonly DateTime Ahora = new(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("CC", " cc ", "ID_CARD")]
    [InlineData("PASAPORTE", "PAS", "PASSPORT")]
    public async Task ImportaMetadataConAdminActivo_AliasLeeMismaHuella_YReplayNoDuplica(
        string tipoPersona, string tipoImportacion, string tipoVeriff)
    {
        if (!Conexion(out var cs)) return;
        await using var datos = new Datos(cs);
        var admin = await datos.CuentaAsync("SUPERUSUARIO");
        var titular = await datos.CuentaAsync();
        var numero = Documento();
        await datos.DocumentoAsync(titular, tipoPersona, numero, tipoVeriff, soloVerificado: true);
        await using var db = Contexto(cs);
        var store = Ledger(db);
        var evidencia = Evidencia();
        var fecha = Ahora.AddDays(-5);
        Assert.True(await store.RegistrarAsync(Registro(titular, admin.Usuario, tipoImportacion, ConPuntos(numero), fecha, evidencia)));
        Assert.False(await store.RegistrarAsync(Registro(titular, admin.Usuario, tipoPersona, numero, fecha, evidencia.ToUpperInvariant())));
        Assert.Equal(1, await FilasLedgerAsync(db, titular.Persona));
        var leida = await store.UltimaReferenciaAsync(tipoPersona, numero);
        Assert.Equal(fecha, leida);
        Assert.Equal(DateTimeKind.Utc, leida!.Value.Kind);
        Assert.Equal(leida, await store.UltimaReferenciaAsync(tipoImportacion, "\t" + ConPuntos(numero) + "\u00a0"));
        Assert.Equal(MiDecisorSujeto.Huella(tipoPersona, numero), await db.Database.SqlQueryRaw<string>(
            "SELECT huella_sujeto AS Value FROM dbo.midecisor_consultas_externas WHERE evidencia_sha256={0}", evidencia).SingleAsync());
        Assert.Equal("CONSULTA_SIN_DECISION", await db.Database.SqlQueryRaw<string>(
            "SELECT ambito AS Value FROM dbo.midecisor_consultas_externas WHERE evidencia_sha256={0}", evidencia).SingleAsync());
        Assert.Equal(admin.Usuario, await db.Database.SqlQueryRaw<long>(
            "SELECT registrado_por_usuario AS Value FROM dbo.midecisor_consultas_externas WHERE evidencia_sha256={0}", evidencia).SingleAsync());

        // Same evidence cannot be silently rebound or have its reference date
        // extended; a distinct manual attestation remains a separate row.
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RegistrarAsync(
            Registro(titular, admin.Usuario, tipoPersona, numero, fecha.AddDays(1), evidencia)));
        var manual = Registro(titular, admin.Usuario, tipoPersona, numero, Ahora.AddDays(-1), Evidencia(),
            exacta: false, origen: MiDecisorConsultaExternaStore.ConfirmacionManual);
        Assert.True(await store.RegistrarAsync(manual));
        Assert.Equal(Ahora.AddDays(-1), await store.UltimaReferenciaAsync(tipoImportacion, ConPuntos(numero)));
        Assert.Equal(2, await FilasLedgerAsync(db, titular.Persona));
        Assert.False(await db.Database.SqlQueryRaw<bool>(
            "SELECT fecha_exacta AS Value FROM dbo.midecisor_consultas_externas WHERE evidencia_sha256={0}", manual.EvidenciaSha256).SingleAsync());
        await SinArtefactosCreditoAsync(db, titular);
    }

    [Fact]
    public async Task FechaManualExactaFuturaOVinculoAjenoSeRechazanSinInsertar()
    {
        if (!Conexion(out var cs)) return;
        await using var datos = new Datos(cs);
        var admin = await datos.CuentaAsync("ADMIN_XPAY");
        var titular = await datos.CuentaAsync();
        var numero = Documento();
        await datos.DocumentoAsync(titular, "CC", numero, "ID_CARD", soloVerificado: true);
        await using var db = Contexto(cs);
        var store = Ledger(db);
        await Assert.ThrowsAsync<ArgumentException>(() => store.RegistrarAsync(Registro(titular, admin.Usuario, "CC", numero,
            Ahora, Evidencia(), exacta: true, origen: MiDecisorConsultaExternaStore.ConfirmacionManual)));
        await Assert.ThrowsAsync<ArgumentException>(() => store.RegistrarAsync(Registro(titular, admin.Usuario, "CC", numero,
            Ahora.AddTicks(1), Evidencia())));
        var otro = numero[..^1] + (numero[^1] == '9' ? '8' : '9');
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RegistrarAsync(Registro(titular, admin.Usuario, "CC", otro,
            Ahora, Evidencia())));
        Assert.Equal(0, await FilasLedgerAsync(db, titular.Persona));
        Assert.Null(await store.UltimaReferenciaAsync("CC", numero));
        var persona = await db.Personas.AsNoTracking().SingleAsync(p => p.IdPersona == titular.Persona);
        Assert.Null(persona.TipoDocumento); Assert.Null(persona.NumeroDocumento);
        Assert.Equal("\t" + ConPuntos(numero) + "\u00a0", persona.NumeroDocumentoVerificado);
        await SinArtefactosCreditoAsync(db, titular);
    }

    [Theory]
    [InlineData("SIN_ROL_ADMIN")]
    [InlineData("USUARIO_INACTIVO")]
    [InlineData("ROL_REVOCADO")]
    public async Task SoloAdministradorActivoConAsignacionVigentePuedeImportar(string caso)
    {
        if (!Conexion(out var cs)) return;
        await using var datos = new Datos(cs);
        var actor = await datos.CuentaAsync(caso == "SIN_ROL_ADMIN" ? "USUARIO_FINAL" : "SUPERUSUARIO");
        var titular = await datos.CuentaAsync();
        var numero = Documento();
        await datos.DocumentoAsync(titular, "CC", numero, "ID_CARD");
        await using var db = Contexto(cs);
        if (caso == "USUARIO_INACTIVO")
            await db.Usuarios.Where(u => u.IdUsuario == actor.Usuario).ExecuteUpdateAsync(s => s.SetProperty(u => u.Estado, "INACTIVO"));
        if (caso == "ROL_REVOCADO")
            await db.UsuarioRoles.Where(r => r.IdUsuario == actor.Usuario).ExecuteUpdateAsync(s => s.SetProperty(r => r.Estado, "INACTIVO"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Ledger(db).RegistrarAsync(
            Registro(titular, actor.Usuario, "CC", numero, Ahora, Evidencia())));
        Assert.Equal(0, await FilasLedgerAsync(db, titular.Persona));
        await SinArtefactosCreditoAsync(db, titular);
    }

    [Fact]
    public async Task DosImportacionesConcurrentesDeMismaEvidenciaInsertanUnaSolaFila()
    {
        if (!Conexion(out var cs)) return;
        await using var datos = new Datos(cs);
        var admin = await datos.CuentaAsync("SUPERUSUARIO");
        var titular = await datos.CuentaAsync();
        var numero = Documento();
        await datos.DocumentoAsync(titular, "CC", numero, "ID_CARD");
        var registro = Registro(titular, admin.Usuario, "CC", numero, Ahora, Evidencia());
        async Task<bool> ImportarAsync()
        {
            await using var db = Contexto(cs);
            return await Ledger(db).RegistrarAsync(registro);
        }
        var resultados = await Task.WhenAll(ImportarAsync(), ImportarAsync());
        Assert.Single(resultados, x => x); Assert.Single(resultados, x => !x);
        await using var consulta = Contexto(cs);
        Assert.Equal(1, await FilasLedgerAsync(consulta, titular.Persona));
        await SinArtefactosCreditoAsync(consulta, titular);
    }

    [Fact]
    public async Task ReferenciaExternaBloqueaOtroUsuarioDelMismoTitularSinFabricarResultadoNiV1()
    {
        if (!Conexion(out var cs)) return;
        await using var datos = new Datos(cs);
        var admin = await datos.CuentaAsync("SUPERUSUARIO");
        var origen = await datos.CuentaAsync();
        var otro = await datos.CuentaAsync();
        var numero = Documento();
        await datos.DocumentoAsync(origen, "CC", numero, "ID_CARD", soloVerificado: true);
        await datos.DocumentoAsync(otro, "CC", numero, "ID_CARD");
        var solicitud = await datos.SolicitudAsync(otro);
        await using var db = Contexto(cs);
        var ledger = Ledger(db);
        Assert.True(await ledger.RegistrarAsync(Registro(origen, admin.Usuario, "CC", numero, Ahora, Evidencia(),
            exacta: false, origen: MiDecisorConsultaExternaStore.ConfirmacionManual)));
        var identidad = Identidad(otro, numero);
        var store = new RiesgoProduccionStore(db, new ResolverSintetico(identidad), ledger);
        var snapshot = await store.CargarAsync(solicitud);
        Assert.NotNull(snapshot); Assert.Null(snapshot!.UltimaRespuestaUtc);
        Assert.Equal(Ahora, snapshot.UltimaConsultaExternaUtc);
        Assert.False(snapshot.TieneCupoQueBloquea); Assert.False(snapshot.TieneConsultaPendiente);
        Assert.False(ReglaReconsultaMiDecisor.Permite(snapshot, Ahora.AddMonths(6).AddTicks(-1)));
        Assert.True(ReglaReconsultaMiDecisor.Permite(snapshot, Ahora.AddMonths(6)));
        Assert.False(await db.CarteraAutorizacionesConsultaRiesgo.AnyAsync(a => a.IdUsuario == origen.Usuario || a.IdUsuario == otro.Usuario));
        var intento = Assert.Single(await db.CarteraSolicitudCupoIntentos.AsNoTracking().Where(i => i.IdSolicitud == solicitud).ToListAsync());
        Assert.Equal(CarteraIntentoFases.PreCall, intento.FaseIntento); Assert.Null(intento.FechaFin);
        Assert.Null(intento.ResultadoTecnico); Assert.False(intento.EsIntentoConResultadoUtil);
        Assert.Null(intento.ScoreRaw); Assert.Null(intento.MontoSugeridoRaw); Assert.Null(intento.P0ProviderRawJson);
        await SinArtefactosCreditoAsync(db, origen);
    }

    [Fact]
    public async Task DosUsuariosMismoDocumentoCanonico_TransicionConcurrenteTieneUnSoloGanador()
    {
        if (!Conexion(out var cs)) return;
        await using var datos = new Datos(cs);
        var primero = await datos.CuentaAsync(); var segundo = await datos.CuentaAsync();
        var numero = Documento();
        // This owner has only a verified number, with whitespace SQL TRIM
        // cannot normalize. The other owner has consolidated legal fields.
        await datos.DocumentoAsync(primero, "CC", numero, "ID_CARD", soloVerificado: true);
        await datos.DocumentoAsync(segundo, "CC", numero, "ID_CARD");
        var solicitud1 = await datos.SolicitudAsync(primero); var solicitud2 = await datos.SolicitudAsync(segundo);
        var listos = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var llegadas = 0;
        async Task<bool> IniciarAsync(PreparacionWalletSqlTests.Cuenta cuenta, long solicitud)
        {
            await using var db = Contexto(cs);
            var resolver = new ResolverSintetico(Identidad(cuenta, numero));
            var riesgo = new RiesgoProduccionStore(db, resolver, Ledger(db));
            var store = new CarteraConsultaRiesgoStore(db, resolver, riesgo);
            if (Interlocked.Increment(ref llegadas) == 2) listos.TrySetResult();
            await listos.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return await store.IntentarIniciarConsultaAsync(solicitud, cuenta.Usuario, Ahora, default);
        }
        var resultados = await Task.WhenAll(IniciarAsync(primero, solicitud1), IniciarAsync(segundo, solicitud2));
        Assert.Single(resultados, x => x); Assert.Single(resultados, x => !x);
        await using var consulta = Contexto(cs);
        var ids = new[] { solicitud1, solicitud2 };
        var estados = await consulta.CarteraSolicitudesCupo.AsNoTracking().Where(s => ids.Contains(s.IdSolicitud)).Select(s => s.EstadoSolicitud).ToListAsync();
        Assert.Single(estados, s => s == CarteraSolicitudCupoEstados.ConsultandoRiesgo);
        Assert.Single(estados, s => s == CarteraSolicitudCupoEstados.Recibida);
        var intentos = await consulta.CarteraSolicitudCupoIntentos.AsNoTracking().Where(i => ids.Contains(i.IdSolicitud)).ToListAsync();
        Assert.All(intentos, i =>
        {
            Assert.Equal(CarteraIntentoFases.PreCall, i.FaseIntento); Assert.Null(i.ResultadoTecnico);
            Assert.Null(i.FechaFin); Assert.False(i.EsIntentoConResultadoUtil); Assert.Null(i.ScoreRaw);
        });
        Assert.False(await consulta.CarteraAutorizacionesConsultaRiesgo.AnyAsync(a => a.IdUsuario == primero.Usuario || a.IdUsuario == segundo.Usuario));
        var snapshot = await new RiesgoProduccionStore(consulta, new ResolverSintetico(Identidad(segundo, numero)), Ledger(consulta)).CargarAsync(solicitud2);
        Assert.NotNull(snapshot); Assert.True(snapshot!.TieneConsultaPendiente);
        Assert.False(ReglaReconsultaMiDecisor.Permite(snapshot, Ahora));
    }

    private static MiDecisorConsultaExternaRegistro Registro(PreparacionWalletSqlTests.Cuenta titular, long admin, string tipo,
        string numero, DateTime fecha, string evidencia, bool exacta = true, string origen = MiDecisorConsultaExternaStore.ReciboDiagnostico) => new()
    {
        IdPersonaOrigen = titular.Persona, RegistradoPorUsuario = admin, TipoDocumento = tipo, NumeroDocumento = numero,
        FechaReferenciaUtc = fecha, FechaExacta = exacta, Origen = origen, EvidenciaSha256 = evidencia,
    };
    private static MiDecisorIdentidad Identidad(PreparacionWalletSqlTests.Cuenta cuenta, string numero) => new()
    {
        IdUsuario = cuenta.Usuario, IdPersona = cuenta.Persona, IdKycVerificacion = 1,
        TipoDocumento = "CC", NumeroDocumento = numero, TipoVeriff = "ID_CARD", PrimerApellido = "SINTETICO",
    };
    private sealed class ResolverSintetico(MiDecisorIdentidad identidad) : IMiDecisorIdentidadResolver
    {
        public Task<MiDecisorIdentidad?> ResolverAsync(long idUsuario, CancellationToken ct = default)
            => Task.FromResult(idUsuario == identidad.IdUsuario ? identidad : null);
    }
    private sealed class Reloj : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Ahora); }
    private static MiDecisorConsultaExternaStore Ledger(XpayDbContext db) => new(db, new Reloj());
    private static string Documento() => "8" + RandomNumberGenerator.GetInt32(100_000_000, 999_999_999).ToString(CultureInfo.InvariantCulture);
    private static string ConPuntos(string numero) => long.Parse(numero, CultureInfo.InvariantCulture).ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.');
    private static string Evidencia() => Convert.ToHexString(SHA256.HashData(Guid.NewGuid().ToByteArray())).ToLowerInvariant();
    private static XpayDbContext Contexto(string cs) => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);
    private static Task<int> FilasLedgerAsync(XpayDbContext db, long persona) => db.Database.SqlQueryRaw<int>(
        "SELECT COUNT(*) AS Value FROM dbo.midecisor_consultas_externas WHERE id_persona_origen={0}", persona).SingleAsync();
    private static async Task SinArtefactosCreditoAsync(XpayDbContext db, PreparacionWalletSqlTests.Cuenta cuenta)
    {
        Assert.False(await db.CarteraSolicitudesCupo.AnyAsync(s => s.IdUsuario == cuenta.Usuario));
        Assert.False(await db.CarteraAutorizacionesConsultaRiesgo.AnyAsync(a => a.IdUsuario == cuenta.Usuario));
        Assert.False(await db.CarteraCuposOrdinarios.AnyAsync(c => c.IdUsuario == cuenta.Usuario));
    }
    private static bool Conexion(out string cs)
    {
        cs = Environment.GetEnvironmentVariable("ConnectionStrings__XpayConnection") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        Assert.False(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")),
            "La conexión SQL de integración es obligatoria en CI para el ledger y la concurrencia por titular de MiDecisor.");
        return false;
    }

    private sealed class Datos(string cs) : IAsyncDisposable
    {
        private readonly PreparacionWalletSqlTests.Semillero semillero = new(cs);
        private readonly List<PreparacionWalletSqlTests.Cuenta> cuentas = [];
        private readonly List<long> solicitudes = [];
        public async Task<PreparacionWalletSqlTests.Cuenta> CuentaAsync(string rol = "USUARIO_FINAL")
        {
            var cuenta = await semillero.CuentaAsync(publica: false, rol: rol);
            cuentas.Add(cuenta); return cuenta;
        }
        public async Task DocumentoAsync(PreparacionWalletSqlTests.Cuenta cuenta, string tipo, string numero, string tipoVeriff, bool soloVerificado = false)
        {
            await using var db = Contexto(cs);
            var p = await db.Personas.SingleAsync(p => p.IdPersona == cuenta.Persona);
            p.TipoDocumento = soloVerificado ? null : tipo; p.NumeroDocumento = soloVerificado ? null : numero;
            p.TipoDocumentoVeriffRaw = tipoVeriff; p.NumeroDocumentoVerificado = "\t" + ConPuntos(numero) + "\u00a0";
            await db.SaveChangesAsync();
        }
        public async Task<long> SolicitudAsync(PreparacionWalletSqlTests.Cuenta cuenta)
        {
            await using var db = Contexto(cs);
            var politica = await db.CarteraPoliticasCredito.Where(p => p.Estado == "ACTIVO").OrderBy(p => p.IdPolitica).Select(p => p.IdPolitica).FirstAsync();
            var solicitud = new CarteraSolicitudCupo
            {
                IdUsuario = cuenta.Usuario, IdPersona = cuenta.Persona, EstadoSolicitud = CarteraSolicitudCupoEstados.Recibida,
                DecisionCrediticia = CarteraDecisionCrediticia.Pendiente, IdPoliticaAplicada = politica,
                CupoMinimoAplicado = 0m, CupoMaximoAplicado = 1_000_000m, EdadMinimaAplicada = 18, EdadMaximaAplicada = 99,
                NumeroIntento = 1, CorrelationId = "ledger-sql-" + Guid.NewGuid().ToString("N"), FechaSolicitud = Ahora, FechaActualizacion = Ahora,
            };
            db.CarteraSolicitudesCupo.Add(solicitud); await db.SaveChangesAsync(); solicitudes.Add(solicitud.IdSolicitud);
            db.CarteraSolicitudCupoIntentos.Add(new()
            {
                IdSolicitud = solicitud.IdSolicitud, NumeroIntento = 1, IdempotencyKey = Guid.NewGuid(), FechaInicio = Ahora,
                CorrelationId = solicitud.CorrelationId, FaseIntento = CarteraIntentoFases.PreCall, EsIntentoConResultadoUtil = false,
            });
            await db.SaveChangesAsync(); return solicitud.IdSolicitud;
        }
        public async ValueTask DisposeAsync()
        {
            await using var db = Contexto(cs);
            foreach (var cuenta in cuentas)
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE dbo.midecisor_consultas_externas WHERE id_persona_origen={cuenta.Persona} OR registrado_por_usuario={cuenta.Usuario}");
            await db.CarteraSolicitudCupoIntentos.Where(i => solicitudes.Contains(i.IdSolicitud)).ExecuteDeleteAsync();
            await db.CarteraSolicitudesCupo.Where(s => solicitudes.Contains(s.IdSolicitud)).ExecuteDeleteAsync();
            await semillero.DisposeAsync();
        }
    }
}
