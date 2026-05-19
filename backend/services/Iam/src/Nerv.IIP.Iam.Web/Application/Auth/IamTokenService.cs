using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserSessionAggregate;

namespace Nerv.IIP.Iam.Web.Application.Auth;

public sealed record AccessTokenPrincipal(string SessionId, string UserId, string SecurityStamp, int PermissionVersion);

public sealed class IamTokenService(IConfiguration configuration, IWebHostEnvironment environment)
{
    private const string DevelopmentSigningKey = "nerv-iip-iam-development-signing-key-local-only-0001";
    private const string DefaultIssuer = "nerv-iip-iam";
    private const string DefaultAudience = "nerv-iip-api";

    public string CreateAccessToken(User user, UserSession session)
    {
        var now = DateTimeOffset.UtcNow;
        var claims = new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.Id),
            new Claim("sessionId", session.Id.Id),
            new Claim("principalType", "user"),
            new Claim("securityStamp", user.SecurityStamp),
            new Claim("permissionVersion", user.PermissionVersion.ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("n"))
        ]);

        var token = new JwtSecurityToken(
            issuer: GetIssuer(),
            audience: GetAudience(),
            claims: claims.Claims,
            notBefore: now.UtcDateTime,
            expires: now.AddMinutes(GetAccessTokenMinutes()).UtcDateTime,
            signingCredentials: new SigningCredentials(GetSigningKey(), SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public DateTimeOffset GetAccessTokenExpiresAtUtc(DateTimeOffset issuedAtUtc)
    {
        return issuedAtUtc.AddMinutes(GetAccessTokenMinutes());
    }

    public AccessTokenPrincipal? TryReadPrincipal(HttpContext httpContext)
    {
        var value = httpContext.Request.Headers.Authorization.ToString();
        if (!value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = value["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        try
        {
            var principal = handler.ValidateToken(token, CreateValidationParameters(), out var securityToken);
            if (securityToken is not JwtSecurityToken jwt
                || !string.Equals(jwt.Header.Alg, SecurityAlgorithms.HmacSha256, StringComparison.Ordinal))
            {
                return null;
            }

            var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var sessionId = principal.FindFirstValue("sessionId");
            var principalType = principal.FindFirstValue("principalType");
            var securityStamp = principal.FindFirstValue("securityStamp");
            var permissionVersionValue = principal.FindFirstValue("permissionVersion");
            if (string.IsNullOrWhiteSpace(userId)
                || string.IsNullOrWhiteSpace(sessionId)
                || !string.Equals(principalType, "user", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(securityStamp)
                || !int.TryParse(permissionVersionValue, out var permissionVersion))
            {
                return null;
            }

            return new AccessTokenPrincipal(sessionId, userId, securityStamp, permissionVersion);
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public string CreateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public string HashSecret(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private TokenValidationParameters CreateValidationParameters()
    {
        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = GetIssuer(),
            ValidateAudience = true,
            ValidAudience = GetAudience(),
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = GetSigningKey(),
            ValidateLifetime = true
        };
    }

    private SymmetricSecurityKey GetSigningKey()
    {
        var signingKey = configuration["Iam:Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException("Iam:Jwt:SigningKey is required outside Development.");
            }

            signingKey = DevelopmentSigningKey;
        }

        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
    }

    private string GetIssuer()
    {
        return configuration["Iam:Jwt:Issuer"] ?? DefaultIssuer;
    }

    private string GetAudience()
    {
        return configuration["Iam:Jwt:Audience"] ?? DefaultAudience;
    }

    private int GetAccessTokenMinutes()
    {
        return int.TryParse(configuration["Iam:Jwt:AccessTokenMinutes"], out var minutes) && minutes > 0
            ? minutes
            : 15;
    }
}
