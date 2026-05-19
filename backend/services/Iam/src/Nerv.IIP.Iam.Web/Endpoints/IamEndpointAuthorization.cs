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
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(
                new { title = "Unauthorized", detail = "Unauthorized.", status = StatusCodes.Status401Unauthorized },
                cancellationToken);
            return false;
        }

        if (await auth.UserHasPermissionAsync(principal.UserId, permissionCode, cancellationToken))
        {
            return true;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(
            new { title = "Forbidden", detail = "Forbidden.", status = StatusCodes.Status403Forbidden },
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
