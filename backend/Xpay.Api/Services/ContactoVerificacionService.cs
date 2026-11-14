using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public enum ResultadoSolicitudCodigo
{
    Enviado,
    YaVerificado,
    SinContacto,
    ContactoInvalido,
    ContactoEnUsoPorOtro,
    ConsentimientoRequerido,
    Espera,
    LimiteDiario,
    CanalNoDisponible,
    ErrorEnvio,
}

public enum ResultadoConfirmacionCodigo
{
    Verificado,
    YaVerificado,
    FormatoInvalido,
    SinCodigoVigente,
    Incorrecto,
    Agotado,
    ContactoCambio,
    ContactoEnUsoPorOtro,
}

public sealed record SolicitudCodigoResultado(
    ResultadoSolicitudCodigo Resultado, string? DestinoEnmascarado = null,
    DateTime? FechaExpiracion = null, int? SegundosEspera = null);

public sealed record ConfirmacionCodigoResultado(ResultadoConfirmacionCodigo Resultado, int? IntentosRestantes = null);

// Verificación de correo (EMAIL) y celular (WHATSAPP) con código de un solo
// uso. Reglas:
//   · Código de 6 dígitos (RandomNumberGenerator); se guarda SHA-256(sal || código).
//   · Vigencia 10 min, un solo uso, 5 intentos fallidos lo agotan.
//   · Reenvío: 60 s entre códigos, 5 por usuario y canal en 24 h, 8 por
//     destino y canal en 24 h (frena el uso de varias cuentas contra un mismo
//     contacto). Los envíos fallidos también cuentan.
//   · El código queda atado al destino al que se envió: si el contacto
//     cambia, deja de servir. PerfilService además invalida los pendientes y
//     revoca la verificación al cambiar el dato.
//   · El contacto se marca verificado SOLO cuando el usuario escribe el código
//     correcto; la confirmación de entrega del proveedor no verifica nada.
//   · No se verifica un contacto que otra cuenta ya tiene verificado.
//   · WhatsApp exige el consentimiento de SERVICIO por ese canal (Meta pide
//     opt-in). La PUBLICIDAD es otro consentimiento, nunca requerido aquí.
// Sin ILogger a propósito: este servicio no puede registrar código, hash ni destino.
public sealed class ContactoVerificacionService(
    XpayDbContext db,
    IEnumerable<ICanalCodigoContacto> canales,
    TimeProvider tiempo)
{
    public const int Digitos              = 6;
    public const int VigenciaMinutos      = 10;
    public const int MaxIntentos          = 5;
    public const int EnfriamientoSegundos = 60;
    public const int MaxEnviosUsuarioDia  = 5;
    public const int MaxEnviosDestinoDia  = 8;
    private const int LongitudMaximaIp    = 100;

    internal const string MotivoReemplazado    = "REEMPLAZADO";
    internal const string MotivoAgotado        = "AGOTADO";
    internal const string MotivoContactoCambio = "CONTACTO_CAMBIO";
    internal const string MotivoErrorEnvio     = "ERROR_ENVIO";
    internal const string MotivoEnUsoPorOtro   = "EN_USO_POR_OTRO";

    public bool CanalDisponible(string canal) => Canal(canal) is { Disponible: true };

    public async Task<SolicitudCodigoResultado> SolicitarAsync(
        long idUsuario, string canal, bool aceptaServicioWhatsApp, string? ip, CancellationToken ct = default)
    {
        if (!CanalContacto.EsValido(canal)) throw new ArgumentException("Canal no válido.", nameof(canal));
        var proveedor = Canal(canal);
        if (proveedor is not { Disponible: true })
            return new(ResultadoSolicitudCodigo.CanalNoDisponible);

        var (usuario, persona) = await CargarAsync(idUsuario, ct);
        if (YaVerificado(usuario, canal)) return new(ResultadoSolicitudCodigo.YaVerificado);

        var crudo = canal == CanalContacto.Email ? persona.Email : persona.Celular;
        if (string.IsNullOrWhiteSpace(crudo)) return new(ResultadoSolicitudCodigo.SinContacto);
        if (!TryNormalizarDestino(canal, crudo, out var destino))
            return new(ResultadoSolicitudCodigo.ContactoInvalido);

        if (await EnUsoPorOtroAsync(idUsuario, canal, destino, ct))
            return new(ResultadoSolicitudCodigo.ContactoEnUsoPorOtro);

        var ahora = tiempo.GetUtcNow().UtcDateTime;
        string codigo;
        ContactoCodigoVerificacion fila;

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            if (await AppLockHelper.AdquirirAsync(db, ClaveLock(idUsuario, canal), ct) < 0)
                throw new InvalidOperationException("No se pudo serializar la solicitud del código.");

            if (canal == CanalContacto.WhatsApp
                && !await ConsentimientoVigenteAsync(idUsuario, CanalContacto.WhatsApp, FinalidadComunicacion.Servicio, ct))
            {
                if (!aceptaServicioWhatsApp)
                    return new(ResultadoSolicitudCodigo.ConsentimientoRequerido);
                db.ConsentimientosComunicacion.Add(NuevoConsentimiento(
                    idUsuario, CanalContacto.WhatsApp, FinalidadComunicacion.Servicio, true, "VERIFICACION_CELULAR", ip, ahora));
            }

            var ultimo = await db.ContactoCodigosVerificacion.AsNoTracking()
                .Where(c => c.IdUsuario == idUsuario && c.Canal == canal)
                .OrderByDescending(c => c.IdCodigo)
                .Select(c => (DateTime?)c.FechaCreacion)
                .FirstOrDefaultAsync(ct);
            if (ultimo is { } u && (ahora - u).TotalSeconds < EnfriamientoSegundos)
            {
                await tx.CommitAsync(ct); // conserva un consentimiento recién otorgado
                return new(ResultadoSolicitudCodigo.Espera,
                    SegundosEspera: (int)Math.Ceiling(EnfriamientoSegundos - (ahora - u).TotalSeconds));
            }

            var desde = ahora.AddHours(-24);
            var enviosUsuario = await db.ContactoCodigosVerificacion
                .CountAsync(c => c.IdUsuario == idUsuario && c.Canal == canal && c.FechaCreacion > desde, ct);
            var enviosDestino = await db.ContactoCodigosVerificacion
                .CountAsync(c => c.Canal == canal && c.Destino == destino && c.FechaCreacion > desde, ct);
            if (enviosUsuario >= MaxEnviosUsuarioDia || enviosDestino >= MaxEnviosDestinoDia)
            {
                await tx.CommitAsync(ct);
                return new(ResultadoSolicitudCodigo.LimiteDiario);
            }

            await InvalidarPendientesAsync(db, idUsuario, canal, MotivoReemplazado, ahora, ct);

            codigo = GenerarCodigo();
            var sal = RandomNumberGenerator.GetBytes(16);
            fila = new ContactoCodigoVerificacion
            {
                IdUsuario       = idUsuario,
                Canal           = canal,
                Destino         = destino,
                CodigoSal       = sal,
                CodigoHash      = Hash(sal, codigo),
                FechaCreacion   = ahora,
                FechaExpiracion = ahora.AddMinutes(VigenciaMinutos),
                IpSolicitud     = NormalizarIp(ip),
            };
            db.ContactoCodigosVerificacion.Add(fila);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        // El envío va fuera de la transacción: no se retiene el lock durante
        // la llamada de red. Si falla, el código se invalida (y sigue contando
        // para los límites).
        try
        {
            await proveedor.EnviarCodigoAsync(destino, codigo, VigenciaMinutos, ct);
        }
        catch (Exception ex) when (ex is CanalContactoException or HttpRequestException or TaskCanceledException)
        {
            await db.ContactoCodigosVerificacion
                .Where(c => c.IdCodigo == fila.IdCodigo && c.FechaUso == null && c.FechaInvalidacion == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.FechaInvalidacion, ahora)
                    .SetProperty(c => c.MotivoInvalidacion, MotivoErrorEnvio), CancellationToken.None);
            return new(ResultadoSolicitudCodigo.ErrorEnvio);
        }

        return new(ResultadoSolicitudCodigo.Enviado, Enmascarar(canal, destino), fila.FechaExpiracion);
    }

    public async Task<ConfirmacionCodigoResultado> ConfirmarAsync(
        long idUsuario, string canal, string? codigo, CancellationToken ct = default)
    {
        if (!CanalContacto.EsValido(canal)) throw new ArgumentException("Canal no válido.", nameof(canal));
        var limpio = codigo?.Trim();
        if (limpio is null || limpio.Length != Digitos || !limpio.All(char.IsAsciiDigit))
            return new(ResultadoConfirmacionCodigo.FormatoInvalido);

        var ahora = tiempo.GetUtcNow().UtcDateTime;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (await AppLockHelper.AdquirirAsync(db, ClaveLock(idUsuario, canal), ct) < 0)
            throw new InvalidOperationException("No se pudo serializar la confirmación del código.");

        var usuario = await db.Usuarios.SingleOrDefaultAsync(u => u.IdUsuario == idUsuario, ct)
            ?? throw new KeyNotFoundException("Usuario no encontrado.");
        var persona = await db.Personas.AsNoTracking().SingleAsync(p => p.IdPersona == usuario.IdPersona, ct);
        if (YaVerificado(usuario, canal)) return new(ResultadoConfirmacionCodigo.YaVerificado);

        var fila = await db.ContactoCodigosVerificacion
            .Where(c => c.IdUsuario == idUsuario && c.Canal == canal
                     && c.FechaUso == null && c.FechaInvalidacion == null && c.FechaExpiracion > ahora)
            .OrderByDescending(c => c.IdCodigo)
            .FirstOrDefaultAsync(ct);
        if (fila is null) return new(ResultadoConfirmacionCodigo.SinCodigoVigente);

        var crudo = canal == CanalContacto.Email ? persona.Email : persona.Celular;
        if (!TryNormalizarDestino(canal, crudo, out var destinoActual) || destinoActual != fila.Destino)
        {
            Invalidar(fila, MotivoContactoCambio, ahora);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new(ResultadoConfirmacionCodigo.ContactoCambio);
        }

        if (!CryptographicOperations.FixedTimeEquals(Hash(fila.CodigoSal, limpio), fila.CodigoHash))
        {
            fila.IntentosFallidos++;
            var agotado = fila.IntentosFallidos >= MaxIntentos;
            if (agotado) Invalidar(fila, MotivoAgotado, ahora);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return agotado
                ? new(ResultadoConfirmacionCodigo.Agotado)
                : new(ResultadoConfirmacionCodigo.Incorrecto, MaxIntentos - fila.IntentosFallidos);
        }

        if (await EnUsoPorOtroAsync(idUsuario, canal, fila.Destino, ct))
        {
            Invalidar(fila, MotivoEnUsoPorOtro, ahora);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new(ResultadoConfirmacionCodigo.ContactoEnUsoPorOtro);
        }

        fila.FechaUso = ahora;
        if (canal == CanalContacto.Email) usuario.EmailVerificado = true;
        else usuario.CelularVerificado = true;
        usuario.FechaActualizacion = ahora;
        db.Auditorias.Add(new Auditoria
        {
            IdUsuario   = idUsuario,
            IdPersona   = usuario.IdPersona,
            Modulo      = "PERFIL",
            Accion      = canal == CanalContacto.Email ? "EMAIL_VERIFICADO" : "CELULAR_VERIFICADO",
            Entidad     = "Usuario",
            IdEntidad   = idUsuario.ToString(),
            Resultado   = "EXITOSO",
            Observacion = $"Verificado con código ({canal}) enviado a {Enmascarar(canal, fila.Destino)}",
            FechaEvento = ahora,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(ResultadoConfirmacionCodigo.Verificado);
    }

    public async Task<bool> ConsentimientoVigenteAsync(long idUsuario, string canal, string finalidad, CancellationToken ct = default) =>
        await db.ConsentimientosComunicacion.AsNoTracking()
            .Where(c => c.IdUsuario == idUsuario && c.Canal == canal && c.Finalidad == finalidad)
            .OrderByDescending(c => c.IdConsentimiento)
            .Select(c => (bool?)c.Otorgado)
            .FirstOrDefaultAsync(ct) == true;

    // Solo inserta si cambia el estado vigente (historial sin filas repetidas).
    public async Task RegistrarConsentimientoAsync(
        long idUsuario, string canal, string finalidad, bool otorgado, string origen, string? ip, CancellationToken ct = default)
    {
        if (!CanalContacto.EsValido(canal)) throw new ArgumentException("Canal no válido.", nameof(canal));
        if (!FinalidadComunicacion.EsValida(finalidad)) throw new ArgumentException("Finalidad no válida.", nameof(finalidad));
        if (await ConsentimientoVigenteAsync(idUsuario, canal, finalidad, ct) == otorgado) return;
        db.ConsentimientosComunicacion.Add(NuevoConsentimiento(
            idUsuario, canal, finalidad, otorgado, origen, ip, tiempo.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);
    }

    // Llamado también por PerfilService cuando cambia el contacto.
    public static Task<int> InvalidarPendientesAsync(
        XpayDbContext db, long idUsuario, string canal, string motivo, DateTime ahora, CancellationToken ct = default) =>
        db.ContactoCodigosVerificacion
            .Where(c => c.IdUsuario == idUsuario && c.Canal == canal
                     && c.FechaUso == null && c.FechaInvalidacion == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.FechaInvalidacion, ahora)
                .SetProperty(c => c.MotivoInvalidacion, motivo), ct);

    // EMAIL: normalización común del sistema. WHATSAPP: celular colombiano
    // (3 + 9 dígitos) en formato E.164 sin '+': 57XXXXXXXXXX.
    public static bool TryNormalizarDestino(string canal, string? crudo, out string destino)
    {
        destino = string.Empty;
        if (canal == CanalContacto.Email) return EmailNormalizador.TryNormalizar(crudo, out destino);
        if (string.IsNullOrWhiteSpace(crudo)) return false;

        var s = crudo.Trim();
        var conMas = s.StartsWith('+');
        var digitos = new string(s.Where(c => !(c is ' ' or '-' or '(' or ')' or '.')).ToArray());
        if (conMas) digitos = digitos[1..];
        if (!digitos.All(char.IsAsciiDigit)) return false;
        if (digitos.Length == 12 && digitos.StartsWith("57")) digitos = digitos[2..];
        else if (conMas || digitos.Length != 10) return false;
        if (digitos[0] != '3') return false;
        destino = "57" + digitos;
        return true;
    }

    public static string Enmascarar(string canal, string destino)
    {
        if (canal == CanalContacto.WhatsApp)
            return destino.Length > 4 ? "+" + destino[..2] + " ******" + destino[^4..] : "***";
        var arroba = destino.IndexOf('@');
        return arroba <= 0 ? "***" : destino[0] + "***" + destino[arroba..];
    }

    internal static byte[] Hash(byte[] sal, string codigo)
    {
        var bytes = new byte[sal.Length + codigo.Length];
        sal.CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes(codigo, 0, codigo.Length, bytes, sal.Length);
        return SHA256.HashData(bytes);
    }

    internal static string GenerarCodigo() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    private ICanalCodigoContacto? Canal(string canal) =>
        canales.LastOrDefault(c => c.Canal == canal);

    private static bool YaVerificado(Usuario u, string canal) =>
        canal == CanalContacto.Email ? u.EmailVerificado : u.CelularVerificado;

    private async Task<(Usuario, Persona)> CargarAsync(long idUsuario, CancellationToken ct)
    {
        var usuario = await db.Usuarios.AsNoTracking().SingleOrDefaultAsync(u => u.IdUsuario == idUsuario, ct)
            ?? throw new KeyNotFoundException("Usuario no encontrado.");
        var persona = await db.Personas.AsNoTracking().SingleAsync(p => p.IdPersona == usuario.IdPersona, ct);
        return (usuario, persona);
    }

    // ¿Otra cuenta ya tiene este contacto verificado? Para el celular se
    // filtra por los últimos 10 dígitos en SQL y se compara normalizado.
    private async Task<bool> EnUsoPorOtroAsync(long idUsuario, string canal, string destino, CancellationToken ct)
    {
        if (canal == CanalContacto.Email)
        {
            return await db.Usuarios.AsNoTracking()
                .Where(u => u.IdUsuario != idUsuario && u.EmailVerificado)
                .Join(db.Personas, u => u.IdPersona, p => p.IdPersona, (u, p) => p.Email)
                .AnyAsync(e => e == destino, ct);
        }
        var ultimos10 = destino[^10..];
        var candidatos = await db.Usuarios.AsNoTracking()
            .Where(u => u.IdUsuario != idUsuario && u.CelularVerificado)
            .Join(db.Personas, u => u.IdPersona, p => p.IdPersona, (u, p) => p.Celular)
            .Where(c => c.EndsWith(ultimos10))
            .ToListAsync(ct);
        return candidatos.Any(c => TryNormalizarDestino(CanalContacto.WhatsApp, c, out var n) && n == destino);
    }

    private static void Invalidar(ContactoCodigoVerificacion fila, string motivo, DateTime ahora)
    {
        fila.FechaInvalidacion  = ahora;
        fila.MotivoInvalidacion = motivo;
    }

    private static UsuarioConsentimientoComunicacion NuevoConsentimiento(
        long idUsuario, string canal, string finalidad, bool otorgado, string origen, string? ip, DateTime ahora) => new()
    {
        IdUsuario     = idUsuario,
        Canal         = canal,
        Finalidad     = finalidad,
        Otorgado      = otorgado,
        Origen        = origen,
        FechaRegistro = ahora,
        IpSolicitud   = NormalizarIp(ip),
    };

    private static string ClaveLock(long idUsuario, string canal) => $"XPAY:CONTACTO_CODIGO:{idUsuario}:{canal}";

    private static string? NormalizarIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return null;
        var t = ip.Trim();
        if (t.Any(c => c < ' ' || c > '~')) return null;
        return t.Length > LongitudMaximaIp ? t[..LongitudMaximaIp] : t;
    }
}
