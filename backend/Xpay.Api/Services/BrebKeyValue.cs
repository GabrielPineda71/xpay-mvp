using System.Security.Cryptography;
using System.Text;

namespace Xpay.Api.Services;

internal static class BrebKeyValue
{
    public static string? NormalizeAlpha(string value)
    {
        var alias = value.StartsWith('@') ? value[1..] : value;
        if (alias.Length is < 5 or > 20
            || !alias.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'z' or >= 'A' and <= 'Z'))
            return null;

        return $"@{alias}";
    }

    public static string ComputeHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
