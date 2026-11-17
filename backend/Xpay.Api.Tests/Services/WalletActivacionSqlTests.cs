using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

[Collection("SqlIntegration")]
public sealed class WalletActivacionSqlTests
{
    private const string EnvConnString = "ConnectionStrings__XpayConnection";

    [Theory]
    [InlineData("VERIFF", false, true, true)]
    [InlineData("VERIFF", true, false, true)]
    [InlineData("VERIFF", true, true, false)]
    [InlineData("SIMULACION_QA", true, true, true)]
    public async Task Sin_evidencia_completa_no_activa(string proveedor, bool vigente, bool correo, bool whatsapp)
    {
        if (!TryConnString(out var cs)) return;
        var cuenta = await SembrarAsync(cs, WalletActivacionService.PendienteVerificacion, proveedor, vigente, correo, whatsapp);
        try
        {
            await using var db = NuevoContexto(cs);
            var estado = await Servicio(db).ActivarAsync(cuenta.Usuario, cuenta.Persona, default);
            Assert.NotNull(estado);
            Assert.False(estado.WalletActiva);
            Assert.False(estado.PuedeActivar);
            Assert.Equal(WalletActivacionService.PendienteVerificacion,
                await db.Wallets.Where(w => w.IdPersona == cuenta.Persona).Select(w => w.Estado).SingleAsync());
            Assert.False(await db.Auditorias.AnyAsync(a => a.IdUsuario == cuenta.Usuario));
        }
        finally { await LimpiarAsync(cs, cuenta); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(WalletActivacionService.PendienteVerificacion)]
    public async Task Activaciones_concurrentes_con_evidencia_crean_un_solo_saldo_y_una_auditoria(string? walletPrevia)
    {
        if (!TryConnString(out var cs)) return;
        var cuenta = await SembrarAsync(cs, walletPrevia);
        try
        {
            await using var primero = NuevoContexto(cs);
            await using var segundo = NuevoContexto(cs);
            var resultados = await Task.WhenAll(
                Servicio(primero).ActivarAsync(cuenta.Usuario, cuenta.Persona, default),
                Servicio(segundo).ActivarAsync(cuenta.Usuario, cuenta.Persona, default));

            Assert.All(resultados, e => Assert.True(e is
                { WalletActiva: true, IdentidadVerificada: true, EmailVerificado: true, WhatsAppVerificado: true }));
            await using var v = NuevoContexto(cs);
            var wallet = await v.Wallets.SingleAsync(w => w.IdPersona == cuenta.Persona);
            Assert.Equal("ACTIVA", wallet.Estado);
            if (cuenta.Wallet is not null) Assert.Equal(cuenta.Wallet.Value, wallet.IdWallet);
            var saldo = await v.WalletSaldos.SingleAsync(s => s.IdWallet == wallet.IdWallet);
            Assert.Equal(0m, saldo.SaldoDisponible);
            Assert.Equal(1, await v.Auditorias.CountAsync(a => a.IdUsuario == cuenta.Usuario && a.Accion == "ACTIVAR_WALLET_PERSONA"));
        }
        finally { await LimpiarAsync(cs, cuenta); }
    }

    [Theory]
    [InlineData("ACTIVA", true, false)]
    [InlineData("BLOQUEADA", false, true)]
    public async Task Activar_no_cambia_una_wallet_existente_activa_o_restringida(string estadoActual, bool activa, bool restringida)
    {
        if (!TryConnString(out var cs)) return;
        var cuenta = await SembrarAsync(cs, estadoActual, correo: false, whatsapp: false);
        try
        {
            await using var db = NuevoContexto(cs);
            Assert.Null(await Servicio(db).ActivarAsync(cuenta.Usuario, cuenta.Persona + 1, default));
            var estado = await Servicio(db).ActivarAsync(cuenta.Usuario, cuenta.Persona, default);
            Assert.NotNull(estado);
            Assert.True(estado.IdentidadVerificada);
            Assert.Equal(activa, estado.WalletActiva);
            Assert.Equal(restringida, estado.WalletRestringida);
            Assert.False(estado.PuedeActivar);
            Assert.Equal(estadoActual,
                await db.Wallets.Where(w => w.IdPersona == cuenta.Persona).Select(w => w.Estado).SingleAsync());
            Assert.False(await db.Auditorias.AnyAsync(a => a.IdUsuario == cuenta.Usuario));
        }
        finally { await LimpiarAsync(cs, cuenta); }
    }

    private sealed record Cuenta(long Usuario, long Persona, long? Wallet);

    private static WalletActivacionService Servicio(XpayDbContext db) =>
        new(db, new ContactoVerificacionService(db, [], TimeProvider.System));

    private static async Task<Cuenta> SembrarAsync(string cs, string? estadoWallet, string proveedor = "VERIFF",
        bool vigente = true, bool correo = true, bool whatsapp = true)
    {
        await using var db = NuevoContexto(cs);
        var unidad = await db.Database.SqlQueryRaw<long>(
            "SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = 'XPAY_COL'").SingleAsync();
        var ahora = DateTime.UtcNow;
        var persona = new Persona
        {
            IdUnidadNegocio = unidad, FechaCreacion = ahora, Celular = "3000000000",
            IdentidadVerificada = true, IdentidadVerificadaProveedor = proveedor,
        };
        db.Personas.Add(persona);
        await db.SaveChangesAsync();
        var usuario = new Usuario
        {
            IdPersona = persona.IdPersona, NombreUsuario = $"wact_{Guid.NewGuid():N}",
            PasswordHash = "fixture", Estado = EstadosUsuario.Activo, FechaCreacion = ahora,
            EstadoKycActual = "APROBADO", EmailVerificado = correo, CelularVerificado = whatsapp,
        };
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        db.KycVerificaciones.Add(new KycVerificacion
        {
            IdUsuario = usuario.IdUsuario, IdPersona = persona.IdPersona, Proveedor = proveedor,
            EsActual = vigente, EstadoKyc = "APROBADO", FechaCreacion = ahora,
        });
        Wallet? wallet = null;
        if (estadoWallet is not null)
        {
            wallet = new Wallet
            {
                IdPersona = persona.IdPersona, TipoWallet = "PERSONA", IdUnidadNegocio = unidad,
                Estado = estadoWallet, FechaCreacion = ahora,
            };
            db.Wallets.Add(wallet);
        }
        await db.SaveChangesAsync();
        return new Cuenta(usuario.IdUsuario, persona.IdPersona, wallet?.IdWallet);
    }

    private static XpayDbContext NuevoContexto(string cs) =>
        new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    private static bool TryConnString(out string cs)
    {
        cs = Environment.GetEnvironmentVariable(EnvConnString) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        Assert.True(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")),
            $"{EnvConnString} es obligatoria en CI para las pruebas SQL de activación.");
        return false;
    }

    private static async Task LimpiarAsync(string cs, Cuenta cuenta)
    {
        await using var db = NuevoContexto(cs);
        var wallets = db.Wallets.Where(w => w.IdPersona == cuenta.Persona);
        await db.Auditorias.Where(a => a.IdUsuario == cuenta.Usuario).ExecuteDeleteAsync();
        await db.KycVerificaciones.Where(k => k.IdUsuario == cuenta.Usuario).ExecuteDeleteAsync();
        await db.WalletSaldos.Where(s => wallets.Any(w => w.IdWallet == s.IdWallet)).ExecuteDeleteAsync();
        await wallets.ExecuteDeleteAsync();
        await db.Usuarios.Where(u => u.IdUsuario == cuenta.Usuario).ExecuteDeleteAsync();
        await db.Personas.Where(p => p.IdPersona == cuenta.Persona).ExecuteDeleteAsync();
    }
}
