using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Nerv.IIP.Contracts.Inventory;

namespace Nerv.IIP.Business.Inventory.Web.Endpoints.Inventory;

public sealed class InventoryForwardedPermissionOptions
{
    public string TrustedIssuer { get; set; } = "business-gateway";

    public string? SigningKey { get; set; }

    public TimeSpan MaxClockSkew { get; set; } = TimeSpan.FromMinutes(5);
}

public static class InventoryPermissionContext
{
    public const string ForwardedPermissionHeaderName = InventoryForwardedPermissionHeaders.PermissionsHeaderName;

    public static bool HasPermission(
        ClaimsPrincipal user,
        IHeaderDictionary headers,
        string permissionCode,
        string organizationId,
        string environmentId,
        string requestKey,
        InventoryForwardedPermissionOptions options)
    {
        return HasPermissionValue(
                user.Claims
                    .Where(claim => claim.Type is "permission" or "permissions" or "permissionCodes")
                    .Select(claim => claim.Value),
                permissionCode)
            || HasTrustedForwardedPermission(headers, permissionCode, organizationId, environmentId, requestKey, options);
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
        string organizationId,
        string environmentId,
        string requestKey,
        InventoryForwardedPermissionOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SigningKey))
        {
            return false;
        }

        var permissions = headers[InventoryForwardedPermissionHeaders.PermissionsHeaderName].ToString();
        var issuer = headers[InventoryForwardedPermissionHeaders.IssuerHeaderName].ToString();
        var forwardedOrganizationId = headers[InventoryForwardedPermissionHeaders.OrganizationHeaderName].ToString();
        var forwardedEnvironmentId = headers[InventoryForwardedPermissionHeaders.EnvironmentHeaderName].ToString();
        var forwardedRequestKey = headers[InventoryForwardedPermissionHeaders.RequestKeyHeaderName].ToString();
        var issuedAt = headers[InventoryForwardedPermissionHeaders.IssuedAtHeaderName].ToString();
        var signature = headers[InventoryForwardedPermissionHeaders.SignatureHeaderName].ToString();
        if (!long.TryParse(issuedAt, out var issuedAtUnixSeconds))
        {
            return false;
        }

        var issuedAtUtc = DateTimeOffset.FromUnixTimeSeconds(issuedAtUnixSeconds);
        if (DateTimeOffset.UtcNow - issuedAtUtc > options.MaxClockSkew
            || issuedAtUtc - DateTimeOffset.UtcNow > options.MaxClockSkew)
        {
            return false;
        }

        return string.Equals(issuer, options.TrustedIssuer, StringComparison.Ordinal)
            && string.Equals(forwardedOrganizationId, organizationId, StringComparison.Ordinal)
            && string.Equals(forwardedEnvironmentId, environmentId, StringComparison.Ordinal)
            && string.Equals(forwardedRequestKey, requestKey, StringComparison.Ordinal)
            && InventoryForwardedPermissionHeaders.VerifySignature(
                options.SigningKey,
                issuer,
                permissions,
                organizationId,
                environmentId,
                requestKey,
                issuedAtUnixSeconds,
                signature)
            && HasPermissionValue([permissions], permissionCode);
    }
}
