using System.Security.Claims;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Approval.Web.Application.Auth;

internal static class CheckOverdueApprovalStepsAuthorization
{
    internal const string PolicyName = "Approval.CheckOverdue";
    private const string SchemeName = "ApprovalOverdueOperatorCaller";

    internal static void AddCheckOverdueApprovalStepsAuthorization(this IServiceCollection services, IConfiguration configuration)
    {
        var approval = configuration.GetSection("Approval");
        var settings = approval.GetSection("OverdueCheck:HttpAuthorization");
        var scopedCallers = settings.GetSection("ScopedCallers");
        var useScopedCallers = scopedCallers.Exists();
        var allowLegacy = settings.GetValue("AllowLegacyBearer", true);

        if (!useScopedCallers && !allowLegacy)
        {
            throw new InvalidOperationException("Approval overdue HTTP checks require scoped caller profiles when legacy authentication is disabled.");
        }

        if (useScopedCallers)
        {
            var otherOperationTokens = approval.GetChildren()
                .SelectMany(section => section.GetSection("ScopedCallers:Profiles").GetChildren())
                .Select(profile => profile["BearerToken"])
                .ToHashSet(StringComparer.Ordinal);

            services.AddNervIipScopedCallerAuthentication(scopedCallers, SchemeName);
            services.AddOptions<ScopedCallerAuthenticationOptions>(SchemeName)
                .Validate<IInternalServiceTokenProvider>((options, legacy) =>
                    options.Profiles.All(profile => profile.BearerToken != legacy.BearerToken
                        && !otherOperationTokens.Contains(profile.BearerToken)),
                    "Approval overdue HTTP credentials must differ from the shared bearer and other Approval operation credentials.");
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
            && caller.HasClaim(ScopedCallerClaimTypes.Subject, "approval-overdue-operator")
            && caller.HasClaim(ScopedCallerClaimTypes.Permission, NervIipPermissionCodes.InternalApprovalTasksCheckOverdue)
            && caller.HasClaim(ScopedCallerClaimTypes.OrganizationId, organizationId)
            && caller.HasClaim(ScopedCallerClaimTypes.EnvironmentId, environmentId);
    }
}
