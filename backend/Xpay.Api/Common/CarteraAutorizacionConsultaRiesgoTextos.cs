using System.Security.Cryptography;
using System.Text;

namespace Xpay.Api.Common;

// Cartera Ordinaria — recurso VERSIONADO EN CÓDIGO del texto de autorización de
// consulta en centrales de riesgo. Única fuente de verdad runtime del texto
// (ni appsettings, ni seed DB).
//
// Fuente jurídica: ACTA 001 v1.0 (08/09/2026, Pereira, firmada) §3.1 "Texto
// base aprobado". El contenido de V1_Texto es el texto EXACTO aprobado —
// transcrito literalmente, sin parafrasear, sin añadir/quitar contenido.
//
// Inmutabilidad: el texto y su hash viven en el binario → auditables por commit.
// Un test (CarteraAutorizacionConsultaRiesgoTextoTests) recomputa
// SHA-256(V1_Texto) y exige igualdad exacta con V1_HashSha256.
//
// V1 es la ÚNICA versión. NO se implementa aquí un catálogo ni banderas de
// compatibilidad para una futura v2 (XPAY-197): esa decisión la tomará
// XPAY/Legal explícitamente cuando exista v2.
public static class CarteraAutorizacionConsultaRiesgoTextos
{
    // Identificador técnico estable de la primera versión (columna version_texto).
    public const string V1_Version = "AUTZ_CONSULTA_RIESGO_2026_V1";

    // Texto EXACTO aprobado en ACTA 001 §3.1. Se construye por concatenación
    // explícita con separador "\n\n" entre párrafos para que la cadena sea
    // determinística e independiente del fin de línea del archivo fuente.
    public const string V1_Texto =
        "En XPAY S.A.S. nos regimos por la Ley 1266 de 2008 y demás normas aplicables sobre Habeas Data financiero. "
      + "Para continuar con tu solicitud de crédito, necesitamos tu autorización para consultar tu información en centrales de riesgo "
      + "(DataCrédito, TransUnion, entre otras), con el fin de tenerla en cuenta en el análisis del cupo actual y en evaluaciones de "
      + "cartera futuras y, en caso de aprobación, reportar el cumplimiento positivo o negativo de tu comportamiento de pago."
      + "\n\n"
      + "Al seleccionar o responder AUTORIZO, confirmas que comprendes y aceptas esta consulta, análisis y reporte conforme a la normatividad aplicable."
      + "\n\n"
      + "Tu información será tratada con confidencialidad y para fines de análisis y gestión de crédito.";

    // SHA-256 hexadecimal (minúsculas) de los bytes UTF-8 exactos de V1_Texto.
    public const string V1_HashSha256 = "994ecc8a4f5d04b9a58dfafc76dbc511de6bcc5a5586a4b6dbbd6cf0cec7c4a4";

    // SHA-256 hex (minúsculas) del UTF-8 de un texto arbitrario. Se usa tanto
    // para verificar V1_Texto como para el chequeo de integridad del snapshot
    // persistido en la validación durable.
    public static string HashSha256Hex(string texto)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(texto));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // ¿Es `version` la versión vigente? V1 es la única. Cuando exista v2, esta
    // resolución incluirá la regla de compatibilidad que XPAY/Legal defina.
    public static bool EsVersionVigente(string? version)
        => string.Equals(version, V1_Version, StringComparison.Ordinal);

    // Devuelve el texto + hash de una versión conocida. Sólo v1 existe.
    public static bool TryObtenerVersion(string? version, out string texto, out string hash)
    {
        if (string.Equals(version, V1_Version, StringComparison.Ordinal))
        {
            texto = V1_Texto;
            hash = V1_HashSha256;
            return true;
        }
        texto = string.Empty;
        hash = string.Empty;
        return false;
    }
}
