using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// XPAY-717 — integración SQL del store productivo de MiDecisor: búsqueda
// TRANSVERSAL por titular documental (tipo + número) entre usuarios, personas,
// solicitudes y unidades de negocio; conjunto R de resultados; cupos que
// bloquean. SIN red, SIN MiDecisor, SIN PII real: datos sintéticos creados y
// borrados por el propio test. Guard del repo: local sin
// ConnectionStrings__XpayConnection → early-return (PASS); en CI sin la
// variable → FALLA. Reutiliza la colección SqlIntegration.
[Collection("SqlIntegration")]
public sealed class RiesgoProduccionStoreTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";

    // Titular A en la unidad XPAY_COL con una respuesta previa; titular B con el
    // MISMO documento en otra unidad, otro usuario y una solicitud nueva.
    [Fact]
    public async Task Mismo_documento_en_otra_unidad_usuario_y_solicitud_aporta_la_ultima_respuesta()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var seed = new Semilla(cs, s);
            var doc = Documento();
            var fechaFin = new DateTime(2026, 8, 31, 10, 0, 0);

            var a = await seed.TitularAsync(await seed.UnidadXpayAsync(), doc);
            await seed.IntentoAsync(await seed.SolicitudAsync(a, "RECHAZADA"),
                CarteraIntentoFases.Finalizado, CarteraConsultaRiesgoResultados.Aceptada, fechaFin);

            var b = await seed.TitularAsync(await seed.UnidadNuevaAsync(), doc);
            var solicitudB = await seed.SolicitudAsync(b, "RECIBIDA");

            await using var ctx = NuevoContexto(cs);
            var snap = await new RiesgoProduccionStore(ctx).CargarAsync(solicitudB);

            Assert.NotNull(snap);
            Assert.Equal(fechaFin, snap!.UltimaRespuestaUtc);
            Assert.False(snap.TieneCupoQueBloquea);
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Theory]
    [InlineData(CarteraConsultaRiesgoResultados.SinInformacion, true)]
    [InlineData(CarteraConsultaRiesgoResultados.RechazadaProveedor, true)]
    [InlineData(CarteraConsultaRiesgoResultados.ErrorProtocolo, false)]
    [InlineData(CarteraConsultaRiesgoResultados.ResultadoIncierto, false)]
    [InlineData(CarteraConsultaRiesgoResultados.ErrorAutenticacion, false)]
    public async Task Solo_el_conjunto_R_abre_ventana(string resultado, bool abre)
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var seed = new Semilla(cs, s);
            var unidad = await seed.UnidadXpayAsync();
            var t = await seed.TitularAsync(unidad, Documento());
            var fechaFin = new DateTime(2026, 9, 1, 8, 0, 0);
            await seed.IntentoAsync(await seed.SolicitudAsync(t, "ERROR_PROVEEDOR"),
                CarteraIntentoFases.Finalizado, resultado, fechaFin);
            var nueva = await seed.SolicitudAsync(t, "RECIBIDA");

            await using var ctx = NuevoContexto(cs);
            var snap = await new RiesgoProduccionStore(ctx).CargarAsync(nueva);

            Assert.Equal(abre ? fechaFin : (DateTime?)null, snap!.UltimaRespuestaUtc);
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Fact]
    public async Task Intento_sin_fecha_fin_no_abre_ventana()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var seed = new Semilla(cs, s);
            var t = await seed.TitularAsync(await seed.UnidadXpayAsync(), Documento());
            await seed.IntentoAsync(await seed.SolicitudAsync(t, "ERROR_PROVEEDOR"),
                CarteraIntentoFases.EnvioIncierto, resultado: null, fechaFin: null);
            var nueva = await seed.SolicitudAsync(t, "RECIBIDA");

            await using var ctx = NuevoContexto(cs);
            Assert.Null((await new RiesgoProduccionStore(ctx).CargarAsync(nueva))!.UltimaRespuestaUtc);
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Fact]
    public async Task Usa_la_respuesta_mas_reciente_del_titular()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var seed = new Semilla(cs, s);
            var doc = Documento();
            var t = await seed.TitularAsync(await seed.UnidadXpayAsync(), doc);
            var antigua = new DateTime(2025, 1, 10, 9, 0, 0);
            var reciente = new DateTime(2026, 2, 20, 9, 0, 0);
            await seed.IntentoAsync(await seed.SolicitudAsync(t, "RECHAZADA"),
                CarteraIntentoFases.Finalizado, CarteraConsultaRiesgoResultados.Aceptada, antigua);
            await seed.IntentoAsync(await seed.SolicitudAsync(t, "RECHAZADA"),
                CarteraIntentoFases.Finalizado, CarteraConsultaRiesgoResultados.RechazadaProveedor, reciente);
            var nueva = await seed.SolicitudAsync(t, "RECIBIDA");

            await using var ctx = NuevoContexto(cs);
            Assert.Equal(reciente, (await new RiesgoProduccionStore(ctx).CargarAsync(nueva))!.UltimaRespuestaUtc);
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Theory]
    [InlineData("ACTIVO", true)]
    [InlineData("SUSPENDIDO", true)]
    [InlineData("CANCELADO", false)]
    public async Task Cupo_de_otro_usuario_del_mismo_documento(string estadoCupo, bool bloquea)
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var seed = new Semilla(cs, s);
            var doc = Documento();
            var a = await seed.TitularAsync(await seed.UnidadXpayAsync(), doc);
            await seed.CupoAsync(a, estadoCupo);
            var b = await seed.TitularAsync(await seed.UnidadNuevaAsync(), doc);
            var solicitudB = await seed.SolicitudAsync(b, "RECIBIDA");

            await using var ctx = NuevoContexto(cs);
            Assert.Equal(bloquea, (await new RiesgoProduccionStore(ctx).CargarAsync(solicitudB))!.TieneCupoQueBloquea);
        }
        finally { await LimpiarAsync(cs, s); }
    }

    [Fact]
    public async Task Persona_sin_documento_devuelve_null()
    {
        if (!TryConnString(out var cs)) return;
        var s = new Sembrados();
        try
        {
            var seed = new Semilla(cs, s);
            var t = await seed.TitularAsync(await seed.UnidadXpayAsync(), documento: null);
            var solicitud = await seed.SolicitudAsync(t, "RECIBIDA");

            await using var ctx = NuevoContexto(cs);
            Assert.Null(await new RiesgoProduccionStore(ctx).CargarAsync(solicitud));
        }
        finally { await LimpiarAsync(cs, s); }
    }

    // ════════════════════ infraestructura de test ════════════════════════

    private static string Documento() => $"77{(uint)Guid.NewGuid().GetHashCode() % 10_000_000:D7}";

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;

        var enCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.False(enCi, $"{EnvConnString} es obligatoria en CI para las pruebas del store productivo de MiDecisor.");
        return false;
    }

    private static XpayDbContext NuevoContexto(string cs)
        => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    private sealed record Titular(long IdUsuario, long IdPersona, long IdWallet, long IdUnidad);

    private sealed class Sembrados
    {
        public List<long> Unidades { get; } = new();
        public List<long> Personas { get; } = new();
        public List<long> Usuarios { get; } = new();
        public List<long> Wallets { get; } = new();
        public List<long> Solicitudes { get; } = new();
        public List<long> Cupos { get; } = new();
    }

    private sealed class Semilla(string cs, Sembrados creados)
    {
        public async Task<long> UnidadXpayAsync()
        {
            await using var ctx = NuevoContexto(cs);
            return await ctx.Database
                .SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL")
                .SingleAsync();
        }

        public async Task<long> UnidadNuevaAsync()
        {
            await using var ctx = NuevoContexto(cs);
            var codigo = $"T717_{Guid.NewGuid().ToString("N")[..12]}";
            var ids = await ctx.Database
                .SqlQueryRaw<long>(
                    "INSERT INTO unidades_negocio (codigo, nombre) OUTPUT INSERTED.id_unidad_negocio AS Value VALUES ({0}, {1})",
                    codigo, "XPAY-717 test")
                .ToListAsync();
            var id = Assert.Single(ids);
            creados.Unidades.Add(id);
            return id;
        }

        public async Task<Titular> TitularAsync(long idUnidad, string? documento)
        {
            await using var ctx = NuevoContexto(cs);
            var ahora = DateTime.UtcNow;
            var sufijo = Guid.NewGuid().ToString("N")[..12];

            var persona = new Persona
            {
                IdUnidadNegocio = idUnidad,
                TipoDocumento   = documento is null ? null : "CC",
                NumeroDocumento = documento,
                PrimerNombre    = "Riesgo717",
                PrimerApellido  = "Sintetico",
                Celular         = "3000000000",
                Pais            = "Colombia",
                Estado          = "ACTIVA",
                FechaCreacion   = ahora,
            };
            ctx.Personas.Add(persona);
            await ctx.SaveChangesAsync();
            creados.Personas.Add(persona.IdPersona);

            var usuario = new Usuario
            {
                IdPersona     = persona.IdPersona,
                NombreUsuario = $"riesgo717_{sufijo}",
                PasswordHash  = "x",
                Estado        = "ACTIVO",
                FechaCreacion = ahora,
            };
            ctx.Usuarios.Add(usuario);
            await ctx.SaveChangesAsync();
            creados.Usuarios.Add(usuario.IdUsuario);

            var wallet = new Wallet
            {
                IdUnidadNegocio = idUnidad,
                TipoWallet      = "PERSONA",
                IdPersona       = persona.IdPersona,
                NombreWallet    = $"w717_{sufijo}",
                Estado          = "ACTIVA",
                FechaCreacion   = ahora,
            };
            ctx.Wallets.Add(wallet);
            await ctx.SaveChangesAsync();
            creados.Wallets.Add(wallet.IdWallet);

            return new Titular(usuario.IdUsuario, persona.IdPersona, wallet.IdWallet, idUnidad);
        }

        public async Task<long> SolicitudAsync(Titular t, string estado)
        {
            await using var ctx = NuevoContexto(cs);
            var idPolitica = await ctx.CarteraPoliticasCredito.AsNoTracking()
                .Where(p => p.Estado == "ACTIVO").OrderBy(p => p.IdPolitica).Select(p => p.IdPolitica).FirstAsync();
            var ahora = DateTime.UtcNow;

            var solicitud = new CarteraSolicitudCupo
            {
                IdUsuario          = t.IdUsuario,
                IdPersona          = t.IdPersona,
                EstadoSolicitud    = estado,
                DecisionCrediticia = "PENDIENTE",
                IdPoliticaAplicada = idPolitica,
                CupoMinimoAplicado = 0m,
                CupoMaximoAplicado = 10_000_000m,
                EdadMinimaAplicada = 18,
                EdadMaximaAplicada = 99,
                NumeroIntento      = 1,
                CorrelationId      = $"r717-{Guid.NewGuid().ToString("N")[..12]}",
                FechaSolicitud     = ahora,
                FechaActualizacion = ahora,
            };
            ctx.CarteraSolicitudesCupo.Add(solicitud);
            await ctx.SaveChangesAsync();
            creados.Solicitudes.Add(solicitud.IdSolicitud);
            return solicitud.IdSolicitud;
        }

        public async Task IntentoAsync(long idSolicitud, string fase, string? resultado, DateTime? fechaFin)
        {
            await using var ctx = NuevoContexto(cs);
            ctx.CarteraSolicitudCupoIntentos.Add(new CarteraSolicitudCupoIntento
            {
                IdSolicitud               = idSolicitud,
                NumeroIntento             = 1,
                IdempotencyKey            = Guid.NewGuid(),
                FechaInicio               = (fechaFin ?? DateTime.UtcNow).AddMinutes(-1),
                FechaFin                  = fechaFin,
                ResultadoTecnico          = resultado,
                CorrelationId             = $"r717-i-{Guid.NewGuid().ToString("N")[..12]}",
                EsIntentoConResultadoUtil = resultado is CarteraConsultaRiesgoResultados.Aceptada
                                                    or CarteraConsultaRiesgoResultados.SinInformacion,
                FaseIntento               = fase,
            });
            await ctx.SaveChangesAsync();
        }

        public async Task CupoAsync(Titular t, string estado)
        {
            await using var ctx = NuevoContexto(cs);
            var ahora = DateTime.UtcNow;
            var cupo = new CarteraCupoOrdinario
            {
                IdUsuario       = t.IdUsuario,
                IdWallet        = t.IdWallet,
                CupoAprobado    = 1_000_000m,
                CupoUsado       = 0m,
                Estado          = estado,
                FechaAprobacion = ahora,
                CreatedAt       = ahora,
            };
            ctx.CarteraCuposOrdinarios.Add(cupo);
            await ctx.SaveChangesAsync();
            creados.Cupos.Add(cupo.IdCupo);
        }
    }

    private static async Task LimpiarAsync(string cs, Sembrados creados)
    {
        if (string.IsNullOrWhiteSpace(cs)) return;
        try
        {
            await using var ctx = NuevoContexto(cs);
            foreach (var id in creados.Solicitudes)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.cartera_solicitud_cupo_intentos WHERE id_solicitud = {id}");
            foreach (var id in creados.Solicitudes)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.cartera_solicitudes_cupo WHERE id_solicitud = {id}");
            foreach (var id in creados.Cupos)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.cartera_cupos_ordinarios WHERE id_cupo = {id}");
            foreach (var id in creados.Wallets)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.wallet_saldos WHERE id_wallet = {id}");
            foreach (var id in creados.Wallets)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.wallets WHERE id_wallet = {id}");
            foreach (var id in creados.Usuarios)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.usuarios WHERE id_usuario = {id}");
            foreach (var id in creados.Personas)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.personas WHERE id_persona = {id}");
            foreach (var id in creados.Unidades)
                await ctx.Database.ExecuteSqlAsync($"DELETE FROM dbo.unidades_negocio WHERE id_unidad_negocio = {id}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[RiesgoProduccionStoreTests] cleanup parcial falló ({ex.GetType().Name}). " +
                $"personas=[{string.Join(",", creados.Personas)}] usuarios=[{string.Join(",", creados.Usuarios)}] " +
                $"solicitudes=[{string.Join(",", creados.Solicitudes)}] unidades=[{string.Join(",", creados.Unidades)}]");
        }
    }
}
