using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// La casilla C es distinta de las aceptaciones A/B del alta. Su aceptación no
// aprueba KYC, no crea Wallet, no habilita el piloto y no vincula empleados.
// El PDF y el texto corresponden a la versión aprobada incluida en public/legal.
public sealed class ConsentimientoBiometricoService(
    XpayDbContext db, IConfiguration config, IAmbienteXpay ambiente, TimeProvider timeProvider)
{
    public const string ClaveHabilitada = "XPAY_ALTA_PUBLICA_KYC_HABILITADA";
    public const string PrefijoDocumento = "XPAY_AUTORIZACION_KYC";
    public const string AccionAutorizar = "AUTORIZAR_BIOMETRIA_KYC";
    public const string TextoCasilla = "AUTORIZACIÓN OPCIONAL Y EXPRESA DE DATOS SENSIBLES: Entiendo que la verificación de identidad mediante Veriff puede implicar tratamiento de fotografías, video y datos biométricos sensibles. Conozco que no estoy obligado a autorizar el tratamiento de datos sensibles, salvo excepciones legales, y autorizo expresamente su tratamiento exclusivamente para verificar mi identidad, detectar suplantaciones y gestionar evidencias y resultados del proceso, según la Política de Tratamiento de Datos Personales.";
    internal const string VersionDocumentoAprobado = "1.2";
    internal const string FechaDocumentoAprobado = "2026-11-18";
    internal const string ShaDocumentoAprobado = "22a41de9c1c736925694c2ac64d92c591f935842e7193fbf9ce4b1ae01d20c6f";
    internal const string Canal = "PERFIL_WEB";
    internal const string TipoConsentimiento = "AUTORIZACION_BIOMETRIA_KYC";
    internal const string TipoDocumento = "AUTORIZACIONES_REGISTRO_KYC";
    internal static readonly string ShaTextoCasilla = Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(TextoCasilla))).ToLowerInvariant();

    // Las versiones de bundle 1.2 y 1.3 conservan exactamente esta casilla
    // y su finalidad biométrica. La configuración activa elige el PDF que
    // se ofrece a una nueva aceptación; no reetiqueta evidencia histórica.
    internal DocumentoRegistroPublico? ObtenerDocumento()
    {
        if (!Uri.TryCreate(config[PlantillasEmailIdentidad.ClaveUrlFrontend], UriKind.Absolute, out var frontend)
            || frontend.Scheme != Uri.UriSchemeHttps || !frontend.IsDefaultPort
            || frontend.UserInfo.Length != 0 || frontend.Query.Length != 0 || frontend.Fragment.Length != 0
            || frontend.AbsolutePath != "/"
            || (ambiente.EsProductivo && frontend.Host != "app.xpaycol.com")) return null;
        var documento = DocumentoFirme(config[PrefijoDocumento + "_VERSION"], frontend.GetLeftPart(UriPartial.Authority));
        if (documento is null
            || config[PrefijoDocumento + "_FECHA"] != documento.DatosPublicos.Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            || !string.Equals(config[PrefijoDocumento + "_SHA256"], documento.Sha256, StringComparison.OrdinalIgnoreCase)
            || config[PrefijoDocumento + "_URL"] != documento.DatosPublicos.Url) return null;
        return documento;
    }

    private static DocumentoRegistroPublico? DocumentoFirme(string? version, string origen)
    {
        (string Fecha, string Sha)? datos = version switch
        {
            VersionDocumentoAprobado => (FechaDocumentoAprobado, ShaDocumentoAprobado),
            PreparacionWalletConfiguracion.Version => (PreparacionWalletConfiguracion.Fecha, PreparacionWalletConfiguracion.AutorizacionSha256),
            _ => null,
        };
        if (datos is not { } d || d.Sha is not { Length: 64 } || !d.Sha.All(Uri.IsHexDigit)
            || !DateOnly.TryParseExact(d.Fecha, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)) return null;
        return new(new(version!, fecha, origen + $"/legal/v{version}/autorizaciones.pdf"), d.Sha.ToLowerInvariant());
    }

    private static bool DocumentoEsFirme(DocumentoRegistroPublico documento)
    {
        if (!Uri.TryCreate(documento.DatosPublicos.Url, UriKind.Absolute, out var url)
            || url.Scheme != Uri.UriSchemeHttps || !url.IsDefaultPort || url.UserInfo.Length != 0
            || url.Query.Length != 0 || url.Fragment.Length != 0) return false;
        return documento == DocumentoFirme(documento.DatosPublicos.Version, url.GetLeftPart(UriPartial.Authority));
    }

    public bool Habilitado => config[ClaveHabilitada] == "true"
        && ObtenerDocumento() is not null;

    public async Task<ConsentimientoBiometricoResponse> ObtenerEstadoAsync(long idUsuario, CancellationToken ct = default)
    {
        var documento = ObtenerDocumento();
        if (!Habilitado) return new(documento?.DatosPublicos, TextoCasilla, false, false);
        var elegible = await UsuarioElegibleAsync(idUsuario, ct);
        return new(documento!.DatosPublicos, TextoCasilla,
            elegible && await TieneConsentimientoVigenteAsync(idUsuario, ct), elegible);
    }

    public async Task<ConsentimientoBiometricoResponse> AutorizarAsync(long idUsuario,
        AutorizarConsentimientoBiometricoRequest request, string? ipSolicitud, CancellationToken ct = default)
    {
        var documento = ObtenerDocumento();
        if (!Habilitado || documento is null) throw new ConsentimientoBiometricoNoDisponibleException();
        ValidarAceptacion(request, documento);
        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("La autorización biométrica requiere su propia transacción.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (await AppLockHelper.AdquirirAsync(db, $"XPAY:CONSENTIMIENTO_KYC:{idUsuario}", ct) < 0)
            throw new InvalidOperationException("Hay otra autorización en curso. Actualiza e intenta de nuevo.");
        var usuario = await db.Usuarios.AsNoTracking().SingleOrDefaultAsync(u => u.IdUsuario == idUsuario, ct)
            ?? throw new UnauthorizedAccessException("Usuario no disponible.");
        await ContactoVerificacionService.BloquearPersonaAsync(db, usuario.IdPersona, ct);
        if (!await UsuarioElegibleAsync(idUsuario, ct))
            throw new InvalidOperationException("Confirma tu correo e ingresa con tu cuenta pública activa antes de autorizar la verificación.");
        if (!await TieneConsentimientoVigenteAsync(idUsuario, ct))
        {
            var ahora = timeProvider.GetUtcNow().UtcDateTime;
            db.Auditorias.Add(new Auditoria
            {
                IdUsuario = idUsuario, IdPersona = usuario.IdPersona,
                Modulo = "USUARIOS", Accion = AccionAutorizar,
                Entidad = "usuarios", IdEntidad = idUsuario.ToString(CultureInfo.InvariantCulture),
                ValorNuevo = JsonSerializer.Serialize(new
                {
                    tipoConsentimiento = TipoConsentimiento, tipoDocumento = TipoDocumento,
                    autorizaBiometria = true, version = documento.DatosPublicos.Version,
                    sha256 = documento.Sha256, fechaDocumento = documento.DatosPublicos.Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    urlDocumento = documento.DatosPublicos.Url, textoCasillaSha256 = ShaTextoCasilla,
                    fechaAceptacionUtc = ahora.ToString("O", CultureInfo.InvariantCulture), canal = Canal,
                }),
                Ip = IPAddress.TryParse(ipSolicitud, out var ip) ? ip.ToString() : null,
                Resultado = "EXITOSO", FechaEvento = ahora,
                Observacion = "Autorización opcional y expresa de la casilla C; no habilita productos financieros.",
            });
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return await ObtenerEstadoAsync(idUsuario, ct);
    }

    internal static void ValidarAceptacion(AutorizarConsentimientoBiometricoRequest request, DocumentoRegistroPublico documento)
    {
        if (request is not { AutorizaBiometria: true })
            throw new InvalidOperationException("La autorización biométrica es opcional y requiere marcar expresamente la casilla para iniciar Veriff.");
        if (!string.Equals(request.VersionDocumento, documento.DatosPublicos.Version, StringComparison.Ordinal))
            throw new InvalidOperationException("El documento de autorización cambió. Recarga la página y revísalo de nuevo.");
    }

    // La última evidencia decide: un registro posterior inválido o revocado no
    // permite rescatar una aceptación anterior. No se reutilizan las casillas A/B.
    public async Task<bool> TieneConsentimientoVigenteAsync(long idUsuario, CancellationToken ct = default)
    {
        if (!Habilitado) return false;
        var documento = ObtenerDocumento()!;
        var entidad = idUsuario.ToString(CultureInfo.InvariantCulture);
        var idPersona = await db.Usuarios.AsNoTracking().Where(u => u.IdUsuario == idUsuario)
            .Select(u => (long?)u.IdPersona).SingleOrDefaultAsync(ct);
        if (idPersona is null) return false;
        var evidencia = await db.Auditorias.AsNoTracking()
            .Where(a => a.IdUsuario == idUsuario && a.IdPersona == idPersona && a.Modulo == "USUARIOS"
                && a.Accion == AccionAutorizar && a.Entidad == "usuarios" && a.IdEntidad == entidad)
            .OrderByDescending(a => a.IdAuditoria)
            .Select(a => new { a.Resultado, a.ValorNuevo }).FirstOrDefaultAsync(ct);
        return evidencia is not null && EvidenciaCompatible(evidencia.Resultado, evidencia.ValorNuevo, documento);
    }

    // Solo se prueba la ÚLTIMA fila seleccionada arriba. La compatibilidad
    // pertenece al propósito/texto fijo de C, no a A/B ni a cualquier versión
    // enviada por el cliente. Una última revocación o evidencia inválida no
    // permite rescatar una aceptación anterior de ninguno de los dos bundles.
    internal static bool EvidenciaCompatible(string resultado, string? valor, DocumentoRegistroPublico documentoActivo)
    {
        if (!DocumentoEsFirme(documentoActivo)) return false;
        var origen = new Uri(documentoActivo.DatosPublicos.Url).GetLeftPart(UriPartial.Authority);
        return new[] { VersionDocumentoAprobado, PreparacionWalletConfiguracion.Version }
            .Select(version => DocumentoFirme(version, origen))
            .Any(documento => documento is not null && EvidenciaVigente(resultado, valor, documento));
    }

    internal static bool EvidenciaVigente(string resultado, string? valor, DocumentoRegistroPublico documento)
    {
        if (resultado != "EXITOSO" || string.IsNullOrWhiteSpace(valor) || !DocumentoEsFirme(documento)) return false;
        try
        {
            using var evidencia = JsonDocument.Parse(valor);
            var e = evidencia.RootElement;
            return e.ValueKind == JsonValueKind.Object
                && e.TryGetProperty("autorizaBiometria", out var autorizado) && autorizado.ValueKind == JsonValueKind.True
                && Texto(e, "tipoConsentimiento", TipoConsentimiento) && Texto(e, "tipoDocumento", TipoDocumento)
                && Texto(e, "version", documento.DatosPublicos.Version) && Texto(e, "sha256", documento.Sha256)
                && Texto(e, "fechaDocumento", documento.DatosPublicos.Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) && Texto(e, "urlDocumento", documento.DatosPublicos.Url)
                && Texto(e, "textoCasillaSha256", ShaTextoCasilla) && Texto(e, "canal", Canal);
        }
        catch (JsonException) { return false; }
    }

    private static bool Texto(JsonElement json, string nombre, string esperado) =>
        json.TryGetProperty(nombre, out var valor) && valor.ValueKind == JsonValueKind.String
            && string.Equals(valor.GetString(), esperado, StringComparison.Ordinal);

    public async Task<bool> PuedeIniciarKycAsync(long idUsuario, CancellationToken ct = default) =>
        Habilitado && await UsuarioElegibleAsync(idUsuario, ct)
            && await TieneConsentimientoVigenteAsync(idUsuario, ct);

    private async Task<bool> UsuarioElegibleAsync(long idUsuario, CancellationToken ct) =>
        await db.Usuarios.AsNoTracking().AnyAsync(u => u.IdUsuario == idUsuario
            && u.Estado == EstadosUsuario.Activo && u.EmailVerificado
            && db.Personas.Any(p => p.IdPersona == u.IdPersona && p.Estado == "ACTIVA"), ct)
        && await RegistroPublicoService.EsAutorregistroAsync(db, idUsuario, ct);
}

public sealed class ConsentimientoBiometricoNoDisponibleException()
    : Exception("La autorización de verificación de identidad no está disponible.");
