using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.PlatformGateway.Web.Application.Auth;

public static class GatewayCacheInvalidationAuthorization
{
    public const string SchemeName = "GatewayCacheScopedCaller";
    public const string PolicyName = "GatewayCacheScopedCaller.Invalidate";

    public static IServiceCollection AddGatewayCacheInvalidationAuthorization(
        this IServiceCollection services, IConfiguration configuration)
    {
        var callers = configuration.GetSection("Gateway:CacheInvalidation:ScopedCallers");
        if (callers.Exists())
        {
            services.AddNervIipScopedCallerAuthentication(callers, SchemeName);
            services.AddNervIipScopedCallerPolicy(
                PolicyName, SchemeName, NervIipPermissionCodes.InternalGatewayCacheInvalidate);
        }
        else
        {
            // Existing hosts may keep using the legacy operation without provisioning scoped callers.
            services.AddAuthorization(options => options.AddPolicy(PolicyName, policy =>
            {
                policy.AddAuthenticationSchemes(InternalServiceAuthentication.SchemeName);
                policy.RequireAssertion(_ => false);
            }));
        }
        return services;
    }
}
