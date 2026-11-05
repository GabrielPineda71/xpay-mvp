using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// P0-B1 — registro público seguro de usuario final y verificación de email.
// El cliente solo aporta RegistroPublicoRequest (email, usuario, clave,
// celular): la unidad (XPAY_COL), el rol (USUARIO_FINAL) y el estado inicial
// (PENDIENTE_VERIFICACION) los decide el servidor. Persona, usuario, rol,
// wallet, saldo, auditoría y token VERIFICAR_EMAIL se crean en UNA
// transacción propia; el email se envía solo DESPUÉS del commit y el token
// nunca se devuelve ni se registra. No reutiliza RegistroUsuarioFinalService
// (acepta IdUnidadNegocio y crea ACTIVO) ni RegistroInicialService (sin email).
public sealed class RegistroPublicoService(
    XpayDbContext db,
    IAmbienteXpay ambiente,
    TokenUsuarioService tokens,
    IEmailSender emailSender,
    PlantillasEmailIdentidad plantillas,
    TimeProvider timeProvider,
    ILogger<RegistroPublicoService> logger)
{
    public const string CodigoUnidadNegocio = "XPAY_COL";
    private const string RolUsuarioFinal    = "USUARIO_FINAL";
    internal const string MensajeUsuarioExiste = "El nombre de usuario ya existe.";

    // P0-B3 — tope de tokens VERIFICAR_EMAIL por usuario y ventana; el del alta
    // cuenta (mismo criterio que la recuperación de P0-B2).
    internal const int MaxTokensVerificacionPorVentana = 3;
    internal static readonly TimeSpan VentanaTokensVerificacion = TimeSpan.FromHours(1);
    private const int LongitudMaximaUsuario = 100; // usuarios.usuario VARCHAR(100)

    public async Task RegistrarAsync(RegistroPublicoRequest request, string? ipSolicitud, CancellationToken ct = default)
    {
        // Validación y configuración del enlace antes de tocar la BD.
        var datos = RegistroPublicoValidacion.Validar(request, ambiente);
        plantillas.ValidarConfiguracion();
        if (db.Database.CurrentTransaction is not null)
            throw new IdentidadConfiguracionException("El registro público requiere su propia transacción.");

        TokenEmitido emitido;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            try
            {
                var idUnidad = await ResolverUnidadAsync(ct);
                var idRol    = await ResolverRolUsuarioFinalAsync(ct);
                if (await db.Usuarios.AnyAsync(u => u.NombreUsuario == datos.NombreUsuario, ct))
                    throw new InvalidOperationException(MensajeUsuarioExiste);

                var ahora = DateTime.UtcNow;
                var persona = new Persona
                {
                    IdUnidadNegocio = idUnidad,
                    Celular         = datos.Celular,
                    Email           = datos.Email,
                    Pais            = "Colombia",
                    Estado          = "ACTIVA",
                    FechaCreacion   = ahora,
                };
                db.Personas.Add(persona);
                await db.SaveChangesAsync(ct);

                var usuario = new Usuario
                {
                    IdPersona       = persona.IdPersona,
                    NombreUsuario   = datos.NombreUsuario,
                    PasswordHash    = BCrypt.Net.BCrypt.HashPassword(request.Password),
                    Estado          = EstadosUsuario.PendienteVerificacion,
                    EmailVerificado = false,
                    FechaCreacion   = ahora,
                };
                db.Usuarios.Add(usuario);
                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException ex) when (EsUsuarioDuplicado(ex))
                {
                    throw new InvalidOperationException(MensajeUsuarioExiste);
                }

                db.UsuarioRoles.Add(new UsuarioRol
                {
                    IdUsuario       = usuario.IdUsuario,
                    IdRol           = idRol,
                    Estado          = "ACTIVO",
                    FechaAsignacion = ahora,
                });

                // Etiqueta técnica: aún no hay nombres legales (los aporta el KYC).
                var wallet = new Wallet
                {
                    IdUnidadNegocio = idUnidad,
                    TipoWallet      = "PERSONA",
                    IdPersona       = persona.IdPersona,
                    NombreWallet    = $"Wallet {datos.NombreUsuario}",
                    Estado          = "ACTIVA",
                    FechaCreacion   = ahora,
                };
                db.Wallets.Add(wallet);
                await db.SaveChangesAsync(ct);

                db.WalletSaldos.Add(new WalletSaldo { IdWallet = wallet.IdWallet, FechaActualizacion = ahora });
                db.Auditorias.Add(new Auditoria
                {
                    IdUsuario   = usuario.IdUsuario,
                    IdPersona   = persona.IdPersona,
                    Modulo      = "USUARIOS",
                    Accion      = "REGISTRO_PUBLICO",
                    Entidad     = "usuarios",
                    IdEntidad   = usuario.IdUsuario.ToString(),
                    Resultado   = "EXITOSO",
                    Observacion = "Registro público de usuario final; pendiente de verificar email.",
                    FechaEvento = ahora,
                });
                await db.SaveChangesAsync(ct);

                emitido = await tokens.EmitirAsync(
                    usuario.IdUsuario, PropositoTokenUsuario.VerificarEmail, datos.Email, ipSolicitud, ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                // El dispose de la transacción no confirmada revierte todo.
                db.ChangeTracker.Clear();
                throw;
            }
        }

        // Solo tras el commit. El token viaja únicamente en el cuerpo del email.
        // P0-B3 — un fallo del envío no revierte ni borra la cuenta (queda
        // PENDIENTE_VERIFICACION y el usuario puede pedir un reenvío) y no se
        // propaga: se registra solo el tipo de error.
        await EnviarSinPropagarAsync(plantillas.VerificacionEmail(datos.Email, emitido), ct);
    }

    // P0-B3 — reenvío del enlace de verificación. Nunca revela si el usuario
    // existe ni su estado: no lanza por elegibilidad y el controlador responde
    // siempre lo mismo. Elegible solo PENDIENTE_VERIFICACION con
    // personas.email normalizable; el token nuevo invalida el anterior
    // (semántica de TokenUsuarioService.EmitirAsync).
    public async Task ReenviarVerificacionAsync(string? nombreUsuario, string? ipSolicitud, CancellationToken ct = default)
    {
        // Configuración y transacción se validan igual para todos.
        plantillas.ValidarConfiguracion();
        if (db.Database.CurrentTransaction is not null)
            throw new IdentidadConfiguracionException("El reenvío de verificación requiere su propia transacción.");

        var nombre = nombreUsuario?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(nombre) || nombre.Length > LongitudMaximaUsuario) return;

        var usuario = await db.Usuarios.AsNoTracking()
            .Where(u => u.NombreUsuario == nombre)
            .Select(u => new { u.IdUsuario, u.IdPersona, u.Estado })
            .SingleOrDefaultAsync(ct);
        if (usuario is null
            || !string.Equals(usuario.Estado, EstadosUsuario.PendienteVerificacion, StringComparison.Ordinal)) return;

        var emailPersona = await db.Personas.AsNoTracking()
            .Where(p => p.IdPersona == usuario.IdPersona)
            .Select(p => p.Email)
            .SingleOrDefaultAsync(ct);
        if (!EmailNormalizador.TryNormalizar(emailPersona, out var email)) return;

        var desde = timeProvider.GetUtcNow().UtcDateTime - VentanaTokensVerificacion;
        var recientes = await db.UsuarioTokens.AsNoTracking()
            .CountAsync(t => t.IdUsuario == usuario.IdUsuario
                          && t.Proposito == PropositoTokenUsuario.VerificarEmail
                          && t.FechaCreacion > desde, ct);
        if (recientes >= MaxTokensVerificacionPorVentana) return;

        TokenEmitido emitido;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            try
            {
                db.Auditorias.Add(new Auditoria
                {
                    IdUsuario   = usuario.IdUsuario,
                    IdPersona   = usuario.IdPersona,
                    Modulo      = "USUARIOS",
                    Accion      = "REENVIAR_VERIFICACION_EMAIL",
                    Entidad     = "usuarios",
                    IdEntidad   = usuario.IdUsuario.ToString(),
                    Resultado   = "EXITOSO",
                    Observacion = "Reenvío del enlace de verificación de email.",
                    FechaEvento = DateTime.UtcNow,
                });
                await db.SaveChangesAsync(ct);
                emitido = await tokens.EmitirAsync(
                    usuario.IdUsuario, PropositoTokenUsuario.VerificarEmail, email, ipSolicitud, ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                // El dispose de la transacción no confirmada revierte todo.
                db.ChangeTracker.Clear();
                throw;
            }
        }

        await EnviarSinPropagarAsync(plantillas.VerificacionEmail(email, emitido), ct);
    }

    // Solo se llama tras un commit. Nunca registra destinatario, usuario ni token.
    private async Task EnviarSinPropagarAsync(MensajeEmail mensaje, CancellationToken ct)
    {
        try
        {
            await emailSender.EnviarAsync(mensaje, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("No se pudo enviar el email de verificación ({TipoError}).", ex.GetType().Name);
        }
    }

    // true solo si activa la cuenta; cualquier otro caso devuelve false sin
    // indicar la causa. Un token válido cuyo usuario ya no cumple (email
    // cambiado o estado distinto de PENDIENTE_VERIFICACION) queda consumido
    // igualmente: un solo uso.
    public async Task<bool> VerificarEmailAsync(string? token, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is not null)
            throw new IdentidadConfiguracionException("La verificación de email requiere su propia transacción.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var consumido = await tokens.ConsumirAsync(token, PropositoTokenUsuario.VerificarEmail, ct);
            if (consumido is null) return false; // nada que confirmar: el dispose revierte

            var usuario = await db.Usuarios.SingleOrDefaultAsync(u => u.IdUsuario == consumido.IdUsuario, ct);
            var persona = usuario is null
                ? null
                : await db.Personas.AsNoTracking().SingleOrDefaultAsync(p => p.IdPersona == usuario.IdPersona, ct);

            var vigente = usuario is not null
                && persona is not null
                && string.Equals(usuario.Estado, EstadosUsuario.PendienteVerificacion, StringComparison.Ordinal)
                && EmailNormalizador.TryNormalizar(persona.Email, out var emailActual)
                && string.Equals(emailActual, consumido.EmailDestino, StringComparison.Ordinal);

            if (!vigente)
            {
                await tx.CommitAsync(ct); // solo persiste el consumo del token
                return false;
            }

            var ahora = DateTime.UtcNow;
            usuario!.EmailVerificado   = true;
            usuario.Estado             = EstadosUsuario.Activo;
            usuario.FechaActualizacion = ahora;
            db.Auditorias.Add(new Auditoria
            {
                IdUsuario   = usuario.IdUsuario,
                IdPersona   = usuario.IdPersona,
                Modulo      = "USUARIOS",
                Accion      = "VERIFICAR_EMAIL",
                Entidad     = "usuarios",
                IdEntidad   = usuario.IdUsuario.ToString(),
                Resultado   = "EXITOSO",
                Observacion = "Email verificado; cuenta activada.",
                FechaEvento = ahora,
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }

    // unidades_negocio.codigo tiene índice UNIQUE; aun así se exige exactamente
    // una fila ACTIVA y cualquier otro resultado falla cerrado.
    private async Task<long> ResolverUnidadAsync(CancellationToken ct)
    {
        var ids = await db.Database
            .SqlQueryRaw<long>(
                "SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0} AND estado = {1}",
                CodigoUnidadNegocio, "ACTIVA")
            .ToListAsync(ct);
        if (ids.Count != 1)
            throw new IdentidadConfiguracionException("La unidad de negocio del registro público no está disponible.");
        return ids[0];
    }

    private async Task<long> ResolverRolUsuarioFinalAsync(CancellationToken ct)
    {
        var ids = await db.Roles
            .Where(r => r.Codigo == RolUsuarioFinal && r.Estado == "ACTIVO")
            .Select(r => r.IdRol)
            .ToListAsync(ct);
        if (ids.Count != 1)
            throw new IdentidadConfiguracionException("El rol del registro público no está disponible.");
        return ids[0];
    }

    private static bool EsUsuarioDuplicado(DbUpdateException ex) =>
        ex.InnerException is SqlException sqlEx
        && (sqlEx.Number == 2601 || sqlEx.Number == 2627)
        && sqlEx.Message.Contains("IX_usuarios_usuario", StringComparison.OrdinalIgnoreCase);
}
