using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Nerv.IIP.ServiceAuth;

public static class ScopedCallerClaimTypes
{
    public const string Subject = "sub";
    public const string OrganizationId = "organizationId";
    public const string EnvironmentId = "environmentId";
    public const string Permission = "permission";
    public const string PrincipalType = "principal_type";
    public const string TokenType = "token_type";
}

public sealed class ScopedCallerAuthenticationOptions : AuthenticationSchemeOptions
{
    public List<ScopedCallerProfile> Profiles { get; set; } = [];
}

public sealed class ScopedCallerProfile
{
    public string Name { get; set; } = string.Empty;
    public string BearerToken { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public string EnvironmentId { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = [];
}

public static class ScopedCallerAuthentication
{
    public const string InternalServicePrincipalType = "internal-service";
    public const string InternalServiceTokenType = "internal_service";

    /// <summary>
    /// Adds an opt-in named inbound scheme without changing the host's default authentication scheme.
    /// </summary>
    public static IServiceCollection AddNervIipScopedCallerAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        string schemeName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemeName);

        services.AddOptions<ScopedCallerAuthenticationOptions>(schemeName)
            .Bind(configuration)
            .Validate(Validate, "Scoped caller profiles are missing, invalid, or ambiguous.")
            .ValidateOnStart();

        services.AddAuthentication()
            .AddScheme<ScopedCallerAuthenticationOptions, ScopedCallerAuthenticationHandler>(schemeName, _ => { });

        return services;
    }

    /// <summary>
    /// Adds a policy bound to one scoped-caller scheme and one application-supplied permission code.
    /// </summary>
    public static IServiceCollection AddNervIipScopedCallerPolicy(
        this IServiceCollection services,
        string policyName,
        string schemeName,
        string permission)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        services.AddAuthorization(options =>
            options.AddPolicy(policyName, policy =>
            {
                policy.AddAuthenticationSchemes(schemeName);
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(ScopedCallerClaimTypes.TokenType, InternalServiceTokenType);
                policy.RequireClaim(ScopedCallerClaimTypes.PrincipalType, InternalServicePrincipalType);
                policy.RequireClaim(ScopedCallerClaimTypes.Subject);
                policy.RequireClaim(ScopedCallerClaimTypes.OrganizationId);
                policy.RequireClaim(ScopedCallerClaimTypes.EnvironmentId);
                policy.RequireClaim(ScopedCallerClaimTypes.Permission, permission);
            }));

        return services;
    }

    private static bool Validate(ScopedCallerAuthenticationOptions options)
    {
        if (options.Profiles.Count == 0)
        {
            return false;
        }

        var profileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bearerTokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in options.Profiles)
        {
            if (!IsCanonicalValue(profile.Name)
                || !InternalServiceBearerToken.IsValidToken(profile.BearerToken)
                || !IsSafeSubject(profile.Subject)
                || !IsCanonicalValue(profile.OrganizationId)
                || !IsCanonicalValue(profile.EnvironmentId)
                || profile.Permissions.Count == 0
                || profile.Permissions.Any(permission => !IsCanonicalValue(permission))
                || !profileNames.Add(profile.Name)
                || !bearerTokens.Add(profile.BearerToken))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsCanonicalValue(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static bool IsSafeSubject(string? subject)
        => IsCanonicalValue(subject)
           && subject!.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or '@');
}

public sealed class ScopedCallerAuthenticationHandler(
    IOptionsMonitor<ScopedCallerAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<ScopedCallerAuthenticationOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!InternalServiceBearerToken.TryParseStrict(authorization, out var suppliedToken))
        {
            return Task.FromResult(AuthenticateResult.Fail("Malformed scoped caller bearer authorization."));
        }

        ScopedCallerProfile? matchedProfile = null;
        foreach (var profile in Options.Profiles)
        {
            if (InternalServiceBearerToken.FixedTimeEquals(suppliedToken, profile.BearerToken))
            {
                matchedProfile = profile;
            }
        }

        if (matchedProfile is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid scoped caller bearer token."));
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, matchedProfile.Subject),
            new(ClaimTypes.Name, matchedProfile.Subject),
            new(ScopedCallerClaimTypes.Subject, matchedProfile.Subject),
            new(ScopedCallerClaimTypes.OrganizationId, matchedProfile.OrganizationId),
            new(ScopedCallerClaimTypes.EnvironmentId, matchedProfile.EnvironmentId),
            new(ScopedCallerClaimTypes.PrincipalType, ScopedCallerAuthentication.InternalServicePrincipalType),
            new(ScopedCallerClaimTypes.TokenType, ScopedCallerAuthentication.InternalServiceTokenType)
        };
        claims.AddRange(matchedProfile.Permissions.Distinct(StringComparer.Ordinal)
            .Select(permission => new Claim(ScopedCallerClaimTypes.Permission, permission)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
