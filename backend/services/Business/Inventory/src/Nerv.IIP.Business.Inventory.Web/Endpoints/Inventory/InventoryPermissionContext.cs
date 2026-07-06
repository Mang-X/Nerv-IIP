using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Nerv.IIP.Contracts.Inventory;

namespace Nerv.IIP.Business.Inventory.Web.Endpoints.Inventory;

public sealed class InventoryForwardedPermissionOptions
{
    public string TrustedIssuer { get; set; } = "business-gateway";

    public string? SigningKey { get; set; }
}

public static class InventoryPermissionContext
{
    public const string ForwardedPermissionHeaderName = InventoryForwardedPermissionHeaders.PermissionsHeaderName;

    public static bool HasPermission(
        ClaimsPrincipal user,
        IHeaderDictionary headers,
        string permissionCode,
        InventoryForwardedPermissionOptions options)
    {
        return HasPermissionValue(
                user.Claims
                    .Where(claim => claim.Type is "permission" or "permissions" or "permissionCodes")
                    .Select(claim => claim.Value),
                permissionCode)
            || HasTrustedForwardedPermission(headers, permissionCode, options);
    }

    private static bool HasPermissionValue(IEnumerable<string?> values, string permissionCode)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Contains(permissionCode, StringComparer.Ordinal);
    }

    private static bool HasTrustedForwardedPermission(
        IHeaderDictionary headers,
        string permissionCode,
        InventoryForwardedPermissionOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SigningKey))
        {
            return false;
        }

        var permissions = headers[InventoryForwardedPermissionHeaders.PermissionsHeaderName].ToString();
        var issuer = headers[InventoryForwardedPermissionHeaders.IssuerHeaderName].ToString();
        var signature = headers[InventoryForwardedPermissionHeaders.SignatureHeaderName].ToString();
        return string.Equals(issuer, options.TrustedIssuer, StringComparison.Ordinal)
            && InventoryForwardedPermissionHeaders.VerifySignature(options.SigningKey, issuer, permissions, signature)
            && HasPermissionValue([permissions], permissionCode);
    }
}
