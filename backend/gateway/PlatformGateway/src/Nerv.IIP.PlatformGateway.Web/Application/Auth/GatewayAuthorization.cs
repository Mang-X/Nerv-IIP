using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

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

    Task<GatewayAuthorizationResult> CheckAsync(
        string bearerToken,
        GatewayPermissionRequirement requirement,
        GatewayAuthorizationContinuityMode continuityMode,
        CancellationToken cancellationToken) =>
        CheckAsync(bearerToken, requirement, cancellationToken);
}

public enum GatewayAuthorizationContinuityMode
{
    ReadCacheAllowed,
    RealtimeRequired
}

public static class GatewayPermissions
{
    public const string AppHubInstancesRead = "apphub.instances.read";
    public const string FilesDownloadGrantsCreate = "files.download-grants.create";
    public const string FilesRead = "files.read";
    public const string FilesUpload = "files.upload";
    public const string IamRolesManage = "iam.roles.manage";
    public const string IamRolesRead = "iam.roles.read";
    public const string IamSessionsRead = "iam.sessions.read";
    public const string IamSessionsRevoke = "iam.sessions.revoke";
    public const string IamUsersManage = "iam.users.manage";
    public const string IamUsersRead = "iam.users.read";
    public const string OpsTasksCreate = "ops.tasks.create";
    public const string OpsTasksRead = "ops.tasks.read";
    public const string ObservabilityLogsRead = "observability.logs.read";
    public const string NotificationIntentsSubmit = "notifications.intents.submit";
    public const string NotificationDeadLettersManage = "notifications.dlq.manage";
    public const string NotificationDeadLettersRead = "notifications.dlq.read";
    public const string NotificationMessagesMarkRead = "notifications.messages.mark-read";
    public const string NotificationMessagesRead = "notifications.messages.read";
    public const string NotificationTasksRead = "notifications.tasks.read";
    public const string NotificationDeliveryManage = "notifications.delivery.manage";
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

        GatewayAuthorizationResult result;
        try
        {
            result = await client.CheckAsync(
                bearerToken,
                requirement,
                ContinuityModeFor(context.Request.Method),
                cancellationToken);
        }
        catch (Exception ex) when (IsAuthorizationUnavailable(ex, cancellationToken))
        {
            logger.LogWarning(
                ex,
                "GatewayPermissionCheckUnavailable PermissionCode={PermissionCode} OrganizationId={OrganizationId} EnvironmentId={EnvironmentId} Path={Path}",
                requirement.PermissionCode,
                requirement.OrganizationId,
                requirement.EnvironmentId,
                context.Request.Path.ToString());
            await ResponseDataEndpointResults.WriteErrorAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "Authorization service unavailable.",
                cancellationToken);
            return null;
        }

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

    public static async Task<(string BearerToken, ConsolePrincipalResponse Principal)?> RequireCurrentPrincipalPermissionAsync(
        HttpContext context,
        IGatewayIamAuthClient iam,
        IGatewayAuthorizationClient auth,
        string permissionCode,
        CancellationToken cancellationToken)
    {
        var bearerToken = await context.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            await ResponseDataEndpointResults.WriteErrorAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "Unauthorized.",
                cancellationToken);
            return null;
        }

        ConsolePrincipalResponse principal;
        try
        {
            principal = TryCreatePrincipalFromClaims(context.User)
                ?? await iam.GetMeAsync(bearerToken, cancellationToken);
        }
        catch (GatewayAuthException ex)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(
                context,
                (int)ex.StatusCode,
                ex.Reason,
                cancellationToken);
            return null;
        }

        GatewayAuthorizationResult result;
        try
        {
            result = await auth.CheckAsync(
                bearerToken,
                new GatewayPermissionRequirement(
                    permissionCode,
                    principal.OrganizationId,
                    principal.EnvironmentId,
                    null,
                    null),
                ContinuityModeFor(context.Request.Method),
                cancellationToken);
        }
        catch (Exception ex) when (IsAuthorizationUnavailable(ex, cancellationToken))
        {
            await ResponseDataEndpointResults.WriteErrorAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "Authorization service unavailable.",
                cancellationToken);
            return null;
        }

        if (!result.IsAllowed)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(
                context,
                StatusCodes.Status403Forbidden,
                "Forbidden.",
                cancellationToken);
            return null;
        }

        context.Items[PrincipalItemKey] = result;
        return (bearerToken, principal);
    }

    private static ConsolePrincipalResponse? TryCreatePrincipalFromClaims(ClaimsPrincipal user)
    {
        var principalId = FirstClaimValue(user, JwtRegisteredClaimNames.Sub, ClaimTypes.NameIdentifier);
        var principalType = FirstClaimValue(user, "principalType");
        var loginName = FirstClaimValue(user, "loginName", ClaimTypes.Name);
        var email = FirstClaimValue(user, "email", ClaimTypes.Email);
        var organizationId = FirstClaimValue(user, "organizationId");
        var environmentId = FirstClaimValue(user, "environmentId");
        var permissionVersionValue = FirstClaimValue(user, "permissionVersion");
        if (string.IsNullOrWhiteSpace(principalId)
            || string.IsNullOrWhiteSpace(principalType)
            || string.IsNullOrWhiteSpace(loginName)
            || string.IsNullOrWhiteSpace(email)
            || string.IsNullOrWhiteSpace(organizationId)
            || string.IsNullOrWhiteSpace(environmentId)
            || !int.TryParse(permissionVersionValue, out var permissionVersion))
        {
            return null;
        }

        return new ConsolePrincipalResponse(
            principalId,
            principalType,
            loginName,
            email,
            organizationId,
            environmentId,
            permissionVersion,
            // Claims carry principal context only; authorization still flows through CheckAsync.
            []);
    }

    private static string? FirstClaimValue(ClaimsPrincipal user, params string[] claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var value = user.FindFirstValue(claimType);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static GatewayAuthorizationContinuityMode ContinuityModeFor(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method)
            ? GatewayAuthorizationContinuityMode.ReadCacheAllowed
            : GatewayAuthorizationContinuityMode.RealtimeRequired;

    private static bool IsAuthorizationUnavailable(Exception ex, CancellationToken requestCancellationToken) =>
        ex is HttpRequestException
            || ex is TimeoutException
            || ex is TaskCanceledException && !requestCancellationToken.IsCancellationRequested;
}
