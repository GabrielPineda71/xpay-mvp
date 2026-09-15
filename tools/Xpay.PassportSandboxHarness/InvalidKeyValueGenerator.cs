using System.Security.Cryptography;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-344 — genera INTERNAMENTE (nunca desde ~/.passport-sandbox.env, ni
// desde ningún archivo persistido) un key_value sintéticamente inválido
// para M3-T6-INVALID. Función PURA — no hace I/O, no llama a Passport.
//
// XPAY-344 §7 exige inspeccionar primero qué tipos tienen un contrato de
// formato INEQUÍVOCO antes de generar cualquier valor "inválido" — nunca
// inventar restricciones. Búsqueda en el repositorio (XPAY-343/344):
//   - BCODE: contrato CONFIRMADO directamente contra documentación oficial
//     de Passport (sesión previa, usado para la ejecución real de M3-T1):
//     ^00[0-9]{8}$ — exactamente 10 caracteres, sólo dígitos, prefijo "00".
//   - ID/PHONE/EMAIL/ALPHA: `docs/PASSPORT_BREB_PLAN.md` sólo documenta una
//     "Validación básica" interna, EXPLÍCITAMENTE no verificada contra
//     Passport ("8-50 chars alfanum/guión" para BCODE ahí, que además
//     CONTRADICE el contrato real confirmado — evidencia de que esa tabla
//     es una estimación, no un contrato). No existe ningún otro contrato
//     de formato confirmado en código/tests/docs para estos 4 tipos.
//
// Por tanto: SOLO BCODE está soportado aquí. Cualquier otro PassportKeyType
// devuelve TryGenerate()=false — el caller (CreateKeyInvalidExecutor) debe
// bloquear localmente en ese caso, nunca inventar un valor "inválido" sin
// contrato.
public static class InvalidKeyValueGenerator
{
    // Único tipo actualmente soportado — ver justificación arriba.
    public static readonly IReadOnlyCollection<PassportKeyType> SupportedKeyTypes = new[] { PassportKeyType.BCODE };

    public static bool TryGenerate(PassportKeyType keyType, out string? invalidValue, out string invalidDimension)
    {
        if (keyType == PassportKeyType.BCODE)
        {
            invalidDimension = "key_value_format";
            invalidValue = GenerateInvalidBcode();
            return true;
        }

        invalidValue = null;
        invalidDimension = "unsupported_key_type_for_invalid_generation";
        return false;
    }

    // Viola ÚNICAMENTE la restricción "sólo dígitos" de ^00[0-9]{8}$ —
    // conserva longitud (10) y prefijo ("00") de un BCODE real, para que
    // la única razón de invalidez sea inequívocamente el formato del
    // contenido, no la longitud ni el prefijo. Generado con
    // RandomNumberGenerator (no determinista, no sensible, no
    // correspondiente a ninguna llave real — nunca colisiona con un BCODE
    // válido, porque un BCODE válido nunca contiene una letra).
    private static string GenerateInvalidBcode()
    {
        Span<byte> randomBytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(randomBytes);

        var digits = new char[7];
        for (var i = 0; i < 7; i++)
            digits[i] = (char)('0' + (randomBytes[i] % 10));
        var letter = (char)('A' + (randomBytes[7] % 26));

        return "00" + new string(digits) + letter; // 10 chars: "00" + 7 dígitos + 1 letra.
    }
}
