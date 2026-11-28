using System.Security.Claims;
using Nerv.IIP.Business.Approval.Web.Application.Queries.Chains;
using Nerv.IIP.Contracts.Approval;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Approval.Web.Application.Auth;

internal static class GetApprovalChainAuthorization
{
    internal const string PolicyName = "Approval.GetChain";
    private const string SchemeName = "ApprovalGetChainCaller";

    internal static void AddGetApprovalChainAuthorization(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("Approval:GetChain");
        var scopedCallers = settings.GetSection("ScopedCallers");
        var useScopedCallers = scopedCallers.Exists();
        var allowLegacy = settings.GetValue("AllowLegacyBearer", true);

        if (!useScopedCallers && !allowLegacy)
        {
            throw new InvalidOperationException("Approval get-chain requires scoped caller profiles when legacy authentication is disabled.");
        }

        if (useScopedCallers)
        {
            services.AddNervIipScopedCallerAuthentication(scopedCallers, SchemeName);
            services.AddOptions<ScopedCallerAuthenticationOptions>(SchemeName)
                .Validate<IInternalServiceTokenProvider>((options, legacy) =>
                    options.Profiles.All(profile => profile.BearerToken != legacy.BearerToken),
                    "Approval get-chain credentials must differ from the shared internal bearer.")
                .Validate(options =>
                {
                    var createTokens = configuration.GetSection("Approval:CreateChain:ScopedCallers:Profiles")
                        .GetChildren().Select(profile => profile["BearerToken"]).ToHashSet(StringComparer.Ordinal);
                    return options.Profiles.All(profile => !createTokens.Contains(profile.BearerToken));
                }, "Approval get-chain credentials must differ from create-chain credentials.");
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

    internal static bool Allows(ClaimsPrincipal principal, ApprovalChainResponse chain)
    {
        var caller = principal.Identities.SingleOrDefault(identity =>
            identity.IsAuthenticated && identity.AuthenticationType == SchemeName);
        if (caller is null)
        {
            return principal.Identities.Any(identity =>
                identity.IsAuthenticated && identity.AuthenticationType == InternalServiceAuthentication.SchemeName);
        }

        if (!caller.HasClaim(ScopedCallerClaimTypes.OrganizationId, chain.OrganizationId)
            || !caller.HasClaim(ScopedCallerClaimTypes.EnvironmentId, chain.EnvironmentId))
        {
            return false;
        }

        return caller.FindFirst(ScopedCallerClaimTypes.Subject)?.Value switch
        {
            "business-mes" => caller.HasClaim(ScopedCallerClaimTypes.Permission, NervIipPermissionCodes.InternalApprovalChainsReadMes)
                && chain.SourceService == ApprovalSourceServices.BusinessMes
                && chain.DocumentType == ApprovalDocumentTypes.MesOperationTaskStartAuthorization,
            "quality" => caller.HasClaim(ScopedCallerClaimTypes.Permission, NervIipPermissionCodes.InternalApprovalChainsReadQuality)
                && ApprovalSourceServices.QualityAliases.Contains(chain.SourceService)
                && (ApprovalDocumentTypes.NcrDispositionAliases.Contains(chain.DocumentType)
                    || ApprovalDocumentTypes.CapaClosureAliases.Contains(chain.DocumentType)),
            "product-engineering" => caller.HasClaim(ScopedCallerClaimTypes.Permission, NervIipPermissionCodes.InternalApprovalChainsReadProductEngineering)
                && chain.TemplateCode == ApprovalTemplateCodes.EngineeringChangeOrder
                && string.Equals(chain.SourceService, ApprovalSourceServices.ProductEngineering, StringComparison.OrdinalIgnoreCase)
                && string.Equals(chain.DocumentType, ApprovalDocumentTypes.EngineeringChangeOrder, StringComparison.OrdinalIgnoreCase),
            "business-gateway" => caller.HasClaim(ScopedCallerClaimTypes.Permission, NervIipPermissionCodes.InternalApprovalChainsReadGateway),
            _ => false,
        };
    }
}
