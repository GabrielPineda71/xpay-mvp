using System.Security.Cryptography;
using System.Text;

namespace Xpay.PassportSandboxHarness;

// XPAY-325 — fingerprint determinístico y criptográfico (SHA-256) para
// correlacionar una misma entidad (id/valor) entre distintas evidencias sin
// revelar jamás el valor original. Deliberadamente NO usa GetHashCode ni
// ningún hash no criptográfico (XPAY-325 FASE 9) — SHA-256 es resistente a
// colisión práctica y determinístico entre ejecuciones/procesos, a
// diferencia de GetHashCode (no garantizado estable entre versiones/runs de
// .NET). El valor original NUNCA se almacena junto al fingerprint.
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
