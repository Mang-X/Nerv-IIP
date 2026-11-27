using System.Security.Claims;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Approval.Web.Application.Auth;

internal sealed class ApprovalGatewayOperationAuthorization(string operation, string permission)
{
    internal static readonly ApprovalGatewayOperationAuthorization ReadCollections =
        new("ReadCollections", NervIipPermissionCodes.InternalApprovalChainsListGateway);
    internal static readonly ApprovalGatewayOperationAuthorization ReadTemplates =
        new("ReadTemplates", NervIipPermissionCodes.InternalApprovalTemplatesReadGateway);
    internal static readonly ApprovalGatewayOperationAuthorization WriteTemplates =
        new("WriteTemplates", NervIipPermissionCodes.InternalApprovalTemplatesWriteGateway);

    internal string PolicyName => $"Approval.{operation}";
    private string SchemeName => $"Approval{operation}Caller";

    internal void Register(IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection($"Approval:{operation}");
        var scopedCallers = settings.GetSection("ScopedCallers");
        var useScopedCallers = scopedCallers.Exists();
        var allowLegacy = settings.GetValue("AllowLegacyBearer", true);

        if (!useScopedCallers && !allowLegacy)
        {
            throw new InvalidOperationException($"{PolicyName} requires scoped caller profiles when legacy authentication is disabled.");
        }

        if (useScopedCallers)
        {
            var otherOperationTokens = configuration.GetSection("Approval").GetChildren()
                .Where(section => !string.Equals(section.Key, operation, StringComparison.OrdinalIgnoreCase))
                .SelectMany(section => section.GetSection("ScopedCallers:Profiles").GetChildren())
                .Select(profile => profile["BearerToken"])
                .ToHashSet(StringComparer.Ordinal);

            services.AddNervIipScopedCallerAuthentication(scopedCallers, SchemeName);
            services.AddOptions<ScopedCallerAuthenticationOptions>(SchemeName)
                .Validate<IInternalServiceTokenProvider>((options, legacy) =>
                    options.Profiles.All(profile => profile.BearerToken != legacy.BearerToken
                        && !otherOperationTokens.Contains(profile.BearerToken)),
                    $"{PolicyName} credentials must differ from shared and other Approval operation credentials.");
        }

        services.AddAuthorization(options => options.AddPolicy(PolicyName, policy =>
        {
            if (useScopedCallers)
            {
                policy.AddAuthenticationSchemes(SchemeName);
            }

            if (allowLegacy)
            {
                policy.AddAuthenticationSchemes(InternalServiceAuthentication.SchemeName);
            }

            policy.RequireAuthenticatedUser();
        }));
    }

    internal bool Allows(ClaimsPrincipal principal, string? organizationId, string? environmentId)
    {
        var caller = principal.Identities.SingleOrDefault(identity =>
            identity.IsAuthenticated && identity.AuthenticationType == SchemeName);
        if (caller is null)
        {
            return principal.Identities.Any(identity =>
                identity.IsAuthenticated && identity.AuthenticationType == InternalServiceAuthentication.SchemeName);
        }

        return !string.IsNullOrWhiteSpace(organizationId)
            && !string.IsNullOrWhiteSpace(environmentId)
            && caller.HasClaim(ScopedCallerClaimTypes.Subject, "business-gateway")
            && caller.HasClaim(ScopedCallerClaimTypes.Permission, permission)
            && caller.HasClaim(ScopedCallerClaimTypes.OrganizationId, organizationId)
            && caller.HasClaim(ScopedCallerClaimTypes.EnvironmentId, environmentId);
    }
}
