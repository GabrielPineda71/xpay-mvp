using System.Data;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Integrations.MiDecisor;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public sealed record CarteraPilotoDisponibilidad(
    bool Disponible, bool Habilitado, bool RequierePrimerApellido, string Estado, string Mensaje);

// Habilitar el producto no crea cupo ni reemplaza la aceptación de riesgo V1.
// Las tres vías comparten capacidad histórica y el mismo AppLock global.
public sealed class CarteraPilotoService(
    XpayDbContext db, IConfiguration config, ConsentimientoBiometricoService consentimiento,
    PerfilService perfil, TimeProvider reloj)
{
    internal const string PlazasSql = """
        SELECT TRY_CONVERT(bigint,a.id_entidad) AS id_usuario FROM dbo.auditoria a
        WHERE a.accion='ADMIN_ALTA_USUARIO_PILOTO' AND a.resultado='EXITOSO'
        UNION
        SELECT a.id_usuario FROM dbo.auditoria a
        WHERE a.accion='ACTIVAR_WALLET_PUBLICA' AND a.resultado='EXITOSO'
        UNION
        SELECT h.id_usuario FROM dbo.cartera_piloto_habilitaciones h
        """;

    // Permiso de producto persistido: pausar consultas no impide utilizar o
    // pagar un cupo existente. Los endpoints mantienen sus demás controles.
    public Task<bool> EsHabilitadoAsync(long idUsuario, CancellationToken ct = default) =>
        idUsuario <= 0 ? Task.FromResult(false) : db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS Value FROM dbo.cartera_piloto_habilitaciones h
            JOIN dbo.usuarios u ON u.id_usuario=h.id_usuario AND u.id_persona=h.id_persona
            WHERE h.id_usuario={0}
            """, idUsuario).Select(value => value == 1).SingleAsync(ct);

    public async Task<CarteraPilotoDisponibilidad> ObtenerDisponibilidadAsync(long idUsuario, CancellationToken ct = default)
    {
        if (idUsuario <= 0) return NoDisponible(false);
        return (await CargarAsync(idUsuario, ct)).Resultado;
    }

    public async Task<CarteraPilotoDisponibilidad> AsegurarHabilitacionAsync(long idUsuario, CancellationToken ct = default)
    {
        if (idUsuario <= 0) return NoDisponible(false);
        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("La habilitación del piloto requiere su propia transacción.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (await AppLockHelper.AdquirirAsync(db, AltaUsuarioPilotoService.ClaveLock, ct) < 0)
            return NoDisponible(false, "Hay otra habilitación en curso. Intenta de nuevo.");
        if (await AppLockHelper.AdquirirAsync(db, $"XPAY:KYC_USUARIO:{idUsuario}", ct) < 0)
            return NoDisponible(false, "Tu identidad se está actualizando. Intenta de nuevo.");
        var idPersona = await db.Usuarios.AsNoTracking().Where(u => u.IdUsuario == idUsuario)
            .Select(u => (long?)u.IdPersona).SingleOrDefaultAsync(ct);
        if (idPersona is null) return NoDisponible(false);
        await ContactoVerificacionService.BloquearPersonaAsync(db, idPersona.Value, ct);
        var actual = await CargarAsync(idUsuario, ct);
        if (!actual.Resultado.Disponible) return actual.Resultado;
        if (!actual.Resultado.Habilitado)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO dbo.cartera_piloto_habilitaciones (id_usuario,id_persona,id_wallet,fecha_habilitacion,fuente)
                VALUES ({idUsuario},{actual.IdPersona},{actual.IdWallet},{reloj.GetUtcNow().UtcDateTime},{actual.Fuente})
                """, ct);
        }
        await tx.CommitAsync(ct);
        return new(true, true, false, "HABILITADO", "Puedes solicitar la evaluación de tu cupo.");
    }

    private sealed record Carga(CarteraPilotoDisponibilidad Resultado, long IdPersona = 0, long IdWallet = 0, string Fuente = "LEGACY");

    private async Task<Carga> CargarAsync(long idUsuario, CancellationToken ct)
    {
        var habilitado = await EsHabilitadoAsync(idUsuario, ct);
        var maximo = Maximo(config[MiDecisorProduccionConfig.EnvProductionEnabled],
            config[AltaUsuarioPilotoService.ClaveHabilitada], config[AltaUsuarioPilotoService.ClaveMaxUsuarios]);
        if (maximo is null) return new(NoDisponible(habilitado));
        var usuario = await db.Usuarios.AsNoTracking().SingleOrDefaultAsync(u => u.IdUsuario == idUsuario, ct);
        if (usuario is null) return new(NoDisponible(habilitado));
        var persona = await db.Personas.AsNoTracking().SingleOrDefaultAsync(p => p.IdPersona == usuario.IdPersona, ct);
        if (persona is null) return new(NoDisponible(habilitado));
        var roles = await (from ur in db.UsuarioRoles.AsNoTracking() join r in db.Roles.AsNoTracking() on ur.IdRol equals r.IdRol
            where ur.IdUsuario == idUsuario && ur.Estado == "ACTIVO" && r.Estado == "ACTIVO" select r.Codigo).ToListAsync(ct);
        var personal = roles.Count == 1 && roles[0] == "USUARIO_FINAL"
            && !await db.LibranzaUsuariosEmpresa.AsNoTracking().AnyAsync(v => v.IdUsuario == idUsuario && v.Estado == "ACTIVO", ct);
        var unidadActiva = await db.Database.SqlQueryRaw<long>("""
            SELECT id_unidad_negocio AS Value FROM dbo.unidades_negocio
            WHERE id_unidad_negocio={0} AND codigo='XPAY_COL' AND estado='ACTIVA'
            """, persona.IdUnidadNegocio).AnyAsync(ct);
        var cuentaUnica = await db.Usuarios.AsNoTracking().CountAsync(u => u.IdPersona == persona.IdPersona, ct) == 1;
        var identidad = await PreparacionWalletService.IdentidadAprobadaAsync(db, usuario, persona, ct);
        var biometria = identidad && await consentimiento.TieneConsentimientoVigenteAsync(idUsuario, ct);
        var wallets = await db.Wallets.AsNoTracking().Where(w => w.IdPersona == persona.IdPersona && w.IdUnidadNegocio == persona.IdUnidadNegocio
                && w.TipoWallet == "PERSONA" && w.Estado == "ACTIVA")
            .Select(w => w.IdWallet).Take(2).ToListAsync(ct);
        var publica = await RegistroPublicoService.EsAutorregistroAsync(db, idUsuario, ct);
        var publicaLista = true;
        if (publica)
        {
            var pin = await db.PinesTransaccionales.AsNoTracking().SingleOrDefaultAsync(p => p.IdUsuario == idUsuario, ct);
            publicaLista = !await RegistroPublicoService.EsCuentaLimitadaAsync(db, idUsuario)
                && pin is { Estado: EstadosPin.Activo, Bloqueado: false, LongitudPin: 6 } && pin.PinHash.Length > 0
                && await perfil.DomicilioDaneCompletoAsync(persona.IdPersona, ct);
        }
        var apellidoPresente = !string.IsNullOrWhiteSpace(persona.PrimerApellido);
        var apellidoCompatible = false;
        if (apellidoPresente)
        {
            try { apellidoCompatible = PrimerApellidoDeclaradoReglas.CoincideConCompleto(
                PrimerApellidoDeclaradoReglas.Normalizar(persona.PrimerApellido), persona.ApellidoVerificadoCompleto); }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
        }
        var ocupa = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM (" + PlazasSql +
            ") plazas WHERE id_usuario={0}", idUsuario).SingleAsync(ct) == 1;
        var plazas = await PreparacionWalletService.ContarPlazasAsync(db, ct);
        var evidencia = new Evidencia(usuario.Estado == "ACTIVO" && persona.Estado == "ACTIVA" && cuentaUnica,
            usuario.EmailVerificado && !string.IsNullOrWhiteSpace(persona.Email), !usuario.RequiereCambioClave,
            personal && unidadActiva, identidad, biometria, wallets.Count == 1, publicaLista,
            apellidoPresente, apellidoCompatible, habilitado, ocupa, plazas);
        return new(Evaluar(evidencia, maximo.Value), persona.IdPersona, wallets.Count == 1 ? wallets[0] : 0, publica ? "PUBLICA" : "LEGACY");
    }

    internal static int? Maximo(string? consultas, string? alta, string? maximo) =>
        consultas == "true" && AltaUsuarioPilotoService.LeerMaximoHabilitado(alta, maximo) is { } limite
            ? Math.Min(limite, 50) : null;

    internal sealed record Evidencia(bool CuentaActiva, bool CorreoVerificado, bool ClaveVigente, bool RolPersonal,
        bool IdentidadVeriff, bool Biometria, bool WalletActiva, bool PreparacionPublica,
        bool PrimerApellidoPresente, bool PrimerApellidoCompatible, bool Habilitado, bool OcupaPlaza, int Plazas);

    internal static CarteraPilotoDisponibilidad Evaluar(Evidencia e, int maximo)
    {
        if (maximo is < 1 or > 50 || !e.CuentaActiva || !e.CorreoVerificado || !e.ClaveVigente || !e.RolPersonal)
            return NoDisponible(e.Habilitado, "Completa y verifica tu cuenta personal para continuar.");
        if (!e.IdentidadVeriff || !e.Biometria)
            return new(false, e.Habilitado, false, "IDENTIDAD_REQUERIDA", "Completa la verificación de identidad en tu perfil.");
        if (!e.WalletActiva || !e.PreparacionPublica)
            return NoDisponible(e.Habilitado, "Completa la activación de tu Wallet en tu perfil.");
        if (!e.PrimerApellidoPresente)
            return new(false, e.Habilitado, true, "REQUIERE_PRIMER_APELLIDO", "Completa tu primer apellido en tu perfil.");
        if (!e.PrimerApellidoCompatible)
            return new(false, e.Habilitado, false, "IDENTIDAD_REQUERIDA", "Tu identidad requiere revisión de XPAY.");
        if (!e.OcupaPlaza && e.Plazas >= maximo)
            return new(false, e.Habilitado, false, "PILOTO_LLENO", "Por ahora no podemos habilitar nuevas solicitudes. Contacta a XPAY.");
        return new(true, e.Habilitado, false, e.Habilitado ? "HABILITADO" : "DISPONIBLE", "Puedes solicitar la evaluación de tu cupo.");
    }

    private static CarteraPilotoDisponibilidad NoDisponible(bool habilitado, string mensaje = "La solicitud de cupo no está disponible en este momento.") =>
        new(false, habilitado, false, "NO_DISPONIBLE", mensaje);
}
