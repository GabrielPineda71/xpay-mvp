using System.Security.Cryptography;

namespace Xpay.PassportSandboxHarness;

// XPAY-458 — genera INTERNAMENTE (nunca desde ~/.passport-sandbox.env, ni
// copiado de ningún correo/ejemplo de Passport) una qr_code_reference NUEVA
// y única por ejecución para create-qr-static (M4-T1). Función PURA — no
// hace I/O, no llama a Passport, no persiste nada.
//
// Contrato ya confirmado y validado en PassportQrClient.Validate:
// "Maximum of 17 alphanumeric characters", y la letra 'P' mayúscula no está
// permitida. Este generador respeta ambas reglas por construcción (nunca
// depende de que el caller filtre después):
//   - Alfabeto ASCII alfanumérico en mayúsculas, EXCLUYENDO explícitamente
//     'P' (no genera minúsculas: la documentación no confirma que Passport
//     distinga case, y el contrato ya probado en tests usa mayúsculas).
//   - Longitud fija de 12 caracteres (bien por debajo del máximo de 17),
//     generada con RandomNumberGenerator (no determinista, no derivada de
//     ningún identificador real) — suficiente entropía para que dos
//     ejecuciones sucesivas nunca produzcan el mismo valor por accidente,
//     satisfaciendo la regla operativa de no reutilizar referencias de
//     intentos anteriores sin depender de persistir un historial.
public static class QrCodeReferenceGenerator
{
    private const int ReferenceLength = 12;

    // 0-9 y A-Z salvo 'P' — 35 símbolos.
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOQRSTUVWXYZ";

    public static string Generate()
    {
        Span<byte> randomBytes = stackalloc byte[ReferenceLength];
        RandomNumberGenerator.Fill(randomBytes);

        var chars = new char[ReferenceLength];
        for (var i = 0; i < ReferenceLength; i++)
            chars[i] = Alphabet[randomBytes[i] % Alphabet.Length];

        return new string(chars);
    }
}
