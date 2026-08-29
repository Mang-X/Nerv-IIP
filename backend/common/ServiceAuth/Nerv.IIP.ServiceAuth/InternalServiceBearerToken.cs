using System.Security.Cryptography;
using System.Text;

namespace Nerv.IIP.ServiceAuth;

internal static class InternalServiceBearerToken
{
    private const string Prefix = "Bearer ";

    public static bool TryParse(string authorization, out string token)
    {
        if (!authorization.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            token = string.Empty;
            return false;
        }

        token = authorization[Prefix.Length..].Trim();
        return true;
    }

    public static bool FixedTimeEquals(string value, string expected)
    {
        var valueHash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(valueHash, expectedHash);
    }
}
