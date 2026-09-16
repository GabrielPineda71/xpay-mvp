using System.Security.Cryptography;
using System.Text;

namespace Xpay.Api.Common;

// XPAY-372 — fingerprint determinístico y criptográfico (SHA-256) para
// correlacionar una misma entidad (id/valor) en logs/DTOs administrativos
// sin revelar jamás el valor original. Mismo algoritmo y semántica exactos
// que tools/Xpay.PassportSandboxHarness/Fingerprint.cs (XPAY-325) —
// duplicado aquí deliberadamente en vez de una referencia cruzada de
// proyecto: Xpay.Api es la librería productiva y NO depende del harness
// (la dependencia va en sentido contrario: el harness referencia
// Xpay.Api). Si en el futuro se decide un único lugar canónico, debe
// hacerse en un ticket propio que actualice ambos sitios a la vez (mismo
// criterio ya aplicado a BrebKeyResolutionRequestBuilder.ComputeKeyHash
// frente a BrebService.ComputeKeyHash).
public static class Fingerprint
{
    public const int DefaultLength = 12;

    // "ABSENT" es un marcador explícito, no un fingerprint de cadena vacía —
    // evita que null/"" produzcan un hash SHA-256 real que pudiera
    // confundirse con el fingerprint de un valor genuino.
    public const string AbsentMarker = "ABSENT";

    public static string Compute(string? value, int length = DefaultLength)
    {
        if (string.IsNullOrEmpty(value))
            return AbsentMarker;

        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        var hex = Convert.ToHexString(hash); // mayúsculas, hexadecimal

        return length >= hex.Length ? hex : hex[..length];
    }
}
