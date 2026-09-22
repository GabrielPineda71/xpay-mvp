using System.Security.Cryptography;

namespace Xpay.PassportSandboxHarness;

// XPAY-474 — key_value BCODE VÁLIDO, sintético, para la llave desechable de
// M4-T3-A (create-m4-t3-suspended-fixture-key). Función PURA — no hace I/O,
// no llama a Passport, no lee ningún archivo/variable de entorno.
//
// A diferencia de InvalidKeyValueGenerator (XPAY-344), que genera
// DELIBERADAMENTE un BCODE que viola el formato (para una prueba
// negativa), este generador produce un BCODE que SÍ CUMPLE el contrato ya
// confirmado en este repositorio (XPAY-343/344, ver InvalidKeyValueGenerator):
//   ^00[0-9]{8}$ — exactamente 10 caracteres, prefijo "00", 8 dígitos.
//
// ALEATORIO (RandomNumberGenerator, criptográfico — NUNCA System.Random,
// NUNCA GetHashCode, NUNCA timestamp como fuente de unicidad) — mismo
// criterio ya establecido en QrCodeReferenceGenerator/InvalidKeyValueGenerator.
// Generado FRESCO en cada llamada, nunca hardcodeado, nunca derivado de
// ningún otro fixture (M3, M4-T1/T2, o cualquier otro).
//
// GARANTÍA CORRECTA sobre colisiones (deliberadamente NO se afirma
// "colisión imposible"): este generador produce un valor fresco,
// criptográficamente aleatorio, de formato válido — la separación primaria
// frente a cualquier otra llave (histórica o activa) es ESTRUCTURAL (este
// generador nunca lee ni deriva de ningún key_value existente, ver
// CreateM4T3SuspendedFixtureKeyExecutor), no una comparación activa contra
// valores históricos (que exigiría leer/imprimir key_values reales —
// exactamente lo que este harness evita en todo momento).
public static class DisposableBcodeGenerator
{
    private const string Prefix = "00";
    private const int RandomDigitCount = 8;

    public static string Generate()
    {
        Span<byte> randomBytes = stackalloc byte[RandomDigitCount];
        RandomNumberGenerator.Fill(randomBytes);

        var digits = new char[RandomDigitCount];
        for (var i = 0; i < RandomDigitCount; i++)
            digits[i] = (char)('0' + (randomBytes[i] % 10));

        return Prefix + new string(digits); // 10 caracteres: "00" + 8 dígitos.
    }
}
