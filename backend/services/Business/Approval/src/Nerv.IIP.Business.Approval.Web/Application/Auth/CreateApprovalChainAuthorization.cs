using System.Security.Claims;
using Nerv.IIP.Business.Approval.Web.Endpoints.Approvals;
using Nerv.IIP.Contracts.Approval;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Approval.Web.Application.Auth;

internal static class CreateApprovalChainAuthorization
{
    internal const string PolicyName = "Approval.CreateChain";
    private const string SchemeName = "ApprovalCreateChainCaller";

    internal static void AddCreateApprovalChainAuthorization(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("Approval:CreateChain");
        var scopedCallers = settings.GetSection("ScopedCallers");
        var useScopedCallers = scopedCallers.Exists();
        var allowLegacy = settings.GetValue("AllowLegacyBearer", true);

        if (!useScopedCallers && !allowLegacy)
        {
            throw new InvalidOperationException("Approval create-chain requires scoped caller profiles when legacy authentication is disabled.");
        }

        if (useScopedCallers)
        {
            services.AddNervIipScopedCallerAuthentication(scopedCallers, SchemeName);
            services.AddOptions<ScopedCallerAuthenticationOptions>(SchemeName)
                .Validate<IInternalServiceTokenProvider>((options, legacy) =>
                    options.Profiles.All(profile => profile.BearerToken != legacy.BearerToken),
                    "Approval create-chain credentials must differ from the shared internal bearer.");
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

    internal static bool Allows(ClaimsPrincipal principal, StartApprovalChainRequest request)
    {
        var caller = principal.Identities.SingleOrDefault(identity =>
            identity.IsAuthenticated && identity.AuthenticationType == SchemeName);
        if (caller is null)
        {
            return principal.Identities.Any(identity =>
                identity.IsAuthenticated && identity.AuthenticationType == InternalServiceAuthentication.SchemeName);
        }

        if (!caller.HasClaim(ScopedCallerClaimTypes.OrganizationId, request.OrganizationId)
            || !caller.HasClaim(ScopedCallerClaimTypes.EnvironmentId, request.EnvironmentId))
        {
            return false;
        }

        return caller.FindFirst(ScopedCallerClaimTypes.Subject)?.Value switch
        {
            "business-erp" => request.SourceService == ApprovalSourceServices.BusinessErp
                && request.DocumentType is ApprovalDocumentTypes.PurchaseOrder or ApprovalDocumentTypes.SalesOrderCreditRelease
                && HasLocalRouting(request)
                && caller.HasClaim(ScopedCallerClaimTypes.Permission, NervIipPermissionCodes.InternalApprovalChainsCreateErp),
            "inventory" => request.SourceService == ApprovalSourceServices.Inventory
                && HasLocalRouting(request)
                && caller.HasClaim(ScopedCallerClaimTypes.Permission, NervIipPermissionCodes.InternalApprovalChainsCreateInventory),
            // The gateway verifies the initiating user's scope and approval permission.
            // Its existing contract carries the business source and routing organization as document metadata.
            "business-gateway" => caller.HasClaim(ScopedCallerClaimTypes.Permission, NervIipPermissionCodes.InternalApprovalChainsCreateGateway),
            _ => false,
        };
    }

    private static bool HasLocalRouting(StartApprovalChainRequest request)
        => request.RoutingOrganizationId is null || request.RoutingOrganizationId == request.OrganizationId;
}
