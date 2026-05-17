using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Iam.Web.Application.Auth;

namespace Nerv.IIP.Iam.Web.Endpoints;

internal static class IamEndpointAuthorization
{
    public static async Task<bool> RequirePermissionAsync(
        IServiceProvider serviceProvider,
        HttpContext context,
        string permissionCode,
        CancellationToken cancellationToken)
    {
        var auth = serviceProvider.GetRequiredService<IamAuthService>();
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
