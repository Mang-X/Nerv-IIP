using System.Security.Claims;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Approval.Web.Application.Auth;

internal static class ReadApprovalCollectionsAuthorization
{
    internal const string PolicyName = "Approval.ReadCollections";
    private const string SchemeName = "ApprovalReadCollectionsCaller";

    internal static void AddReadApprovalCollectionsAuthorization(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("Approval:ReadCollections");
        var scopedCallers = settings.GetSection("ScopedCallers");
        var useScopedCallers = scopedCallers.Exists();
        var allowLegacy = settings.GetValue("AllowLegacyBearer", true);

        if (!useScopedCallers && !allowLegacy)
        {
            throw new InvalidOperationException("Approval collection reads require scoped caller profiles when legacy authentication is disabled.");
        }

        if (useScopedCallers)
        {
            var otherOperationTokens = configuration.GetSection("Approval:CreateChain:ScopedCallers:Profiles").GetChildren()
                .Concat(configuration.GetSection("Approval:GetChain:ScopedCallers:Profiles").GetChildren())
                .Select(profile => profile["BearerToken"])
                .ToHashSet(StringComparer.Ordinal);

            services.AddNervIipScopedCallerAuthentication(scopedCallers, SchemeName);
            services.AddOptions<ScopedCallerAuthenticationOptions>(SchemeName)
                .Validate<IInternalServiceTokenProvider>((options, legacy) =>
                    options.Profiles.All(profile => profile.BearerToken != legacy.BearerToken
                        && !otherOperationTokens.Contains(profile.BearerToken)),
                    "Approval collection-read credentials must differ from the shared, create-chain and get-chain credentials.");
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

    internal static bool Allows(ClaimsPrincipal principal, string organizationId, string environmentId)
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
            && caller.HasClaim(ScopedCallerClaimTypes.Permission, NervIipPermissionCodes.InternalApprovalChainsListGateway)
            && caller.HasClaim(ScopedCallerClaimTypes.OrganizationId, organizationId)
            && caller.HasClaim(ScopedCallerClaimTypes.EnvironmentId, environmentId);
    }
}
