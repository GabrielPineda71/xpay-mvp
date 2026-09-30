using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Xpay.Api.Integrations.Passport;

// XPAY-594 — canonicalización JSON exigida por el esquema de firma
// "X-Passport-Signature-V2" de Passport.
//
// CONTRATO (ver docs/certificacion/passport-breb/M5-T6-T7-WEBHOOK-CONTRATO-FINAL.md):
// la firma NO se calcula sobre los bytes crudos recibidos, sino sobre una
// RE-SERIALIZACIÓN canónica del payload, que la documentación oficial define
// exactamente como:
//
//     json.dumps(payload, sort_keys=True, separators=(',', ':')).encode()
//
// Esta clase reproduce ese comportamiento de Python con fidelidad literal.
// Los cuatro puntos donde una implementación .NET "aproximada" divergiría —y
// por tanto invalidaría toda firma— son:
//
//   1. ORDEN DE CLAVES: Python ordena por PUNTO DE CÓDIGO Unicode. El orden
//      ordinal de .NET compara unidades UTF-16, que difiere para caracteres
//      suplementarios (un par suplente D800-DBFF ordena antes que U+E000-U+FFFF
//      en UTF-16, pero después por punto de código). Se implementa comparación
//      por punto de código.
//   2. SEPARADORES: ',' y ':' sin ningún espacio.
//   3. UNICODE: json.dumps usa ensure_ascii=True por defecto, así que TODO
//      carácter fuera de 0x20..0x7E se escapa como \uXXXX en hexadecimal
//      MINÚSCULA (los suplementarios, como par suplente). Relevante de forma
//      inmediata: tildes y "ñ" de nombres colombianos.
//   4. NÚMEROS: un entero JSON se emite tal cual; un flotante se emite con el
//      repr() de Python (dígitos de ida y vuelta más cortos, con umbral de
//      notación exponencial en exp < -4 o exp >= 16), que NO coincide con el
//      formato "R" de .NET (p. ej. 1e15 → .NET "1E+15" vs Python
//      "1000000000000000.0").
//
// Ningún payload documentado por Passport contiene números JSON desnudos —
// todos los valores de sus ejemplos son cadenas u objetos anidados—, por lo que
// la ruta numérica es DEFENSIVA. Se implementa igualmente para no dejar un
// agujero silencioso si el proveedor añade un número en el futuro.
public static class PassportWebhookCanonicalJson
{
    /// <summary>
    /// Re-serializa el JSON recibido en la forma canónica que Passport firma.
    /// Lanza <see cref="JsonException"/> si el cuerpo no es JSON válido.
    /// </summary>
    public static string Canonicalize(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var sb = new StringBuilder(json.Length + 32);
        WriteValue(doc.RootElement, sb);
        return sb.ToString();
    }

    private static void WriteValue(JsonElement el, StringBuilder sb)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                WriteObject(el, sb);
                break;

            case JsonValueKind.Array:
                sb.Append('[');
                var firstItem = true;
                foreach (var item in el.EnumerateArray())
                {
                    if (!firstItem) sb.Append(',');
                    firstItem = false;
                    WriteValue(item, sb);
                }
                sb.Append(']');
                break;

            case JsonValueKind.String:
                WriteString(el.GetString() ?? string.Empty, sb);
                break;

            case JsonValueKind.Number:
                sb.Append(FormatNumber(el.GetRawText()));
                break;

            case JsonValueKind.True:
                sb.Append("true");
                break;

            case JsonValueKind.False:
                sb.Append("false");
                break;

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
            default:
                sb.Append("null");
                break;
        }
    }

    private static void WriteObject(JsonElement el, StringBuilder sb)
    {
        // Python: sorted(dict) — orden por punto de código Unicode.
        var props = new List<JsonProperty>();
        foreach (var p in el.EnumerateObject()) props.Add(p);
        props.Sort(static (a, b) => CompareByCodePoint(a.Name, b.Name));

        sb.Append('{');
        for (var i = 0; i < props.Count; i++)
        {
            if (i > 0) sb.Append(',');
            WriteString(props[i].Name, sb);
            sb.Append(':');
            WriteValue(props[i].Value, sb);
        }
        sb.Append('}');
    }

    /// <summary>
    /// Compara dos cadenas por punto de código Unicode, como <c>sorted()</c> de
    /// Python — no por unidad UTF-16, como haría <c>StringComparer.Ordinal</c>.
    /// </summary>
    internal static int CompareByCodePoint(string a, string b)
    {
        int ia = 0, ib = 0;
        while (ia < a.Length && ib < b.Length)
        {
            var ca = NextCodePoint(a, ref ia);
            var cb = NextCodePoint(b, ref ib);
            if (ca != cb) return ca < cb ? -1 : 1;
        }
        return (a.Length - ia).CompareTo(b.Length - ib);
    }

    private static int NextCodePoint(string s, ref int i)
    {
        var c = s[i];
        if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
        {
            var cp = char.ConvertToUtf32(c, s[i + 1]);
            i += 2;
            return cp;
        }
        i += 1;
        return c;
    }

    /// <summary>
    /// Escapa una cadena como <c>py_encode_basestring_ascii</c>: se escapan la
    /// comilla doble, la barra invertida y todo lo que quede fuera de
    /// 0x20..0x7E. La barra inclinada NO se escapa.
    /// </summary>
    private static void WriteString(string s, StringBuilder sb)
    {
        sb.Append('"');
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            switch (c)
            {
                case '"':  sb.Append("\\\""); continue;
                case '\\': sb.Append("\\\\"); continue;
                case '\b': sb.Append("\\b");  continue;
                case '\f': sb.Append("\\f");  continue;
                case '\n': sb.Append("\\n");  continue;
                case '\r': sb.Append("\\r");  continue;
                case '\t': sb.Append("\\t");  continue;
            }

            if (c >= 0x20 && c <= 0x7E)
            {
                sb.Append(c);
                continue;
            }

            // ensure_ascii=True: \uXXXX en minúscula. Los caracteres
            // suplementarios se emiten como par suplente, que es exactamente lo
            // que hace Python al recorrer la cadena por unidades UTF-16.
            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
        }
        sb.Append('"');
    }

    /// <summary>
    /// Emite un número como lo haría Python tras parsear el JSON: un token sin
    /// '.', 'e' ni 'E' es un <c>int</c> y se conserva literal; cualquier otro es
    /// un <c>float</c> y se re-emite con el <c>repr()</c> de Python.
    /// </summary>
    internal static string FormatNumber(string rawToken)
    {
        var raw = rawToken.Trim();
        var isFloat = raw.IndexOf('.') >= 0 || raw.IndexOf('e') >= 0 || raw.IndexOf('E') >= 0;

        if (!isFloat)
        {
            // Python int: dígitos canónicos. JSON ya prohíbe ceros a la
            // izquierda y el signo '+', así que el token es su propia forma
            // canónica.
            return raw;
        }

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            throw new JsonException("Token numérico no interpretable en la canonicalización.");

        return FormatPythonFloat(d);
    }

    /// <summary>
    /// Reproduce <c>repr(float)</c> de Python: dígitos de ida y vuelta más
    /// cortos, con notación exponencial cuando el exponente decimal es
    /// &lt; -4 o &gt;= 16.
    /// </summary>
    internal static string FormatPythonFloat(double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d))
            throw new JsonException("Número no finito no admitido en la canonicalización.");

        var negative = double.IsNegative(d);
        var abs = Math.Abs(d);

        if (abs == 0.0)
            return negative ? "-0.0" : "0.0";

        // "R" en .NET Core 3.0+ produce los dígitos de ida y vuelta más cortos,
        // igual que el repr de Python; sólo difiere la PRESENTACIÓN.
        var shortest = abs.ToString("R", CultureInfo.InvariantCulture);

        // Normaliza a (dígitos, exponente) con valor = d[0].d[1..] × 10^exp.
        string digits;
        int exp;
        var eIdx = shortest.IndexOfAny(new[] { 'e', 'E' });
        if (eIdx >= 0)
        {
            var mantissa = shortest[..eIdx];
            exp = int.Parse(shortest[(eIdx + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture);
            var dot = mantissa.IndexOf('.');
            if (dot >= 0)
            {
                digits = mantissa.Remove(dot, 1);
                exp += dot - 1;
            }
            else
            {
                digits = mantissa;
                exp += mantissa.Length - 1;
            }
        }
        else
        {
            var dot = shortest.IndexOf('.');
            if (dot >= 0)
            {
                digits = shortest.Remove(dot, 1);
                exp = dot - 1;
            }
            else
            {
                digits = shortest;
                exp = shortest.Length - 1;
            }
        }

        // Quita ceros a la izquierda ajustando el exponente (p. ej. "0.0001").
        var lead = 0;
        while (lead < digits.Length - 1 && digits[lead] == '0') { lead++; exp--; }
        digits = digits[lead..];
        digits = digits.TrimEnd('0');
        if (digits.Length == 0) digits = "0";

        var sign = negative ? "-" : string.Empty;

        if (exp < -4 || exp >= 16)
        {
            var mant = digits.Length == 1 ? digits : $"{digits[0]}.{digits[1..]}";
            var expSign = exp < 0 ? "-" : "+";
            var expAbs = Math.Abs(exp).ToString(CultureInfo.InvariantCulture);
            if (expAbs.Length < 2) expAbs = "0" + expAbs;
            return $"{sign}{mant}e{expSign}{expAbs}";
        }

        if (exp >= 0)
        {
            if (digits.Length > exp + 1)
                return $"{sign}{digits[..(exp + 1)]}.{digits[(exp + 1)..]}";
            return $"{sign}{digits.PadRight(exp + 1, '0')}.0";
        }

        return $"{sign}0.{new string('0', -exp - 1)}{digits}";
    }
}
