using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Nerv.IIP.Business.Inventory.Web.Endpoints.Inventory;

public static class InventoryPermissionContext
{
    public const string ForwardedPermissionHeaderName = "X-Nerv-IIP-Permissions";

    public static bool HasPermission(ClaimsPrincipal user, IHeaderDictionary headers, string permissionCode)
    {
        return HasPermissionValue(
                user.Claims
                    .Where(claim => claim.Type is "permission" or "permissions" or "permissionCodes")
                    .Select(claim => claim.Value),
                permissionCode)
            || HasPermissionValue(headers[ForwardedPermissionHeaderName], permissionCode);
    }

    private static bool HasPermissionValue(IEnumerable<string?> values, string permissionCode)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Contains(permissionCode, StringComparer.Ordinal);
    }
}
