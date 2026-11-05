using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Exceptions;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// El alta de usuario del piloto está deshabilitada o mal configurada: el
// controlador responde 404, como si el endpoint no existiera.
public class AltaUsuarioPilotoNoDisponibleException() : Exception("Alta de usuario del piloto no disponible.");

// Piloto PROD — corrección 2b. En PROD no existe ninguna vía de alta de
// usuarios (registro público, registro-final y registro-inicial son
// [SoloAmbienteNoProductivo]). Esta es la única, y está acotada para no
// convertirse en un atajo permanente:
//   · Solo con XPAY_PILOTO_ALTA_HABILITADA = "true" y un máximo
//     XPAY_PILOTO_MAX_USUARIOS (> 0) de altas por esta vía, contadas en
//     auditoria bajo un AppLock (dos altas simultáneas no superan el máximo).
//   · Solo un SUPERUSUARIO (validado contra la BD, no contra el JWT).
//   · Identidad: el usuario nace sin documento y con estado_kyc NO_INICIADO;
//     KycAprobado sigue siendo obligatorio para cualquier operación de dinero.
//   · Autenticación: el hash inicial es de un secreto aleatorio descartado
//     (nadie puede entrar); la clave se emite con restablecer-clave, que
//     fuerza el cambio en el primer ingreso. email_verificado queda en false.
//   · Consentimiento: exige versiones de T&C, política de datos y
//     autorización KYC, fecha y referencia de evidencia, que quedan en
//     auditoria (sin datos personales).
public class AltaUsuarioPilotoService(XpayDbContext db, IConfiguration config, IAmbienteXpay ambiente)
{
    public const string ClaveHabilitada  = "XPAY_PILOTO_ALTA_HABILITADA";
    public const string ClaveMaxUsuarios = "XPAY_PILOTO_MAX_USUARIOS";
    public const string AccionAuditoria  = "ADMIN_ALTA_USUARIO_PILOTO";
    internal const string ClaveLock      = "XPAY:ALTA_USUARIO_PILOTO";

    internal const int LongitudMaximaVersion    = 50;
    internal const int LongitudMaximaReferencia = 200;

    internal const string MensajeMaximoAlcanzado = "Se alcanzó el número máximo de usuarios del piloto.";
    internal const string MensajeUsuarioExiste   = "El nombre de usuario ya existe.";
    internal const string MensajeEmailExiste     = "El email ya está asociado a otra persona.";

    public async Task<AltaUsuarioPilotoResponse> CrearAsync(AltaUsuarioPilotoRequest request, long idAdmin, CancellationToken ct = default)
    {
        var maximo = LeerMaximoHabilitado(config[ClaveHabilitada], config[ClaveMaxUsuarios])
            ?? throw new AltaUsuarioPilotoNoDisponibleException();

        var datos = Validar(request, ambiente, DateTime.UtcNow);

        if (!await EsSuperusuarioActivoAsync(idAdmin, ct))
            throw new UnauthorizedAccessException("Solo un SUPERUSUARIO puede dar de alta usuarios del piloto.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            if (await AppLockHelper.AdquirirAsync(db, ClaveLock, ct) < 0)
                throw new InvalidOperationException("Hay otra alta en curso. Intenta de nuevo.");

            var altas = await db.Auditorias.CountAsync(a => a.Accion == AccionAuditoria && a.Resultado == "EXITOSO", ct);
            if (altas >= maximo)
                throw new InvalidOperationException(MensajeMaximoAlcanzado);

            if (await db.Usuarios.AnyAsync(u => u.NombreUsuario == datos.NombreUsuario, ct))
                throw new InvalidOperationException(MensajeUsuarioExiste);
            if (await db.Personas.AnyAsync(p => p.Email == datos.Email, ct))
                throw new InvalidOperationException(MensajeEmailExiste);

            var idUnidad = await db.Database
                .SqlQueryRaw<long>(
                    "SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0} AND estado = {1}",
                    "XPAY_COL", "ACTIVA")
                .SingleAsync(ct);
            var idRol    = await db.Roles.Where(r => r.Codigo == "USUARIO_FINAL" && r.Estado == "ACTIVO").Select(r => r.IdRol).SingleAsync(ct);
            var ahora    = DateTime.UtcNow;

            var persona = new Persona
            {
                IdUnidadNegocio = idUnidad, Celular = datos.Celular, Email = datos.Email,
                Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = ahora,
            };
            db.Personas.Add(persona);
            await db.SaveChangesAsync(ct);

            var usuario = new Usuario
            {
                IdPersona           = persona.IdPersona,
                NombreUsuario       = datos.NombreUsuario,
                PasswordHash        = BCrypt.Net.BCrypt.HashPassword(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))),
                Estado              = EstadosUsuario.Activo,
                EmailVerificado     = false,
                RequiereCambioClave = true,
                FechaCreacion       = ahora,
            };
            db.Usuarios.Add(usuario);
            await db.SaveChangesAsync(ct);

            db.UsuarioRoles.Add(new UsuarioRol
            {
                IdUsuario = usuario.IdUsuario, IdRol = idRol, Estado = "ACTIVO",
                FechaAsignacion = ahora, AsignadoPor = idAdmin,
            });

            var wallet = new Wallet
            {
                IdUnidadNegocio = idUnidad, TipoWallet = "PERSONA", IdPersona = persona.IdPersona,
                NombreWallet = $"Wallet {datos.NombreUsuario}", Estado = "ACTIVA", FechaCreacion = ahora,
            };
            db.Wallets.Add(wallet);
            await db.SaveChangesAsync(ct);

            db.WalletSaldos.Add(new WalletSaldo { IdWallet = wallet.IdWallet, FechaActualizacion = ahora });
            db.Auditorias.Add(new Auditoria
            {
                IdUsuario     = idAdmin,
                IdPersona     = persona.IdPersona,
                Modulo        = "USUARIOS",
                Accion        = AccionAuditoria,
                Entidad       = "usuarios",
                IdEntidad     = usuario.IdUsuario.ToString(CultureInfo.InvariantCulture),
                Resultado     = "EXITOSO",
                ValorNuevo    = JsonSerializer.Serialize(new
                {
                    versionTerminos        = datos.VersionTerminos,
                    versionPoliticaDatos   = datos.VersionPoliticaDatos,
                    versionAutorizacionKyc = datos.VersionAutorizacionKyc,
                    fechaAceptacionUtc     = datos.FechaAceptacionUtc.ToString("O", CultureInfo.InvariantCulture),
                    referenciaEvidencia    = datos.ReferenciaEvidencia,
                }),
                Observacion   = "Alta controlada de usuario del piloto: sin clave utilizable, email sin verificar, KYC no iniciado.",
                FechaEvento   = ahora,
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return new AltaUsuarioPilotoResponse(usuario.IdUsuario, wallet.IdWallet,
                "Emitir la clave temporal con POST api/admin/usuarios/{id}/restablecer-clave; el usuario debe completar KYC.");
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }

    // null = deshabilitada o configuración inválida (fail-closed).
    internal static int? LeerMaximoHabilitado(string? habilitada, string? maximo) =>
        string.Equals(habilitada, "true", StringComparison.Ordinal)
        && int.TryParse(maximo?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var m) && m > 0
            ? m
            : null;

    internal sealed record DatosAlta(
        string NombreUsuario, string Email, string Celular,
        string VersionTerminos, string VersionPoliticaDatos, string VersionAutorizacionKyc,
        DateTime FechaAceptacionUtc, string ReferenciaEvidencia);

    internal static DatosAlta Validar(AltaUsuarioPilotoRequest r, IAmbienteXpay ambiente, DateTime ahoraUtc)
    {
        ArgumentNullException.ThrowIfNull(r);

        if (string.IsNullOrWhiteSpace(r.NombreUsuario))
            throw new InvalidOperationException("El nombre de usuario es obligatorio.");
        if (!NombreUsuarioReservado.PermiteCrear(r.NombreUsuario, ambiente))
            throw new InvalidOperationException(RegistroInicialService.MensajeUsuarioNoDisponible);
        var nombre = r.NombreUsuario.Trim().ToLower();
        if (nombre.Length > RegistroPublicoValidacion.LongitudMaximaUsuario)
            throw new InvalidOperationException("El nombre de usuario no puede superar 100 caracteres.");

        if (string.IsNullOrWhiteSpace(r.Email) || !EmailNormalizador.TryNormalizar(r.Email, out var email))
            throw new InvalidOperationException("El email no es válido.");

        if (string.IsNullOrWhiteSpace(r.Celular))
            throw new InvalidOperationException("El celular es obligatorio.");
        var celular = r.Celular.Trim();
        if (celular.Length > RegistroPublicoValidacion.LongitudMaximaCelular)
            throw new InvalidOperationException("El celular no puede superar 30 caracteres.");

        var vT = Version(r.VersionTerminos, "términos y condiciones");
        var vP = Version(r.VersionPoliticaDatos, "política de tratamiento de datos");
        var vK = Version(r.VersionAutorizacionKyc, "autorización KYC");

        if (r.FechaAceptacionUtc is not { } fecha)
            throw new InvalidOperationException("La fecha de aceptación es obligatoria.");
        var fechaUtc = DateTime.SpecifyKind(fecha, DateTimeKind.Utc);
        if (fechaUtc > ahoraUtc.AddMinutes(5))
            throw new InvalidOperationException("La fecha de aceptación no puede ser futura.");

        var referencia = r.ReferenciaEvidencia?.Trim() ?? string.Empty;
        if (referencia.Length == 0 || referencia.Length > LongitudMaximaReferencia || !referencia.All(EsCaracterReferencia))
            throw new InvalidOperationException("La referencia de evidencia es obligatoria y solo admite letras, dígitos y . _ : / -");

        return new DatosAlta(nombre, email, celular, vT, vP, vK, fechaUtc, referencia);
    }

    private static string Version(string? valor, string documento)
    {
        var v = valor?.Trim() ?? string.Empty;
        if (v.Length == 0 || v.Length > LongitudMaximaVersion || !v.All(EsCaracterReferencia))
            throw new InvalidOperationException($"La versión aceptada de {documento} es obligatoria.");
        return v;
    }

    private static bool EsCaracterReferencia(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or ':' or '/' or '-';

    private Task<bool> EsSuperusuarioActivoAsync(long idUsuario, CancellationToken ct) =>
        (from ur in db.UsuarioRoles
         join ro in db.Roles on ur.IdRol equals ro.IdRol
         join u in db.Usuarios on ur.IdUsuario equals u.IdUsuario
         where ur.IdUsuario == idUsuario && ur.Estado == "ACTIVO"
            && ro.Codigo == "SUPERUSUARIO" && ro.Estado == "ACTIVO" && u.Estado == EstadosUsuario.Activo
         select ur.IdUsuario).AnyAsync(ct);
}
