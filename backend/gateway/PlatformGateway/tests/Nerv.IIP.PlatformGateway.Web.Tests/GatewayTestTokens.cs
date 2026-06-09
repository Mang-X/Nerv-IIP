using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

internal static class GatewayTestTokens
{
    private const string SigningKey = "nerv-iip-iam-development-signing-key-local-only-0001";

    public static string ValidAccessToken(int permissionVersion = 7)
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
            new("permissionVersion", permissionVersion.ToString()),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        var token = new JwtSecurityToken(
            issuer: "nerv-iip-iam",
            audience: "nerv-iip-api",
            claims: claims,
            notBefore: now.AddMinutes(-1).UtcDateTime,
            expires: now.AddMinutes(15).UtcDateTime,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
