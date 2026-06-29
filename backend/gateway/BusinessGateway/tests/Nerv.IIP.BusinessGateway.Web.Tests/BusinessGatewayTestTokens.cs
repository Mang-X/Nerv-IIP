using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

internal static class BusinessGatewayTestTokens
{
    private const string RsaKid = "business-gateway-test-rsa-key";
    private const string PrivateKeyPem = """
-----BEGIN PRIVATE KEY-----
MIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQC0RhTT3ru98EhB
W2yY7zsawlQL/09cQPaGmUj1UyespZb/lQ7fw6WjJw05xUoUNR6RuWmvphnXRKl2
uSjIz2cQ0uiLxZgvn/9VQKj3oNt3mijuRqcCLmbQXO+dj1rQDHf1MVOxTIFnYbxq
VYw6TDX4FgkW2bz7PqP+ROXP3fF7eCuJVwbJdOU1aLT2mC8ALwuPU43jZ+i9eIuP
KCe8IDlkl6+IwVl7jeR+3GMXT5+7QjoHLqP4PKIg8Z0cAhpJafdxKXQiXa4FGaRb
5oKz0ZQsdOzRndeleSWlAJzl1yf9SwU8ZjmAhb5Nuqp8F5tkJkFE52BKdWtI2cix
1ZGCmVH1AgMBAAECggEAF6w0Q/Y1tSV+d4an5hVUL5lhLAokw7qMJPSwDfcTeKpt
/7X1NBEfCSOxquprZefr0bsFU9l9/zS3BC4gWu5RXHY1r1UNPQPHpcxN4+atqzEF
OvTwLWsmeSobFRekFznr7rjBgsDHJWpCMbx2I5mqZJ+QJf4FwQBizJsDip5cfZf7
uO9QKdJ27V8wLPlmuci/Eg/FrsAzxrPub4JrMk+C6sXLD241FE9XzfILyS/qhzOf
R6dJYo4iVCl6Q79RKsmY6Cv8nsBhQFzMhKe11oC54E75ormNueJFQdjQ1VMkLwq1
0E7/akERIjyfth7U95uOwjDf/pOAUfWwu+Z+RG2q6QKBgQDOm/qvlKnorSNodXNE
j+bp2/0OlELbYf/3lRSfm3re5UIWXFlzu3LFJLgPEpgFRD7kKabTEjJ3G8gBWPVu
0I/LnIQ2DUsTi2TLY43KLHQ7gOX4KkOEwIEpRRlImJx/XDTgWT53PHvMMIF9VPOo
cxB5J0qIWxderHld508WOsOnQwKBgQDfXm1Deuq7zvIFhid7oG130qpzN2kJlRG9
V2Vfpnwlxx8qt6RjFmsUApJ1W4bjA3jwgpuYrpsvxVT3Z57gmLfLCrLIj5lqUryd
bvg1OHWq+mvbMxFApYyBwVoDJgYUyGKRUD31RhtWBjTk9xz0tqWgM/46iClPvEPH
u2Cjh7uCZwKBgQCEe7B79jAdayhRSz7mr/+55b6XIqrcUjL4ZzgaQHDBjPCbtgwG
EiS+FZWQ1LN2bRSG6c53eiuyBLZzZr+6lzIdtfdxUYTau3+ei+/XvDmsDjNotnEl
JuurswtLadCwOkgNtCxB+R7JCDGAVIEJev8NMQyx8vdBVgddF323G2dqUQKBgDaW
TP2AvHzJRjwzXNLJkfcGdMFTeUfuNjefdBa8CPryfpth5bqRb/mj50bm5z/zSUr9
oCjgAuzZvLn5iMo6iDAGnUqGTWe+cHnI9L+M3LS8Hj+ja0PxMTVEm0rJsBLEJdJ9
WabnSybqvWJ3QYxMVo2gJzEGtZHW4HmfQS61rQ1hAoGAUKQnhO0i7rOaWEFpdDTS
kGY2MAgEEpWYfkEZGf2ybuDun3x5eQjO8QdQO68AmeifwKHBkGDhdmo2OTSHht4N
FqLC4SCdXVlfmzcW7zeCPEQptoGzHl2lGg5MMEH/4Dp92Q5jPiliv8kyVppuR9UC
yKndmINUKXFRt+mFo0HU2Ec=
-----END PRIVATE KEY-----
""";
    private static readonly RSA Rsa = CreateRsa();

    public const string Issuer = "nerv-iip-iam-test";
    public const string Audience = "nerv-iip-business-gateway-test";

    public static string ValidAccessToken(
        string organizationId = "org-001",
        string environmentId = "env-dev",
        bool includeOrganizationId = true,
        bool includeEnvironmentId = true)
    {
        var now = DateTimeOffset.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "user-admin"),
            new("sessionId", "session-001"),
            new("principalType", "user"),
            new("loginName", "admin"),
            new("email", "admin@nerv.local"),
            new("securityStamp", "security-stamp-001"),
            new("permissionVersion", "7"),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };
        if (includeOrganizationId)
        {
            claims.Add(new Claim("organizationId", organizationId));
        }

        if (includeEnvironmentId)
        {
            claims.Add(new Claim("environmentId", environmentId));
        }

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: now.AddMinutes(-1).UtcDateTime,
            expires: now.AddMinutes(15).UtcDateTime,
            signingCredentials: new SigningCredentials(
                new RsaSecurityKey(Rsa) { KeyId = RsaKid },
                SecurityAlgorithms.RsaSha256));
        token.Header["kid"] = RsaKid;

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static string PublicJwksJson()
    {
        var parameters = Rsa.ExportParameters(false);
        return $$"""
        {"keys":[{"kty":"RSA","use":"sig","kid":"{{RsaKid}}","alg":"RS256","n":"{{Base64UrlEncoder.Encode(parameters.Modulus)}}","e":"{{Base64UrlEncoder.Encode(parameters.Exponent)}}"}]}
        """;
    }

    public static string PublicJwksJsonWithoutAlgorithm()
    {
        var parameters = Rsa.ExportParameters(false);
        return $$"""
        {"keys":[{"kty":"RSA","use":"sig","kid":"{{RsaKid}}","n":"{{Base64UrlEncoder.Encode(parameters.Modulus)}}","e":"{{Base64UrlEncoder.Encode(parameters.Exponent)}}"}]}
        """;
    }

    public static string Hs256AccessTokenWithRsaKid()
    {
        var now = DateTimeOffset.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "user-admin"),
            new("sessionId", "session-001"),
            new("principalType", "user"),
            new("loginName", "admin"),
            new("email", "admin@nerv.local"),
            new("organizationId", "org-001"),
            new("environmentId", "env-dev"),
            new("securityStamp", "security-stamp-001"),
            new("permissionVersion", "7"),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: now.AddMinutes(-1).UtcDateTime,
            expires: now.AddMinutes(15).UtcDateTime,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes("public-jwks-material-must-not-be-accepted-as-hmac-secret")) { KeyId = RsaKid },
                SecurityAlgorithms.HmacSha256));
        token.Header["kid"] = RsaKid;

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static RSA CreateRsa()
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(PrivateKeyPem);
        return rsa;
    }
}
