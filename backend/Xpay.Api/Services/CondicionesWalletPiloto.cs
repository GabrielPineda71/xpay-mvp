using Xpay.Api.Common;
using Xpay.Api.DTOs;

namespace Xpay.Api.Services;

// Documento separado de las aceptaciones A/B/C existentes. El interruptor
// permanece cerrado hasta que XPAY apruebe y configure el anexo exacto.
public sealed class CondicionesWalletPiloto(IConfiguration config, IAmbienteXpay ambiente)
{
    public const string ClaveHabilitada = "XPAY_ALTA_WALLET_GUIADA_HABILITADA";
    public const string Prefijo = "XPAY_CONDICIONES_WALLET";
    public const string Version = "1.0";
    public const string Fecha = "2026-11-21";
    public const string Ruta = "/legal/wallet/v1.0/condiciones.pdf";
    // Se fija al PDF revisado antes de preparar el artefacto de publicación.
    public const string Sha256 = "c3cbbe4c1be895b552143707c5f9990ec362e62f1bc337bbb9b2d847fc2d0152";

    public CondicionesWalletResponse? Obtener()
    {
        if (config[ClaveHabilitada] != "true" || config[Prefijo + "_VERSION"] != Version
            || config[Prefijo + "_FECHA"] != Fecha || config[Prefijo + "_SHA256"] != Sha256
            || Sha256.Length != 64
            || !Uri.TryCreate(config[PlantillasEmailIdentidad.ClaveUrlFrontend], UriKind.Absolute, out var frontend)
            || frontend.Scheme != "https" || !frontend.IsDefaultPort || frontend.UserInfo.Length != 0
            || frontend.AbsolutePath != "/" || frontend.Query.Length != 0 || frontend.Fragment.Length != 0
            || (ambiente.EsProductivo && frontend.Host != "app.xpaycol.com")
            || !Uri.TryCreate(config[Prefijo + "_URL"], UriKind.Absolute, out var documento)
            || !documento.IsWellFormedOriginalString() || documento.Scheme != "https"
            || !documento.IsDefaultPort || documento.Host != frontend.Host || documento.UserInfo.Length != 0
            || documento.AbsolutePath != Ruta || documento.Query.Length != 0 || documento.Fragment.Length != 0)
            return null;
        return new(Version, Fecha, documento.AbsoluteUri, Sha256);
    }
}
