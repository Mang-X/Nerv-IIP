using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Nerv.IIP.Business.Erp.Web.Application.Auth;

public static class MachineOverheadInternalCallerAuthentication
{
    public const string SchemeName = "ErpMachineOverheadInternalCaller";
    public const string PolicyName = "ErpMachineOverheadInternalCaller";
    public const string OrganizationClaim = "organization_id";
    public const string EnvironmentClaim = "environment_id";
    public const string TokenType = "erp_machine_overhead_internal";

    public static IServiceCollection AddErpMachineOverheadInternalCallerAuthentication(
        this IServiceCollection services)
    {
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, MachineOverheadInternalCallerAuthenticationHandler>(
                SchemeName,
                _ => { });
        services.AddAuthorization(options => options.AddPolicy(PolicyName, policy =>
        {
            policy.AddAuthenticationSchemes(SchemeName);
            policy.RequireAuthenticatedUser();
            policy.RequireClaim("token_type", TokenType);
            policy.RequireClaim(ClaimTypes.NameIdentifier);
            policy.RequireClaim(OrganizationClaim);
            policy.RequireClaim(EnvironmentClaim);
        }));
        return services;
    }
}

public sealed class MachineOverheadInternalCallerAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        const string bearerPrefix = "Bearer ";
        if (!authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        var token = authorization[bearerPrefix.Length..].Trim();
        var matches = configuration
            .GetSection("Erp:MachineOverheadReconciliation:AuthorizedCallers")
            .GetChildren()
            .Select(ReadCaller)
            .Where(caller => caller is not null && TimeConstantEquals(token, caller.BearerToken))
            .Cast<AuthorizedCaller>()
            .ToArray();
        if (matches.Length != 1)
            return Task.FromResult(AuthenticateResult.Fail("Invalid ERP machine-overhead internal caller credential."));

        var caller = matches[0];
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, caller.Subject),
            new Claim(ClaimTypes.Name, caller.Subject),
            new Claim("token_type", MachineOverheadInternalCallerAuthentication.TokenType),
            new Claim(MachineOverheadInternalCallerAuthentication.OrganizationClaim, caller.OrganizationId),
            new Claim(MachineOverheadInternalCallerAuthentication.EnvironmentClaim, caller.EnvironmentId),
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }

    private static AuthorizedCaller? ReadCaller(IConfigurationSection section)
    {
        var subject = section["Subject"]?.Trim();
        var bearerToken = section["BearerToken"]?.Trim();
        var organizationId = section["OrganizationId"]?.Trim();
        var environmentId = section["EnvironmentId"]?.Trim();
        if (!IsSafeSubject(subject)
            || string.IsNullOrWhiteSpace(bearerToken)
            || string.IsNullOrWhiteSpace(organizationId)
            || string.IsNullOrWhiteSpace(environmentId))
        {
            return null;
        }

        return new(subject!, bearerToken, organizationId, environmentId);
    }

    private static bool IsSafeSubject(string? subject)
        => !string.IsNullOrWhiteSpace(subject)
            && subject.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or '@');

    private static bool TimeConstantEquals(string value, string expected)
    {
        var valueBytes = System.Text.Encoding.UTF8.GetBytes(value);
        var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(valueBytes, expectedBytes);
    }

    private sealed record AuthorizedCaller(
        string Subject,
        string BearerToken,
        string OrganizationId,
        string EnvironmentId);
}
