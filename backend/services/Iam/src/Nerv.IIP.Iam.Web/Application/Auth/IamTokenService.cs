using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserSessionAggregate;

namespace Nerv.IIP.Iam.Web.Application.Auth;

public sealed record AccessTokenPrincipal(
    string SessionId,
    string UserId,
    string SecurityStamp,
    int PermissionVersion,
    string? OrganizationId,
    string? EnvironmentId);
public sealed record ExternalClientAccessTokenPrincipal(
    string ClientId,
    string OrganizationId,
    string EnvironmentId,
    int PermissionVersion,
    IReadOnlyList<string> Scope);

public sealed class IamTokenService(IConfiguration configuration, IWebHostEnvironment environment)
{
    private const string DevelopmentKeyId = "dev-rsa-2026-01";
    private const string DevelopmentPrivateKeyPem = """
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
    private const string DefaultIssuer = "nerv-iip-iam";
    private const string DefaultAudience = "nerv-iip-api";
    private readonly Lazy<JwtKeyMaterial> keyMaterial = new(
        () => BuildKeyMaterial(configuration, environment),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public string CreateAccessToken(User user, UserSession session, DateTimeOffset? issuedAtUtc = null)
    {
        return CreateAccessToken(
            user.Id.Id,
            session.Id.Id,
            user.SecurityStamp,
            user.PermissionVersion,
            user.LoginName,
            user.Email,
            issuedAtUtc: issuedAtUtc);
    }

    public string CreateAccessToken(
        User user,
        UserSession session,
        string organizationId,
        string environmentId,
        DateTimeOffset? issuedAtUtc = null)
    {
        return CreateAccessToken(
            user.Id.Id,
            session.Id.Id,
            user.SecurityStamp,
            user.PermissionVersion,
            user.LoginName,
            user.Email,
            organizationId,
            environmentId,
            issuedAtUtc);
    }

    public string CreateAccessToken(
        string userId,
        string sessionId,
        string securityStamp,
        int permissionVersion,
        string? loginName = null,
        string? email = null,
        string? organizationId = null,
        string? environmentId = null,
        DateTimeOffset? issuedAtUtc = null)
    {
        var now = issuedAtUtc ?? DateTimeOffset.UtcNow;
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim("sessionId", sessionId),
            new Claim("principalType", "user"),
            new Claim("securityStamp", securityStamp),
            new Claim("permissionVersion", permissionVersion.ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("n"))
        };
        AddIfPresent(claims, "loginName", loginName);
        AddIfPresent(claims, "email", email);
        AddIfPresent(claims, "organizationId", organizationId);
        AddIfPresent(claims, "environmentId", environmentId);

        var token = new JwtSecurityToken(
            issuer: GetIssuer(),
            audience: GetAudience(),
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: now.AddMinutes(GetAccessTokenMinutes()).UtcDateTime,
            signingCredentials: GetSigningCredentials());

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string CreateExternalClientAccessToken(
        string clientId,
        string organizationId,
        string environmentId,
        int permissionVersion,
        IEnumerable<string> scope)
    {
        var now = DateTimeOffset.UtcNow;
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, clientId),
            new Claim("principalType", "external-client"),
            new Claim("organizationId", organizationId),
            new Claim("environmentId", environmentId),
            new Claim("permissionVersion", permissionVersion.ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("n"))
        };

        foreach (var permission in scope.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            claims.Add(new Claim("scope", permission));
        }

        var token = new JwtSecurityToken(
            issuer: GetIssuer(),
            audience: GetAudience(),
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: now.AddMinutes(GetAccessTokenMinutes()).UtcDateTime,
            signingCredentials: GetSigningCredentials());

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

        return TryReadPrincipal(token);
    }

    public AccessTokenPrincipal? TryReadPrincipal(string token)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        try
        {
            var principal = handler.ValidateToken(token, CreateValidationParameters(), out var securityToken);
            if (securityToken is not JwtSecurityToken jwt
                || !string.Equals(jwt.Header.Alg, SecurityAlgorithms.RsaSha256, StringComparison.Ordinal))
            {
                return null;
            }

            var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var sessionId = principal.FindFirstValue("sessionId");
            var principalType = principal.FindFirstValue("principalType");
            var securityStamp = principal.FindFirstValue("securityStamp");
            var organizationId = principal.FindFirstValue("organizationId");
            var environmentId = principal.FindFirstValue("environmentId");
            var permissionVersionValue = principal.FindFirstValue("permissionVersion");
            if (string.IsNullOrWhiteSpace(userId)
                || string.IsNullOrWhiteSpace(sessionId)
                || !string.Equals(principalType, "user", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(securityStamp)
                || !int.TryParse(permissionVersionValue, out var permissionVersion))
            {
                return null;
            }

            return new AccessTokenPrincipal(
                sessionId,
                userId,
                securityStamp,
                permissionVersion,
                organizationId,
                environmentId);
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

    public ExternalClientAccessTokenPrincipal? TryReadExternalClientPrincipal(HttpContext httpContext)
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

        return TryReadExternalClientPrincipal(token);
    }

    public ExternalClientAccessTokenPrincipal? TryReadExternalClientPrincipal(string token)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        try
        {
            var principal = handler.ValidateToken(token, CreateValidationParameters(), out var securityToken);
            if (securityToken is not JwtSecurityToken jwt
                || !string.Equals(jwt.Header.Alg, SecurityAlgorithms.RsaSha256, StringComparison.Ordinal))
            {
                return null;
            }

            var clientId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var principalType = principal.FindFirstValue("principalType");
            var organizationId = principal.FindFirstValue("organizationId");
            var environmentId = principal.FindFirstValue("environmentId");
            var permissionVersionValue = principal.FindFirstValue("permissionVersion");
            if (string.IsNullOrWhiteSpace(clientId)
                || !string.Equals(principalType, "external-client", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(organizationId)
                || string.IsNullOrWhiteSpace(environmentId)
                || !int.TryParse(permissionVersionValue, out var permissionVersion))
            {
                return null;
            }

            return new ExternalClientAccessTokenPrincipal(
                clientId,
                organizationId,
                environmentId,
                permissionVersion,
                principal.FindAll("scope").Select(x => x.Value).ToArray());
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

    public string GetJwksJson()
    {
        var keys = GetValidationKeys()
            .Select(ToJwk)
            .ToArray();
        return JsonSerializer.Serialize(new { keys });
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
            ValidateLifetime = true,
            RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            IssuerSigningKeyResolver = (_, _, kid, _) => GetValidationKeys()
                .Where(key => string.Equals(key.KeyId, kid, StringComparison.Ordinal))
        };
    }

    private SigningCredentials GetSigningCredentials() => keyMaterial.Value.SigningCredentials;

    private IReadOnlyList<RsaSecurityKey> GetValidationKeys() => keyMaterial.Value.ValidationKeys;

    private static JwtKeyMaterial BuildKeyMaterial(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configured = GetConfiguredSigningKeys(configuration);
        var selected = configured.FirstOrDefault(key => key.Active) ?? configured.FirstOrDefault();
        if (selected is null)
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException("Iam:Jwt:SigningKeys:0:PrivateKeyPem is required outside Development.");
            }

            selected = new JwtPrivateKey(DevelopmentKeyId, DevelopmentPrivateKeyPem, true);
        }

        var signingRsa = RSA.Create();
        signingRsa.ImportFromPem(selected.PrivateKeyPem);
        var signingCredentials = new SigningCredentials(
            new RsaSecurityKey(signingRsa) { KeyId = selected.Kid },
            SecurityAlgorithms.RsaSha256);

        var keys = new List<RsaSecurityKey>();
        foreach (var signingKey in configured)
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(signingKey.PrivateKeyPem);
            keys.Add(new RsaSecurityKey(rsa) { KeyId = signingKey.Kid });
        }

        foreach (var publicKey in configuration.GetSection("Iam:Jwt:ValidationKeys").GetChildren())
        {
            var kid = publicKey["Kid"];
            var publicKeyPem = publicKey["PublicKeyPem"];
            if (string.IsNullOrWhiteSpace(kid) || string.IsNullOrWhiteSpace(publicKeyPem))
            {
                continue;
            }

            var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            keys.Add(new RsaSecurityKey(rsa) { KeyId = kid });
        }

        if (keys.Count == 0 && environment.IsDevelopment())
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(DevelopmentPrivateKeyPem);
            keys.Add(new RsaSecurityKey(rsa) { KeyId = DevelopmentKeyId });
        }

        return new JwtKeyMaterial(signingCredentials, keys);
    }

    private static IReadOnlyList<JwtPrivateKey> GetConfiguredSigningKeys(IConfiguration configuration)
    {
        return configuration.GetSection("Iam:Jwt:SigningKeys")
            .GetChildren()
            .Select(section => new JwtPrivateKey(
                section["Kid"] ?? string.Empty,
                section["PrivateKeyPem"] ?? string.Empty,
                section.GetValue("Active", false)))
            .Where(key => !string.IsNullOrWhiteSpace(key.Kid) && !string.IsNullOrWhiteSpace(key.PrivateKeyPem))
            .ToArray();
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
        var minutes = int.TryParse(configuration["Iam:Jwt:AccessTokenMinutes"], out var configuredMinutes) && configuredMinutes > 0
            ? configuredMinutes
            : 15;
        if (!environment.IsDevelopment() && minutes > 60)
        {
            throw new InvalidOperationException("Iam:Jwt:AccessTokenMinutes must be between 1 and 60 outside Development.");
        }

        return minutes;
    }

    private static void AddIfPresent(List<Claim> claims, string type, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            claims.Add(new Claim(type, value));
        }
    }

    private static object ToJwk(RsaSecurityKey key)
    {
        var parameters = key.Rsa!.ExportParameters(false);
        return new
        {
            kty = "RSA",
            use = "sig",
            kid = key.KeyId,
            alg = SecurityAlgorithms.RsaSha256,
            n = Base64UrlEncoder.Encode(parameters.Modulus),
            e = Base64UrlEncoder.Encode(parameters.Exponent)
        };
    }

    private sealed record JwtPrivateKey(string Kid, string PrivateKeyPem, bool Active);

    private sealed record JwtKeyMaterial(SigningCredentials SigningCredentials, IReadOnlyList<RsaSecurityKey> ValidationKeys);
}
