using System.Globalization;
using Xpay.Api.DTOs;

namespace Xpay.Api.Services;

internal sealed record DocumentosRegistroPublico(
    DocumentoRegistroPublico Terminos,
    DocumentoRegistroPublico PoliticaDatos)
{
    internal const string ClaveHabilitado = "XPAY_REGISTRO_PUBLICO_HABILITADO";
    internal const string ClaveModalidad = "XPAY_REGISTRO_PUBLICO_MODALIDAD";
    internal const string ModalidadLimitada = "CUENTA_LIMITADA";
    internal const string PrefijoTerminos = "XPAY_REGISTRO_PUBLICO_TERMINOS";
    internal const string PrefijoPoliticaDatos = "XPAY_REGISTRO_PUBLICO_POLITICA_DATOS";

    public RegistroPublicoConfiguracionResponse DatosPublicos =>
        new(Terminos.DatosPublicos, PoliticaDatos.DatosPublicos);

    public static DocumentosRegistroPublico? Desde(IConfiguration configuracion)
    {
        var terminos = LeerDocumento(configuracion, PrefijoTerminos);
        var politica = LeerDocumento(configuracion, PrefijoPoliticaDatos);

        return terminos is not null && politica is not null ? new(terminos, politica) : null;
    }

    public void ValidarConsentimiento(RegistroPublicoConsentimiento? consentimiento)
    {
        if (consentimiento is not { AceptaTerminos: true, AutorizaTratamientoDatos: true })
            throw new InvalidOperationException("Acepta los términos y autoriza el tratamiento de datos para continuar.");

        if (!string.Equals(consentimiento.VersionTerminos, Terminos.DatosPublicos.Version, StringComparison.Ordinal)
            || !string.Equals(consentimiento.VersionPoliticaDatos, PoliticaDatos.DatosPublicos.Version, StringComparison.Ordinal))
            throw new InvalidOperationException("Los documentos del registro cambiaron. Recarga la página y revísalos de nuevo.");
    }

    private static DocumentoRegistroPublico? LeerDocumento(IConfiguration configuracion, string prefijo)
    {
        var version = configuracion[prefijo + "_VERSION"]?.Trim();
        var fecha = configuracion[prefijo + "_FECHA"];
        var url = configuracion[prefijo + "_URL"];
        var sha256 = configuracion[prefijo + "_SHA256"];

        if (string.IsNullOrEmpty(version) || version.Length > 50
            || !DateOnly.TryParseExact(fecha, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fechaDocumento)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host)
            || !uri.IsWellFormedOriginalString() || uri.UserInfo.Length != 0
            || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || sha256 is not { Length: 64 } || !sha256.All(Uri.IsHexDigit))
            return null;

        return new(new(version, fechaDocumento, uri.AbsoluteUri), sha256.ToLowerInvariant());
    }
}

internal sealed record DocumentoRegistroPublico(RegistroPublicoDocumentoResponse DatosPublicos, string Sha256);

internal sealed class RegistroPublicoNoDisponibleException : Exception { }
