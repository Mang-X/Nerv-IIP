using Nerv.IIP.Iam.Web.Application.Auth;

namespace Nerv.IIP.Iam.Web.Endpoints;

public interface IIamPermissionAuthorizer
{
    Task<bool> RequirePermissionAsync(HttpContext context, string permissionCode, CancellationToken cancellationToken);
}

public sealed class IamPermissionAuthorizer(IIamAuthService auth) : IIamPermissionAuthorizer
{
    public async Task<bool> RequirePermissionAsync(
        HttpContext context,
        string permissionCode,
        CancellationToken cancellationToken)
    {
        var principal = await auth.GetCurrentPrincipalAsync(context, cancellationToken);
        if (principal is null)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "Unauthorized.",
                cancellationToken);
            return false;
        }

        if (await auth.UserHasPermissionAsync(
                principal.UserId,
                principal.OrganizationId,
                principal.EnvironmentId,
                permissionCode,
                cancellationToken))
        {
            return true;
        }

        await ResponseDataEndpointResults.WriteErrorAsync(
            context,
            StatusCodes.Status403Forbidden,
            "Forbidden.",
            cancellationToken);
        return false;
    }
}

public sealed class InMemoryIamPermissionAuthorizer : IIamPermissionAuthorizer
{
    public Task<bool> RequirePermissionAsync(HttpContext context, string permissionCode, CancellationToken cancellationToken)
    {
        return Task.FromResult(true);
    }
}
