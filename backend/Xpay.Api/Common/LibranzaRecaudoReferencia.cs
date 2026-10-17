using System.Security.Cryptography;

namespace Xpay.Api.Common;

// P0 anticipo - referencia publica opaca de un lote de recaudo. Es el
// qr_code_reference del QR dinamico Passport, cuyo contrato (validado en
// PassportQrClient) exige: alfanumerico, <= 17 caracteres y sin la letra 'P'.
// Aleatoria (CSPRNG), sin PII ni datos derivables del lote/empresa/empleado.
// Unicidad garantizada por uq_llr_referencia; inmutable (ningun camino de
// codigo la reescribe tras crear el lote).
public static class LibranzaRecaudoReferencia
{
    public const int Longitud = 16;
    private const string Alfabeto = "0123456789ABCDEFGHIJKLMNOQRSTUVWXYZ"; // sin 'P'

    public static string Generar()
    {
        // Rechazo por sesgo de modulo: solo bytes < 245 (= 7 * 35).
        var chars = new char[Longitud];
        Span<byte> b = stackalloc byte[1];
        var i = 0;
        while (i < Longitud)
        {
            RandomNumberGenerator.Fill(b);
            if (b[0] >= 245) continue;
            chars[i++] = Alfabeto[b[0] % Alfabeto.Length];
        }
        return new string(chars);
    }

    public static bool EsValida(string? referencia) =>
        !string.IsNullOrEmpty(referencia)
        && referencia.Length is >= 8 and <= 17
        && referencia.All(c => c is (>= '0' and <= '9') or (>= 'A' and <= 'Z'))
        && !referencia.Contains('P');
}
