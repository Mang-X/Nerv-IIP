using Microsoft.AspNetCore.Authentication;

namespace Nerv.IIP.PlatformGateway.Web.Application.Auth;

public sealed record GatewayPermissionRequirement(
    string PermissionCode,
    string OrganizationId,
    string EnvironmentId,
    string? ResourceType,
    string? ResourceId);

public sealed record GatewayAuthorizationResult(
    bool IsAllowed,
    string? PrincipalId,
    string? PrincipalType,
    string? LoginName,
    string? DenialReason)
{
    public static GatewayAuthorizationResult Allowed(string principalId, string principalType, string loginName) =>
        new(true, principalId, principalType, loginName, null);

    public static GatewayAuthorizationResult Forbidden(string reason) =>
        new(false, null, null, null, reason);
}

public interface IGatewayAuthorizationClient
{
    Task<GatewayAuthorizationResult> CheckAsync(
        string bearerToken,
        GatewayPermissionRequirement requirement,
        CancellationToken cancellationToken);
}

public static class GatewayPermissions
{
    public const string AppHubInstancesRead = "apphub.instances.read";
    public const string OpsTasksCreate = "ops.tasks.create";
    public const string OpsTasksRead = "ops.tasks.read";
}

public static class GatewayAuthorization
{
    public const string PrincipalItemKey = "Nerv.IIP.PlatformGateway.Principal";

    public static async Task<GatewayAuthorizationResult?> RequirePermissionAsync(
        HttpContext context,
        IGatewayAuthorizationClient client,
        GatewayPermissionRequirement requirement,
        CancellationToken cancellationToken)
    {
        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Nerv.IIP.PlatformGateway.Authorization");
        var bearerToken = await context.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            logger.LogWarning(
                "GatewayPermissionCheckMissingAccessToken PermissionCode={PermissionCode} OrganizationId={OrganizationId} EnvironmentId={EnvironmentId} Path={Path}",
                requirement.PermissionCode,
                requirement.OrganizationId,
                requirement.EnvironmentId,
                context.Request.Path.ToString());
            await ResponseDataEndpointResults.WriteErrorAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "Unauthorized.",
                cancellationToken);
            return null;
        }

        var result = await client.CheckAsync(bearerToken, requirement, cancellationToken);
        if (!result.IsAllowed)
        {
            logger.LogWarning(
                "GatewayPermissionDenied PrincipalId={PrincipalId} PrincipalType={PrincipalType} PermissionCode={PermissionCode} OrganizationId={OrganizationId} EnvironmentId={EnvironmentId} ResourceType={ResourceType} ResourceId={ResourceId} Reason={Reason} Path={Path}",
                result.PrincipalId,
                result.PrincipalType,
                requirement.PermissionCode,
                requirement.OrganizationId,
                requirement.EnvironmentId,
                requirement.ResourceType,
                requirement.ResourceId,
                result.DenialReason,
                context.Request.Path.ToString());
            await ResponseDataEndpointResults.WriteErrorAsync(
                context,
                StatusCodes.Status403Forbidden,
                "Forbidden.",
                cancellationToken);
            return null;
        }

        context.Items[PrincipalItemKey] = result;
        return result;
    }
}
