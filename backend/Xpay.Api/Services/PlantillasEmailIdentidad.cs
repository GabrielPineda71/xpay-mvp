using System.Globalization;
using Xpay.Api.Common;

namespace Xpay.Api.Services;

// P0-B1/P0-B2 — plantillas de email de identidad, separadas de la lógica de
// registro y recuperación. El enlace lleva el token en el FRAGMENTO
// (#token=...): el fragmento no viaja al servidor ni queda en logs de acceso;
// las pantallas del frontend (pendientes, fuera de estos bloques) lo leerán y
// lo enviarán en el body de POST api/usuarios/verificar-email o de
// POST api/auth/restablecer-clave.
//
// Configuración: Identidad:UrlFrontend (URL absoluta sin query ni fragmento;
// https obligatorio en productivo), Identidad:RutaVerificacionEmail e
// Identidad:RutaRecuperacionClave (rutas que empiezan por "/", sin query ni
// fragmento). En un ambiente productivo su ausencia o invalidez falla cerrado
// ANTES de tocar la BD. En Development/QA/Test se usa un destino no real
// (.invalid): el sender local no envía correo.
public sealed class PlantillasEmailIdentidad(IConfiguration configuration, IAmbienteXpay ambiente)
{
    public const string ClaveUrlFrontend      = "Identidad:UrlFrontend";
    public const string ClaveRutaVerificacion = "Identidad:RutaVerificacionEmail";
    public const string ClaveRutaRecuperacion = "Identidad:RutaRecuperacionClave";
    internal const string DestinoNoReal             = "https://xpay.invalid/verificacion-pendiente";
    internal const string DestinoNoRealRecuperacion = "https://xpay.invalid/recuperacion-pendiente";

    // Sin efectos: permiten fallar cerrado antes de tocar la BD.
    public void ValidarConfiguracion() => _ = Base(ClaveRutaVerificacion, DestinoNoReal);

    public void ValidarConfiguracionRecuperacion() => _ = Base(ClaveRutaRecuperacion, DestinoNoRealRecuperacion);

    public MensajeEmail VerificacionEmail(string destinatario, TokenEmitido emitido)
    {
        ArgumentNullException.ThrowIfNull(emitido);
        var enlace = Base(ClaveRutaVerificacion, DestinoNoReal) + "#token=" + Uri.EscapeDataString(emitido.Token);
        var cuerpo =
            "Hola,\n\n" +
            "Para activar tu cuenta XPAY confirma tu correo en el siguiente enlace:\n" +
            enlace + "\n\n" +
            "El enlace es de un solo uso y vence el " + Vence(emitido) + " (UTC).\n" +
            "Si no solicitaste esta cuenta, ignora este mensaje.\n";
        return new MensajeEmail(destinatario, "Confirma tu correo en XPAY", cuerpo);
    }

    public MensajeEmail RecuperacionClave(string destinatario, TokenEmitido emitido)
    {
        ArgumentNullException.ThrowIfNull(emitido);
        var enlace = Base(ClaveRutaRecuperacion, DestinoNoRealRecuperacion) + "#token=" + Uri.EscapeDataString(emitido.Token);
        var cuerpo =
            "Hola,\n\n" +
            "Recibimos una solicitud para restablecer la contraseña de tu cuenta XPAY. Usa este enlace:\n" +
            enlace + "\n\n" +
            "El enlace es de un solo uso y vence el " + Vence(emitido) + " (UTC).\n" +
            "Si no lo solicitaste, ignora este mensaje: tu contraseña no cambiará.\n";
        return new MensajeEmail(destinatario, "Restablece tu contraseña de XPAY", cuerpo);
    }

    private static string Vence(TokenEmitido emitido) =>
        emitido.FechaExpiracion.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private string Base(string claveRuta, string destinoNoReal)
    {
        var url  = configuration[ClaveUrlFrontend];
        var ruta = configuration[claveRuta];

        var valida = Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps
                || (uri.Scheme == Uri.UriSchemeHttp && ambiente.PermiteMecanismosNoProductivos))
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment)
            && !string.IsNullOrWhiteSpace(ruta)
            && ruta.StartsWith('/')
            && ruta.IndexOfAny(new[] { '?', '#', ' ' }) < 0;

        if (valida) return url!.TrimEnd('/') + ruta;
        if (ambiente.PermiteMecanismosNoProductivos) return destinoNoReal;
        throw new IdentidadConfiguracionException(
            "Falta o es inválida la configuración de identidad para construir el enlace (" + claveRuta + ").");
    }
}
