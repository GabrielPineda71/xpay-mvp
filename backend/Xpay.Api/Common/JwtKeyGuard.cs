using System.Security.Cryptography;
using System.Text;

namespace Xpay.Api.Common;

// P0-A: validación fail-fast de Jwt:Key al arrancar, en TODOS los ambientes.
// Sin llamadas externas. Nunca incluye la clave en mensajes ni logs.
public static class JwtKeyGuard
{
    public const int LongitudMinimaBytes = 32; // 256 bits, mínimo para HS256

    // SHA-256 (hex) de la clave de desarrollo que estuvo versionada en
    // appsettings.json y en la documentación: está comprometida y se rechaza
    // siempre. Solo la huella; el literal no se incluye en el código.
    internal const string HuellaClaveComprometida =
        "0C608822F2F89CB9C974B6A000CD72C2C1A2B0F1428C0EFB5149B288F2A0F535";

    public static void Validar(string? clave) => Validar(clave, HuellaClaveComprometida);

    // Misma lógica con una huella arbitraria: permite a los tests demostrar el
    // rechazo con una clave ficticia, sin reintroducir el literal comprometido.
    internal static void Validar(string? clave, string huellaComprometidaHex)
    {
        if (string.IsNullOrWhiteSpace(clave))
            throw new InvalidOperationException(
                "Jwt:Key no está configurada. Defina Jwt__Key (App Settings, user-secrets o variable de entorno).");

        if (Encoding.UTF8.GetByteCount(clave) < LongitudMinimaBytes)
            throw new InvalidOperationException(
                $"Jwt:Key debe tener al menos {LongitudMinimaBytes} bytes UTF-8.");

        if (EsClaveComprometida(clave, huellaComprometidaHex))
            throw new InvalidOperationException(
                "Jwt:Key coincide con una clave comprometida. Configure una clave nueva.");
    }

    internal static bool EsClaveComprometida(string clave, string huellaHex) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(clave)),
            Convert.FromHexString(huellaHex));
}
