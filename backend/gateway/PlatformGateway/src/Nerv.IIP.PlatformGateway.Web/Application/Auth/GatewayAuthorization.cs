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

    public static async Task<GatewayAuthorizationResult?> RequireAsync(
        HttpContext context,
        IGatewayAuthorizationClient client,
        GatewayPermissionRequirement requirement,
        CancellationToken cancellationToken)
    {
        var bearerToken = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(bearerToken)
            || !bearerToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(
                new { title = "Unauthorized", detail = "Unauthorized.", status = StatusCodes.Status401Unauthorized },
                cancellationToken);
            return null;
        }

        var result = await client.CheckAsync(bearerToken["Bearer ".Length..], requirement, cancellationToken);
        if (!result.IsAllowed)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(
                new { title = "Forbidden", detail = "Forbidden.", status = StatusCodes.Status403Forbidden },
                cancellationToken);
            return null;
        }

        context.Items[PrincipalItemKey] = result;
        return result;
    }
}
