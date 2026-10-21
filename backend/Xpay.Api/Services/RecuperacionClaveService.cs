using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// P0-B2 — recuperación universal de contraseña (cualquier rol). La solicitud
// nunca revela si el usuario existe: no lanza por motivos de elegibilidad y el
// controlador responde siempre lo mismo. Elegibles: ACTIVO (con o sin email
// verificado) y BLOQUEADO solo por BloqueoAutomaticoUsuario; INACTIVO,
// PENDIENTE_VERIFICACION y cualquier otro bloqueo no. El destino es
// exclusivamente personas.email normalizado: sin email utilizable no se
// emite token. El restablecimiento consume el token RECUPERAR_CLAVE, revalida
// estado y email, aplica la política de AuthService y, en UNA transacción,
// cambia la clave, levanta el bloqueo automático e incrementa
// version_seguridad (revoca todos los JWT emitidos). No toca roles, unidad,
// organización ni persona. Token, email y usuario nunca se registran en logs.
public sealed class RecuperacionClaveService(
    XpayDbContext db,
    TokenUsuarioService tokens,
    IEmailSender emailSender,
    PlantillasEmailIdentidad plantillas,
    TimeProvider timeProvider,
    ILogger<RecuperacionClaveService> logger)
{
    // Tope de emisiones por usuario: frena el envío masivo de correos a una
    // misma cuenta desde IPs distintas (el rate limiting público es por IP).
    internal const int MaxSolicitudesPorVentana = 3;
    internal static readonly TimeSpan VentanaSolicitudes = TimeSpan.FromHours(1);
    private const int LongitudMaximaUsuario = 100; // usuarios.usuario VARCHAR(100)

    internal const string MensajeConfirmacionDistinta = "La contraseña nueva y su confirmación no coinciden.";
    internal const string MensajeClaveIgualActual     = "La contraseña nueva no puede ser igual a la contraseña actual.";

    public async Task SolicitarAsync(string? nombreUsuario, string? ipSolicitud, CancellationToken ct = default)
    {
        // Configuración y transacción se validan igual para todos: no
        // dependen de que el usuario exista.
        plantillas.ValidarConfiguracionRecuperacion();
        if (db.Database.CurrentTransaction is not null)
            throw new IdentidadConfiguracionException("La recuperación de contraseña requiere su propia transacción.");

        var nombre = nombreUsuario?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(nombre) || nombre.Length > LongitudMaximaUsuario) return;

        var usuario = await db.Usuarios.AsNoTracking()
            .Where(u => u.NombreUsuario == nombre)
            .Select(u => new { u.IdUsuario, u.IdPersona, u.Estado, u.MotivoBloqueo, u.FechaBloqueo })
            .SingleOrDefaultAsync(ct);
        if (usuario is null || !EsElegible(usuario.Estado, usuario.MotivoBloqueo, usuario.FechaBloqueo)) return;

        var emailPersona = await db.Personas.AsNoTracking()
            .Where(p => p.IdPersona == usuario.IdPersona)
            .Select(p => p.Email)
            .SingleOrDefaultAsync(ct);
        if (!EmailNormalizador.TryNormalizar(emailPersona, out var email)) return;

        var desde = timeProvider.GetUtcNow().UtcDateTime - VentanaSolicitudes;
        var recientes = await db.UsuarioTokens.AsNoTracking()
            .CountAsync(t => t.IdUsuario == usuario.IdUsuario
                          && t.Proposito == PropositoTokenUsuario.RecuperarClave
                          && t.FechaCreacion > desde, ct);
        if (recientes >= MaxSolicitudesPorVentana) return;

        TokenEmitido emitido;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            try
            {
                db.Auditorias.Add(NuevaAuditoria(usuario.IdUsuario, usuario.IdPersona,
                    "SOLICITAR_RECUPERACION_CLAVE", "Solicitud de recuperación de contraseña; enlace emitido."));
                await db.SaveChangesAsync(ct);
                emitido = await tokens.EmitirAsync(
                    usuario.IdUsuario, PropositoTokenUsuario.RecuperarClave, email, ipSolicitud, ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                // El dispose de la transacción no confirmada revierte todo.
                db.ChangeTracker.Clear();
                throw;
            }
        }

        // Solo tras el commit. Un fallo del envío no se propaga: la respuesta
        // pública debe ser idéntica a la de un usuario no elegible. Se registra
        // únicamente el tipo de error, nunca destinatario, usuario ni token.
        try
        {
            await emailSender.EnviarAsync(plantillas.RecuperacionClave(email, emitido), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("No se pudo enviar el email de recuperación de contraseña ({TipoError}).", ex.GetType().Name);
        }
    }

    // true solo si la contraseña quedó restablecida. Token inválido, usado,
    // vencido o de otro propósito: false sin consumir nada. Token válido cuyo
    // usuario ya no es elegible (estado o email cambiados): false y el token
    // queda consumido (un solo uso). Confirmación distinta, política o clave
    // repetida: ClaveNoValidaException y el token NO se consume.
    public async Task<bool> RestablecerAsync(RestablecerClaveRecuperacionRequest? request, CancellationToken ct = default)
    {
        if (request is null) return false;
        if (!string.Equals(request.ClaveNueva, request.ConfirmacionClaveNueva, StringComparison.Ordinal))
            throw new ClaveNoValidaException(MensajeConfirmacionDistinta);
        if (db.Database.CurrentTransaction is not null)
            throw new IdentidadConfiguracionException("El restablecimiento de contraseña requiere su propia transacción.");

        var claveNueva = request.ClaveNueva ?? string.Empty;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var consumido = await tokens.ConsumirAsync(request.Token, PropositoTokenUsuario.RecuperarClave, ct);
            if (consumido is null) return false; // nada que confirmar: el dispose revierte

            // Serializa contra cambios concurrentes del usuario (login fallido,
            // acciones administrativas) hasta el commit.
            await db.Database
                .SqlQueryRaw<int>("SELECT 1 AS Value FROM usuarios WITH (UPDLOCK, ROWLOCK) WHERE id_usuario = {0}",
                    consumido.IdUsuario)
                .ToListAsync(ct);

            var usuario = await db.Usuarios.SingleOrDefaultAsync(u => u.IdUsuario == consumido.IdUsuario, ct);
            var emailPersona = usuario is null
                ? null
                : await db.Personas.AsNoTracking()
                    .Where(p => p.IdPersona == usuario.IdPersona)
                    .Select(p => p.Email)
                    .SingleOrDefaultAsync(ct);

            var vigente = usuario is not null
                && EsElegible(usuario.Estado, usuario.MotivoBloqueo, usuario.FechaBloqueo)
                && EmailNormalizador.TryNormalizar(emailPersona, out var emailActual)
                && string.Equals(emailActual, consumido.EmailDestino, StringComparison.Ordinal);

            if (usuario is null || !vigente)
            {
                await tx.CommitAsync(ct); // solo persiste el consumo del token
                return false;
            }

            try { AuthService.ValidarPoliticaClave(claveNueva, usuario.NombreUsuario); }
            catch (InvalidOperationException ex) { throw new ClaveNoValidaException(ex.Message); }
            if (BCrypt.Net.BCrypt.Verify(claveNueva, usuario.PasswordHash))
                throw new ClaveNoValidaException(MensajeClaveIgualActual);

            var levantaBloqueo = BloqueoAutomaticoUsuario.Es(usuario);
            usuario.PasswordHash        = BCrypt.Net.BCrypt.HashPassword(claveNueva);
            usuario.RequiereCambioClave = false;
            usuario.IntentosFallidos    = 0;
            usuario.FechaActualizacion  = DateTime.UtcNow;
            if (levantaBloqueo)
            {
                usuario.Estado        = EstadosUsuario.Activo;
                usuario.FechaBloqueo  = null;
                usuario.MotivoBloqueo = null;
            }
            db.Auditorias.Add(NuevaAuditoria(usuario.IdUsuario, usuario.IdPersona, "RECUPERAR_CLAVE",
                levantaBloqueo
                    ? "Contraseña restablecida por recuperación; bloqueo automático levantado."
                    : "Contraseña restablecida por recuperación."));

            await db.SaveChangesAsync(ct);
            await VersionSeguridadUsuario.IncrementarAsync(db, usuario.IdUsuario, ct);
            await tx.CommitAsync(ct);
            return true;
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private static bool EsElegible(string? estado, string? motivoBloqueo, DateTime? fechaBloqueo) =>
        string.Equals(estado, EstadosUsuario.Activo, StringComparison.Ordinal)
        || BloqueoAutomaticoUsuario.Es(estado, motivoBloqueo, fechaBloqueo);

    // Observación neutra: nunca email, token, clave ni hash.
    private static Auditoria NuevaAuditoria(long idUsuario, long idPersona, string accion, string observacion) => new()
    {
        IdUsuario   = idUsuario,
        IdPersona   = idPersona,
        Modulo      = "SEGURIDAD",
        Accion      = accion,
        Entidad     = "usuarios",
        IdEntidad   = idUsuario.ToString(),
        Resultado   = "EXITOSO",
        Observacion = observacion,
        FechaEvento = DateTime.UtcNow,
    };
}

// P0-B2 — rechazo de la clave nueva (confirmación, política o reutilización).
// Es el ÚNICO error cuyo mensaje se devuelve al cliente (400): cualquier otra
// excepción, incluida una InvalidOperationException interna de EF, termina
// en el 500 genérico y nunca expone su mensaje.
public sealed class ClaveNoValidaException(string message) : Exception(message);
