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

    public static bool TryParseStrict(string authorization, out string token)
    {
        if (!authorization.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            token = string.Empty;
            return false;
        }

        token = authorization[Prefix.Length..];
        return IsValidToken(token);
    }

    public static bool IsValidToken(string token)
    {
        var hasTokenCharacter = false;
        var paddingStarted = false;
        foreach (var character in token)
        {
            if (character == '=')
            {
                paddingStarted = true;
                continue;
            }

            if (paddingStarted || !IsTokenCharacter(character))
            {
                return false;
            }

            hasTokenCharacter = true;
        }

        return hasTokenCharacter;
    }

    public static bool FixedTimeEquals(string value, string expected)
    {
        var valueHash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(valueHash, expectedHash);
    }

    public static bool HasJwtCompactSerializationShape(string token)
    {
        var segments = token.Split('.');
        return segments.Length is 3 or 5 && segments.All(segment => segment.Length > 0);
    }

    private static bool IsTokenCharacter(char character)
        => char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '_' or '~' or '+' or '/';
}
