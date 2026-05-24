using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

internal static class BusinessGatewayTestTokens
{
    private const string SigningKey = "business-gateway-test-signing-key-32";

    public static string ValidAccessToken(
        string organizationId = "org-001",
        string environmentId = "env-dev")
    {
        var now = DateTimeOffset.UtcNow;
        var header = Base64UrlEncode("""{"alg":"HS256","typ":"JWT"}"""u8.ToArray());
        var payload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["iss"] = "nerv-iip-iam",
            ["aud"] = "nerv-iip-api",
            ["sub"] = "user-admin",
            ["sessionId"] = "session-001",
            ["principalType"] = "user",
            ["loginName"] = "admin",
            ["email"] = "admin@nerv.local",
            ["organizationId"] = organizationId,
            ["environmentId"] = environmentId,
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
