using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// La confirmación del correo activa Perfil. Solo una revisión administrativa
// de pertenencia laboral puede incorporar esa MISMA cuenta al piloto. Nunca
// se borra la aceptación original ni se crean otra Persona o credenciales.
public sealed class HabilitacionCuentaPublicaService(
    XpayDbContext db, IConfiguration config, ConsentimientoBiometricoService consentimiento)
{
    public const string ClaveHabilitada = "XPAY_ALTA_PUBLICA_NOMINA_HABILITADA";
    public const string AccionHabilitar = "ADMIN_HABILITAR_USUARIO_PILOTO";

    public bool Habilitado => string.Equals(config[ClaveHabilitada], "true", StringComparison.Ordinal);

    public static Task<bool> EsAutorregistroAsync(XpayDbContext db, long idUsuario, CancellationToken ct = default) =>
        db.Auditorias.AnyAsync(a => a.IdUsuario == idUsuario && a.Modulo == "USUARIOS"
            && a.Accion == RegistroPublicoService.AccionAceptarTerminos && a.Resultado == "EXITOSO", ct);

    public static Task<bool> TieneHabilitacionAsync(XpayDbContext db, long idUsuario, CancellationToken ct = default)
    {
        var entidad = idUsuario.ToString(CultureInfo.InvariantCulture);
        return db.Auditorias.AnyAsync(a => a.Modulo == "USUARIOS" && a.Accion == AccionHabilitar
            && a.Entidad == "usuarios" && a.IdEntidad == entidad && a.Resultado == "EXITOSO", ct);
    }

    internal static Task<int> ContarPlazasAsync(XpayDbContext db, CancellationToken ct = default) =>
        db.Auditorias.CountAsync(a => a.Resultado == "EXITOSO"
            && (a.Accion == AltaUsuarioPilotoService.AccionAuditoria || a.Accion == AccionHabilitar), ct);

    public static Task<bool> EsAdministradorActivoAsync(XpayDbContext db, long idAdmin, CancellationToken ct = default) =>
        (from u in db.Usuarios
         join ur in db.UsuarioRoles on u.IdUsuario equals ur.IdUsuario
         join r in db.Roles on ur.IdRol equals r.IdRol
         where u.IdUsuario == idAdmin && u.Estado == "ACTIVO" && ur.Estado == "ACTIVO" && r.Estado == "ACTIVO"
            && (r.Codigo == "ADMIN_XPAY" || r.Codigo == "SUPERUSUARIO")
         select u.IdUsuario).AnyAsync(ct);

    // Regla estricta también en QA: los candidatos de esta vía deben tener una
    // única decisión vigente VERIFF/APROBADA. La simulación QA no habilita el
    // alta pública financiera.
    public static async Task<bool> IdentidadAprobadaAsync(XpayDbContext db, Usuario usuario, Persona persona,
        CancellationToken ct = default)
    {
        if (usuario.EstadoKycActual != "APROBADO" || !persona.IdentidadVerificada
            || persona.IdentidadVerificadaProveedor != "VERIFF"
            || persona.IdentidadVerificadaFecha is null
            || string.IsNullOrWhiteSpace(persona.NumeroDocumentoVerificado)) return false;
        var vigentes = await db.KycVerificaciones.AsNoTracking()
            .Where(k => k.IdUsuario == usuario.IdUsuario && k.EsActual)
            .Select(k => new { k.IdPersona, k.Proveedor, k.EstadoKyc, k.Decision }).Take(2).ToListAsync(ct);
        return vigentes.Count == 1 && vigentes[0].IdPersona == persona.IdPersona
            && vigentes[0].Proveedor == "VERIFF" && vigentes[0].EstadoKyc == "APROBADO" && vigentes[0].Decision == "approved";
    }

    public async Task<AltaPublicaEstadoResponse> ObtenerEstadoAsync(long idUsuario, CancellationToken ct = default)
    {
        if (!Habilitado) throw new AltaPublicaNoDisponibleException();
        var usuario = await db.Usuarios.AsNoTracking().SingleOrDefaultAsync(u => u.IdUsuario == idUsuario, ct)
            ?? throw new UnauthorizedAccessException("Usuario no disponible.");
        var persona = await db.Personas.AsNoTracking().SingleAsync(p => p.IdPersona == usuario.IdPersona, ct);
        var publica = await EsAutorregistroAsync(db, idUsuario, ct);
        var habilitada = !publica || await TieneHabilitacionAsync(db, idUsuario, ct);
        var identidad = await IdentidadAprobadaAsync(db, usuario, persona, ct);
        var pendientes = new List<string>();
        if (!usuario.EmailVerificado) pendientes.Add("Verificar el correo electrónico");
        if (!usuario.CelularVerificado) pendientes.Add("Verificar el celular por WhatsApp");
        if (publica && !await consentimiento.TieneConsentimientoVigenteAsync(idUsuario, ct))
            pendientes.Add("Autorizar expresamente la verificación de identidad");
        if (!identidad) pendientes.Add("Completar la verificación de identidad con Veriff");
        if (publica && !habilitada) pendientes.Add("Aprobación de XPAY de la vinculación laboral y del ingreso al piloto");
        var wallets = await db.Wallets.AsNoTracking().Where(w => w.IdPersona == persona.IdPersona
            && w.TipoWallet == "PERSONA" && w.Estado == "ACTIVA").Select(w => w.IdWallet).Take(2).ToListAsync(ct);
        return new(publica, habilitada, usuario.EmailVerificado, usuario.CelularVerificado, identidad,
            usuario.EstadoKycActual, habilitada ? (pendientes.Count == 0 ? "HABILITADA" : "VERIFICACIONES_PENDIENTES")
                : "PENDIENTE_HABILITACION", pendientes, habilitada && wallets.Count == 1 ? wallets[0] : null);
    }

    // El aprobador de coincidencias posee la transacción: Wallet, capacidad,
    // auditoría de habilitación y vínculo se confirman o revierten juntos.
    public async Task<long> HabilitarEnTransaccionAsync(long idUsuario, long idAdmin, CancellationToken ct = default)
    {
        if (!Habilitado) throw new AltaPublicaNoDisponibleException();
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("La habilitación requiere la transacción de aprobación laboral.");
        if (!await EsAdministradorActivoAsync(db, idAdmin, ct))
            throw new UnauthorizedAccessException("Solo un administrador activo de XPAY puede aprobar la vinculación.");
        // Mismo recurso que el alta histórica: ambas vías consumen el máximo50.
        if (await AppLockHelper.AdquirirAsync(db, AltaUsuarioPilotoService.ClaveLock, ct) < 0
            || await AppLockHelper.AdquirirAsync(db, $"XPAY:KYC_USUARIO:{idUsuario}", ct) < 0)
            throw new InvalidOperationException("Hay otra habilitación en curso. Actualiza e intenta de nuevo.");
        var usuario = await db.Usuarios.AsNoTracking().SingleOrDefaultAsync(u => u.IdUsuario == idUsuario, ct)
            ?? throw new InvalidOperationException("El usuario ya no está disponible.");
        await ContactoVerificacionService.BloquearPersonaAsync(db, usuario.IdPersona, ct);
        usuario = await db.Usuarios.AsNoTracking().SingleAsync(u => u.IdUsuario == idUsuario, ct);
        var persona = await db.Personas.AsNoTracking().SingleAsync(p => p.IdPersona == usuario.IdPersona, ct);
        if (usuario.Estado != "ACTIVO" || persona.Estado != "ACTIVA" || usuario.RequiereCambioClave)
            throw new InvalidOperationException("La cuenta debe estar activa y con su contraseña vigente.");
        if (!await (from ur in db.UsuarioRoles join r in db.Roles on ur.IdRol equals r.IdRol
            where ur.IdUsuario == idUsuario && ur.Estado == "ACTIVO" && r.Codigo == "USUARIO_FINAL" && r.Estado == "ACTIVO"
            select ur.IdUsuario).AnyAsync(ct))
            throw new InvalidOperationException("La cuenta no tiene el rol de usuario final activo.");
        if (!usuario.EmailVerificado || !usuario.CelularVerificado)
            throw new InvalidOperationException("Falta verificar el correo o el celular por WhatsApp.");
        if (!await IdentidadAprobadaAsync(db, usuario, persona, ct))
            throw new InvalidOperationException("La identidad aún no tiene una aprobación vigente de Veriff.");
        await ValidarIdentidadUnicaAsync(persona, ct);
        var publica = await EsAutorregistroAsync(db, idUsuario, ct);
        var habilitada = publica && await TieneHabilitacionAsync(db, idUsuario, ct);
        if (publica && !await consentimiento.TieneConsentimientoVigenteAsync(idUsuario, ct))
            throw new InvalidOperationException("Falta la autorización biométrica vigente del usuario.");
        var wallets = await db.Wallets.Where(w => w.IdPersona == persona.IdPersona && w.TipoWallet == "PERSONA")
            .Take(2).ToListAsync(ct);
        if (wallets.Count > 1 || wallets.Any(w => w.Estado != "ACTIVA"))
            throw new InvalidOperationException("La cuenta tiene Wallets que requieren revisión de XPAY.");
        if (!publica || habilitada)
        {
            if (wallets.Count != 1) throw new InvalidOperationException("La Wallet del usuario no está disponible.");
            return wallets[0].IdWallet;
        }
        var maximo = AltaUsuarioPilotoService.LeerMaximoHabilitado(
            config[AltaUsuarioPilotoService.ClaveHabilitada], config[AltaUsuarioPilotoService.ClaveMaxUsuarios])
            ?? throw new InvalidOperationException("La incorporación al piloto no está habilitada.");
        if (await ContarPlazasAsync(db, ct) >= maximo)
            throw new InvalidOperationException(AltaUsuarioPilotoService.MensajeMaximoAlcanzado);
        // Una cuenta pública limitada nunca debe haber recibido una Wallet.
        if (wallets.Count != 0)
            throw new InvalidOperationException("La cuenta limitada tiene una Wallet inesperada; requiere revisión.");
        var ahora = DateTime.UtcNow;
        var wallet = new Wallet
        {
            IdPersona = persona.IdPersona, IdUnidadNegocio = persona.IdUnidadNegocio, TipoWallet = "PERSONA",
            NombreWallet = $"Wallet {usuario.NombreUsuario}", Estado = "ACTIVA", FechaCreacion = ahora,
        };
        db.Wallets.Add(wallet);
        await db.SaveChangesAsync(ct);
        db.WalletSaldos.Add(new WalletSaldo { IdWallet = wallet.IdWallet, FechaActualizacion = ahora });
        db.Auditorias.Add(new Auditoria
        {
            IdUsuario = idAdmin, IdPersona = persona.IdPersona, Modulo = "USUARIOS", Accion = AccionHabilitar,
            Entidad = "usuarios", IdEntidad = idUsuario.ToString(CultureInfo.InvariantCulture), Resultado = "EXITOSO",
            ValorNuevo = JsonSerializer.Serialize(new { idWallet = wallet.IdWallet, metodo = "REVISION_MANUAL_NOMINA",
                emailVerificado = true, whatsAppVerificado = true, identidadVerificada = true }),
            Observacion = "Incorporación de la misma cuenta pública al piloto tras verificaciones y revisión laboral de XPAY.",
            FechaEvento = ahora,
        });
        await db.SaveChangesAsync(ct);
        // La sesión anterior sigue presentando Perfil. El usuario inicia sesión
        // de nuevo para recibir el destino actualizado; el filtro consulta SQL.
        await VersionSeguridadUsuario.IncrementarAsync(db, idUsuario, ct);
        return wallet.IdWallet;
    }

    private async Task ValidarIdentidadUnicaAsync(Persona persona, CancellationToken ct)
    {
        var numero = persona.NumeroDocumentoVerificado!.Trim().ToUpperInvariant();
        // La consolidación de Veriff acepta solo números canónicos ASCII.
        // No se convierte un documento desconocido en otro mediante limpieza.
        if (numero.Length is 0 or > 30 || !numero.All(char.IsAsciiLetterOrDigit))
            throw new InvalidOperationException("El documento verificado requiere revisión de XPAY.");
        var tipo = persona.TipoDocumento?.Trim().ToUpperInvariant();
        if (tipo is not ("CC" or "PASAPORTE") || persona.NumeroDocumento?.Trim().ToUpperInvariant() != numero)
            throw new InvalidOperationException("La identidad legal y la verificada deben estar consolidadas sin diferencias.");
        if (await db.Usuarios.CountAsync(u => u.IdPersona == persona.IdPersona, ct) != 1)
            throw new InvalidOperationException("La identidad está asociada a más de una cuenta.");
        var duplicada = await db.Personas.AnyAsync(p => p.IdPersona != persona.IdPersona
            && ((p.TipoDocumento == tipo && p.NumeroDocumento != null
                    && p.NumeroDocumento.Replace(" ", "").Replace(".", "").Replace("-", "").ToUpper() == numero)
                || (p.NumeroDocumentoVerificado != null
                    && p.NumeroDocumentoVerificado.Replace(" ", "").Replace(".", "").Replace("-", "").ToUpper() == numero
                    && (p.TipoDocumentoVeriffRaw == persona.TipoDocumentoVeriffRaw || p.TipoDocumento == tipo))), ct);
        if (duplicada) throw new InvalidOperationException("El documento coincide con otra cuenta; requiere revisión de XPAY.");
    }
}

public sealed class AltaPublicaNoDisponibleException() : Exception("La incorporación pública a nómina no está disponible.");
