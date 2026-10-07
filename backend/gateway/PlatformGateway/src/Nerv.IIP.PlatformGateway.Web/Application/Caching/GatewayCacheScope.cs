namespace Nerv.IIP.PlatformGateway.Web.Application.Caching;

internal static class GatewayCacheScope
{
    // Escape each explicit dimension so delimiters in identifiers cannot merge distinct scopes.
    public static string Tag(string organizationId, string environmentId) =>
        $"gateway:scope:{Uri.EscapeDataString(organizationId)}:{Uri.EscapeDataString(environmentId)}";
}
