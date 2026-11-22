using Xpay.Api.Common;

namespace Xpay.Api.Services;

// Términos y Datos del registro; KYC tiene su autorización independiente.
// No existe un anexo ni una aceptación adicional para el piloto.
public sealed class PreparacionWalletConfiguracion(IConfiguration config, IAmbienteXpay ambiente)
{
    public const string ClaveHabilitada = "XPAY_ALTA_WALLET_GUIADA_HABILITADA";
    public const string Version = "1.3";
    public const string Fecha = "2026-11-21";
    public const string TerminosSha256 = "1c8f52efc1e8ebe28bf5b69a0c1060246631a2f8bf43b29904b331fb53b00be7";
    public const string PoliticaDatosSha256 = "33374a875e07674b97d37d7228f44ee253e256a5ecc6a3ea205d8a507352c973";
    public const string AutorizacionSha256 = "2da5683379ac6064f108d9b90e2b81156332fdcff03e9be1367b80ddb3144afd";

    internal DocumentosRegistroPublico? Obtener()
    {
        if (config[ClaveHabilitada] != "true"
            || !Uri.TryCreate(config[PlantillasEmailIdentidad.ClaveUrlFrontend], UriKind.Absolute, out var frontend)
            || frontend.Scheme != "https" || !frontend.IsDefaultPort || frontend.UserInfo.Length != 0
            || frontend.AbsolutePath != "/" || frontend.Query.Length != 0 || frontend.Fragment.Length != 0
            || (ambiente.EsProductivo && frontend.Host != "app.xpaycol.com")) return null;
        var documentos = DocumentosRegistroPublico.Desde(config);
        return documentos is not null && Coincide(documentos.Terminos, frontend, "terminos.pdf", TerminosSha256)
            && Coincide(documentos.PoliticaDatos, frontend, "politica.pdf", PoliticaDatosSha256) ? documentos : null;
    }

    private static bool Coincide(DocumentoRegistroPublico d, Uri origen, string archivo, string sha) =>
        sha.Length == 64 && d.Sha256 == sha && d.DatosPublicos.Version == Version
        && d.DatosPublicos.Fecha.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) == Fecha
        && Uri.TryCreate(d.DatosPublicos.Url, UriKind.Absolute, out var url)
        && url.Scheme == "https" && url.Host == origen.Host && url.IsDefaultPort
        && url.AbsolutePath == "/legal/v1.3/" + archivo && url.UserInfo.Length == 0
        && url.Query.Length == 0 && url.Fragment.Length == 0;
}
