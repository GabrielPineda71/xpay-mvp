using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// Solo datos sintéticos en la base de integración prevista por el proyecto.
// No levanta host, no resuelve credenciales ni llama proveedores externos.
[Collection("SqlIntegration")]
public sealed class PerfilPrimerApellidoSqlTests
{
    [Fact]
    public async Task CompletaSoloElCampoFaltante_SinModificarIdentidad_YReenvioEsIdempotente()
    {
        if (!TryConexion(out var cs)) return;
        await using var actor = await Actor.CrearAsync(cs);
        await using var db = Contexto(cs);
        var servicio = new PerfilService(db);
        Assert.True((await servicio.ObtenerAsync(actor.IdPersona)).PuedeCompletarPrimerApellido);
        var r = await servicio.CompletarPrimerApellidoAsync(actor.IdUsuario, actor.IdPersona, new("  De la Hoz  "));
        Assert.Equal("De la Hoz", r.PrimerApellido);
        Assert.False(r.PuedeCompletarPrimerApellido);
        var persona = await db.Personas.AsNoTracking().SingleAsync(p => p.IdPersona == actor.IdPersona);
        var fecha = persona.FechaActualizacion;
        Assert.True(persona.IdentidadVerificada);
        Assert.Equal("VERIFF", persona.IdentidadVerificadaProveedor);
        Assert.Equal("DE LA HOZ GARCÍA", persona.ApellidoVerificadoCompleto);
        Assert.Equal(actor.Documento, persona.NumeroDocumentoVerificado);
        Assert.Equal("ID_CARD", persona.TipoDocumentoVeriffRaw);
        Assert.Null(persona.TipoDocumento);
        Assert.Null(persona.NumeroDocumento);
        Assert.Null(persona.SegundoApellido);

        await servicio.CompletarPrimerApellidoAsync(actor.IdUsuario, actor.IdPersona, new("de la hoz"));
        await Assert.ThrowsAsync<PrimerApellidoYaRegistradoException>(() =>
            servicio.CompletarPrimerApellidoAsync(actor.IdUsuario, actor.IdPersona, new("De")));
        await using var consulta = Contexto(cs);
        persona = await consulta.Personas.AsNoTracking().SingleAsync(p => p.IdPersona == actor.IdPersona);
        Assert.Equal("De la Hoz", persona.PrimerApellido);
        Assert.Equal(fecha, persona.FechaActualizacion);
        var auditoria = Assert.Single(await consulta.Auditorias.Where(a => a.IdUsuario == actor.IdUsuario
            && a.Accion == PerfilService.AccionPrimerApellido).ToListAsync());
        Assert.Null(auditoria.ValorAnterior);
        Assert.Null(auditoria.ValorNuevo);
        Assert.False(await consulta.Wallets.AnyAsync(w => w.IdPersona == actor.IdPersona));
        Assert.False(await consulta.CarteraSolicitudesCupo.AnyAsync(s => s.IdUsuario == actor.IdUsuario));
        Assert.False(await consulta.CarteraAutorizacionesConsultaRiesgo.AnyAsync(a => a.IdUsuario == actor.IdUsuario));
    }

    [Fact]
    public async Task RechazaOtraCuentaCorreoSinVerificarYApellidoDiscordante_SinEscribir()
    {
        if (!TryConexion(out var cs)) return;
        await using var actor = await Actor.CrearAsync(cs);
        await using var db = Contexto(cs);
        var servicio = new PerfilService(db);
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            servicio.CompletarPrimerApellidoAsync(-1, actor.IdPersona, new("De la Hoz")));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.CompletarPrimerApellidoAsync(actor.IdUsuario, actor.IdPersona, new("Otro")));
        var usuario = await db.Usuarios.SingleAsync(u => u.IdUsuario == actor.IdUsuario);
        usuario.EmailVerificado = false;
        await db.SaveChangesAsync();
        Assert.False((await servicio.ObtenerAsync(actor.IdPersona)).PuedeCompletarPrimerApellido);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            servicio.CompletarPrimerApellidoAsync(actor.IdUsuario, actor.IdPersona, new("De la Hoz")));
        await using var consulta = Contexto(cs);
        Assert.Null((await consulta.Personas.SingleAsync(p => p.IdPersona == actor.IdPersona)).PrimerApellido);
        Assert.False(await consulta.Auditorias.AnyAsync(a => a.IdUsuario == actor.IdUsuario));
    }

    [Fact]
    public async Task DosPeticionesConcurrentesDelMismoDato_GuardanUnaSolaDeclaracion()
    {
        if (!TryConexion(out var cs)) return;
        await using var actor = await Actor.CrearAsync(cs);
        async Task GuardarAsync()
        {
            await using var db = Contexto(cs);
            await new PerfilService(db).CompletarPrimerApellidoAsync(actor.IdUsuario, actor.IdPersona, new("De la Hoz"));
        }
        await Task.WhenAll(GuardarAsync(), GuardarAsync());
        await using var consulta = Contexto(cs);
        Assert.Equal("De la Hoz", (await consulta.Personas.SingleAsync(p => p.IdPersona == actor.IdPersona)).PrimerApellido);
        Assert.Equal(1, await consulta.Auditorias.CountAsync(a => a.IdUsuario == actor.IdUsuario && a.Accion == PerfilService.AccionPrimerApellido));
    }

    private static bool TryConexion(out string cs)
    {
        cs = Environment.GetEnvironmentVariable("ConnectionStrings__XpayConnection") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        Assert.False(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")),
            "La base de integración SQL es obligatoria en CI.");
        return false;
    }
    private static XpayDbContext Contexto(string cs) => new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    private sealed class Actor(string cs, long idUsuario, long idPersona, string documento) : IAsyncDisposable
    {
        public long IdUsuario { get; } = idUsuario;
        public long IdPersona { get; } = idPersona;
        public string Documento { get; } = documento;
        public static async Task<Actor> CrearAsync(string cs)
        {
            await using var db = Contexto(cs);
            var sufijo = Guid.NewGuid().ToString("N");
            var unidad = await db.Database.SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL").SingleAsync();
            var documento = "9" + Random.Shared.NextInt64(100_000_000_000, 999_999_999_999).ToString();
            var persona = new Persona
            {
                IdUnidadNegocio = unidad, Celular = "3000000000", Email = "apellido." + sufijo + "@example.test",
                IdentidadVerificada = true, IdentidadVerificadaProveedor = "VERIFF", IdentidadVerificadaFecha = DateTime.UtcNow,
                NombreVerificadoCompleto = "PERSONA SINTÉTICA", ApellidoVerificadoCompleto = "DE LA HOZ GARCÍA",
                TipoDocumentoVeriffRaw = "ID_CARD", NumeroDocumentoVerificado = documento, FechaCreacion = DateTime.UtcNow,
            };
            db.Personas.Add(persona); await db.SaveChangesAsync();
            var usuario = new Usuario { IdPersona = persona.IdPersona, NombreUsuario = "apellido_" + sufijo,
                PasswordHash = "hash-sintético", EmailVerificado = true, EstadoKycActual = "APROBADO", FechaCreacion = DateTime.UtcNow };
            db.Usuarios.Add(usuario); await db.SaveChangesAsync();
            db.KycVerificaciones.Add(new KycVerificacion { IdUsuario = usuario.IdUsuario, IdPersona = persona.IdPersona,
                Proveedor = "VERIFF", EstadoKyc = "APROBADO", EsActual = true, Decision = "approved", SessionId = "sintetica-" + sufijo, FechaCreacion = DateTime.UtcNow });
            await db.SaveChangesAsync();
            return new(cs, usuario.IdUsuario, persona.IdPersona, documento);
        }
        public async ValueTask DisposeAsync()
        {
            await using var db = Contexto(cs);
            await db.KycVerificaciones.Where(k => k.IdUsuario == IdUsuario).ExecuteDeleteAsync();
            await db.Auditorias.Where(a => a.IdUsuario == IdUsuario).ExecuteDeleteAsync();
            await db.Usuarios.Where(u => u.IdUsuario == IdUsuario).ExecuteDeleteAsync();
            await db.Personas.Where(p => p.IdPersona == IdPersona).ExecuteDeleteAsync();
        }
    }
}
