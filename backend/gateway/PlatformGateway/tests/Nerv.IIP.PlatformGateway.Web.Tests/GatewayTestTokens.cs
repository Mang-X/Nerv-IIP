using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

internal static class GatewayTestTokens
{
    private const string SigningKey = "nerv-iip-iam-development-signing-key-local-only-0001";
    private static readonly DateTimeOffset DefaultIssuedAtUtc = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DefaultExpiresAtUtc = new(2036, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static string ValidAccessToken(
        int permissionVersion = 7,
        DateTimeOffset? issuedAtUtc = null,
        DateTimeOffset? expiresAtUtc = null)
    {
        var issuedAt = issuedAtUtc ?? DefaultIssuedAtUtc;
        var expiresAt = expiresAtUtc ?? DefaultExpiresAtUtc;
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
            new("permissionVersion", permissionVersion.ToString()),
            new(JwtRegisteredClaimNames.Iat, issuedAt.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        var token = new JwtSecurityToken(
            issuer: "nerv-iip-iam",
            audience: "nerv-iip-api",
            claims: claims,
            notBefore: issuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
