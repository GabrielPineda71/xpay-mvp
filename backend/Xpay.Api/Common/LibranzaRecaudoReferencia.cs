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

    // Passport devuelve qr_code_reference como PrefijoPassportQr + nuestra
    // referencia (evidencia M4-T4: "VMD47K47G5M1" → "CO.COM.VISI.TRXIDPVMD47K47G5M1";
    // por eso la referencia XPAY no puede contener 'P').
    public const string PrefijoPassportQr = "CO.COM.VISI.TRXIDP";

    // Normalización ESTRICTA: devuelve la referencia XPAY solo si el valor es
    // una referencia válida o exactamente PrefijoPassportQr + referencia
    // válida. Cualquier otro valor (p. ej. "LLAVE") devuelve null.
    public static string? ExtraerDePassport(string? valor)
    {
        if (EsValida(valor)) return valor;
        if (valor is not null && valor.StartsWith(PrefijoPassportQr, StringComparison.Ordinal))
        {
            var resto = valor[PrefijoPassportQr.Length..];
            if (EsValida(resto)) return resto;
        }
        return null;
    }
}
