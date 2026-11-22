using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// P0-B1 — registro público seguro de usuario final y verificación de email.
// El servidor asigna unidad XPAY_COL, rol USUARIO_FINAL y estado
// PENDIENTE_VERIFICACION. El autorregistro habilitado guarda las aceptaciones
// en la misma transacción que la cuenta y el token. Solo el flujo legacy no
// productivo conserva la creación de wallet. El correo se envía tras confirmar
// la transacción y el token solo viaja al destinatario.
public sealed class RegistroPublicoService(
    XpayDbContext db,
    IAmbienteXpay ambiente,
    TokenUsuarioService tokens,
    IEmailSender emailSender,
    PlantillasEmailIdentidad plantillas,
    TimeProvider timeProvider,
    ILogger<RegistroPublicoService> logger,
    IConfiguration configuracion)
{
    public const string CodigoUnidadNegocio = "XPAY_COL";
    internal const string RolUsuarioFinal = "USUARIO_FINAL";
    internal const string AccionAceptarTerminos = "ACEPTAR_TERMINOS_REGISTRO";
    internal const string AccionAutorizarDatos = "AUTORIZAR_DATOS_REGISTRO";
    internal const string MensajeUsuarioExiste = "El nombre de usuario ya existe.";

    // P0-B3 — tope de tokens VERIFICAR_EMAIL por usuario y ventana; el del alta
    // cuenta (mismo criterio que la recuperación de P0-B2).
    internal const int MaxTokensVerificacionPorVentana = 3;
    internal static readonly TimeSpan VentanaTokensVerificacion = TimeSpan.FromHours(1);
    private const int LongitudMaximaUsuario = 100; // usuarios.usuario VARCHAR(100)

    // Las dos evidencias se guardan juntas con la cuenta. La autorización de
    // tratamiento en el registro no habilita por sí misma la captura de KYC.
    internal static async Task<bool> EsCuentaLimitadaAsync(XpayDbContext db, long idUsuario)
    {
        if (!await EsAutorregistroAsync(db, idUsuario)) return false;
        var habilitada = await (from p in db.WalletPreparaciones.AsNoTracking()
            join u in db.Usuarios.AsNoTracking() on p.IdUsuario equals u.IdUsuario
            join persona in db.Personas.AsNoTracking() on p.IdPersona equals persona.IdPersona
            join w in db.Wallets.AsNoTracking() on p.IdWallet equals w.IdWallet
            where p.IdUsuario == idUsuario && p.IdPersona == u.IdPersona && w.IdPersona == p.IdPersona
                && w.IdUnidadNegocio == persona.IdUnidadNegocio && w.TipoWallet == "PERSONA" && w.Estado == "ACTIVA"
                && p.CriteriosVersion == PreparacionWalletService.CriteriosVersion
                && p.TerminosVersion == PreparacionWalletConfiguracion.Version && p.TerminosSha256 == PreparacionWalletConfiguracion.TerminosSha256
                && p.PoliticaDatosVersion == PreparacionWalletConfiguracion.Version && p.PoliticaDatosSha256 == PreparacionWalletConfiguracion.PoliticaDatosSha256
                && u.Estado == "ACTIVO" && u.EmailVerificado && !u.RequiereCambioClave && u.EstadoKycActual == "APROBADO"
                && persona.Estado == "ACTIVA" && persona.IdentidadVerificada
                && persona.IdentidadVerificadaProveedor == "VERIFF" && persona.IdentidadVerificadaFecha != null
            select new { u, persona }).SingleOrDefaultAsync();
        if (habilitada is null || !await PreparacionWalletService.IdentidadAprobadaAsync(db, habilitada.u, habilitada.persona)) return true;
        if (!await AceptacionesRegistroWallet.VigenteAsync(db, idUsuario, habilitada.persona.IdPersona, true)
            || !await AceptacionesRegistroWallet.VigenteAsync(db, idUsuario, habilitada.persona.IdPersona, false)) return true;
        var entidad = idUsuario.ToString(CultureInfo.InvariantCulture);
        // La prueba durable y la evidencia de aceptación deben existir juntas.
        return !await db.Auditorias.AnyAsync(a => a.IdUsuario == idUsuario && a.IdPersona == habilitada.persona.IdPersona
            && a.Modulo == "USUARIOS" && a.Entidad == "usuarios" && a.IdEntidad == entidad
            && a.Accion == PreparacionWalletService.AccionActivar && a.Resultado == "EXITOSO");
    }

    internal static Task<bool> EsAutorregistroAsync(XpayDbContext db, long idUsuario, CancellationToken ct = default) =>
        db.Auditorias.AnyAsync(a => a.IdUsuario == idUsuario && a.Modulo == "USUARIOS"
            && a.Accion == AccionAceptarTerminos && a.Resultado == "EXITOSO", ct);

    public RegistroPublicoConfiguracionResponse? ObtenerConfiguracion() =>
        ObtenerRegistroHabilitado()?.Documentos.DatosPublicos;

    private bool AutorregistroHabilitado =>
        string.Equals(configuracion[DocumentosRegistroPublico.ClaveHabilitado], "true", StringComparison.Ordinal);

    private (DocumentosRegistroPublico Documentos, IEmailSender Sender)? ObtenerRegistroHabilitado()
    {
        if (!AutorregistroHabilitado) return null;

        var qa = string.Equals(ambiente.Nombre, AmbienteXpay.Qa, StringComparison.Ordinal)
            && emailSender is EmailSenderQaSmtp;
        var prod = string.Equals(ambiente.Nombre, "Production", StringComparison.Ordinal)
            && emailSender is EmailSenderAcs
            && string.Equals(configuracion[DocumentosRegistroPublico.ClaveModalidad],
                DocumentosRegistroPublico.ModalidadLimitada, StringComparison.Ordinal);
        if (!qa && !prod) return null;

        var documentos = DocumentosRegistroPublico.Desde(configuracion);
        if (documentos is null || (prod && !DocumentosEnOrigenProductivo(documentos))) return null;
        return (documentos, emailSender);
    }

    private bool DocumentosEnOrigenProductivo(DocumentosRegistroPublico documentos)
    {
        if (!Uri.TryCreate(configuracion[PlantillasEmailIdentidad.ClaveUrlFrontend], UriKind.Absolute, out var frontend)
            || frontend.Scheme != Uri.UriSchemeHttps || frontend.Host != "app.xpaycol.com"
            || !string.IsNullOrEmpty(frontend.UserInfo) || !string.IsNullOrEmpty(frontend.Query)
            || !string.IsNullOrEmpty(frontend.Fragment) || frontend.AbsolutePath != "/" || !frontend.IsDefaultPort)
            return false;
        return new[] { documentos.Terminos, documentos.PoliticaDatos }.All(d =>
            Uri.TryCreate(d.DatosPublicos.Url, UriKind.Absolute, out var url)
            && url.Scheme == Uri.UriSchemeHttps && url.Host == frontend.Host && url.IsDefaultPort);
    }

    private void ExigirDisponibilidadProductiva()
    {
        if (ambiente.EsProductivo && ObtenerRegistroHabilitado() is null)
            throw new RegistroPublicoNoDisponibleException();
    }

    public async Task RegistrarAsync(RegistroPublicoRequest request, string? ipSolicitud, CancellationToken ct = default)
    {
        var registro = ObtenerRegistroHabilitado();
        if (registro is null && (ambiente.EsProductivo || AutorregistroHabilitado || request.Consentimiento is not null))
            throw new RegistroPublicoNoDisponibleException();

        // Validación y configuración del enlace antes de tocar la BD.
        var datos = RegistroPublicoValidacion.Validar(request, ambiente);
        if (registro is { } alta)
        {
            alta.Documentos.ValidarConsentimiento(request.Consentimiento);
            if (alta.Sender is EmailSenderQaSmtp smtp && !smtp.PermiteDestinatario(datos.Email))
                throw new InvalidOperationException("El registro en QA solo está disponible para correos de prueba autorizados.");
        }
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

                var ahora = timeProvider.GetUtcNow().UtcDateTime;
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

                if (registro is { } autorregistro)
                {
                    var documentos = autorregistro.Documentos;
                    db.Auditorias.AddRange(
                        EvidenciaConsentimiento(usuario, AccionAceptarTerminos, "TERMINOS_Y_CONDICIONES",
                            documentos.Terminos, ahora, ipSolicitud),
                        EvidenciaConsentimiento(usuario, AccionAutorizarDatos, "POLITICA_TRATAMIENTO_DATOS",
                            documentos.PoliticaDatos, ahora, ipSolicitud));
                }
                else
                {
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
                }

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

    internal static Auditoria EvidenciaConsentimiento(Usuario usuario, string accion, string tipoDocumento,
        DocumentoRegistroPublico documento, DateTime fechaUtc, string? ipSolicitud, string canal = "WEB_AUTORREGISTRO") =>
        new()
        {
            IdUsuario   = usuario.IdUsuario,
            IdPersona   = usuario.IdPersona,
            Modulo      = "USUARIOS",
            Accion      = accion,
            Entidad     = "usuarios",
            IdEntidad   = usuario.IdUsuario.ToString(CultureInfo.InvariantCulture),
            ValorNuevo  = JsonSerializer.Serialize(new
            {
                tipoDocumento,
                version = documento.DatosPublicos.Version,
                sha256 = documento.Sha256,
                canal,
            }),
            Ip          = IPAddress.TryParse(ipSolicitud, out var ip) ? ip.ToString() : null,
            Resultado   = "EXITOSO",
            FechaEvento = fechaUtc,
        };

    // P0-B3 — reenvío del enlace de verificación. Nunca revela si el usuario
    // existe ni su estado: no lanza por elegibilidad y el controlador responde
    // siempre lo mismo. Elegible solo PENDIENTE_VERIFICACION con
    // personas.email normalizable; el token nuevo invalida el anterior
    // (semántica de TokenUsuarioService.EmitirAsync).
    public async Task ReenviarVerificacionAsync(string? nombreUsuario, string? ipSolicitud, CancellationToken ct = default)
    {
        ExigirDisponibilidadProductiva();
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
        ExigirDisponibilidadProductiva();
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
