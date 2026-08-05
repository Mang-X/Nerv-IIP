namespace Nerv.IIP.BusinessGateway.Web.Tests;

/// <summary>
/// The named host profiles this assembly runs gateway HTTP tests against.
/// </summary>
internal enum BusinessGatewayTestHostProfile
{
    /// <summary>
    /// JWT validation configured, downstream base URLs left at their Development defaults.
    /// </summary>
    Default,

    /// <summary>
    /// JWT validation configured plus every downstream base URL pinned to a <c>*.local</c> host, so
    /// a facade that forgets to use its injected client fails as an unreachable downstream instead
    /// of silently hitting a developer's localhost port.
    /// </summary>
    ServiceBaseUrls,
}
