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
}

public static class GatewayPermissions
{
    public const string AppHubInstancesRead = "apphub.instances.read";
    public const string IamRolesManage = "iam.roles.manage";
    public const string IamRolesRead = "iam.roles.read";
    public const string IamSessionsRead = "iam.sessions.read";
    public const string IamSessionsRevoke = "iam.sessions.revoke";
    public const string IamUsersManage = "iam.users.manage";
    public const string IamUsersRead = "iam.users.read";
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

        var result = await auth.CheckAsync(
            bearerToken,
            new GatewayPermissionRequirement(
                permissionCode,
                principal.OrganizationId,
                principal.EnvironmentId,
                null,
                null),
            cancellationToken);
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
}
