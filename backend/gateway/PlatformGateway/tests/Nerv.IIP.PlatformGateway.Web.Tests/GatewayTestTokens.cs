using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

internal static class GatewayTestTokens
{
    private const string SigningKey = "nerv-iip-iam-development-signing-key-local-only-0001";

    public static string ValidAccessToken()
    {
        var now = DateTimeOffset.UtcNow;
        var header = Base64UrlEncode("""{"alg":"HS256","typ":"JWT"}"""u8.ToArray());
        var payload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["iss"] = "nerv-iip-iam",
            ["aud"] = "nerv-iip-api",
            ["sub"] = "user-admin",
            ["sessionId"] = "session-001",
            ["securityStamp"] = "security-stamp-001",
            ["permissionVersion"] = 7,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nbf"] = now.AddMinutes(-1).ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(15).ToUnixTimeSeconds()
        }));
        var tokenWithoutSignature = $"{header}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(SigningKey));
        var signature = Base64UrlEncode(hmac.ComputeHash(Encoding.ASCII.GetBytes(tokenWithoutSignature)));
        return $"{tokenWithoutSignature}.{signature}";
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
